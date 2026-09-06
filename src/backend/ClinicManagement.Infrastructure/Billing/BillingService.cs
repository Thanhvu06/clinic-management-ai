using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Billing.DTOs;
using ClinicManagement.Application.Billing.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Billing;

public class BillingService : IBillingService
{
    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BillingService> _logger;

    public BillingService(
        AppDbContext dbContext,
        INotificationService notificationService,
        IDateTimeProvider dateTimeProvider,
        ILogger<BillingService> logger)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<InvoiceDetailDto> CreateInvoiceFromAppointmentAsync(long appointmentId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Specialty)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment == null)
            throw new NotFoundException("Lịch khám không tồn tại.");

        if (appointment.Status != AppointmentStatus.Completed)
            throw new BusinessException("INVALID_STATUS", "Chỉ có thể lập hóa đơn cho ca khám đã hoàn tất (Completed).");

        var existingActiveInvoice = await _dbContext.Invoices
            .AnyAsync(i => i.AppointmentId == appointmentId && i.Status != InvoiceStatus.Cancelled, cancellationToken);

        if (existingActiveInvoice)
            throw new BusinessException("INVOICE_ALREADY_EXISTS", "Lịch khám này đã có hóa đơn đang hoạt động (chưa hủy).");

        var patientUser = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == appointment.Patient.UserId, cancellationToken);

        var consultationFee = appointment.Specialty.ConsultationFee;
        var invoiceCode = GenerateInvoiceCode();

        var invoice = new Invoice
        {
            InvoiceCode = invoiceCode,
            PatientId = appointment.PatientId,
            SourceType = InvoiceSourceType.Appointment,
            AppointmentId = appointmentId,
            Status = InvoiceStatus.Unpaid,
            Subtotal = consultationFee,
            TotalAmount = consultationFee,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var item = new InvoiceItem
        {
            ItemCode = appointment.Specialty.SpecialtyCode,
            Description = $"Khám chuyên khoa: {appointment.Specialty.Name}",
            Quantity = 1,
            UnitPrice = consultationFee,
            LineTotal = consultationFee,
            ReferenceType = "Specialty",
            ReferenceId = appointment.SpecialtyId
        };

        invoice.Items.Add(item);

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = createdByUserId,
            Action = "CREATE_INVOICE",
            EntityName = "Invoice",
            EntityId = invoice.Id.ToString(),
            Description = $"Lập hóa đơn #{invoice.InvoiceCode} cho lịch hẹn #{appointment.AppointmentCode} ({invoice.TotalAmount:N0}đ)",
            CreatedAt = _dateTimeProvider.VietnamNow
        });

        await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = appointment.Patient.UserId,
            Type = NotificationType.Invoice,
            Title = "Hóa đơn mới được tạo",
            Message = $"Hóa đơn #{invoice.InvoiceCode} cho lịch khám #{appointment.AppointmentCode} đã được tạo với số tiền {invoice.TotalAmount:N0}đ.",
            Route = "/patient/invoices",
            RelatedEntityType = "Invoice",
            RelatedEntityId = invoice.Id.ToString(),
            DedupeKey = $"inv_created_{invoice.Id}"
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetInvoiceDetailAsync(invoice.Id, cancellationToken);
    }

    public async Task<InvoiceDetailDto> CreateInvoiceFromHealthPackageAsync(long registrationId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var registration = await _dbContext.HealthPackageRegistrations
            .Include(r => r.Patient)
            .Include(r => r.HealthPackage)
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
        var invoiceCode = GenerateInvoiceCode();

        var invoice = new Invoice
        {
            InvoiceCode = invoiceCode,
            PatientId = registration.PatientId,
            SourceType = InvoiceSourceType.HealthPackageRegistration,
            HealthPackageRegistrationId = registrationId,
            Status = InvoiceStatus.Unpaid,
            Subtotal = packagePrice,
            TotalAmount = packagePrice,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var item = new InvoiceItem
        {
            ItemCode = registration.HealthPackage.Code,
            Description = $"Gói khám sức khỏe: {registration.HealthPackage.Name}",
            Quantity = 1,
            UnitPrice = packagePrice,
            LineTotal = packagePrice,
            ReferenceType = "HealthPackage",
            ReferenceId = registration.HealthPackageId
        };

        invoice.Items.Add(item);

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = createdByUserId,
            Action = "CREATE_INVOICE",
            EntityName = "Invoice",
            EntityId = invoice.Id.ToString(),
            Description = $"Lập hóa đơn #{invoice.InvoiceCode} cho đăng ký gói #{registration.RegistrationCode} ({invoice.TotalAmount:N0}đ)",
            CreatedAt = _dateTimeProvider.VietnamNow
        });

        await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = registration.Patient.UserId,
            Type = NotificationType.Invoice,
            Title = "Hóa đơn mới được tạo",
            Message = $"Hóa đơn #{invoice.InvoiceCode} cho gói khám #{registration.RegistrationCode} đã được tạo với số tiền {invoice.TotalAmount:N0}đ.",
            Route = "/patient/invoices",
            RelatedEntityType = "Invoice",
            RelatedEntityId = invoice.Id.ToString(),
            DedupeKey = $"inv_created_{invoice.Id}"
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetInvoiceDetailAsync(invoice.Id, cancellationToken);
    }

    public async Task<PaymentDto> ProcessPaymentAsync(long invoiceId, ProcessPaymentRequest request, Guid receivedByUserId, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        if (invoice.Status == InvoiceStatus.Paid)
            throw new BusinessException("ALREADY_PAID", "Hóa đơn này đã được thanh toán.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new BusinessException("INVOICE_CANCELLED", "Không thể thanh toán hóa đơn đã bị hủy.");

        if (invoice.Status != InvoiceStatus.Unpaid)
            throw new BusinessException("INVALID_STATUS", "Trạng thái hóa đơn không hợp lệ để thanh toán.");

        if (request.Amount != invoice.TotalAmount)
            throw new BusinessException("INVALID_AMOUNT", $"Số tiền thanh toán ({request.Amount:N0}đ) phải chính xác bằng tổng tiền hóa đơn ({invoice.TotalAmount:N0}đ).");

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Re-fetch with tracking inside transaction to avoid race conditions
                var lockedInvoice = await _dbContext.Invoices
                    .Include(i => i.Patient)
                    .Include(i => i.Payments)
                    .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

                if (lockedInvoice == null || lockedInvoice.Status != InvoiceStatus.Unpaid)
                    throw new BusinessException("ALREADY_PAID", "Hóa đơn này đã được thanh toán hoặc không còn hiệu lực.");

                var paymentCode = GeneratePaymentCode();

                lockedInvoice.Status = InvoiceStatus.Paid;
                lockedInvoice.PaidByUserId = receivedByUserId;
                lockedInvoice.PaidAtUtc = DateTime.UtcNow;

                var payment = new Payment
                {
                    PaymentCode = paymentCode,
                    InvoiceId = lockedInvoice.Id,
                    Amount = request.Amount,
                    Method = request.Method,
                    ReferenceCode = request.ReferenceCode?.Trim(),
                    Note = request.Note?.Trim(),
                    ReceivedByUserId = receivedByUserId,
                    ReceivedAtUtc = DateTime.UtcNow,
                    Status = PaymentStatus.Succeeded
                };

                _dbContext.Payments.Add(payment);

                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = receivedByUserId,
                    Action = "PROCESS_PAYMENT",
                    EntityName = "Payment",
                    EntityId = paymentCode,
                    Description = $"Thu tiền hóa đơn #{lockedInvoice.InvoiceCode}: {payment.Amount:N0}đ ({payment.Method})",
                    CreatedAt = _dateTimeProvider.VietnamNow
                });

                var methodLabel = payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";
                await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
                {
                    UserId = lockedInvoice.Patient.UserId,
                    Type = NotificationType.Payment,
                    Title = "Thanh toán thành công",
                    Message = $"Hóa đơn #{lockedInvoice.InvoiceCode} đã được thanh toán thành công ({payment.Amount:N0}đ qua {methodLabel}).",
                    Route = "/patient/invoices",
                    RelatedEntityType = "Invoice",
                    RelatedEntityId = lockedInvoice.Id.ToString(),
                    DedupeKey = $"pay_success_{paymentCode}"
                }, cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var receiver = await _dbContext.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == receivedByUserId, cancellationToken);

                return new PaymentDto
                {
                    Id = payment.Id,
                    PaymentCode = payment.PaymentCode,
                    InvoiceId = payment.InvoiceId,
                    InvoiceCode = lockedInvoice.InvoiceCode,
                    Amount = payment.Amount,
                    Method = payment.Method,
                    MethodName = payment.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản",
                    ReferenceCode = payment.ReferenceCode,
                    Note = payment.Note,
                    ReceivedByUserId = payment.ReceivedByUserId,
                    ReceivedByUserName = receiver?.FullName,
                    ReceivedAtUtc = payment.ReceivedAtUtc,
                    Status = payment.Status,
                    StatusName = "Thành công"
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<InvoiceDetailDto> CancelInvoiceAsync(long invoiceId, string reason, Guid cancelledByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("REASON_REQUIRED", "Vui lòng nhập lý do hủy hóa đơn.");

        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        if (invoice.Status == InvoiceStatus.Paid)
            throw new BusinessException("CANNOT_CANCEL_PAID", "Không thể hủy hóa đơn đã thanh toán.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new BusinessException("ALREADY_CANCELLED", "Hóa đơn này đã bị hủy trước đó.");

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancelledAtUtc = DateTime.UtcNow;
        invoice.CancellationReason = reason.Trim();

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = cancelledByUserId,
            Action = "CANCEL_INVOICE",
            EntityName = "Invoice",
            EntityId = invoice.Id.ToString(),
            Description = $"Hủy hóa đơn #{invoice.InvoiceCode}. Lý do: {invoice.CancellationReason}",
            CreatedAt = _dateTimeProvider.VietnamNow
        });

        await _notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = invoice.Patient.UserId,
            Type = NotificationType.Invoice,
            Title = "Hóa đơn đã bị hủy",
            Message = $"Hóa đơn #{invoice.InvoiceCode} đã bị hủy. Lý do: {invoice.CancellationReason}.",
            Route = "/patient/invoices",
            RelatedEntityType = "Invoice",
            RelatedEntityId = invoice.Id.ToString(),
            DedupeKey = $"inv_cancelled_{invoice.Id}"
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetInvoiceDetailAsync(invoice.Id, cancellationToken);
    }

    public async Task<PagedResult<InvoiceDto>> GetReceptionInvoicesAsync(InvoiceFilterParams filters, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Appointment)
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
            var fromUtc = filters.FromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(i => i.CreatedAtUtc >= fromUtc);
        }

        if (filters.ToDate.HasValue)
        {
            var toUtc = filters.ToDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(i => i.CreatedAtUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(i =>
                i.InvoiceCode.Contains(search) ||
                (i.Appointment != null && i.Appointment.AppointmentCode.Contains(search)) ||
                (i.HealthPackageRegistration != null && i.HealthPackageRegistration.RegistrationCode.Contains(search)) ||
                (i.HealthPackageRegistration != null && i.HealthPackageRegistration.ContactPhone.Contains(search)));
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var page = filters.Page < 1 ? 1 : filters.Page;
        var pageSize = filters.PageSize < 1 ? 10 : filters.PageSize;

        var items = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var patientUserIds = items.Select(i => i.Patient.UserId).Distinct().ToList();
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
        var invoice = await _dbContext.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Appointment)
            .Include(i => i.HealthPackageRegistration)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        var userIds = new List<Guid> { invoice.Patient.UserId, invoice.CreatedByUserId };
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
            .Include(i => i.HealthPackageRegistration)
            .Where(i => i.PatientId == patientId)
            .AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : pageSize;

        var items = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var userIds = items.Select(i => i.Patient.UserId)
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
            .Include(i => i.HealthPackageRegistration)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null || invoice.PatientId != patientId)
            throw new NotFoundException("Hóa đơn không tồn tại.");

        var userIds = new List<Guid> { invoice.Patient.UserId, invoice.CreatedByUserId };
        if (invoice.PaidByUserId.HasValue) userIds.Add(invoice.PaidByUserId.Value);
        userIds.AddRange(invoice.Payments.Select(p => p.ReceivedByUserId));

        var userMap = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserInfo { FullName = u.FullName, PhoneNumber = u.PhoneNumber }, cancellationToken);

        return MapToDetailDto(invoice, userMap);
    }

    public async Task<BillingKpiDto> GetTodayKpiAsync(CancellationToken cancellationToken = default)
    {
        var todayVn = _dateTimeProvider.VietnamToday;
        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(todayVn.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(todayVn.ToDateTime(TimeOnly.MaxValue));

        var unpaidCount = await _dbContext.Invoices
            .CountAsync(i => i.Status == InvoiceStatus.Unpaid && i.CreatedAtUtc >= startUtc && i.CreatedAtUtc <= endUtc, cancellationToken);

        var paidCount = await _dbContext.Invoices
            .CountAsync(i => i.Status == InvoiceStatus.Paid && i.PaidAtUtc.HasValue && i.PaidAtUtc >= startUtc && i.PaidAtUtc <= endUtc, cancellationToken);

        var cancelledCount = await _dbContext.Invoices
            .CountAsync(i => i.Status == InvoiceStatus.Cancelled && i.CancelledAtUtc.HasValue && i.CancelledAtUtc >= startUtc && i.CancelledAtUtc <= endUtc, cancellationToken);

        var todayRevenue = await _dbContext.Payments
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
        var end = toDate ?? _dateTimeProvider.VietnamToday;
        var start = fromDate ?? end.AddDays(-29);

        if (end < start)
        {
            (start, end) = (end, start);
        }

        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(start.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(end.ToDateTime(TimeOnly.MaxValue));

        var succeededPayments = await _dbContext.Payments
            .AsNoTracking()
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
        var invoicesInRange = await _dbContext.Invoices
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

    public async Task<SpecialtyFeeDto> UpdateSpecialtyFeeAsync(long specialtyId, decimal fee, CancellationToken cancellationToken = default)
    {
        if (fee < 0)
            throw new BusinessException("INVALID_FEE", "Mức phí khám không được âm.");

        var specialty = await _dbContext.Specialties.FirstOrDefaultAsync(s => s.Id == specialtyId, cancellationToken);
        if (specialty == null)
            throw new NotFoundException("Chuyên khoa không tồn tại.");

        specialty.ConsultationFee = fee;
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

    private string GenerateInvoiceCode()
    {
        return $"INV-{_dateTimeProvider.VietnamNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..7].ToUpper()}";
    }

    private string GeneratePaymentCode()
    {
        return $"PAY-{_dateTimeProvider.VietnamNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..7].ToUpper()}";
    }

    private static InvoiceDto MapToDto(Invoice i, Dictionary<Guid, UserInfo> userMap)
    {
        userMap.TryGetValue(i.Patient.UserId, out var patUser);
        userMap.TryGetValue(i.CreatedByUserId, out var creatorUser);
        UserInfo? payerUser = null;
        if (i.PaidByUserId.HasValue) userMap.TryGetValue(i.PaidByUserId.Value, out payerUser);

        return new InvoiceDto
        {
            Id = i.Id,
            InvoiceCode = i.InvoiceCode,
            PatientId = i.PatientId,
            PatientName = patUser?.FullName ?? string.Empty,
            PatientPhone = patUser?.PhoneNumber ?? (i.HealthPackageRegistration?.ContactPhone ?? string.Empty),
            SourceType = i.SourceType,
            SourceTypeName = i.SourceType == InvoiceSourceType.Appointment ? "Khám bệnh" : "Gói khám sức khỏe",
            AppointmentId = i.AppointmentId,
            AppointmentCode = i.Appointment?.AppointmentCode,
            HealthPackageRegistrationId = i.HealthPackageRegistrationId,
            RegistrationCode = i.HealthPackageRegistration?.RegistrationCode,
            Status = i.Status,
            StatusName = FormatStatus(i.Status),
            Subtotal = i.Subtotal,
            TotalAmount = i.TotalAmount,
            CreatedAtUtc = i.CreatedAtUtc,
            PaidAtUtc = i.PaidAtUtc,
            CancelledAtUtc = i.CancelledAtUtc,
            CancellationReason = i.CancellationReason,
            CreatedByUserName = creatorUser?.FullName,
            PaidByUserName = payerUser?.FullName
        };
    }

    private static InvoiceDetailDto MapToDetailDto(Invoice i, Dictionary<Guid, UserInfo> userMap)
    {
        var baseDto = MapToDto(i, userMap);

        var items = i.Items.Select(item => new InvoiceItemDto
        {
            Id = item.Id,
            InvoiceId = item.InvoiceId,
            ItemCode = item.ItemCode,
            Description = item.Description,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            LineTotal = item.LineTotal,
            ReferenceType = item.ReferenceType,
            ReferenceId = item.ReferenceId
        }).ToList();

        var payments = i.Payments.Select(p =>
        {
            userMap.TryGetValue(p.ReceivedByUserId, out var receiver);
            return new PaymentDto
            {
                Id = p.Id,
                PaymentCode = p.PaymentCode,
                InvoiceId = p.InvoiceId,
                InvoiceCode = i.InvoiceCode,
                Amount = p.Amount,
                Method = p.Method,
                MethodName = p.Method == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản",
                ReferenceCode = p.ReferenceCode,
                Note = p.Note,
                ReceivedByUserId = p.ReceivedByUserId,
                ReceivedByUserName = receiver?.FullName,
                ReceivedAtUtc = p.ReceivedAtUtc,
                Status = p.Status,
                StatusName = p.Status == PaymentStatus.Succeeded ? "Thành công" : "Đã hủy"
            };
        }).ToList();

        return new InvoiceDetailDto
        {
            Id = baseDto.Id,
            InvoiceCode = baseDto.InvoiceCode,
            PatientId = baseDto.PatientId,
            PatientName = baseDto.PatientName,
            PatientPhone = baseDto.PatientPhone,
            SourceType = baseDto.SourceType,
            SourceTypeName = baseDto.SourceTypeName,
            AppointmentId = baseDto.AppointmentId,
            AppointmentCode = baseDto.AppointmentCode,
            HealthPackageRegistrationId = baseDto.HealthPackageRegistrationId,
            RegistrationCode = baseDto.RegistrationCode,
            Status = baseDto.Status,
            StatusName = baseDto.StatusName,
            Subtotal = baseDto.Subtotal,
            TotalAmount = baseDto.TotalAmount,
            CreatedAtUtc = baseDto.CreatedAtUtc,
            PaidAtUtc = baseDto.PaidAtUtc,
            CancelledAtUtc = baseDto.CancelledAtUtc,
            CancellationReason = baseDto.CancellationReason,
            CreatedByUserName = baseDto.CreatedByUserName,
            PaidByUserName = baseDto.PaidByUserName,
            Items = items,
            Payments = payments
        };
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
