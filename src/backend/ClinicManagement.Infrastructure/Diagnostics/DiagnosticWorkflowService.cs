using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Policies;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Diagnostics;

public class DiagnosticWorkflowService : IDiagnosticWorkflowService
{
    private readonly AppDbContext _dbContext;
    private readonly IDoctorContextService _doctorContextService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<DiagnosticWorkflowService> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly DiagnosticTatOptions _tatOptions;

    public DiagnosticWorkflowService(
        AppDbContext dbContext,
        IDoctorContextService doctorContextService,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        ILogger<DiagnosticWorkflowService> logger,
        IHttpContextAccessor? httpContextAccessor = null,
        IOptions<DiagnosticTatOptions>? tatOptions = null)
    {
        _dbContext = dbContext;
        _doctorContextService = doctorContextService;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tatOptions = tatOptions?.Value ?? new DiagnosticTatOptions();
    }

    private Guid GetUserId()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");
        return currentUserId.Value;
    }

    private async Task EnsureTechnicianFacilityScopeAsync(DiagnosticOrder order)
    {
        var userId = GetUserId();
        if (!TryResolveEffectiveFacility(order, out var facilityId) ||
            !await _dbContext.StaffFacilityAssignments.AnyAsync(x =>
                x.UserId == userId && x.IsActive && x.Role == RoleNames.DiagnosticTechnician &&
                x.FacilityId == facilityId))
        {
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");
        }
    }

    private Task<Doctor> GetCurrentDoctorAsync()
    {
        return _doctorContextService.GetCurrentActiveDoctorAsync();
    }

    private async Task<long> ResolveAppointmentFacilityAsync(Doctor doctor, Appointment appointment, long? explicitFacilityId)
    {
        var persistedFacilityIds = new List<long>();
        if (appointment.FacilityId.HasValue)
            persistedFacilityIds.Add(appointment.FacilityId.Value);

        if (appointment.PatientVisit != null)
        {
            persistedFacilityIds.Add(appointment.PatientVisit.FacilityId);

            if (appointment.PatientVisit.AppointmentId.HasValue && appointment.PatientVisit.Appointment == null)
                throw new BusinessException("FACILITY_SCOPE_DENIED", "Không thể xác minh cơ sở của lượt khám liên kết.");

            if (appointment.PatientVisit.Appointment?.FacilityId is long visitAppointmentFacilityId)
                persistedFacilityIds.Add(visitAppointmentFacilityId);
        }

        var distinctPersistedFacilityIds = persistedFacilityIds.Distinct().ToList();
        if (distinctPersistedFacilityIds.Count > 1)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Các nguồn cơ sở của lịch hẹn không nhất quán.");

        long resolvedFacilityId;
        if (explicitFacilityId.HasValue)
        {
            if (distinctPersistedFacilityIds.Count == 1 && distinctPersistedFacilityIds[0] != explicitFacilityId.Value)
                throw new BusinessException("FACILITY_SCOPE_DENIED", "Lịch hẹn không thuộc cơ sở chỉ định.");

            resolvedFacilityId = explicitFacilityId.Value;
        }
        else if (distinctPersistedFacilityIds.Count == 1)
        {
            resolvedFacilityId = distinctPersistedFacilityIds[0];
        }
        else
        {
            var assignmentFacilityIds = await _dbContext.StaffFacilityAssignments
                .AsNoTracking()
                .Where(x => x.UserId == doctor.UserId && x.IsActive && x.Role == RoleNames.Doctor)
                .Select(x => x.FacilityId)
                .Distinct()
                .Take(2)
                .ToListAsync();

            if (assignmentFacilityIds.Count != 1)
                throw new BusinessException("FACILITY_SCOPE_DENIED", "Không thể xác định duy nhất cơ sở của lịch hẹn.");

            resolvedFacilityId = assignmentFacilityIds[0];
        }

        var hasExactDoctorAssignment = await _dbContext.StaffFacilityAssignments
            .AsNoTracking()
            .AnyAsync(x => x.UserId == doctor.UserId && x.IsActive && x.Role == RoleNames.Doctor && x.FacilityId == resolvedFacilityId);

        if (!hasExactDoctorAssignment)
            throw new BusinessException("FACILITY_SCOPE_DENIED", "Bác sĩ không được phân quyền tại cơ sở của lịch hẹn.");

        return resolvedFacilityId;
    }

    private async Task<long> ResolveVisitFacilityAsync(Doctor doctor, PatientVisit visit, long? explicitFacilityId)
    {
        return await VisitDoctorAuthorizationGuard.ResolveFacilityAsync(_dbContext, doctor, visit, explicitFacilityId);
    }

    private static bool TryResolveEffectiveFacility(DiagnosticOrder order, out long facilityId)
    {
        if (order.PatientVisitId.HasValue && order.PatientVisit == null)
        {
            facilityId = default;
            return false;
        }

        if (order.AppointmentId.HasValue && order.Appointment == null)
        {
            facilityId = default;
            return false;
        }

        if (order.PatientVisit?.AppointmentId.HasValue == true && order.PatientVisit.Appointment == null)
        {
            facilityId = default;
            return false;
        }

        var persistedFacilityIds = new List<long>();
        if (order.FacilityId.HasValue)
            persistedFacilityIds.Add(order.FacilityId.Value);
        if (order.PatientVisit != null)
            persistedFacilityIds.Add(order.PatientVisit.FacilityId);
        if (order.Appointment?.FacilityId is long appointmentFacilityId)
            persistedFacilityIds.Add(appointmentFacilityId);
        if (order.PatientVisit?.Appointment?.FacilityId is long visitAppointmentFacilityId)
            persistedFacilityIds.Add(visitAppointmentFacilityId);

        var distinctPersistedFacilityIds = persistedFacilityIds.Distinct().ToList();
        if (distinctPersistedFacilityIds.Count != 1)
        {
            facilityId = default;
            return false;
        }

        facilityId = distinctPersistedFacilityIds[0];
        return true;
    }

    private async Task LoadLinkedVisitAppointmentAsync(DiagnosticOrder order)
    {
        if (order.PatientVisit?.AppointmentId is not long appointmentId || order.PatientVisit.Appointment != null)
            return;

        if (_dbContext.Entry(order).State == EntityState.Detached)
        {
            order.PatientVisit.Appointment = await _dbContext.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appointmentId);
        }
        else
        {
            await _dbContext.Entry(order.PatientVisit)
                .Reference(v => v.Appointment)
                .LoadAsync();
        }
    }

    private static IQueryable<DiagnosticOrder> WhereTechnicianFacilityScope(
        IQueryable<DiagnosticOrder> query,
        IReadOnlyCollection<long> facilityIds)
    {
        return query.Where(o =>
            // Explicit order facility is authoritative, but every linked persisted
            // facility source must be present (when referenced) and agree with it.
            (o.FacilityId.HasValue &&
             (!o.PatientVisitId.HasValue ||
              (o.PatientVisit != null &&
               o.PatientVisit.FacilityId == o.FacilityId &&
               (!o.PatientVisit.AppointmentId.HasValue ||
                (o.PatientVisit.Appointment != null &&
                 (!o.PatientVisit.Appointment.FacilityId.HasValue ||
                  o.PatientVisit.Appointment.FacilityId == o.FacilityId))))) &&
             (!o.AppointmentId.HasValue ||
              (o.Appointment != null &&
               (!o.Appointment.FacilityId.HasValue || o.Appointment.FacilityId == o.FacilityId))) &&
             facilityIds.Contains(o.FacilityId.Value)) ||
            // Legacy order: linked visit facility is the next authority.
            (!o.FacilityId.HasValue && o.PatientVisitId.HasValue && o.PatientVisit != null &&
             (!o.PatientVisit.AppointmentId.HasValue ||
              (o.PatientVisit.Appointment != null &&
               (!o.PatientVisit.Appointment.FacilityId.HasValue ||
                o.PatientVisit.Appointment.FacilityId == o.PatientVisit.FacilityId))) &&
             (!o.AppointmentId.HasValue ||
              (o.Appointment != null &&
               (!o.Appointment.FacilityId.HasValue || o.Appointment.FacilityId == o.PatientVisit.FacilityId))) &&
             facilityIds.Contains(o.PatientVisit.FacilityId)) ||
            // Legacy order without a visit: linked appointment facility is the authority.
            (!o.FacilityId.HasValue && !o.PatientVisitId.HasValue && o.AppointmentId.HasValue &&
             o.Appointment != null && o.Appointment.FacilityId.HasValue &&
             facilityIds.Contains(o.Appointment.FacilityId.Value)));
    }

    private static void ValidateRowVersion(byte[]? entityVersion, string? clientVersion)
    {
        if (entityVersion != null && entityVersion.Length > 0 && !string.IsNullOrEmpty(clientVersion))
        {
            try
            {
                var clientBytes = Convert.FromBase64String(clientVersion);
                if (!entityVersion.SequenceEqual(clientBytes))
                {
                    throw new ConflictException("Dữ liệu phiếu chỉ định đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
                }
            }
            catch (FormatException)
            {
                throw new ConflictException("RowVersion không hợp lệ.");
            }
        }
    }

    private async Task<string> GenerateOrderCodeAsync()
    {
        var todayStr = _dateTimeProvider.VietnamToday.ToString("yyyyMMdd");
        for (int i = 0; i < 10; i++)
        {
            var randomSuffix = RandomNumberGenerator.GetInt32(1000, 9999);
            var code = $"DX-{todayStr}-{randomSuffix}";
            var exists = await _dbContext.DiagnosticOrders.AnyAsync(o => o.OrderCode == code);
            if (!exists) return code;
        }
        return $"DX-{todayStr}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
    }

    /// <summary>
    /// Returns true only when <paramref name="ex"/> was caused by a UNIQUE constraint violation
    /// specifically on the <c>DiagnosticOrders.OrderCode</c> column.
    /// All other <see cref="DbUpdateException"/> instances must be rethrown by the caller.
    /// Handles both SQL Server (error numbers 2627/2601) and SQLite (message-based detection).
    /// </summary>
    private static bool IsOrderCodeUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner == null) return false;

        // SQL Server: 2627 = PRIMARY KEY violation, 2601 = UNIQUE KEY violation
        if (inner is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return sqlEx.Message.Contains("OrderCode", StringComparison.OrdinalIgnoreCase);
        }

        // SQLite (integration tests): "UNIQUE constraint failed: DiagnosticOrders.OrderCode"
        if (inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            && inner.Message.Contains("OrderCode", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public async Task<List<DiagnosticServiceDto>> GetDiagnosticServicesAsync(DiagnosticCategory? category, string? search)
    {
        var query = _dbContext.DiagnosticServices.AsNoTracking().Where(s => s.IsActive);

        if (category.HasValue)
        {
            query = query.Where(s => s.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var clean = search.Trim().ToLower();
            query = query.Where(s => s.Code.ToLower().Contains(clean) || s.Name.ToLower().Contains(clean));
        }

        var services = await query.OrderBy(s => s.Code).ToListAsync();

        return services.Select(s => new DiagnosticServiceDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            Category = s.Category.ToString(),
            PreparationInstructions = s.PreparationInstructions,
            Price = s.Price,
            IsActive = s.IsActive
        }).ToList();
    }

    public async Task<List<DiagnosticServiceDto>> GetAllDiagnosticServicesPricingAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        var services = await _dbContext.DiagnosticServices
            .AsNoTracking()
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Code)
            .ToListAsync(cancellationToken);

        return services.Select(s => new DiagnosticServiceDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            Category = s.Category.ToString(),
            PreparationInstructions = s.PreparationInstructions,
            Price = s.Price,
            IsActive = s.IsActive
        }).ToList();
    }

    public async Task<DiagnosticServiceDto> UpdateServicePriceAsync(long serviceId, decimal price, System.Threading.CancellationToken cancellationToken = default)
    {
        var service = await _dbContext.DiagnosticServices.FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);
        if (service == null)
            throw new NotFoundException("Dịch vụ cận lâm sàng không tồn tại.");

        if (price < 0)
            throw new BusinessException("INVALID_PRICE", "Đơn giá dịch vụ không được âm.");

        service.Price = price;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DiagnosticServiceDto
        {
            Id = service.Id,
            Code = service.Code,
            Name = service.Name,
            Category = service.Category.ToString(),
            PreparationInstructions = service.PreparationInstructions,
            Price = service.Price,
            IsActive = service.IsActive
        };
    }

    public async Task<DiagnosticOrderDto> CreateOrderForDoctorAsync(long appointmentId, CreateDiagnosticOrderRequest request, Guid? sourceAiActionId = null, long? facilityId = null)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        if (sourceAiActionId.HasValue)
        {
            var existingOrderId = await _dbContext.DiagnosticOrders.AsNoTracking()
                .Where(x => x.SourceAiActionId == sourceAiActionId.Value)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync();
            if (existingOrderId.HasValue)
                return (await GetOrderDtoByIdAsync(existingOrderId.Value))!;
        }

        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.PatientVisit)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status != AppointmentStatus.InConsultation)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể tạo phiếu chỉ định cận lâm sàng khi lịch hẹn đang trong phiên khám (InConsultation).");

        var resolvedFacilityId = await ResolveAppointmentFacilityAsync(doctor, appointment, facilityId);

        if (string.IsNullOrWhiteSpace(request.ClinicalIndication))
            throw new BusinessException("VALIDATION_ERROR", "Chỉ định lâm sàng không được để trống.");

        var cleanServiceIds = request.ServiceIds?.Distinct().ToList() ?? new List<long>();
        if (cleanServiceIds.Count == 0)
            throw new BusinessException("VALIDATION_ERROR", "Cần chọn ít nhất một dịch vụ cận lâm sàng.");

        var services = await _dbContext.DiagnosticServices
            .Where(s => cleanServiceIds.Contains(s.Id) && s.IsActive)
            .ToListAsync();

        if (services.Count != cleanServiceIds.Count)
            throw new BusinessException("INVALID_SERVICE", "Một hoặc nhiều dịch vụ chỉ định không tồn tại hoặc đã ngừng hoạt động.");

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var orderCode = await GenerateOrderCodeAsync();

                var order = new DiagnosticOrder
                {
                    OrderCode = orderCode,
                    AppointmentId = appointment.Id,
                    FacilityId = resolvedFacilityId,
                    SourceAiActionId = sourceAiActionId,
                    PatientId = appointment.PatientId,
                    OrderingDoctorId = doctor.Id,
                    ClinicalIndication = request.ClinicalIndication.Trim(),
                    Note = request.Note?.Trim(),
                    Status = DiagnosticOrderStatus.Ordered,
                    OrderedAtUtc = DateTime.UtcNow,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                foreach (var svc in services)
                {
                    order.Items.Add(new DiagnosticOrderItem
                    {
                        DiagnosticServiceId = svc.Id,
                        Status = DiagnosticItemStatus.Ordered,
                        RowVersion = Guid.NewGuid().ToByteArray()
                    });
                }

                _dbContext.DiagnosticOrders.Add(order);
                await _dbContext.SaveChangesAsync();

                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = userId,
                    Action = "DiagnosticOrderCreated",
                    EntityName = "DiagnosticOrder",
                    EntityId = order.Id.ToString(),
                    Description = $"Bác sĩ tạo phiếu chỉ định #{order.OrderCode} gồm {services.Count} dịch vụ.",
                    CreatedAt = DateTime.UtcNow
                });

                // Notifications
                var techRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.DiagnosticTechnician);
                if (techRole != null)
                {
                    var techUserIds = await _dbContext.UserRoles
                        .Where(ur => ur.RoleId == techRole.Id)
                        .Select(ur => ur.UserId)
                        .ToListAsync();

                    var activeTechUsers = await _dbContext.Users
                        .Where(u => techUserIds.Contains(u.Id) && u.IsActive &&
                            (!order.FacilityId.HasValue || _dbContext.StaffFacilityAssignments.Any(a =>
                                a.UserId == u.Id && a.IsActive &&
                                a.Role == RoleNames.DiagnosticTechnician && a.FacilityId == order.FacilityId.Value)))
                        .Select(u => u.Id)
                        .ToListAsync();

                    foreach (var techUserId in activeTechUsers)
                    {
                        _dbContext.Notifications.Add(new Notification
                        {
                            UserId = techUserId,
                            Type = NotificationType.Diagnostic,
                            Title = "Chỉ định cận lâm sàng mới",
                            Message = $"Phiếu chỉ định #{order.OrderCode} vừa được chỉ định. Vui lòng tiếp nhận và thực hiện.",
                            Route = $"/diagnostics/orders/{order.Id}",
                            RelatedEntityType = "DiagnosticOrder",
                            RelatedEntityId = order.Id.ToString(),
                            DedupeKey = $"diag_created_tech_{order.Id}_{techUserId}",
                            IsRead = false,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                var patientUserId = await _dbContext.Patients
                    .Where(p => p.Id == appointment.PatientId)
                    .Select(p => p.UserId)
                    .FirstOrDefaultAsync();

                if (patientUserId.HasValue && patientUserId.Value != Guid.Empty)
                {
                    _dbContext.Notifications.Add(new Notification
                    {
                        UserId = patientUserId.Value,
                        Type = NotificationType.Diagnostic,
                        Title = "Chỉ định cận lâm sàng mới",
                        Message = $"Bác sĩ đã tạo phiếu chỉ định cận lâm sàng #{order.OrderCode} cho lịch khám của bạn.",
                        Route = "/patient/diagnostic-results",
                        RelatedEntityType = "DiagnosticOrder",
                        RelatedEntityId = order.Id.ToString(),
                        DedupeKey = $"diag_created_pat_{order.Id}",
                        IsRead = false,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return (await GetOrderDtoByIdAsync(order.Id))!;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _dbContext.ChangeTracker.Clear();

                if (sourceAiActionId.HasValue)
                {
                    var recoveredOrderId = await _dbContext.DiagnosticOrders.AsNoTracking()
                        .Where(x => x.SourceAiActionId == sourceAiActionId.Value)
                        .Select(x => (long?)x.Id)
                        .FirstOrDefaultAsync();
                    if (recoveredOrderId.HasValue)
                        return (await GetOrderDtoByIdAsync(recoveredOrderId.Value))!;
                }

                if (!IsOrderCodeUniqueViolation(ex))
                {
                    // Not an OrderCode collision — rethrow the original error unchanged.
                    _logger.LogError(ex, "Non-collision DbUpdateException during diagnostic order creation. Re-throwing.");
                    throw;
                }

                if (attempt < maxRetries)
                {
                    _logger.LogWarning(ex, "OrderCode unique-constraint collision on attempt {Attempt}/{MaxRetries}. Retrying with fresh order code...", attempt, maxRetries);
                    continue;
                }

                _logger.LogError(ex, "Failed to create diagnostic order after {MaxRetries} attempts due to OrderCode constraint collision.", maxRetries);
                throw new ConflictException("ORDER_CODE_COLLISION", "Không thể tạo mã phiếu chỉ định duy nhất. Vui lòng thử lại.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _dbContext.ChangeTracker.Clear();
                _logger.LogError(ex, "Unhandled exception during atomic diagnostic order creation. Rolling back all changes.");
                throw;
            }
        }

        throw new ConflictException("ORDER_CREATION_FAILED", "Không thể tạo phiếu chỉ định cận lâm sàng. Vui lòng thử lại.");
    }

    public async Task<DiagnosticOrderDto> CreateOrderForVisitDoctorAsync(long visitId, CreateDiagnosticOrderRequest request, Guid? sourceAiActionId = null, long? facilityId = null)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        if (sourceAiActionId.HasValue)
        {
            var existingOrderId = await _dbContext.DiagnosticOrders.AsNoTracking()
                .Where(x => x.SourceAiActionId == sourceAiActionId.Value)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync();
            if (existingOrderId.HasValue)
                return (await GetOrderDtoByIdAsync(existingOrderId.Value))!;
        }

        var visit = await _dbContext.PatientVisits
            .Include(v => v.Patient)
            .Include(v => v.Appointment)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        visit = VisitDoctorAuthorizationGuard.ValidateAssignedDoctor(visit, doctor);

        if (visit.Status != VisitStatus.InConsultation && visit.Status != VisitStatus.WaitingForDoctor && visit.Status != VisitStatus.WaitingForDiagnostics)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể tạo phiếu chỉ định cận lâm sàng khi lượt khám đang trong phiên khám.");

        var resolvedFacilityId = await ResolveVisitFacilityAsync(doctor, visit, facilityId);

        if (string.IsNullOrWhiteSpace(request.ClinicalIndication))
            throw new BusinessException("VALIDATION_ERROR", "Chỉ định lâm sàng không được để trống.");

        var cleanServiceIds = request.ServiceIds?.Distinct().ToList() ?? new List<long>();
        if (cleanServiceIds.Count == 0)
            throw new BusinessException("VALIDATION_ERROR", "Cần chọn ít nhất một dịch vụ cận lâm sàng.");

        var services = await _dbContext.DiagnosticServices
            .Where(s => cleanServiceIds.Contains(s.Id) && s.IsActive)
            .ToListAsync();

        if (services.Count != cleanServiceIds.Count)
            throw new BusinessException("INVALID_SERVICE", "Một hoặc nhiều dịch vụ chỉ định không tồn tại hoặc đã ngừng hoạt động.");

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var orderCode = await GenerateOrderCodeAsync();

                var order = new DiagnosticOrder
                {
                    OrderCode = orderCode,
                    PatientVisitId = visit.Id,
                    AppointmentId = visit.AppointmentId,
                    FacilityId = resolvedFacilityId,
                    SourceAiActionId = sourceAiActionId,
                    PatientId = visit.PatientId,
                    OrderingDoctorId = doctor.Id,
                    ClinicalIndication = request.ClinicalIndication.Trim(),
                    Note = request.Note?.Trim(),
                    Status = DiagnosticOrderStatus.Ordered,
                    OrderedAtUtc = DateTime.UtcNow,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                foreach (var svc in services)
                {
                    order.Items.Add(new DiagnosticOrderItem
                    {
                        DiagnosticServiceId = svc.Id,
                        Status = DiagnosticItemStatus.Ordered,
                        RowVersion = Guid.NewGuid().ToByteArray()
                    });
                }

                _dbContext.DiagnosticOrders.Add(order);

                // Update visit status to WaitingForDiagnostics
                visit.Status = VisitStatus.WaitingForDiagnostics;
                visit.UpdatedAtUtc = _dateTimeProvider.UtcNow;

                await _dbContext.SaveChangesAsync();

                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = userId,
                    Action = "DiagnosticOrderCreated",
                    EntityName = "DiagnosticOrder",
                    EntityId = order.Id.ToString(),
                    Description = $"Bác sĩ tạo phiếu chỉ định #{order.OrderCode} cho lượt khám #{visit.VisitCode} gồm {services.Count} dịch vụ.",
                    CreatedAt = DateTime.UtcNow
                });

                // Notifications
                var techRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.DiagnosticTechnician);
                if (techRole != null)
                {
                    var techUserIds = await _dbContext.UserRoles
                        .Where(ur => ur.RoleId == techRole.Id)
                        .Select(ur => ur.UserId)
                        .ToListAsync();

                    var activeTechUsers = await _dbContext.Users
                        .Where(u => techUserIds.Contains(u.Id) && u.IsActive &&
                            (!order.FacilityId.HasValue || _dbContext.StaffFacilityAssignments.Any(a =>
                                a.UserId == u.Id && a.IsActive &&
                                a.Role == RoleNames.DiagnosticTechnician && a.FacilityId == order.FacilityId.Value)))
                        .Select(u => u.Id)
                        .ToListAsync();

                    foreach (var techUserId in activeTechUsers)
                    {
                        _dbContext.Notifications.Add(new Notification
                        {
                            UserId = techUserId,
                            Type = NotificationType.Diagnostic,
                            Title = "Chỉ định cận lâm sàng mới",
                            Message = $"Phiếu chỉ định #{order.OrderCode} vừa được chỉ định. Vui lòng tiếp nhận và thực hiện.",
                            Route = $"/diagnostics/orders/{order.Id}",
                            RelatedEntityType = "DiagnosticOrder",
                            RelatedEntityId = order.Id.ToString(),
                            DedupeKey = $"diag_created_tech_{order.Id}_{techUserId}",
                            IsRead = false,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                if (visit.Patient.UserId.HasValue)
                {
                    _dbContext.Notifications.Add(new Notification
                    {
                        UserId = visit.Patient.UserId.Value,
                        Type = NotificationType.Diagnostic,
                        Title = "Có chỉ định cận lâm sàng mới",
                        Message = $"Bác sĩ đã tạo phiếu chỉ định #{order.OrderCode} gồm {services.Count} dịch vụ cận lâm sàng.",
                        Route = "/patient/diagnostic-results",
                        RelatedEntityType = "DiagnosticOrder",
                        RelatedEntityId = order.Id.ToString(),
                        DedupeKey = $"diag_created_pat_{order.Id}_{visit.Patient.UserId.Value}",
                        IsRead = false,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return (await GetOrderDtoByIdAsync(order.Id))!;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _dbContext.ChangeTracker.Clear();

                if (sourceAiActionId.HasValue)
                {
                    var recoveredOrderId = await _dbContext.DiagnosticOrders.AsNoTracking()
                        .Where(x => x.SourceAiActionId == sourceAiActionId.Value)
                        .Select(x => (long?)x.Id)
                        .FirstOrDefaultAsync();
                    if (recoveredOrderId.HasValue)
                        return (await GetOrderDtoByIdAsync(recoveredOrderId.Value))!;
                }

                if (!IsOrderCodeUniqueViolation(ex)) throw;

                if (attempt < maxRetries) continue;

                throw new ConflictException("ORDER_CODE_COLLISION", "Không thể tạo mã phiếu chỉ định duy nhất. Vui lòng thử lại.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        throw new ConflictException("ORDER_CREATION_FAILED", "Không thể tạo phiếu chỉ định cận lâm sàng. Vui lòng thử lại.");
    }

    public async Task<List<DiagnosticOrderDto>> GetOrdersByVisitForDoctorAsync(long visitId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var visitExists = await _dbContext.PatientVisits
            .AnyAsync(v => v.Id == visitId && v.AssignedDoctorId == doctor.Id);

        if (!visitExists)
            throw new NotFoundException("Lượt khám không tồn tại hoặc không thuộc quyền quản lý.");

        var orderIds = await _dbContext.DiagnosticOrders
            .Where(o => o.PatientVisitId == visitId)
            .OrderByDescending(o => o.OrderedAtUtc)
            .Select(o => o.Id)
            .ToListAsync();

        return (await GetOrderDtosByIdsAsync(orderIds)).Select(RedactUnverifiedResultsForDoctor).ToList();
    }

    public async Task<List<DiagnosticOrderDto>> GetOrdersByAppointmentForDoctorAsync(long appointmentId)
    {
        var doctor = await GetCurrentDoctorAsync();

        var appointmentExists = await _dbContext.Appointments
            .AnyAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (!appointmentExists)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        var orderIds = await _dbContext.DiagnosticOrders
            .Where(o => o.AppointmentId == appointmentId)
            .OrderByDescending(o => o.OrderedAtUtc)
            .Select(o => o.Id)
            .ToListAsync();

        return (await GetOrderDtosByIdsAsync(orderIds)).Select(RedactUnverifiedResultsForDoctor).ToList();
    }

    public async Task<DiagnosticOrderDto> GetOrderByIdForDoctorAsync(long orderId)
    {
        var doctor = await GetCurrentDoctorAsync();
        var order = await _dbContext.DiagnosticOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderingDoctorId == doctor.Id);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc không thuộc quyền quản lý.");

        return RedactUnverifiedResultsForDoctor((await GetOrderDtoByIdAsync(orderId))!);
    }

    private static DiagnosticOrderDto RedactUnverifiedResultsForDoctor(DiagnosticOrderDto order)
    {
        if (!(order.Status == nameof(DiagnosticOrderStatus.Completed) && order.VerificationStatus == nameof(DiagnosticVerificationStatus.NotRequired)) &&
            order.VerificationStatus != nameof(DiagnosticVerificationStatus.Verified) &&
            order.Items.Any(item => item.Status != nameof(DiagnosticItemStatus.Cancelled) && item.SpecimenType != null))
        {
            foreach (var item in order.Items) item.Result = null;
        }
        return order;
    }

    public async Task<DiagnosticOrderDto> ReviewOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderingDoctorId == doctor.Id);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc không thuộc quyền quản lý.");

        if (order.Status != DiagnosticOrderStatus.Completed)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể xác nhận đã xem kết quả khi phiếu chỉ định đã hoàn tất.");

        if (order.ReviewedAtUtc.HasValue)
            return (await GetOrderDtoByIdAsync(orderId))!;

        if (request?.RowVersion != null)
        {
            ValidateRowVersion(order.RowVersion, request.RowVersion);
        }

        order.ReviewedAtUtc = DateTime.UtcNow;
        order.ReviewedByDoctorId = doctor.Id;
        order.RowVersion = Guid.NewGuid().ToByteArray();

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "DiagnosticOrderReviewed",
            EntityName = "DiagnosticOrder",
            EntityId = order.Id.ToString(),
            Description = $"Bác sĩ xác nhận đã xem kết quả phiếu chỉ định #{order.OrderCode}.",
            CreatedAt = DateTime.UtcNow
        });

        var patientUser = await (from p in _dbContext.Patients
                                 join u in _dbContext.Users on p.UserId equals u.Id
                                 where p.Id == order.PatientId
                                 select u).FirstOrDefaultAsync();
        if (patientUser != null)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = patientUser.Id,
                Type = NotificationType.Diagnostic,
                Title = "Kết quả cận lâm sàng đã được bác sĩ xem",
                Message = $"Phiếu chỉ định #{order.OrderCode} đã được bác sĩ xác nhận. Bạn có thể xem kết quả trực tuyến.",
                Route = "/patient/diagnostic-results",
                RelatedEntityType = "DiagnosticOrder",
                RelatedEntityId = order.Id.ToString(),
                DedupeKey = $"diag_reviewed_pat_{order.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu phiếu chỉ định đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }

        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    public async Task<DiagnosticOrderDto> CancelOrderAsync(long orderId, CancelDiagnosticOrderRequest? request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderingDoctorId == doctor.Id);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc không thuộc quyền quản lý.");

        if (order.Status != DiagnosticOrderStatus.Ordered)
            throw new BusinessException("CANNOT_CANCEL", "Không thể hủy phiếu chỉ định khi kỹ thuật viên đã tiếp nhận hoặc hoàn tất.");

        if (request?.RowVersion != null)
        {
            ValidateRowVersion(order.RowVersion, request.RowVersion);
        }

        order.Status = DiagnosticOrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request?.Reason))
        {
            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? $"Lý do hủy: {request.Reason.Trim()}"
                : $"{order.Note} (Lý do hủy: {request.Reason.Trim()})";
        }

        foreach (var item in order.Items)
        {
            item.Status = DiagnosticItemStatus.Cancelled;
            item.RowVersion = Guid.NewGuid().ToByteArray();
        }

        order.RowVersion = Guid.NewGuid().ToByteArray();

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "DiagnosticOrderCancelled",
            EntityName = "DiagnosticOrder",
            EntityId = order.Id.ToString(),
            Description = $"Bác sĩ hủy phiếu chỉ định #{order.OrderCode}.",
            CreatedAt = DateTime.UtcNow
        });

        var visit = await FindOrderVisitAsync(order);
        if (visit?.Status == VisitStatus.WaitingForDiagnostics)
        {
            var remaining = await RemainingVisitOrderStatusesAsync(visit, order.Id);
            if (remaining.Count == 0) visit.Status = VisitStatus.InConsultation;
            else if (remaining.All(status => status == DiagnosticOrderStatus.Completed)) visit.Status = VisitStatus.ResultsReady;
            visit.UpdatedAtUtc = _dateTimeProvider.UtcNow;
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu phiếu chỉ định đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }

        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    public async Task<PagedResult<DiagnosticOrderDto>> GetTechnicianOrdersAsync(DiagnosticOrderStatus? status, DateOnly? date, string? search, int page, int pageSize)
    {
        var userId = GetUserId();
        var facilityIds = await _dbContext.StaffFacilityAssignments
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Role == RoleNames.DiagnosticTechnician)
            .Select(x => x.FacilityId)
            .Distinct()
            .ToListAsync();

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var query = _dbContext.DiagnosticOrders
            .AsNoTracking()
            .Include(o => o.Appointment)
            .Include(o => o.PatientVisit)
            .Include(o => o.Patient)
            .AsQueryable();

        query = WhereTechnicianFacilityScope(query, facilityIds);

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        if (date.HasValue)
        {
            query = query.Where(o => (o.Appointment != null && o.Appointment.AppointmentDate == date.Value) ||
                                     (o.PatientVisit != null && o.PatientVisit.VisitDate == date.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var clean = search.Trim().ToLower();
            query = query.Where(o => o.OrderCode.ToLower().Contains(clean) ||
                                     (o.Appointment != null && o.Appointment.AppointmentCode.ToLower().Contains(clean)) ||
                                     (o.PatientVisit != null && o.PatientVisit.VisitCode.ToLower().Contains(clean)) ||
                                     (o.Patient.FullName != null && o.Patient.FullName.ToLower().Contains(clean)) ||
                                     (o.Patient.PhoneNumber != null && o.Patient.PhoneNumber.Contains(clean)) ||
                                     (o.Patient.MedicalRecordNumber != null && o.Patient.MedicalRecordNumber.ToLower().Contains(clean)) ||
                                     _dbContext.Users.Any(u => o.Patient.UserId != null && u.Id == o.Patient.UserId && (u.FullName.ToLower().Contains(clean) || (u.PhoneNumber != null && u.PhoneNumber.Contains(clean)))));
        }

        var totalItems = await query.CountAsync();
        var orderedQuery = status is DiagnosticOrderStatus.Ordered or DiagnosticOrderStatus.InProgress
            ? query.OrderBy(o => o.OrderedAtUtc).ThenBy(o => o.Id)
            : query.OrderByDescending(o => o.OrderedAtUtc);
        var orderIds = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => o.Id)
            .ToListAsync();

        var items = await GetOrderDtosByIdsAsync(orderIds);

        return new PagedResult<DiagnosticOrderDto>(items, totalItems, page, pageSize);
    }

    public async Task<DiagnosticOrderDto> GetTechnicianOrderByIdAsync(long orderId)
    {
        var order = await _dbContext.DiagnosticOrders
            .AsNoTracking()
            .Include(o => o.Appointment)
            .Include(o => o.PatientVisit)
            .FirstOrDefaultAsync(x => x.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

        await LoadLinkedVisitAppointmentAsync(order);
        await EnsureTechnicianFacilityScopeAsync(order);

        var dto = await GetOrderDtoByIdAsync(orderId);
        if (dto == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");
        return dto;
    }

    public async Task<TechnicianDiagnosticStatsDto> GetTechnicianStatsAsync()
    {
        var userId = GetUserId();
        var facilityIds = await _dbContext.StaffFacilityAssignments
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive && x.Role == RoleNames.DiagnosticTechnician)
            .Select(x => x.FacilityId)
            .Distinct()
            .ToListAsync();
        var scopedOrders = _dbContext.DiagnosticOrders.AsQueryable();
        scopedOrders = WhereTechnicianFacilityScope(scopedOrders, facilityIds);
        var today = _dateTimeProvider.VietnamToday;
        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(today.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var orderedCount = await scopedOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.Ordered);
        var inProgressCount = await scopedOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.InProgress);
        var pendingVerificationCount = await scopedOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.InProgress &&
            o.VerificationStatus == DiagnosticVerificationStatus.Pending);
        var returnedCount = await scopedOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.InProgress &&
            o.VerificationStatus == DiagnosticVerificationStatus.Returned);
        var now = _dateTimeProvider.UtcNow;
        // Preserve an empty overdue range when a clock returns its default value.
        if (now < DateTime.MinValue.AddMinutes(30)) now = DateTime.MinValue.AddMinutes(30);
        var overdueBefore = now.AddMinutes(-30);
        var overdueCount = await scopedOrders.CountAsync(o =>
            (o.Status == DiagnosticOrderStatus.Ordered || o.Status == DiagnosticOrderStatus.InProgress) &&
            o.OrderedAtUtc < overdueBefore);

        var completedTodayCount = await scopedOrders
            .CountAsync(o => o.Status == DiagnosticOrderStatus.Completed &&
                             o.CompletedAtUtc.HasValue &&
                             o.CompletedAtUtc.Value >= startUtc && o.CompletedAtUtc.Value < endUtc);

        return new TechnicianDiagnosticStatsDto
        {
            OrderedCount = orderedCount,
            InProgressCount = inProgressCount,
            CompletedTodayCount = completedTodayCount,
            PendingVerificationCount = pendingVerificationCount,
            ReturnedCount = returnedCount,
            OverdueCount = overdueCount
        };
    }

    public async Task<DiagnosticOrderDto> StartOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request)
    {
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .Include(o => o.Items)
                .ThenInclude(i => i.DiagnosticService)
            .Include(o => o.Appointment)
            .Include(o => o.PatientVisit)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

        await LoadLinkedVisitAppointmentAsync(order);
        await EnsureTechnicianFacilityScopeAsync(order);

        if (order.Status != DiagnosticOrderStatus.Ordered)
            throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể tiếp nhận thực hiện phiếu chỉ định đang ở trạng thái Ordered.");

        if (request?.RowVersion != null)
        {
            ValidateRowVersion(order.RowVersion, request.RowVersion);
        }

        order.Status = DiagnosticOrderStatus.InProgress;
        order.StartedAtUtc = DateTime.UtcNow;
        order.StartedByUserId = userId;
        order.RowVersion = Guid.NewGuid().ToByteArray();

        foreach (var item in order.Items.Where(i => i.Status == DiagnosticItemStatus.Ordered))
        {
            item.Status = DiagnosticItemStatus.InProgress;
            item.RowVersion = Guid.NewGuid().ToByteArray();
        }

        foreach (var item in order.Items.Where(i => i.Status != DiagnosticItemStatus.Cancelled &&
                     i.DiagnosticService.SpecimenType != null && i.SpecimenStatus == SpecimenStatus.NotRequired))
        {
            item.SpecimenStatus = SpecimenStatus.PendingCollection;
            item.RowVersion = Guid.NewGuid().ToByteArray();
        }

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "DiagnosticOrderStarted",
            EntityName = "DiagnosticOrder",
            EntityId = order.Id.ToString(),
            Description = $"Kỹ thuật viên tiếp nhận thực hiện phiếu chỉ định #{order.OrderCode}.",
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu phiếu chỉ định đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }

        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    public async Task<DiagnosticOrderDto> RecordItemResultAsync(long orderId, long itemId, RecordDiagnosticResultRequest request)
    {
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .Include(o => o.Items)
                .ThenInclude(i => i.Result)
                    .ThenInclude(r => r!.Values)
            .Include(o => o.Items).ThenInclude(i => i.DiagnosticService).ThenInclude(s => s.Parameters)
            .Include(o => o.Patient)
            .Include(o => o.Appointment)
            .Include(o => o.PatientVisit)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

        await LoadLinkedVisitAppointmentAsync(order);
        await EnsureTechnicianFacilityScopeAsync(order);

        EnsureResultsEditable(order);

        if (order.Status != DiagnosticOrderStatus.InProgress)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể nhập kết quả khi phiếu chỉ định đang trong trạng thái InProgress.");

        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            throw new NotFoundException("Dịch vụ chỉ định không tồn tại trong phiếu này.");

        if (item.Status == DiagnosticItemStatus.Cancelled)
            throw new BusinessException("ITEM_CANCELLED", "Dịch vụ chỉ định này đã bị hủy, không thể nhập kết quả.");

        if (item.SpecimenStatus is SpecimenStatus.PendingCollection or SpecimenStatus.Collected or SpecimenStatus.Rejected)
            throw new BusinessException("SPECIMEN_NOT_RECEIVED", "Chỉ nhập kết quả sau khi phòng xét nghiệm đã nhận mẫu đạt yêu cầu.");

        if (request?.RowVersion != null)
        {
            ValidateRowVersion(item.RowVersion, request.RowVersion);
        }

        var structured = request?.Values?.Count > 0;
        if (request == null || (!structured && string.IsNullOrWhiteSpace(request.ResultText)))
            throw new BusinessException("VALIDATION_ERROR", "Kết quả cận lâm sàng không được để trống.");

        var values = new List<DiagnosticResultValue>();
        if (structured)
        {
            var parameters = item.DiagnosticService.Parameters.Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();
            var inputs = request.Values!;
            if (parameters.Count == 0 || inputs.Any(v => parameters.All(p => p.Id != v.ParameterId)))
                throw new BusinessException("VALIDATION_ERROR", "Chỉ số không thuộc dịch vụ này.");
            if (inputs.Select(v => v.ParameterId).Distinct().Count() != inputs.Count)
                throw new BusinessException("VALIDATION_ERROR", "Không được nhập trùng chỉ số.");
            var missing = parameters.Where(p => inputs.All(v => v.ParameterId != p.Id)).ToList();
            if (missing.Count > 0)
                throw new BusinessException("VALIDATION_ERROR", $"Thiếu chỉ số: {string.Join(", ", missing.Select(p => p.Name))}.");
            if (inputs.Any(v => v.Value < 0 || v.Value >= 1_000_000))
                throw new BusinessException("VALIDATION_ERROR", "Giá trị chỉ số phải từ 0 đến dưới 1.000.000.");
            var deltaNow = _dateTimeProvider.UtcNow;
            var history = await LoadDeltaHistoryAsync(order, parameters, deltaNow);
            foreach (var parameter in parameters)
            {
                var (low, high) = SelectReferenceRange(parameter, order.Patient.Gender);
                var value = inputs.Single(v => v.ParameterId == parameter.Id).Value;
                var resultValue = new DiagnosticResultValue
                {
                    DiagnosticServiceParameterId = parameter.Id, ParameterCode = parameter.Code,
                    ParameterName = parameter.Name, Unit = parameter.Unit, RefLow = low, RefHigh = high,
                    CriticalLow = parameter.CriticalLow, CriticalHigh = parameter.CriticalHigh,
                    NumericValue = value, Flag = ResultFlag(value, low, high, parameter.CriticalLow, parameter.CriticalHigh),
                    SortOrder = parameter.SortOrder
                };
                ApplyDeltaCheck(resultValue, parameter, history, deltaNow);
                values.Add(resultValue);
            }
        }

        var result = item.Result ?? new DiagnosticResult { DiagnosticOrderItemId = item.Id };
        var oldCritical = result.Values.Where(v => IsCritical(v.Flag)).OrderBy(v => v.DiagnosticServiceParameterId)
            .Select(v => (v.DiagnosticServiceParameterId, v.NumericValue, v.Flag)).ToList();
        var newCritical = values.Where(v => IsCritical(v.Flag)).OrderBy(v => v.DiagnosticServiceParameterId)
            .Select(v => (v.DiagnosticServiceParameterId, v.NumericValue, v.Flag)).ToList();
        var now = DateTime.UtcNow;
        var resultText = request.ResultText?.Trim() ?? string.Empty;
        if (structured)
        {
            var summary = string.Join("\n", values.Select(v =>
                $"{v.ParameterName} ({v.ParameterCode}): {v.NumericValue.ToString("0.####", CultureInfo.InvariantCulture)} {v.Unit} [{FlagLabel(v.Flag)}]"));
            // Reserve space for every numeric row even when the optional comment is long.
            var commentLimit = Math.Max(0, 4000 - summary.Length - 2);
            resultText = string.IsNullOrEmpty(resultText) ? summary : resultText[..Math.Min(resultText.Length, commentLimit)] + "\n\n" + summary;
        }
        result.ResultText = resultText[..Math.Min(4000, resultText.Length)];
        result.Conclusion = request.Conclusion?.Trim();
        result.ReferenceRange = structured ? null : request.ReferenceRange?.Trim();
        result.Unit = structured ? null : request.Unit?.Trim();
        result.ResultedAtUtc = now;
        result.ResultedByUserId = userId;
        result.RowVersion = Guid.NewGuid().ToByteArray();
        result.HasCriticalValue = newCritical.Count > 0;
        _dbContext.DiagnosticResultValues.RemoveRange(result.Values);
        result.Values = values;
        if (item.Result == null)
        {
            item.Result = result;
            _dbContext.DiagnosticResults.Add(result);
        }

        if (result.HasCriticalValue && (result.CriticalNotifiedAtUtc == null || !oldCritical.SequenceEqual(newCritical)))
        {
            var doctorUser = await (from d in _dbContext.Doctors join u in _dbContext.Users on d.UserId equals u.Id
                                    where d.Id == order.OrderingDoctorId select u).FirstOrDefaultAsync();
            if (doctorUser != null)
            {
                var message = $"Phiếu #{order.OrderCode}: " + string.Join(", ", values.Where(v => IsCritical(v.Flag))
                    .Select(v => $"{v.ParameterName} ({v.ParameterCode}): {v.NumericValue.ToString("0.####", CultureInfo.InvariantCulture)} {v.Unit}"));
                _dbContext.Notifications.Add(new Notification
                {
                    UserId = doctorUser.Id, Type = NotificationType.Diagnostic,
                    Title = "CẢNH BÁO: kết quả có giá trị nguy hiểm", Message = message[..Math.Min(1000, message.Length)],
                    Route = order.AppointmentId.HasValue ? $"/doctor/appointments/{order.AppointmentId}/examination" : $"/doctor/visits/{order.PatientVisitId}/examination",
                    RelatedEntityType = "DiagnosticOrder", RelatedEntityId = order.Id.ToString(),
                    DedupeKey = $"diag_critical_{item.Id}_{Convert.ToHexString(result.RowVersion)}", CreatedAtUtc = now
                });
                result.CriticalNotifiedAtUtc = now;
                _dbContext.SystemAuditLogs.Add(new SystemAuditLog
                {
                    UserId = userId, Action = "DiagnosticCriticalValueNotified", EntityName = "DiagnosticOrderItem",
                    EntityId = item.Id.ToString(), Description = $"Đã báo bác sĩ chỉ định về giá trị nguy hiểm của phiếu #{order.OrderCode}.", CreatedAt = now
                });
            }
        }

        item.Status = DiagnosticItemStatus.Completed;
        item.RowVersion = Guid.NewGuid().ToByteArray();

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "DiagnosticResultRecorded",
            EntityName = "DiagnosticOrderItem",
            EntityId = item.Id.ToString(),
            Description = $"Kỹ thuật viên nhập kết quả cho dịch vụ #{item.DiagnosticServiceId} thuộc phiếu #{order.OrderCode}.",
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }

        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    public async Task<DiagnosticOrderDto> CollectSpecimenAsync(long orderId, long itemId, CollectSpecimenRequest request)
    {
        var (order, item) = await LoadSpecimenItemAsync(orderId, itemId, request.RowVersion);
        if (item.SpecimenStatus is not (SpecimenStatus.PendingCollection or SpecimenStatus.Rejected))
            throw InvalidSpecimenState();
        if (item.SpecimenStatus == SpecimenStatus.Rejected) item.RecollectCount++;
        item.RejectedAtUtc = null;
        item.RejectedByUserId = null;
        item.RejectionReason = null;
        item.RejectionNote = null;
        item.ReceivedAtUtc = null;
        item.ReceivedByUserId = null;
        item.SpecimenBarcode = $"S{item.Id:D7}-{item.RecollectCount + 1}";
        item.SpecimenStatus = SpecimenStatus.Collected;
        item.CollectedAtUtc = DateTime.UtcNow;
        item.CollectedByUserId = GetUserId();
        return await SaveSpecimenStepAsync(order, item, "SpecimenCollected", request.Note);
    }

    public async Task<DiagnosticOrderDto> ReceiveSpecimenAsync(long orderId, long itemId, ReceiveSpecimenRequest request)
    {
        var (order, item) = await LoadSpecimenItemAsync(orderId, itemId, request.RowVersion);
        if (item.SpecimenStatus != SpecimenStatus.Collected) throw InvalidSpecimenState();
        item.SpecimenStatus = SpecimenStatus.Received;
        item.ReceivedAtUtc = DateTime.UtcNow;
        item.ReceivedByUserId = GetUserId();
        return await SaveSpecimenStepAsync(order, item, "SpecimenReceived", null);
    }

    public async Task<DiagnosticOrderDto> RejectSpecimenAsync(long orderId, long itemId, RejectSpecimenRequest request)
    {
        var (order, item) = await LoadSpecimenItemAsync(orderId, itemId, request.RowVersion);
        if (item.SpecimenStatus != SpecimenStatus.Collected) throw InvalidSpecimenState();
        if (!Enum.IsDefined(request.Reason) || (request.Reason == SpecimenRejectionReason.Other && (request.Note?.Trim().Length ?? 0) < 5))
            throw new BusinessException("VALIDATION_ERROR", "Vui lòng chọn lý do hợp lệ; lý do Khác cần ghi chú ít nhất 5 ký tự.");
        item.SpecimenStatus = SpecimenStatus.Rejected;
        item.RejectedAtUtc = DateTime.UtcNow;
        item.RejectedByUserId = GetUserId();
        item.RejectionReason = request.Reason;
        item.RejectionNote = request.Note?.Trim();
        var reason = SpecimenReasonLabel(request.Reason);
        var doctorUser = await (from d in _dbContext.Doctors join u in _dbContext.Users on d.UserId equals u.Id
                                where d.Id == order.OrderingDoctorId select u).FirstOrDefaultAsync();
        if (doctorUser != null)
        {
            var message = $"Phiếu #{order.OrderCode}, dịch vụ {item.DiagnosticService.Name}: {reason}.";
            _dbContext.Notifications.Add(new Notification
            {
                UserId = doctorUser.Id, Type = NotificationType.Diagnostic,
                Title = "Mẫu xét nghiệm bị từ chối — cần lấy lại", Message = message[..Math.Min(1000, message.Length)],
                Route = order.AppointmentId.HasValue ? $"/doctor/appointments/{order.AppointmentId}/examination" : $"/doctor/visits/{order.PatientVisitId}/examination",
                RelatedEntityType = "DiagnosticOrder", RelatedEntityId = order.Id.ToString(),
                DedupeKey = $"diag_specimen_rejected_{item.Id}_{item.RecollectCount}", CreatedAtUtc = DateTime.UtcNow
            });
        }
        return await SaveSpecimenStepAsync(order, item, "SpecimenRejected", $"{reason}; {item.RejectionNote}");
    }

    private async Task<(DiagnosticOrder Order, DiagnosticOrderItem Item)> LoadSpecimenItemAsync(long orderId, long itemId, string? rowVersion)
    {
        GetUserId();
        var order = await _dbContext.DiagnosticOrders.Include(o => o.Appointment).Include(o => o.PatientVisit)
            .Include(o => o.Items).ThenInclude(i => i.DiagnosticService)
            .Include(o => o.Items).ThenInclude(i => i.Result).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new NotFoundException("Phiếu chỉ định không tồn tại.");
        await LoadLinkedVisitAppointmentAsync(order);
        await EnsureTechnicianFacilityScopeAsync(order);
        EnsureResultsEditable(order);
        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) throw new NotFoundException("Dịch vụ chỉ định không tồn tại trong phiếu này.");
        if (rowVersion != null) ValidateRowVersion(item.RowVersion, rowVersion);
        if (order.Status != DiagnosticOrderStatus.InProgress || item.Status == DiagnosticItemStatus.Cancelled || item.Result != null)
            throw InvalidSpecimenState();
        return (order, item);
    }

    private async Task<DiagnosticOrderDto> SaveSpecimenStepAsync(DiagnosticOrder order, DiagnosticOrderItem item, string action, string? note)
    {
        item.RowVersion = Guid.NewGuid().ToByteArray();
        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = GetUserId(), Action = action, EntityName = "DiagnosticOrderItem", EntityId = item.Id.ToString(),
            Description = $"Phiếu #{order.OrderCode}; mẫu {item.SpecimenBarcode}; lần lấy lại {item.RecollectCount}; {note?.Trim()}", CreatedAt = DateTime.UtcNow
        });
        try { await _dbContext.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }
        return (await GetOrderDtoByIdAsync(order.Id))!;
    }

    private static BusinessException InvalidSpecimenState() =>
        new("INVALID_SPECIMEN_STATE", "Trạng thái mẫu không phù hợp để thực hiện thao tác này.");

    private static string SpecimenReasonLabel(SpecimenRejectionReason reason) => reason switch
    {
        SpecimenRejectionReason.Hemolyzed => "Mẫu tán huyết", SpecimenRejectionReason.Clotted => "Mẫu đông",
        SpecimenRejectionReason.InsufficientVolume => "Không đủ thể tích", SpecimenRejectionReason.WrongContainer => "Sai loại ống",
        SpecimenRejectionReason.LeakedOrBroken => "Ống vỡ/rò rỉ", SpecimenRejectionReason.LabelMismatch => "Thông tin nhãn không khớp",
        SpecimenRejectionReason.PatientNotPrepared => "Bệnh nhân chưa chuẩn bị đúng", _ => "Khác"
    };

    // Parameters without their own window still show the latest prior value, but are never flagged.
    private const int DefaultDeltaLookbackDays = 30;
    private const decimal MaxStoredDeltaPercent = 9_999_999.99m;

    private sealed record DeltaHistoryRow(string ParameterCode, string Unit, decimal NumericValue, DateTime ResultedAtUtc, string OrderCode);

    private async Task<List<DeltaHistoryRow>> LoadDeltaHistoryAsync(DiagnosticOrder order, List<DiagnosticServiceParameter> parameters, DateTime now)
    {
        var codes = parameters.Select(p => p.Code).Distinct().ToList();
        var since = now.AddDays(-parameters.Max(p => p.DeltaWindowDays ?? DefaultDeltaLookbackDays));
        // Only released results (Completed order, active item) of the same patient count as "previous".
        return await _dbContext.DiagnosticResultValues.AsNoTracking()
            .Where(v => v.DiagnosticResult.DiagnosticOrderItem.DiagnosticOrder.PatientId == order.PatientId &&
                v.DiagnosticResult.DiagnosticOrderItem.DiagnosticOrderId != order.Id &&
                v.DiagnosticResult.DiagnosticOrderItem.DiagnosticOrder.Status == DiagnosticOrderStatus.Completed &&
                v.DiagnosticResult.DiagnosticOrderItem.Status != DiagnosticItemStatus.Cancelled &&
                codes.Contains(v.ParameterCode) && v.DiagnosticResult.ResultedAtUtc >= since)
            .Select(v => new DeltaHistoryRow(v.ParameterCode, v.Unit, v.NumericValue, v.DiagnosticResult.ResultedAtUtc,
                v.DiagnosticResult.DiagnosticOrderItem.DiagnosticOrder.OrderCode))
            .ToListAsync();
    }

    private static void ApplyDeltaCheck(DiagnosticResultValue value, DiagnosticServiceParameter parameter,
        IEnumerable<DeltaHistoryRow> history, DateTime now)
    {
        var since = now.AddDays(-(parameter.DeltaWindowDays ?? DefaultDeltaLookbackDays));
        var previous = history.Where(h => h.ParameterCode == parameter.Code && h.Unit == parameter.Unit && h.ResultedAtUtc >= since)
            .OrderByDescending(h => h.ResultedAtUtc).FirstOrDefault();
        value.PreviousNumericValue = previous?.NumericValue;
        value.PreviousResultedAtUtc = previous == null ? null : DateTime.SpecifyKind(previous.ResultedAtUtc, DateTimeKind.Utc);
        value.PreviousOrderCode = previous?.OrderCode;
        value.DeltaChange = previous == null ? null : value.NumericValue - previous.NumericValue;
        decimal? percent = previous == null || previous.NumericValue == 0 ? null :
            Math.Round((value.NumericValue - previous.NumericValue) / previous.NumericValue * 100m, 2, MidpointRounding.AwayFromZero);
        // The flag uses the exact percent; the stored snapshot is capped to fit decimal(9,2).
        value.DeltaPercent = percent.HasValue ? Math.Clamp(percent.Value, -MaxStoredDeltaPercent, MaxStoredDeltaPercent) : null;
        value.IsDeltaFailure = previous != null &&
            ((parameter.DeltaAbsoluteLimit.HasValue && Math.Abs(value.DeltaChange!.Value) > parameter.DeltaAbsoluteLimit.Value) ||
             (parameter.DeltaPercentLimit.HasValue && percent.HasValue && Math.Abs(percent.Value) > parameter.DeltaPercentLimit.Value));
    }

    private static bool IsCritical(DiagnosticResultFlag flag) => flag is DiagnosticResultFlag.CriticalLow or DiagnosticResultFlag.CriticalHigh;

    private static DiagnosticResultFlag ResultFlag(decimal value, decimal? low, decimal? high, decimal? criticalLow, decimal? criticalHigh) =>
        criticalLow.HasValue && value <= criticalLow.Value ? DiagnosticResultFlag.CriticalLow :
        criticalHigh.HasValue && value >= criticalHigh.Value ? DiagnosticResultFlag.CriticalHigh :
        low.HasValue && value < low.Value ? DiagnosticResultFlag.Low :
        high.HasValue && value > high.Value ? DiagnosticResultFlag.High : DiagnosticResultFlag.Normal;

    private static string FlagLabel(DiagnosticResultFlag flag) => flag switch
    {
        DiagnosticResultFlag.Low => "Thấp", DiagnosticResultFlag.High => "Cao",
        DiagnosticResultFlag.CriticalLow => "Thấp – NGUY HIỂM", DiagnosticResultFlag.CriticalHigh => "Cao – NGUY HIỂM",
        _ => "Bình thường"
    };

    private static (decimal? Low, decimal? High) SelectReferenceRange(DiagnosticServiceParameter parameter, Gender? gender) =>
        gender == Gender.Female ? (parameter.RefLowFemale ?? parameter.RefLow, parameter.RefHighFemale ?? parameter.RefHigh) :
            (parameter.RefLow, parameter.RefHigh);

    private static string ReferenceRangeText(decimal? low, decimal? high)
    {
        string Number(decimal value) => value.ToString("0.0###", CultureInfo.InvariantCulture);
        return low.HasValue && high.HasValue ? $"{Number(low.Value)} – {Number(high.Value)}" :
            high.HasValue ? $"≤ {Number(high.Value)}" : low.HasValue ? $"≥ {Number(low.Value)}" : string.Empty;
    }

    private static List<DiagnosticServiceParameterDto> MapParameters(DiagnosticService? service, Gender? gender) =>
        service?.Parameters.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p =>
        {
            var (low, high) = SelectReferenceRange(p, gender);
            return new DiagnosticServiceParameterDto
            {
                Id = p.Id, Code = p.Code, Name = p.Name, Unit = p.Unit, RefLow = low, RefHigh = high,
                CriticalLow = p.CriticalLow, CriticalHigh = p.CriticalHigh, ReferenceRangeText = ReferenceRangeText(low, high)
            };
        }).ToList() ?? new();

    private static List<DiagnosticResultValueDto> MapValues(DiagnosticResult result) => result.Values
        .OrderBy(v => v.SortOrder).ThenBy(v => v.Id).Select(v => new DiagnosticResultValueDto
        {
            ParameterId = v.DiagnosticServiceParameterId, ParameterCode = v.ParameterCode,
            ParameterName = v.ParameterName, Unit = v.Unit, Value = v.NumericValue, RefLow = v.RefLow, RefHigh = v.RefHigh,
            ReferenceRangeText = ReferenceRangeText(v.RefLow, v.RefHigh), Flag = v.Flag.ToString(), SortOrder = v.SortOrder,
            PreviousValue = v.PreviousNumericValue, PreviousResultedAtUtc = AsUtc(v.PreviousResultedAtUtc),
            PreviousOrderCode = v.PreviousOrderCode, DeltaChange = v.DeltaChange, DeltaPercent = v.DeltaPercent,
            IsDeltaFailure = v.IsDeltaFailure
        }).ToList();

    public async Task<DiagnosticOrderDto> CompleteOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request)
    {
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .Include(o => o.Items)
                .ThenInclude(i => i.Result)
            .Include(o => o.Items).ThenInclude(i => i.DiagnosticService)
            .Include(o => o.Appointment)
            .Include(o => o.PatientVisit)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

        await LoadLinkedVisitAppointmentAsync(order);
        await EnsureTechnicianFacilityScopeAsync(order);

        if (order.Status != DiagnosticOrderStatus.InProgress)
            throw new BusinessException("INVALID_STATE_TRANSITION", "Chỉ có thể hoàn tất phiếu chỉ định đang ở trạng thái InProgress.");

        if (request?.RowVersion != null)
        {
            ValidateRowVersion(order.RowVersion, request.RowVersion);
        }

        var activeItems = order.Items.Where(i => i.Status != DiagnosticItemStatus.Cancelled).ToList();
        if (activeItems.Count == 0)
            throw new BusinessException("INVALID_STATE", "Phiếu chỉ định không có dịch vụ hợp lệ để hoàn tất.");

        var incomplete = activeItems.Where(i => i.Result == null || string.IsNullOrWhiteSpace(i.Result.ResultText) || i.Status != DiagnosticItemStatus.Completed).ToList();
        if (incomplete.Count > 0)
            throw new BusinessException("ITEMS_INCOMPLETE", "Tất cả các dịch vụ chỉ định cần có kết quả trước khi hoàn tất phiếu.");

        if (order.VerificationStatus == DiagnosticVerificationStatus.Pending)
            throw new BusinessException("INVALID_STATE", "Phiếu đang chờ duyệt.");

        if (RequiresVerification(order) && order.VerificationStatus is DiagnosticVerificationStatus.NotRequired or DiagnosticVerificationStatus.Returned)
        {
            order.VerificationStatus = DiagnosticVerificationStatus.Pending;
            order.SubmittedForVerificationAtUtc = DateTime.UtcNow;
            order.SubmittedForVerificationByUserId = userId;
            order.RowVersion = Guid.NewGuid().ToByteArray();
            _dbContext.SystemAuditLogs.Add(new SystemAuditLog
            {
                UserId = userId, Action = "DiagnosticResultsSubmittedForVerification", EntityName = "DiagnosticOrder",
                EntityId = order.Id.ToString(), Description = $"Gửi duyệt kết quả phiếu #{order.OrderCode}.", CreatedAt = DateTime.UtcNow
            });
            await NotifyLabSupervisorsAsync(order);
        }
        else
        {
            await FinalizeOrderAsync(order, userId);
        }

        try { await _dbContext.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu phiếu chỉ định đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }
        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    public async Task<DiagnosticOrderDto> VerifyOrderAsync(long orderId, VerifyDiagnosticOrderRequest request)
    {
        var order = await LoadPendingVerificationAsync(orderId, request.RowVersion);
        var hasDeltaFailure = await _dbContext.DiagnosticResultValues.AnyAsync(v => v.IsDeltaFailure &&
            v.DiagnosticResult.DiagnosticOrderItem.DiagnosticOrderId == order.Id &&
            v.DiagnosticResult.DiagnosticOrderItem.Status != DiagnosticItemStatus.Cancelled);
        if (hasDeltaFailure && (request.Note?.Trim().Length ?? 0) < 5)
            throw new BusinessException("DELTA_NOTE_REQUIRED",
                "Kết quả lệch delta so với lần trước, vui lòng ghi chú đã kiểm tra (nhầm mẫu/nhầm bệnh nhân/diễn tiến lâm sàng).");
        order.VerificationStatus = DiagnosticVerificationStatus.Verified;
        order.VerifiedAtUtc = DateTime.UtcNow;
        order.VerifiedByUserId = GetUserId();
        order.VerificationNote = request.Note?.Trim();
        AddVerificationAudit(order, "DiagnosticResultsVerified", order.VerificationNote);
        await FinalizeOrderAsync(order, order.SubmittedForVerificationByUserId!.Value);
        return await SaveVerificationAsync(order);
    }

    public async Task<DiagnosticOrderDto> ReturnOrderAsync(long orderId, ReturnDiagnosticOrderRequest request)
    {
        var order = await LoadPendingVerificationAsync(orderId, request.RowVersion);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw new BusinessException("VALIDATION_ERROR", "Lý do trả lại phải có ít nhất 5 ký tự.");
        order.VerificationStatus = DiagnosticVerificationStatus.Returned;
        order.ReturnedAtUtc = DateTime.UtcNow;
        order.ReturnedByUserId = GetUserId();
        order.ReturnReason = request.Reason.Trim();
        order.ReturnCount++;
        order.RowVersion = Guid.NewGuid().ToByteArray();
        AddVerificationAudit(order, "DiagnosticResultsReturned", order.ReturnReason);
        _dbContext.Notifications.Add(new Notification
        {
            UserId = order.SubmittedForVerificationByUserId!.Value, Type = NotificationType.Diagnostic,
            Title = "Kết quả bị trả lại — cần chỉnh sửa", Message = $"Phiếu #{order.OrderCode}: {order.ReturnReason}",
            Route = $"/diagnostics/orders/{order.Id}", RelatedEntityType = "DiagnosticOrder", RelatedEntityId = order.Id.ToString(),
            DedupeKey = $"diag_returned_{order.Id}_{order.ReturnCount}", CreatedAtUtc = DateTime.UtcNow
        });
        return await SaveVerificationAsync(order);
    }

    public async Task<PagedResult<DiagnosticOrderDto>> GetVerificationQueueAsync(int page, int pageSize)
    {
        var role = GetVerificationRole();
        var userId = GetUserId();
        var facilityIds = await GetVerificationFacilityIdsAsync(role);
        page = Math.Max(1, page);
        pageSize = pageSize < 1 ? 15 : Math.Min(pageSize, 100);
        var query = WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds)
            .Where(order => order.Status == DiagnosticOrderStatus.InProgress && order.VerificationStatus == DiagnosticVerificationStatus.Pending);
        if (role == RoleNames.Doctor) query = query.Where(order => order.OrderingDoctor.UserId != userId);
        var count = await query.CountAsync();
        var ids = await query.OrderBy(order => order.SubmittedForVerificationAtUtc).ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(order => order.Id).ToListAsync();
        return new PagedResult<DiagnosticOrderDto>(await GetOrderDtosByIdsAsync(ids), count, page, pageSize);
    }

    public async Task<VerificationStatsDto> GetVerificationStatsAsync()
    {
        var role = GetVerificationRole();
        var userId = GetUserId();
        var facilityIds = await GetVerificationFacilityIdsAsync(role);
        var scopedOrders = WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds);
        if (role == RoleNames.Doctor)
            scopedOrders = scopedOrders.Where(order => order.OrderingDoctor.UserId != userId);

        var pending = scopedOrders.Where(order => order.Status == DiagnosticOrderStatus.InProgress &&
            order.VerificationStatus == DiagnosticVerificationStatus.Pending);
        var now = _dateTimeProvider.UtcNow;
        var overdueBefore = now.AddMinutes(-30);
        var today = _dateTimeProvider.VietnamToday;
        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(today.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var oldest = await pending.MinAsync(order => order.SubmittedForVerificationAtUtc);

        return new VerificationStatsDto
        {
            PendingCount = await pending.CountAsync(),
            CriticalPendingCount = await pending.CountAsync(order => order.Items.Any(item =>
                item.Status != DiagnosticItemStatus.Cancelled && item.Result != null && item.Result.HasCriticalValue)),
            OverduePendingCount = await pending.CountAsync(order => order.SubmittedForVerificationAtUtc < overdueBefore),
            OldestPendingMinutes = oldest.HasValue ? Math.Max(0, (int)(now - oldest.Value).TotalMinutes) : 0,
            ReturnedTodayCount = await scopedOrders.CountAsync(order => order.ReturnedAtUtc >= startUtc && order.ReturnedAtUtc < endUtc),
            VerifiedTodayCount = await scopedOrders.CountAsync(order => order.VerifiedAtUtc >= startUtc && order.VerifiedAtUtc < endUtc)
        };
    }

    public async Task<DiagnosticOrderDto> GetOrderForVerificationAsync(long orderId)
    {
        var role = GetVerificationRole();
        var facilityIds = await GetVerificationFacilityIdsAsync(role);
        var order = await WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds)
            .Where(order => order.Id == orderId)
            .Select(order => new { order.Status, order.VerificationStatus, OrderingDoctorUserId = order.OrderingDoctor.UserId }).FirstOrDefaultAsync();
        if (order == null || !(order.Status == DiagnosticOrderStatus.Completed || order.VerificationStatus == DiagnosticVerificationStatus.Pending || order.VerificationStatus == DiagnosticVerificationStatus.Returned))
            throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc chưa gửi duyệt.");
        if (role == RoleNames.Doctor && order.OrderingDoctorUserId == GetUserId() && order.VerificationStatus == DiagnosticVerificationStatus.Pending)
            throw OrderingDoctorVerificationError();
        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    private string? CurrentVerificationRole()
    {
        var principal = _httpContextAccessor?.HttpContext?.User;
        return new[] { RoleNames.DiagnosticTechnician, RoleNames.LabSupervisor, RoleNames.Doctor }
            .FirstOrDefault(role => principal?.IsInRole(role) == true);
    }

    private string GetVerificationRole() => CurrentVerificationRole() ??
        throw new NotFoundException("Không có quyền duyệt kết quả.");

    private Task<List<long>> GetVerificationFacilityIdsAsync(string role)
    {
        var userId = GetUserId();
        return _dbContext.StaffFacilityAssignments.AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsActive && assignment.Role == role)
            .Select(assignment => assignment.FacilityId).Distinct().ToListAsync();
    }

    private const int MaxTurnaroundRangeDays = 31;
    private const int MaxSlowestOrders = 20;
    private const string SegmentOrderToReceive = "OrderToReceive";
    private const string SegmentReceiveToResult = "ReceiveToResult";
    private const string SegmentResultToVerify = "ResultToVerify";
    private const string SegmentTotal = "Total";

    private sealed class TurnaroundOrderRow
    {
        public long Id { get; init; }
        public string OrderCode { get; init; } = string.Empty;
        public Guid? PatientUserId { get; init; }
        public string? PatientFullName { get; init; }
        public DateTime OrderedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public DiagnosticVerificationStatus VerificationStatus { get; init; }
        public DateTime? SubmittedForVerificationAtUtc { get; init; }
        public DateTime? VerifiedAtUtc { get; init; }
        public List<TurnaroundItemRow> Items { get; init; } = new();
    }

    private sealed class TurnaroundItemRow
    {
        public long ServiceId { get; init; }
        public string ServiceCode { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public DiagnosticCategory Category { get; init; }
        public DateTime? ReceivedAtUtc { get; init; }
        public DateTime? ResultedAtUtc { get; init; }
    }

    public async Task<DiagnosticTurnaroundReportDto> GetTurnaroundReportAsync(DateOnly from, DateOnly to, DiagnosticCategory? category)
    {
        if (_httpContextAccessor?.HttpContext?.User.IsInRole(RoleNames.LabSupervisor) != true)
            throw new ForbiddenException("Chỉ trưởng khoa xét nghiệm được xem báo cáo thời gian trả kết quả.");
        if (from > to)
            throw new BusinessException("VALIDATION_ERROR", "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        if (to.DayNumber - from.DayNumber + 1 > MaxTurnaroundRangeDays)
            throw new BusinessException("VALIDATION_ERROR", $"Khoảng thời gian tối đa là {MaxTurnaroundRangeDays} ngày.");

        var facilityIds = await GetVerificationFacilityIdsAsync(RoleNames.LabSupervisor);
        var startUtc = _dateTimeProvider.ConvertVietnamToUtc(from.ToDateTime(TimeOnly.MinValue));
        var endUtc = _dateTimeProvider.ConvertVietnamToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var query = WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds)
            .Where(order => order.Status == DiagnosticOrderStatus.Completed && order.CompletedAtUtc.HasValue &&
                            order.CompletedAtUtc.Value >= startUtc && order.CompletedAtUtc.Value < endUtc);
        if (category.HasValue)
            query = query.Where(order => order.Items.Any(item =>
                item.Status != DiagnosticItemStatus.Cancelled && item.DiagnosticService.Category == category.Value));

        var rows = await query
            .Select(order => new TurnaroundOrderRow
            {
                Id = order.Id,
                OrderCode = order.OrderCode,
                PatientUserId = order.Patient.UserId,
                PatientFullName = order.Patient.FullName,
                OrderedAtUtc = order.OrderedAtUtc,
                CompletedAtUtc = order.CompletedAtUtc,
                VerificationStatus = order.VerificationStatus,
                SubmittedForVerificationAtUtc = order.SubmittedForVerificationAtUtc,
                VerifiedAtUtc = order.VerifiedAtUtc,
                Items = order.Items
                    .Where(item => item.Status != DiagnosticItemStatus.Cancelled)
                    .Select(item => new TurnaroundItemRow
                    {
                        ServiceId = item.DiagnosticServiceId,
                        ServiceCode = item.DiagnosticService.Code,
                        ServiceName = item.DiagnosticService.Name,
                        Category = item.DiagnosticService.Category,
                        ReceivedAtUtc = item.ReceivedAtUtc,
                        ResultedAtUtc = item.Result != null ? item.Result.ResultedAtUtc : (DateTime?)null
                    }).ToList()
            })
            .ToListAsync();

        // Same patient name fallback as MapBatchedOrder: linked user account → patient record → generic label.
        var patientUserIds = rows.Where(row => row.PatientUserId.HasValue).Select(row => row.PatientUserId!.Value).Distinct().ToList();
        var patientUserNames = patientUserIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking().Where(user => patientUserIds.Contains(user.Id))
                .Select(user => new { user.Id, user.FullName }).ToDictionaryAsync(user => user.Id, user => user.FullName);
        string PatientName(TurnaroundOrderRow row) =>
            (row.PatientUserId.HasValue && patientUserNames.TryGetValue(row.PatientUserId.Value, out var userName) ? userName : null)
            ?? row.PatientFullName ?? "Bệnh nhân";

        var orders = rows.Select(row =>
        {
            var firstReceived = row.Items.Where(item => item.ReceivedAtUtc.HasValue).Min(item => item.ReceivedAtUtc);
            var lastResulted = row.Items.Where(item => item.ResultedAtUtc.HasValue).Max(item => item.ResultedAtUtc);
            // An order's target is the most lenient target among the categories it actually contains.
            var target = row.Items.Count == 0
                ? _tatOptions.TargetFor(DiagnosticCategory.Other)
                : row.Items.Max(item => _tatOptions.TargetFor(item.Category));
            var total = ElapsedMinutes(row.OrderedAtUtc, row.CompletedAtUtc);
            return new
            {
                Row = row,
                OrderToReceive = ElapsedMinutes(row.OrderedAtUtc, firstReceived),
                ReceiveToResult = ElapsedMinutes(firstReceived, lastResulted),
                ResultToVerify = row.VerificationStatus == DiagnosticVerificationStatus.Verified
                    ? ElapsedMinutes(row.SubmittedForVerificationAtUtc, row.VerifiedAtUtc)
                    : null,
                Total = total,
                Target = target,
                WithinTarget = total.HasValue && total.Value <= target
            };
        }).ToList();

        // Only orders with a usable total can be judged against their target.
        var evaluatedCount = orders.Count(order => order.Total.HasValue);
        var withinTargetCount = orders.Count(order => order.WithinTarget);

        var byService = orders
            .SelectMany(order => order.Row.Items
                .Where(item => !category.HasValue || item.Category == category.Value)
                .DistinctBy(item => item.ServiceId)
                .Select(item => new { Item = item, order.Total }))
            .GroupBy(entry => entry.Item.ServiceId)
            .Select(group =>
            {
                var first = group.First().Item;
                var target = _tatOptions.TargetFor(first.Category);
                var totals = group.Where(entry => entry.Total.HasValue).Select(entry => entry.Total!.Value).ToList();
                return new DiagnosticTurnaroundServiceDto
                {
                    ServiceCode = first.ServiceCode,
                    ServiceName = first.ServiceName,
                    Category = first.Category.ToString(),
                    OrderCount = group.Count(),
                    MedianTotalMinutes = Median(totals),
                    P90TotalMinutes = NearestRankP90(totals),
                    TargetMinutes = target,
                    WithinTargetPercent = Percent(totals.Count(total => total <= target), totals.Count)
                };
            })
            .OrderByDescending(service => service.P90TotalMinutes ?? double.MinValue)
            .ThenBy(service => service.ServiceCode, StringComparer.Ordinal)
            .ToList();

        var slowestOrders = orders
            .Where(order => order.Total.HasValue && !order.WithinTarget)
            .OrderByDescending(order => order.Total!.Value)
            .ThenBy(order => order.Row.Id)
            .Take(MaxSlowestOrders)
            .Select(order => new DiagnosticTurnaroundOrderDto
            {
                OrderId = order.Row.Id,
                OrderCode = order.Row.OrderCode,
                PatientName = PatientName(order.Row),
                ServiceNames = order.Row.Items.Select(item => item.ServiceName).Distinct().ToList(),
                OrderedAtUtc = DateTime.SpecifyKind(order.Row.OrderedAtUtc, DateTimeKind.Utc),
                CompletedAtUtc = DateTime.SpecifyKind(order.Row.CompletedAtUtc!.Value, DateTimeKind.Utc),
                TotalMinutes = Math.Round(order.Total!.Value, 1, MidpointRounding.AwayFromZero),
                TargetMinutes = order.Target
            })
            .ToList();

        return new DiagnosticTurnaroundReportDto
        {
            From = from,
            To = to,
            Category = category?.ToString(),
            OrderCount = orders.Count,
            Segments = new List<DiagnosticTurnaroundSegmentDto>
            {
                Segment(SegmentOrderToReceive, orders.Select(order => order.OrderToReceive)),
                Segment(SegmentReceiveToResult, orders.Select(order => order.ReceiveToResult)),
                Segment(SegmentResultToVerify, orders.Select(order => order.ResultToVerify)),
                Segment(SegmentTotal, orders.Select(order => order.Total))
            },
            WithinTargetCount = withinTargetCount,
            WithinTargetPercent = Percent(withinTargetCount, evaluatedCount),
            ByService = byService,
            SlowestOrders = slowestOrders
        };
    }

    /// <summary>Minutes between two instants, or null when either is missing or the span is negative.</summary>
    private static double? ElapsedMinutes(DateTime? start, DateTime? end)
    {
        if (!start.HasValue || !end.HasValue) return null;
        var minutes = (end.Value - start.Value).TotalMinutes;
        return minutes < 0 ? null : minutes;
    }

    private static DiagnosticTurnaroundSegmentDto Segment(string key, IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return new DiagnosticTurnaroundSegmentDto
        {
            Key = key,
            Count = present.Count,
            MedianMinutes = Median(present),
            P90Minutes = NearestRankP90(present)
        };
    }

    private static double? Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return Math.Round(median, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Nearest-rank P90: the ceil(0.9 × n)-th smallest value (integer arithmetic avoids float drift).</summary>
    private static double? NearestRankP90(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(value => value).ToList();
        var rank = (9 * sorted.Count + 9) / 10;
        return Math.Round(sorted[rank - 1], 1, MidpointRounding.AwayFromZero);
    }

    private static double? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round(100.0 * part / whole, 1, MidpointRounding.AwayFromZero);

    private static BusinessException OrderingDoctorVerificationError() => new("ORDERING_DOCTOR_CANNOT_VERIFY", "Bác sĩ chỉ định không được tự duyệt kết quả phiếu của mình.");

    private async Task NotifyLabSupervisorsAsync(DiagnosticOrder order)
    {
        var facilityId = order.FacilityId ?? order.PatientVisit?.FacilityId ?? order.Appointment?.FacilityId;
        var userIds = await (from assignment in _dbContext.StaffFacilityAssignments
            join user in _dbContext.Users on assignment.UserId equals user.Id
            join userRole in _dbContext.UserRoles on user.Id equals userRole.UserId
            join role in _dbContext.Roles on userRole.RoleId equals role.Id
            where assignment.FacilityId == facilityId && assignment.IsActive && assignment.Role == RoleNames.LabSupervisor &&
                user.IsActive && role.Name == RoleNames.LabSupervisor
            select user.Id).Distinct().ToListAsync();
        foreach (var userId in userIds)
            _dbContext.Notifications.Add(new Notification
            {
                UserId = userId, Type = NotificationType.Diagnostic, Title = "Có phiếu kết quả chờ duyệt",
                Message = $"Phiếu #{order.OrderCode} đang chờ duyệt kết quả.", Route = $"/lab/orders/{order.Id}",
                RelatedEntityType = "DiagnosticOrder", RelatedEntityId = order.Id.ToString(),
                DedupeKey = $"diag_pending_{order.Id}_{userId}_{order.ReturnCount}", CreatedAtUtc = DateTime.UtcNow
            });
    }

    private async Task<DiagnosticOrder> LoadPendingVerificationAsync(long orderId, string? rowVersion)
    {
        var order = await _dbContext.DiagnosticOrders.Include(order => order.Appointment).Include(order => order.PatientVisit)
            .Include(order => order.OrderingDoctor)
            .Include(order => order.Items).ThenInclude(item => item.Result)
            .Include(order => order.Items).ThenInclude(item => item.DiagnosticService).FirstOrDefaultAsync(order => order.Id == orderId);
        if (order == null) throw new NotFoundException("Phiếu chỉ định không tồn tại.");
        await LoadLinkedVisitAppointmentAsync(order);
        var role = GetVerificationRole();
        if (role == RoleNames.DiagnosticTechnician) await EnsureTechnicianFacilityScopeAsync(order);
        else
        {
            var facilityIds = await GetVerificationFacilityIdsAsync(role);
            if (!await WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds).AnyAsync(candidate => candidate.Id == order.Id))
                throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc không thuộc cơ sở được phân công.");
        }
        if (rowVersion != null) ValidateRowVersion(order.RowVersion, rowVersion);
        if (order.Status != DiagnosticOrderStatus.InProgress || order.VerificationStatus != DiagnosticVerificationStatus.Pending || !order.SubmittedForVerificationByUserId.HasValue)
            throw new BusinessException("INVALID_STATE", "Phiếu không ở trạng thái chờ duyệt.");
        var userId = GetUserId();
        if (role == RoleNames.Doctor && order.OrderingDoctor.UserId == userId) throw OrderingDoctorVerificationError();
        if (userId == order.SubmittedForVerificationByUserId || order.Items.Any(item => item.Result?.ResultedByUserId == userId))
            throw new BusinessException("SELF_VERIFICATION_NOT_ALLOWED", "Người nhập kết quả không được tự duyệt. Cần một kỹ thuật viên khác duyệt.");
        return order;
    }

    private void AddVerificationAudit(DiagnosticOrder order, string action, string? note) => _dbContext.SystemAuditLogs.Add(new SystemAuditLog
    {
        UserId = GetUserId(), Action = action, EntityName = "DiagnosticOrder", EntityId = order.Id.ToString(),
        Description = $"VerifiedByRole={GetVerificationRole()}; Phiếu #{order.OrderCode}; {note}", CreatedAt = DateTime.UtcNow
    });

    private async Task<DiagnosticOrderDto> SaveVerificationAsync(DiagnosticOrder order)
    {
        try { await _dbContext.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu phiếu chỉ định đã bị thay đổi bởi phiên làm việc khác. Vui lòng tải lại trang.");
        }
        return (await GetOrderDtoByIdAsync(order.Id))!;
    }

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static bool RequiresVerification(DiagnosticOrder order) => order.Items.Any(item =>
        item.Status != DiagnosticItemStatus.Cancelled && item.DiagnosticService.SpecimenType != null);

    private static void EnsureResultsEditable(DiagnosticOrder order)
    {
        if (order.VerificationStatus == DiagnosticVerificationStatus.Pending)
            throw new BusinessException("RESULTS_LOCKED_PENDING_VERIFICATION", "Kết quả đang chờ duyệt, không thể chỉnh sửa.");
    }

    private async Task FinalizeOrderAsync(DiagnosticOrder order, Guid completedByUserId)
    {
        var userId = GetUserId();
        order.Status = DiagnosticOrderStatus.Completed;
        order.CompletedAtUtc = DateTime.UtcNow;
        order.CompletedByUserId = completedByUserId;
        order.RowVersion = Guid.NewGuid().ToByteArray();

        _dbContext.SystemAuditLogs.Add(new SystemAuditLog
        {
            UserId = userId,
            Action = "DiagnosticOrderCompleted",
            EntityName = "DiagnosticOrder",
            EntityId = order.Id.ToString(),
            Description = $"Kỹ thuật viên hoàn tất phiếu chỉ định #{order.OrderCode}.",
            CreatedAt = DateTime.UtcNow
        });

        // Notify ordering doctor
        var doctorUser = await (from d in _dbContext.Doctors
                                join u in _dbContext.Users on d.UserId equals u.Id
                                where d.Id == order.OrderingDoctorId
                                select u).FirstOrDefaultAsync();

        if (doctorUser != null)
        {
            _dbContext.Notifications.Add(new Notification
            {
                UserId = doctorUser.Id,
                Type = NotificationType.Diagnostic,
                Title = "Có kết quả cận lâm sàng",
                Message = $"Phiếu chỉ định #{order.OrderCode} cho bệnh nhân đã có đầy đủ kết quả.",
                Route = order.AppointmentId.HasValue
                    ? $"/doctor/appointments/{order.AppointmentId}/examination"
                    : $"/doctor/visits/{order.PatientVisitId}/examination",
                RelatedEntityType = "DiagnosticOrder",
                RelatedEntityId = order.Id.ToString(),
                DedupeKey = $"diag_completed_doc_{order.Id}_{doctorUser.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Update visit status to ResultsReady if connected to a PatientVisit
        var visit = await FindOrderVisitAsync(order);

        if (visit?.Status == VisitStatus.WaitingForDiagnostics)
        {
            var remaining = await RemainingVisitOrderStatusesAsync(visit, order.Id);
            var otherPending = remaining.Any(status => status != DiagnosticOrderStatus.Completed);

            if (!otherPending)
            {
                visit.Status = VisitStatus.ResultsReady;
                visit.UpdatedAtUtc = _dateTimeProvider.UtcNow;
            }
        }

    }

    private Task<PatientVisit?> FindOrderVisitAsync(DiagnosticOrder order) =>
        _dbContext.PatientVisits.FirstOrDefaultAsync(visit =>
            order.PatientVisitId.HasValue ? visit.Id == order.PatientVisitId.Value :
            order.AppointmentId.HasValue && visit.AppointmentId == order.AppointmentId.Value);

    private Task<List<DiagnosticOrderStatus>> RemainingVisitOrderStatusesAsync(PatientVisit visit, long currentOrderId) =>
        _dbContext.DiagnosticOrders.AsNoTracking()
            .Where(order => order.Id != currentOrderId && order.Status != DiagnosticOrderStatus.Cancelled &&
                (order.PatientVisitId == visit.Id || (visit.AppointmentId.HasValue && order.AppointmentId == visit.AppointmentId)))
            .Select(order => order.Status).ToListAsync();

    public async Task<PagedResult<DiagnosticOrderDto>> GetPatientOrdersAsync(int page, int pageSize)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var query = _dbContext.DiagnosticOrders
            .AsNoTracking()
            .Where(o => o.PatientId == patient.Id);

        var totalItems = await query.CountAsync();
        var orderIds = await query
            .OrderByDescending(o => o.OrderedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => o.Id)
            .ToListAsync();

        var items = (await GetOrderDtosByIdsAsync(orderIds))
            .Select(RedactUnpublishedResultsForPatient).ToList();

        return new PagedResult<DiagnosticOrderDto>(items, totalItems, page, pageSize);
    }

    public async Task<DiagnosticOrderDto> GetPatientOrderByIdAsync(long orderId)
    {
        var userId = GetUserId();
        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null)
            throw new NotFoundException("Hồ sơ bệnh nhân không tồn tại.");

        var order = await _dbContext.DiagnosticOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.PatientId == patient.Id);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc không thuộc quyền xem của bạn.");

        return RedactUnpublishedResultsForPatient((await GetOrderDtoByIdAsync(orderId))!);
    }

    private static DiagnosticOrderDto RedactUnpublishedResultsForPatient(DiagnosticOrderDto order)
    {
        var published = Enum.TryParse<DiagnosticOrderStatus>(order.Status, out var status) &&
            DiagnosticResultPublicationPolicy.IsPublishedToPatient(status, order.ReviewedAtUtc);
        order.SubmittedForVerificationByUserName = null;
        order.VerifiedByUserName = null;
        order.VerifiedByRole = null;
        order.ReturnedByUserName = null;
        order.VerificationNote = null;
        order.ReturnReason = null;
        order.HasDeltaFailure = false;
        foreach (var item in order.Items)
        {
            if (!published) item.Result = null;
            foreach (var value in item.Result?.Values ?? new List<DiagnosticResultValueDto>())
            {
                value.PreviousValue = null;
                value.PreviousResultedAtUtc = null;
                value.PreviousOrderCode = null;
                value.DeltaChange = null;
                value.DeltaPercent = null;
                value.IsDeltaFailure = false;
            }
            item.SpecimenBarcode = null;
            item.CollectedByUserName = null;
            item.ReceivedByUserName = null;
            item.RejectedByUserName = null;
            item.RejectionNote = null;
        }
        return order;
    }

    private async Task<List<DiagnosticOrderDto>> GetOrderDtosByIdsAsync(List<long> orderIds)
    {
        if (orderIds.Count == 0) return new();
        var rows = await _dbContext.DiagnosticOrders.AsNoTracking().AsSplitQuery()
            .Include(o => o.Facility)
            .Include(o => o.Appointment).Include(o => o.PatientVisit).Include(o => o.Patient)
            .Include(o => o.OrderingDoctor).ThenInclude(d => d.DoctorSpecialties).ThenInclude(ds => ds.Specialty)
            .Include(o => o.ReviewedByDoctor)
            .Where(o => orderIds.Contains(o.Id))
            .Select(order => new
            {
                Order = order,
                VerificationAudit = _dbContext.SystemAuditLogs.Where(audit => audit.EntityName == "DiagnosticOrder" &&
                    audit.EntityId == order.Id.ToString() && audit.Action == "DiagnosticResultsVerified")
                    .OrderByDescending(audit => audit.Id).Select(audit => audit.Description).FirstOrDefault()
            }).ToListAsync();
        var orders = rows.Select(row => row.Order).ToList();

        // Keep root/specialties split, but load the entire item graph in its existing
        // single command so parameter/value collections do not add split commands.
        var items = await _dbContext.DiagnosticOrderItems.AsNoTrackingWithIdentityResolution().AsSingleQuery()
            .Include(i => i.DiagnosticService).ThenInclude(s => s.Parameters.Where(p => p.IsActive))
            .Include(i => i.Result).ThenInclude(r => r!.Values)
            .Where(i => orderIds.Contains(i.DiagnosticOrderId)).OrderBy(i => i.Id).ToListAsync();
        var itemsByOrder = items.ToLookup(i => i.DiagnosticOrderId);
        foreach (var order in orders)
            order.Items = itemsByOrder[order.Id].ToList();

        var userIds = orders.SelectMany(order => new Guid?[]
            {
                order.Patient.UserId, order.OrderingDoctor.UserId, order.ReviewedByDoctor?.UserId,
                order.StartedByUserId, order.CompletedByUserId,
                order.SubmittedForVerificationByUserId, order.VerifiedByUserId, order.ReturnedByUserId
            }.Concat(order.Items.Where(item => item.Result != null)
                .Select(item => (Guid?)item.Result!.ResultedByUserId))
                .Concat(order.Items.SelectMany(item => new[] { item.CollectedByUserId, item.ReceivedByUserId, item.RejectedByUserId })))
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var users = (await _dbContext.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName, user.PhoneNumber }).ToListAsync())
            .ToDictionary(user => user.Id, user => (user.FullName, user.PhoneNumber));
        var byId = rows.ToDictionary(row => row.Order.Id, row => MapBatchedOrder(row.Order, users, row.VerificationAudit));
        return orderIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private DiagnosticOrderDto MapBatchedOrder(DiagnosticOrder order,
        IReadOnlyDictionary<Guid, (string FullName, string? PhoneNumber)> users, string? verificationAudit)
    {
        string? UserName(Guid? id) => id.HasValue && users.TryGetValue(id.Value, out var user) ? user.FullName : null;
        var patientUserName = UserName(order.Patient.UserId);
        var patientUserPhone = order.Patient.UserId.HasValue && users.TryGetValue(order.Patient.UserId.Value, out var patientUser)
            ? patientUser.PhoneNumber : null;
        var orderingDoctorName = UserName(order.OrderingDoctor.UserId);
        var reviewedDoctorName = UserName(order.ReviewedByDoctor?.UserId);
        var startedUserName = UserName(order.StartedByUserId);
        var completedUserName = UserName(order.CompletedByUserId);
        int? patientAge = null;
        if (order.Patient.DateOfBirth.HasValue)
        {
            var today = _dateTimeProvider.VietnamToday;
            var age = today.Year - order.Patient.DateOfBirth.Value.Year;
            if (today < order.Patient.DateOfBirth.Value.AddYears(age)) age--;
            patientAge = age;
        }

        var specialtyName = order.OrderingDoctor.DoctorSpecialties
            .FirstOrDefault(ds => ds.IsPrimary)?.Specialty?.Name ??
            order.OrderingDoctor.DoctorSpecialties.FirstOrDefault()?.Specialty?.Name ??
            "Chuyên khoa";

        var itemDtos = order.Items.OrderBy(i => i.Id).Select(i =>
        {
            DiagnosticResultDto? resDto = null;
            if (i.Result != null)
            {
                var recorderName = UserName(i.Result.ResultedByUserId);
                resDto = new DiagnosticResultDto
                {
                    Id = i.Result.Id,
                    DiagnosticOrderItemId = i.Result.DiagnosticOrderItemId,
                    ResultText = i.Result.ResultText,
                    Conclusion = i.Result.Conclusion,
                    ReferenceRange = i.Result.ReferenceRange,
                    Unit = i.Result.Unit,
                    Values = MapValues(i.Result),
                    HasCriticalValue = i.Result.HasCriticalValue,
                    CriticalNotifiedAtUtc = i.Result.CriticalNotifiedAtUtc,
                    ResultedAtUtc = i.Result.ResultedAtUtc,
                    ResultedByUserId = i.Result.ResultedByUserId,
                    ResultedByUserName = recorderName ?? "Kỹ thuật viên",
                    RowVersion = i.Result.RowVersion != null ? Convert.ToBase64String(i.Result.RowVersion) : null
                };
            }

            return new DiagnosticOrderItemDto
            {
                Id = i.Id,
                DiagnosticOrderId = i.DiagnosticOrderId,
                DiagnosticServiceId = i.DiagnosticServiceId,
                ServiceCode = i.DiagnosticService?.Code ?? string.Empty,
                ServiceName = i.DiagnosticService?.Name ?? string.Empty,
                Category = i.DiagnosticService?.Category.ToString() ?? string.Empty,
                PreparationInstructions = i.DiagnosticService?.PreparationInstructions,
                Price = i.DiagnosticService?.Price,
                IsPackageCovered = i.IsPackageCovered,
                PackageRegistrationId = i.PackageRegistrationId,
                Status = i.Status.ToString(),
                RowVersion = i.RowVersion != null ? Convert.ToBase64String(i.RowVersion) : null,
                Parameters = MapParameters(i.DiagnosticService, order.Patient.Gender),
                SpecimenStatus = i.SpecimenStatus.ToString(), SpecimenType = i.DiagnosticService?.SpecimenType,
                ContainerType = i.DiagnosticService?.ContainerType, SpecimenBarcode = i.SpecimenBarcode,
                CollectedAtUtc = i.CollectedAtUtc, CollectedByUserName = UserName(i.CollectedByUserId),
                ReceivedAtUtc = i.ReceivedAtUtc, ReceivedByUserName = UserName(i.ReceivedByUserId),
                RejectedAtUtc = i.RejectedAtUtc, RejectedByUserName = UserName(i.RejectedByUserId),
                RejectionReason = i.RejectionReason?.ToString(), RejectionNote = i.RejectionNote, RecollectCount = i.RecollectCount,
                Result = resDto
            };
        }).ToList();

        return new DiagnosticOrderDto
        {
            Id = order.Id,
            FacilityName = order.Facility?.Name,
            OrderCode = order.OrderCode,
            AppointmentId = order.AppointmentId,
            AppointmentCode = order.Appointment?.AppointmentCode ?? (order.PatientVisit != null ? order.PatientVisit.VisitCode : string.Empty),
            PatientVisitId = order.PatientVisitId,
            VisitCode = order.PatientVisit?.VisitCode,
            AppointmentDate = order.Appointment?.AppointmentDate ?? (order.PatientVisit != null ? order.PatientVisit.VisitDate : DateOnly.FromDateTime(order.OrderedAtUtc)),
            PatientId = order.PatientId,
            PatientName = patientUserName ?? order.Patient.FullName ?? "Bệnh nhân",
            PatientPhone = patientUserPhone ?? order.Patient.PhoneNumber ?? string.Empty,
            PatientGender = order.Patient.Gender.HasValue ? order.Patient.Gender.Value.ToString() : string.Empty,
            PatientDob = order.Patient.DateOfBirth,
            PatientAge = patientAge,
            OrderingDoctorId = order.OrderingDoctorId,
            OrderingDoctorName = orderingDoctorName != null ? (string.IsNullOrWhiteSpace(order.OrderingDoctor.AcademicTitle) ? orderingDoctorName : $"{order.OrderingDoctor.AcademicTitle}. {orderingDoctorName}") : "Bác sĩ",
            SpecialtyName = specialtyName,
            ClinicalIndication = order.ClinicalIndication,
            Note = order.Note,
            Status = order.Status.ToString(),
            OrderedAtUtc = order.OrderedAtUtc,
            StartedAtUtc = order.StartedAtUtc,
            CompletedAtUtc = order.CompletedAtUtc,
            CancelledAtUtc = order.CancelledAtUtc,
            StartedByUserName = startedUserName,
            CompletedByUserName = completedUserName,
            ReviewedAtUtc = order.ReviewedAtUtc,
            ReviewedByDoctorName = reviewedDoctorName,
            VerificationStatus = order.VerificationStatus.ToString(),
            SubmittedForVerificationAtUtc = AsUtc(order.SubmittedForVerificationAtUtc),
            SubmittedForVerificationByUserName = UserName(order.SubmittedForVerificationByUserId),
            VerifiedAtUtc = AsUtc(order.VerifiedAtUtc), VerifiedByUserName = UserName(order.VerifiedByUserId),
            VerifiedByRole = new[] { RoleNames.DiagnosticTechnician, RoleNames.LabSupervisor, RoleNames.Doctor }
                .FirstOrDefault(role => verificationAudit?.StartsWith($"VerifiedByRole={role};", StringComparison.Ordinal) == true),
            VerificationNote = order.VerificationNote, ReturnedAtUtc = AsUtc(order.ReturnedAtUtc),
            ReturnedByUserName = UserName(order.ReturnedByUserId), ReturnReason = order.ReturnReason, ReturnCount = order.ReturnCount,
            CanCurrentUserVerify = order.Status == DiagnosticOrderStatus.InProgress && order.VerificationStatus == DiagnosticVerificationStatus.Pending &&
                CurrentVerificationRole() != null && _currentUserService.UserId.HasValue &&
                (CurrentVerificationRole() != RoleNames.Doctor || order.OrderingDoctor.UserId != _currentUserService.UserId) &&
                _currentUserService.UserId != order.SubmittedForVerificationByUserId &&
                order.Items.All(item => item.Result == null || item.Result.ResultedByUserId != _currentUserService.UserId),
            RowVersion = order.RowVersion != null ? Convert.ToBase64String(order.RowVersion) : null,
            HasDeltaFailure = order.Items.Any(item => item.Status != DiagnosticItemStatus.Cancelled &&
                item.Result?.Values.Any(value => value.IsDeltaFailure) == true),
            Items = itemDtos
        };
    }

    private async Task<DiagnosticOrderDto?> GetOrderDtoByIdAsync(long orderId)
    {
        // A shared query and mapper keep single/list DTOs and the user lookup identical.
        return (await GetOrderDtosByIdsAsync(new List<long> { orderId })).SingleOrDefault();
    }
}
