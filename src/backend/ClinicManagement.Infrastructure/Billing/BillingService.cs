using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Pharmacy;
using ClinicManagement.Infrastructure.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Billing;

public partial class BillingService : IBillingService
{
    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BillingService> _logger;
    private readonly IFacilityAuthorizationService _facilityAuthService;
    private readonly ICurrentUserService _currentUserService;

    public async Task<InvoiceDetailDto> CreateInvoiceFromAppointmentAsync(long appointmentId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        await _facilityAuthService.ValidateAppointmentAccessAsync(createdByUserId, appointmentId, cancellationToken);
        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Specialty)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment == null)
            throw new NotFoundException("Lịch khám không tồn tại.");

        if (appointment.Status != AppointmentStatus.Completed)
            throw new BusinessException("INVALID_STATUS", "Chỉ có thể lập hóa đơn cho ca khám đã hoàn tất (Completed).");

        var existingActiveInvoice = await _dbContext.Invoices
            .AnyAsync(i => i.AppointmentId == appointmentId && i.Status != InvoiceStatus.Cancelled, cancellationToken);

        if (existingActiveInvoice)
            throw new BusinessException("INVOICE_ALREADY_EXISTS", "Lịch khám này đã có hóa đơn đang hoạt động (chưa hủy).");

        var consultationFee = appointment.Specialty.ConsultationFee;
        if (consultationFee <= 0)
            throw new BusinessException("FEE_NOT_CONFIGURED", "Chuyên khoa chưa được cấu hình mức phí hợp lệ. Vui lòng liên hệ quản trị viên.");

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        var invoiceId = await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var invoice = new Invoice
                {
                    InvoiceCode = GenerateInvoiceCode(),
                    PatientId = appointment.PatientId,
                    SourceType = InvoiceSourceType.Appointment,
                    AppointmentId = appointmentId,
                    Status = InvoiceStatus.Unpaid,
                    Subtotal = consultationFee,
                    TotalAmount = consultationFee,
                    CreatedByUserId = createdByUserId,
                    CreatedAtUtc = DateTime.UtcNow
                };

                invoice.Items.Add(new InvoiceItem
                {
                    ItemCode = appointment.Specialty.SpecialtyCode,
                    Description = $"Khám chuyên khoa: {appointment.Specialty.Name}",
                    Quantity = 1,
                    UnitPrice = consultationFee,
                    LineTotal = consultationFee,
                    ReferenceType = "Appointment",
                    ReferenceId = appointmentId,
                    IsCancelled = false
                });

                _dbContext.Invoices.Add(invoice);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = createdByUserId,
                    Action = "CREATE_INVOICE",
                    EntityName = "Invoice",
                    EntityId = invoice.Id.ToString(),
                    Description = $"Lập hóa đơn #{invoice.InvoiceCode} cho lịch hẹn #{appointment.AppointmentCode} ({ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)})",
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                if (appointment.Patient.UserId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
                    {
                        UserId = appointment.Patient.UserId.Value,
                        Type = NotificationType.Invoice,
                        Title = "Hóa đơn mới được tạo",
                        Message = $"Hóa đơn #{invoice.InvoiceCode} cho lịch khám #{appointment.AppointmentCode} đã được tạo với số tiền {ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)}.",
                        Route = "/patient/invoices",
                        RelatedEntityType = "Invoice",
                        RelatedEntityId = invoice.Id.ToString(),
                        DedupeKey = $"inv_created_{invoice.Id}"
                    }, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return invoice.Id;
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var duplicateExists = await _dbContext.Invoices.AsNoTracking()
                    .AnyAsync(i => i.AppointmentId == appointmentId && i.Status != InvoiceStatus.Cancelled, cancellationToken);

                if (duplicateExists)
                {
                    _logger.LogWarning(exception, "Concurrent invoice creation was blocked for appointment {AppointmentId}.", appointmentId);
                    throw new ConflictException("INVOICE_ALREADY_EXISTS", "Lịch khám này vừa được lập hóa đơn bởi một yêu cầu khác.");
                }

                throw;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });

        return await GetInvoiceDetailAsync(invoiceId, cancellationToken);
    }

    public async Task<InvoiceDetailDto> CreateInvoiceFromVisitAsync(long visitId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        await _facilityAuthService.ValidateVisitAccessAsync(createdByUserId, visitId, cancellationToken);
        var visit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Department)
                .ThenInclude(d => d!.Specialty)
            .Include(v => v.Appointment)
                .ThenInclude(a => a!.Specialty)
            .Include(v => v.DiagnosticOrders)
                .ThenInclude(o => o.Items)
                    .ThenInclude(i => i.DiagnosticService)
            .Include(v => v.Prescriptions)
                .ThenInclude(p => p.Items)
                    .ThenInclude(i => i.Medicine)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken);

        if (visit == null)
            throw new NotFoundException("Lượt khám không tồn tại.");

        var hasPendingUnpaidInvoice = await _dbContext.Invoices
            .AnyAsync(i => i.PatientVisitId == visitId && i.Status == InvoiceStatus.Unpaid, cancellationToken);

        if (hasPendingUnpaidInvoice)
            throw new BusinessException("PENDING_INVOICE_EXISTS", "Lượt khám đang có hóa đơn chưa thanh toán. Vui lòng thanh toán hoặc hủy hóa đơn trước khi lập hóa đơn mới.");

        var alreadyBilledItems = await _dbContext.InvoiceItems
            .Where(ii => !ii.IsCancelled && ii.Invoice.Status != InvoiceStatus.Cancelled)
            .Where(ii => ii.Invoice.PatientVisitId == visitId || (visit.AppointmentId.HasValue && ii.Invoice.AppointmentId == visit.AppointmentId.Value))
            .Select(ii => new { ii.ReferenceType, ii.ReferenceId })
            .ToListAsync(cancellationToken);

        var alreadyBilledSet = alreadyBilledItems.Select(x => $"{x.ReferenceType}:{x.ReferenceId}").ToHashSet();
        var itemsToCreate = new List<InvoiceItem>();

        // 1. Consultation fee
        var consultationFee = visit.Department?.Specialty?.ConsultationFee ?? visit.Appointment?.Specialty?.ConsultationFee ?? 0m;
        if (!alreadyBilledSet.Contains($"Consultation:{visit.Id}"))
        {
            if (consultationFee <= 0)
                throw new BusinessException("FEE_NOT_CONFIGURED", "Chuyên khoa/khoa khám chưa được cấu hình mức phí hợp lệ.");

            itemsToCreate.Add(new InvoiceItem
            {
                ItemCode = visit.Department?.Specialty?.SpecialtyCode ?? "KHAM",
                Description = $"Khám bệnh: {visit.Department?.Name ?? "Khám chuyên khoa"}",
                Quantity = 1,
                UnitPrice = consultationFee,
                LineTotal = consultationFee,
                ReferenceType = "Consultation",
                ReferenceId = visit.Id,
                IsCancelled = false
            });
        }

        // 2. Diagnostic services
        var activeDiagnosticOrders = visit.DiagnosticOrders
            .Where(o => o.Status != DiagnosticOrderStatus.Cancelled)
            .ToList();

        foreach (var order in activeDiagnosticOrders)
        {
            foreach (var item in order.Items.Where(i => i.Status != DiagnosticItemStatus.Cancelled))
            {
                if (alreadyBilledSet.Contains($"DiagnosticItem:{item.Id}"))
                    continue;

                if (item.IsPackageCovered)
                {
                    itemsToCreate.Add(new InvoiceItem
                    {
                        ItemCode = item.DiagnosticService?.Code ?? "CLS",
                        Description = $"Chỉ định CLS: {item.DiagnosticService?.Name ?? "Dịch vụ"} (Gói khám chi trả)",
                        Quantity = 1,
                        UnitPrice = 0m,
                        LineTotal = 0m,
                        ReferenceType = "DiagnosticItem",
                        ReferenceId = item.Id,
                        IsCancelled = false
                    });
                    continue;
                }

                if (item.DiagnosticService == null || !item.DiagnosticService.Price.HasValue || item.DiagnosticService.Price.Value <= 0)
                {
                    throw new BusinessException("FEE_NOT_CONFIGURED", $"Dịch vụ cận lâm sàng '{item.DiagnosticService?.Name ?? item.DiagnosticServiceId.ToString()}' chưa được cấu hình giá hợp lệ.");
                }

                itemsToCreate.Add(new InvoiceItem
                {
                    ItemCode = item.DiagnosticService.Code,
                    Description = $"Chỉ định CLS: {item.DiagnosticService.Name}",
                    Quantity = 1,
                    UnitPrice = item.DiagnosticService.Price.Value,
                    LineTotal = item.DiagnosticService.Price.Value,
                    ReferenceType = "DiagnosticItem",
                    ReferenceId = item.Id,
                    IsCancelled = false
                });
            }
        }

        // 3. Prescriptions
        var activePrescriptions = visit.Prescriptions
            .Where(p => p.Status == PrescriptionStatus.ReservedForPurchase || p.Status == PrescriptionStatus.Issued || p.Status == PrescriptionStatus.Dispensed)
            .ToList();

        foreach (var p in activePrescriptions)
        {
            foreach (var item in p.Items)
            {
                var refId = PrescriptionItemBillingReference.Encode(p.Id, item.MedicineId);
                var legacyRefId = p.Id * 100000L + item.MedicineId;
                if (alreadyBilledSet.Contains($"PrescriptionItem:v2:{refId}") ||
                    alreadyBilledSet.Contains($"PrescriptionItem:{refId}") ||
                    alreadyBilledSet.Contains($"PrescriptionItem:{legacyRefId}") ||
                    alreadyBilledSet.Contains($"PrescriptionItem:{p.Id}"))
                    continue;

                if (item.Medicine == null || !item.Medicine.UnitPrice.HasValue || item.Medicine.UnitPrice.Value <= 0)
                {
                    throw new BusinessException("FEE_NOT_CONFIGURED", $"Thuốc '{item.Medicine?.Name ?? item.MedicineId.ToString()}' chưa được cấu hình đơn giá hợp lệ.");
                }

                var lineTotal = item.Medicine.UnitPrice.Value * item.Quantity;
                itemsToCreate.Add(new InvoiceItem
                {
                    ItemCode = item.Medicine.Code,
                    Description = $"Thuốc: {item.Medicine.Name}",
                    Quantity = item.Quantity,
                    UnitPrice = item.Medicine.UnitPrice.Value,
                    LineTotal = lineTotal,
                    ReferenceType = PrescriptionItemBillingReference.ModernReferenceType,
                    ReferenceId = refId,
                    IsCancelled = false
                });
            }
        }

        if (itemsToCreate.Count == 0)
        {
            throw new BusinessException("NO_UNBILLED_CHARGES", "Tất cả các dịch vụ, cận lâm sàng và đơn thuốc của lượt khám này đã được lập hóa đơn.");
        }

        var subtotal = itemsToCreate.Sum(i => i.LineTotal);

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        var invoiceId = await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Older cancelled invoices may still hold the filtered unique charge index.
                // Release those rows in the same transaction as re-billing this visit.
                await _dbContext.InvoiceItems
                    .Where(ii => !ii.IsCancelled && ii.Invoice.Status == InvoiceStatus.Cancelled &&
                        (ii.Invoice.PatientVisitId == visitId ||
                         (visit.AppointmentId.HasValue && ii.Invoice.AppointmentId == visit.AppointmentId.Value)))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(ii => ii.IsCancelled, true), cancellationToken);

                var invoice = new Invoice
                {
                    InvoiceCode = GenerateInvoiceCode(),
                    PatientId = visit.PatientId,
                    SourceType = InvoiceSourceType.Appointment,
                    AppointmentId = visit.AppointmentId,
                    PatientVisitId = visit.Id,
                    Status = InvoiceStatus.Unpaid,
                    Subtotal = subtotal,
                    TotalAmount = subtotal,
                    CreatedByUserId = createdByUserId,
                    CreatedAtUtc = DateTime.UtcNow
                };

                foreach (var itm in itemsToCreate)
                {
                    invoice.Items.Add(itm);
                }

                _dbContext.Invoices.Add(invoice);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = createdByUserId,
                    Action = "CREATE_INVOICE",
                    EntityName = "Invoice",
                    EntityId = invoice.Id.ToString(),
                    Description = $"Lập hóa đơn tổng hợp #{invoice.InvoiceCode} cho lượt khám #{visit.VisitCode} ({ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)})",
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                if (visit.Patient.UserId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
                    {
                        UserId = visit.Patient.UserId.Value,
                        Type = NotificationType.Invoice,
                        Title = "Hóa đơn viện phí mới",
                        Message = $"Hóa đơn #{invoice.InvoiceCode} cho lượt khám #{visit.VisitCode} đã được lập với số tiền {ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)}.",
                        Route = "/patient/invoices",
                        RelatedEntityType = "Invoice",
                        RelatedEntityId = invoice.Id.ToString(),
                        DedupeKey = $"inv_created_{invoice.Id}"
                    }, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return invoice.Id;
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                _logger.LogWarning(exception, "Concurrent invoice creation conflict for visit {VisitId}.", visitId);
                throw new ConflictException("INVOICE_ITEM_ALREADY_BILLED", "Một hoặc nhiều khoản mục trong lượt khám này vừa được lập hóa đơn bởi yêu cầu khác.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });

        return await GetInvoiceDetailAsync(invoiceId, cancellationToken);
    }

    public async Task<InvoiceDetailDto> CreateInvoiceFromHealthPackageAsync(long registrationId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var registration = await _dbContext.HealthPackageRegistrations
            .Include(r => r.Patient)
            .Include(r => r.HealthPackage)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

        if (registration == null)
            throw new NotFoundException("Đăng ký gói khám không tồn tại.");

        if (registration.Status != HealthPackageRegistrationStatus.Confirmed)
            throw new BusinessException("INVALID_STATUS", "Chỉ có thể lập hóa đơn cho gói khám đã được xác nhận (Confirmed).");

        var existingActiveInvoice = await _dbContext.Invoices
            .AnyAsync(i => i.HealthPackageRegistrationId == registrationId && i.Status != InvoiceStatus.Cancelled, cancellationToken);

        if (existingActiveInvoice)
            throw new BusinessException("INVOICE_ALREADY_EXISTS", "Đăng ký gói khám này đã có hóa đơn đang hoạt động (chưa hủy).");

        var packagePrice = registration.HealthPackage.Price;
        if (packagePrice <= 0)
            throw new BusinessException("FEE_NOT_CONFIGURED", "Gói khám chưa được cấu hình mức giá hợp lệ. Vui lòng liên hệ quản trị viên.");

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        var invoiceId = await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var invoice = new Invoice
                {
                    InvoiceCode = GenerateInvoiceCode(),
                    PatientId = registration.PatientId,
                    SourceType = InvoiceSourceType.HealthPackageRegistration,
                    HealthPackageRegistrationId = registrationId,
                    Status = InvoiceStatus.Unpaid,
                    Subtotal = packagePrice,
                    TotalAmount = packagePrice,
                    CreatedByUserId = createdByUserId,
                    CreatedAtUtc = DateTime.UtcNow
                };

                invoice.Items.Add(new InvoiceItem
                {
                    ItemCode = registration.HealthPackage.Code,
                    Description = $"Gói khám sức khỏe: {registration.HealthPackage.Name}",
                    Quantity = 1,
                    UnitPrice = packagePrice,
                    LineTotal = packagePrice,
                    ReferenceType = "HealthPackage",
                    ReferenceId = registration.HealthPackageId
                });

                _dbContext.Invoices.Add(invoice);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = createdByUserId,
                    Action = "CREATE_INVOICE",
                    EntityName = "Invoice",
                    EntityId = invoice.Id.ToString(),
                    Description = $"Lập hóa đơn #{invoice.InvoiceCode} cho đăng ký gói #{registration.RegistrationCode} ({ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)})",
                    CreatedAt = _dateTimeProvider.UtcNow
                });

                if (registration.Patient.UserId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
                    {
                        UserId = registration.Patient.UserId.Value,
                        Type = NotificationType.Invoice,
                        Title = "Hóa đơn mới được tạo",
                        Message = $"Hóa đơn #{invoice.InvoiceCode} cho gói khám #{registration.RegistrationCode} đã được tạo với số tiền {ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)}.",
                        Route = "/patient/invoices",
                        RelatedEntityType = "Invoice",
                        RelatedEntityId = invoice.Id.ToString(),
                        DedupeKey = $"inv_created_{invoice.Id}"
                    }, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return invoice.Id;
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var duplicateExists = await _dbContext.Invoices.AsNoTracking()
                    .AnyAsync(i => i.HealthPackageRegistrationId == registrationId && i.Status != InvoiceStatus.Cancelled, cancellationToken);

                if (duplicateExists)
                {
                    _logger.LogWarning(exception, "Concurrent invoice creation was blocked for health-package registration {RegistrationId}.", registrationId);
                    throw new ConflictException("INVOICE_ALREADY_EXISTS", "Đăng ký gói khám này vừa được lập hóa đơn bởi một yêu cầu khác.");
                }

                throw;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });

        return await GetInvoiceDetailAsync(invoiceId, cancellationToken);
    }



    private async Task ReleaseCancelledInvoiceReservationsAsync(Invoice invoice, Guid actorUserId, CancellationToken cancellationToken)
    {
        var prescriptions = await _dbContext.Prescriptions
            .Where(p => p.PatientId == invoice.PatientId && p.Status == PrescriptionStatus.ReservedForPurchase)
            .ToListAsync(cancellationToken);
        var affected = prescriptions.Where(p => invoice.Items.Any(item =>
            PrescriptionItemBillingReference.MatchesPrescription(item.ReferenceType, item.ReferenceId, p.Id))).ToList();
        if (affected.Count == 0) return;

        // Exclude this invoice explicitly: its cancellation is still only tracked in memory.
        var otherReferences = await _dbContext.InvoiceItems.AsNoTracking()
            .Where(item => item.InvoiceId != invoice.Id && !item.IsCancelled && item.Invoice.Status != InvoiceStatus.Cancelled &&
                (item.ReferenceType == PrescriptionItemBillingReference.ModernReferenceType || item.ReferenceType == PrescriptionItemBillingReference.LegacyReferenceType))
            .Select(item => new { item.ReferenceType, item.ReferenceId }).ToListAsync(cancellationToken);
        var releasable = affected.Where(p => !otherReferences.Any(item =>
            PrescriptionItemBillingReference.MatchesPrescription(item.ReferenceType, item.ReferenceId, p.Id))).ToList();
        var prescriptionIds = releasable.Select(p => p.Id).ToList();
        var reservations = await _dbContext.MedicineStockTransactions
            .Where(t => t.PrescriptionId.HasValue && prescriptionIds.Contains(t.PrescriptionId.Value) &&
                (t.Type == MedicineStockTransactionType.Reservation || t.Type == MedicineStockTransactionType.ReservationCancelled))
            .Select(t => new { PrescriptionId = t.PrescriptionId!.Value, t.MedicineId }).Distinct().ToListAsync(cancellationToken);
        var medicineIds = reservations.Select(r => r.MedicineId).Distinct().ToList();
        var medicines = await _dbContext.Medicines.Where(m => medicineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var allocator = new MedicineBatchAllocator(_dbContext, _dateTimeProvider);
        foreach (var reservation in reservations)
        {
            await allocator.Return(medicines[reservation.MedicineId], reservation.PrescriptionId,
                $"Trả thuốc giữ chỗ do hủy hóa đơn #{invoice.InvoiceCode}", actorUserId, cancellationToken);
        }
        foreach (var prescription in releasable) prescription.Status = PrescriptionStatus.Issued;
    }

    public async Task<PagedResult<InvoiceDto>> GetReceptionInvoicesAsync(InvoiceFilterParams filters, CancellationToken cancellationToken = default)
    {
        var query = (await GetAccessibleInvoicesAsync(cancellationToken))
            .Include(i => i.Patient)
            .Include(i => i.Appointment)
            .Include(i => i.PatientVisit)
            .Include(i => i.HealthPackageRegistration)
            .AsNoTracking();

        if (filters.Status.HasValue)
        {
            query = query.Where(i => i.Status == filters.Status.Value);
        }

        if (filters.SourceType.HasValue)
        {
            query = query.Where(i => i.SourceType == filters.SourceType.Value);
        }

        if (filters.FromDate.HasValue)
        {
            var fromUtc = _dateTimeProvider.ConvertVietnamToUtc(filters.FromDate.Value.ToDateTime(TimeOnly.MinValue));
            query = query.Where(i => i.CreatedAtUtc >= fromUtc);
        }

        if (filters.ToDate.HasValue)
        {
            var toUtc = _dateTimeProvider.ConvertVietnamToUtc(filters.ToDate.Value.ToDateTime(TimeOnly.MaxValue));
            query = query.Where(i => i.CreatedAtUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(i =>
                i.InvoiceCode.Contains(search) ||
                (i.Appointment != null && i.Appointment.AppointmentCode.Contains(search)) ||
                (i.PatientVisit != null && i.PatientVisit.VisitCode.Contains(search)) ||
                (i.HealthPackageRegistration != null && i.HealthPackageRegistration.RegistrationCode.Contains(search)) ||
                (i.HealthPackageRegistration != null && i.HealthPackageRegistration.ContactPhone.Contains(search)) ||
                (i.Patient.FullName != null && i.Patient.FullName.Contains(search)) ||
                (i.Patient.PhoneNumber != null && i.Patient.PhoneNumber.Contains(search)) ||
                (i.Patient.MedicalRecordNumber != null && i.Patient.MedicalRecordNumber.Contains(search)) ||
                _dbContext.Users.Any(u => i.Patient.UserId != null && u.Id == i.Patient.UserId &&
                    ((u.FullName != null && u.FullName.Contains(search)) ||
                     (u.PhoneNumber != null && u.PhoneNumber.Contains(search)))));
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var page = filters.Page < 1 ? 1 : filters.Page;
        var pageSize = filters.PageSize < 1 ? 10 : Math.Min(filters.PageSize, 100);

        var items = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var patientUserIds = items.Where(i => i.Patient.UserId.HasValue).Select(i => i.Patient.UserId!.Value).Distinct().ToList();
        var staffUserIds = items.Select(i => i.CreatedByUserId)
            .Concat(items.Where(i => i.PaidByUserId.HasValue).Select(i => i.PaidByUserId!.Value))
            .Distinct().ToList();

        var allUserIds = patientUserIds.Concat(staffUserIds).Distinct().ToList();
        var userMap = await _dbContext.Users.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserInfo { FullName = u.FullName, PhoneNumber = u.PhoneNumber }, cancellationToken);

        var dtos = items.Select(i => MapToDto(i, userMap)).ToList();

        return new PagedResult<InvoiceDto>(dtos, totalItems, page, pageSize);
    }

    public async Task<InvoiceDetailDto> GetInvoiceDetailAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        if (_currentUserService.UserId is Guid userId && userId != Guid.Empty)
            await _facilityAuthService.ValidateInvoiceAccessAsync(userId, invoiceId, cancellationToken);
        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Appointment)
            .Include(i => i.PatientVisit)
            .Include(i => i.HealthPackageRegistration)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        var userIds = new List<Guid> { invoice.CreatedByUserId };
        if (invoice.Patient.UserId.HasValue) userIds.Add(invoice.Patient.UserId.Value);
        if (invoice.PaidByUserId.HasValue) userIds.Add(invoice.PaidByUserId.Value);
        userIds.AddRange(invoice.Payments.Select(p => p.ReceivedByUserId));

        var userMap = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserInfo { FullName = u.FullName, PhoneNumber = u.PhoneNumber }, cancellationToken);

        return MapToDetailDto(invoice, userMap);
    }

    public async Task<PagedResult<InvoiceDto>> GetPatientInvoicesAsync(long patientId, int page, int pageSize, InvoiceStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Appointment)
            .Include(i => i.PatientVisit)
            .Include(i => i.HealthPackageRegistration)
            .Where(i => i.PatientId == patientId)
            .AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var items = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var userIds = items.Where(i => i.Patient.UserId.HasValue).Select(i => i.Patient.UserId!.Value)
            .Concat(items.Select(i => i.CreatedByUserId))
            .Concat(items.Where(i => i.PaidByUserId.HasValue).Select(i => i.PaidByUserId!.Value))
            .Distinct().ToList();

        var userMap = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserInfo { FullName = u.FullName, PhoneNumber = u.PhoneNumber }, cancellationToken);

        var dtos = items.Select(i => MapToDto(i, userMap)).ToList();

        return new PagedResult<InvoiceDto>(dtos, totalItems, page, pageSize);
    }

    public async Task<InvoiceDetailDto> GetPatientInvoiceDetailAsync(long invoiceId, long patientId, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Appointment)
            .Include(i => i.PatientVisit)
            .Include(i => i.HealthPackageRegistration)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null || invoice.PatientId != patientId)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        var userIds = new List<Guid> { invoice.CreatedByUserId };
        if (invoice.Patient.UserId.HasValue) userIds.Add(invoice.Patient.UserId.Value);
        if (invoice.PaidByUserId.HasValue) userIds.Add(invoice.PaidByUserId.Value);
        userIds.AddRange(invoice.Payments.Select(p => p.ReceivedByUserId));

        var userMap = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserInfo { FullName = u.FullName, PhoneNumber = u.PhoneNumber }, cancellationToken);

        return MapToDetailDto(invoice, userMap);
    }

    public async Task<BillingKpiDto> GetTodayKpiAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await GetAccessibleInvoicesAsync(cancellationToken);
        var todayVn = _dateTimeProvider.VietnamToday;
        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(todayVn.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(todayVn.ToDateTime(TimeOnly.MaxValue));

        var unpaidCount = await invoices
            .CountAsync(i => i.Status == InvoiceStatus.Unpaid, cancellationToken);

        var paidCount = await invoices
            .CountAsync(i => i.Status == InvoiceStatus.Paid && i.PaidAtUtc.HasValue && i.PaidAtUtc >= startUtc && i.PaidAtUtc <= endUtc, cancellationToken);

        var cancelledCount = await invoices
            .CountAsync(i => i.Status == InvoiceStatus.Cancelled && i.CancelledAtUtc.HasValue && i.CancelledAtUtc >= startUtc && i.CancelledAtUtc <= endUtc, cancellationToken);

        var todayRevenue = await _dbContext.Payments
            .Where(p => invoices.Any(i => i.Id == p.InvoiceId))
            .Where(p => p.Status == PaymentStatus.Succeeded && p.ReceivedAtUtc >= startUtc && p.ReceivedAtUtc <= endUtc)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new BillingKpiDto
        {
            TodayUnpaidInvoices = unpaidCount,
            TodayPaidInvoices = paidCount,
            TodayCancelledInvoices = cancelledCount,
            TodayRevenue = todayRevenue
        };
    }

    public async Task<RevenueReportDto> GetRevenueReportAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        var invoices = await GetAccessibleInvoicesAsync(cancellationToken);
        var end = toDate ?? _dateTimeProvider.VietnamToday;
        var start = fromDate ?? end.AddDays(-29);

        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end.DayNumber - start.DayNumber > 365)
            throw new BusinessException("DATE_RANGE_TOO_LARGE", "Khoảng thời gian báo cáo không được vượt quá 366 ngày.");

        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(start.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(end.ToDateTime(TimeOnly.MaxValue));

        var succeededPayments = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => invoices.Any(i => i.Id == p.InvoiceId))
            .Where(p => p.Status == PaymentStatus.Succeeded && p.ReceivedAtUtc >= startUtc && p.ReceivedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

        var totalRevenue = succeededPayments.Sum(p => p.Amount);
        var totalTransactions = succeededPayments.Count;

        var dailyMap = succeededPayments
            .GroupBy(p => DateOnly.FromDateTime(_dateTimeProvider.ConvertUtcToVietnam(p.ReceivedAtUtc)))
            .ToDictionary(g => g.Key, g => new
            {
                Revenue = g.Sum(x => x.Amount),
                Count = g.Count()
            });

        var dailyBreakdown = new List<DailyRevenueDto>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            dailyMap.TryGetValue(d, out var stats);
            dailyBreakdown.Add(new DailyRevenueDto
            {
                Date = d,
                Revenue = stats?.Revenue ?? 0m,
                SucceededPaymentsCount = stats?.Count ?? 0,
                PaidInvoicesCount = stats?.Count ?? 0
            });
        }

        // Status breakdown of invoices created in this range
        var invoicesInRange = await invoices
            .AsNoTracking()
            .Where(i => i.CreatedAtUtc >= startUtc && i.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

        var unpaid = invoicesInRange.Where(i => i.Status == InvoiceStatus.Unpaid).ToList();
        var paid = invoicesInRange.Where(i => i.Status == InvoiceStatus.Paid).ToList();
        var cancelled = invoicesInRange.Where(i => i.Status == InvoiceStatus.Cancelled).ToList();

        return new RevenueReportDto
        {
            FromDate = start,
            ToDate = end,
            TotalRevenue = totalRevenue,
            TotalSucceededTransactions = totalTransactions,
            DailyBreakdown = dailyBreakdown,
            StatusBreakdown = new InvoiceStatusBreakdownDto
            {
                UnpaidCount = unpaid.Count,
                UnpaidAmount = unpaid.Sum(x => x.TotalAmount),
                PaidCount = paid.Count,
                PaidAmount = paid.Sum(x => x.TotalAmount),
                CancelledCount = cancelled.Count,
                CancelledAmount = cancelled.Sum(x => x.TotalAmount)
            }
        };
    }

    public async Task<List<SpecialtyFeeDto>> GetSpecialtyFeesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Specialties
            .AsNoTracking()
            .OrderBy(s => s.SpecialtyCode)
            .Select(s => new SpecialtyFeeDto
            {
                Id = s.Id,
                SpecialtyCode = s.SpecialtyCode,
                Name = s.Name,
                ConsultationFee = s.ConsultationFee,
                IsActive = s.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SpecialtyFeeDto> UpdateSpecialtyFeeAsync(long specialtyId, decimal fee, Guid updatedByUserId, CancellationToken cancellationToken = default)
    {
        if (fee < 0)
            throw new BusinessException("INVALID_FEE", "Mức phí khám không được âm.");

        var specialty = await _dbContext.Specialties.FirstOrDefaultAsync(s => s.Id == specialtyId, cancellationToken);
        if (specialty == null)
            throw new NotFoundException("Chuyên khoa không tồn tại.");

        var previousFee = specialty.ConsultationFee;
        specialty.ConsultationFee = fee;

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = updatedByUserId,
            Action = "UPDATE_SPECIALTY_FEE",
            EntityName = "Specialty",
            EntityId = specialty.Id.ToString(),
            Description = $"Cập nhật phí {specialty.SpecialtyCode} từ {ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(previousFee)} thành {ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(fee)}",
            CreatedAt = _dateTimeProvider.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SpecialtyFeeDto
        {
            Id = specialty.Id,
            SpecialtyCode = specialty.SpecialtyCode,
            Name = specialty.Name,
            ConsultationFee = specialty.ConsultationFee,
            IsActive = specialty.IsActive
        };
    }

    private async Task<IQueryable<Invoice>> GetAccessibleInvoicesAsync(CancellationToken cancellationToken)
    {
        var query = _dbContext.Invoices.AsQueryable();
        var userId = _currentUserService.UserId ?? Guid.Empty;
        // Background seed/service calls have no HTTP actor. HTTP endpoints require authentication.
        if (userId == Guid.Empty || await _facilityAuthService.HasFullFacilityAccessAsync(userId, cancellationToken)) return query;
        var facilities = await _facilityAuthService.GetUserAccessibleFacilityIdsAsync(userId, cancellationToken);
        return query.Where(i => i.PatientVisit != null
            ? facilities.Contains(i.PatientVisit.FacilityId)
            : i.Appointment != null && i.Appointment.FacilityId.HasValue
                ? facilities.Contains(i.Appointment.FacilityId.Value)
                : true); // Preserve package/legacy invoices with no determinable facility.
    }

    private static void ValidatePayableInvoice(Invoice invoice, decimal amount)
    {
        if (invoice.Status == InvoiceStatus.Paid)
            throw new BusinessException("ALREADY_PAID", "Hóa đơn này đã được thanh toán.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new BusinessException("INVOICE_CANCELLED", "Không thể thanh toán hóa đơn đã bị hủy.");

        if (invoice.Status != InvoiceStatus.Unpaid)
            throw new BusinessException("INVALID_STATUS", "Trạng thái hóa đơn không hợp lệ để thanh toán.");

        if (amount <= 0 || amount != invoice.TotalAmount)
            throw new BusinessException("INVALID_AMOUNT", $"Số tiền thanh toán ({ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(amount)}) phải chính xác bằng tổng tiền hóa đơn ({ClinicManagement.Infrastructure.Common.BusinessDisplay.Money(invoice.TotalAmount)}).");
    }

    private string GenerateInvoiceCode()
    {
        return ClinicManagement.Infrastructure.Common.BusinessDisplay.RandomCode("INV", _dateTimeProvider.VietnamToday, 8);
    }

    private string GeneratePaymentCode()
    {
        return ClinicManagement.Infrastructure.Common.BusinessDisplay.RandomCode("PAY", _dateTimeProvider.VietnamToday, 8);
    }

    private static string FormatStatus(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Unpaid => "Chưa thanh toán",
        InvoiceStatus.Paid => "Đã thanh toán",
        InvoiceStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };

    private class UserInfo
    {
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
    }
}
