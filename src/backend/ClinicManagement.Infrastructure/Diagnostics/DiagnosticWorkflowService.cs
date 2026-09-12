using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Application.Diagnostics.Interfaces;
using ClinicManagement.Application.Doctors.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Diagnostics;

public class DiagnosticWorkflowService : IDiagnosticWorkflowService
{
    private readonly AppDbContext _dbContext;
    private readonly IDoctorContextService _doctorContextService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<DiagnosticWorkflowService> _logger;

    public DiagnosticWorkflowService(
        AppDbContext dbContext,
        IDoctorContextService doctorContextService,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        ILogger<DiagnosticWorkflowService> logger)
    {
        _dbContext = dbContext;
        _doctorContextService = doctorContextService;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null || currentUserId == Guid.Empty)
            throw new UnauthorizedException("Chưa đăng nhập.");
        return currentUserId.Value;
    }

    private Task<Doctor> GetCurrentDoctorAsync()
    {
        return _doctorContextService.GetCurrentActiveDoctorAsync();
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
            IsActive = s.IsActive
        }).ToList();
    }

    public async Task<DiagnosticOrderDto> CreateOrderForDoctorAsync(long appointmentId, CreateDiagnosticOrderRequest request)
    {
        var doctor = await GetCurrentDoctorAsync();
        var userId = GetUserId();

        var appointment = await _dbContext.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor.Id);

        if (appointment == null)
            throw new NotFoundException("Lịch hẹn không tồn tại hoặc không thuộc quyền quản lý.");

        if (appointment.Status != AppointmentStatus.InConsultation)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể tạo phiếu chỉ định cận lâm sàng khi lịch hẹn đang trong phiên khám (InConsultation).");

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
                        .Where(u => techUserIds.Contains(u.Id) && u.IsActive)
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

                if (patientUserId != Guid.Empty)
                {
                    _dbContext.Notifications.Add(new Notification
                    {
                        UserId = patientUserId,
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

        var result = new List<DiagnosticOrderDto>();
        foreach (var id in orderIds)
        {
            var dto = await GetOrderDtoByIdAsync(id);
            if (dto != null) result.Add(dto);
        }
        return result;
    }

    public async Task<DiagnosticOrderDto> GetOrderByIdForDoctorAsync(long orderId)
    {
        var doctor = await GetCurrentDoctorAsync();
        var order = await _dbContext.DiagnosticOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderingDoctorId == doctor.Id);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại hoặc không thuộc quyền quản lý.");

        return (await GetOrderDtoByIdAsync(orderId))!;
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
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var query = _dbContext.DiagnosticOrders
            .AsNoTracking()
            .Include(o => o.Appointment)
            .Include(o => o.Patient)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        if (date.HasValue)
        {
            query = query.Where(o => o.Appointment.AppointmentDate == date.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var clean = search.Trim().ToLower();
            query = query.Where(o => o.OrderCode.ToLower().Contains(clean) ||
                                     o.Appointment.AppointmentCode.ToLower().Contains(clean) ||
                                     _dbContext.Users.Any(u => u.Id == o.Patient.UserId && (u.FullName.ToLower().Contains(clean) || (u.PhoneNumber != null && u.PhoneNumber.Contains(clean)))));
        }

        var totalItems = await query.CountAsync();
        var orderIds = await query
            .OrderByDescending(o => o.OrderedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => o.Id)
            .ToListAsync();

        var items = new List<DiagnosticOrderDto>();
        foreach (var id in orderIds)
        {
            var dto = await GetOrderDtoByIdAsync(id);
            if (dto != null) items.Add(dto);
        }

        return new PagedResult<DiagnosticOrderDto>(items, totalItems, page, pageSize);
    }

    public async Task<DiagnosticOrderDto> GetTechnicianOrderByIdAsync(long orderId)
    {
        var dto = await GetOrderDtoByIdAsync(orderId);
        if (dto == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");
        return dto;
    }

    public async Task<TechnicianDiagnosticStatsDto> GetTechnicianStatsAsync()
    {
        var today = _dateTimeProvider.VietnamToday;
        var orderedCount = await _dbContext.DiagnosticOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.Ordered);
        var inProgressCount = await _dbContext.DiagnosticOrders.CountAsync(o => o.Status == DiagnosticOrderStatus.InProgress);

        var completedTodayCount = await _dbContext.DiagnosticOrders
            .CountAsync(o => o.Status == DiagnosticOrderStatus.Completed &&
                             o.CompletedAtUtc.HasValue &&
                             DateOnly.FromDateTime(o.CompletedAtUtc.Value.AddHours(7)) == today);

        return new TechnicianDiagnosticStatsDto
        {
            OrderedCount = orderedCount,
            InProgressCount = inProgressCount,
            CompletedTodayCount = completedTodayCount
        };
    }

    public async Task<DiagnosticOrderDto> StartOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request)
    {
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

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
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

        if (order.Status != DiagnosticOrderStatus.InProgress)
            throw new BusinessException("INVALID_STATE", "Chỉ có thể nhập kết quả khi phiếu chỉ định đang trong trạng thái InProgress.");

        var item = order.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            throw new NotFoundException("Dịch vụ chỉ định không tồn tại trong phiếu này.");

        if (item.Status == DiagnosticItemStatus.Cancelled)
            throw new BusinessException("ITEM_CANCELLED", "Dịch vụ chỉ định này đã bị hủy, không thể nhập kết quả.");

        if (request?.RowVersion != null)
        {
            ValidateRowVersion(item.RowVersion, request.RowVersion);
        }

        if (request == null || string.IsNullOrWhiteSpace(request.ResultText))
            throw new BusinessException("VALIDATION_ERROR", "Kết quả cận lâm sàng không được để trống.");

        if (item.Result == null)
        {
            item.Result = new DiagnosticResult
            {
                DiagnosticOrderItemId = item.Id,
                ResultText = request.ResultText.Trim(),
                Conclusion = request.Conclusion?.Trim(),
                ReferenceRange = request.ReferenceRange?.Trim(),
                Unit = request.Unit?.Trim(),
                ResultedAtUtc = DateTime.UtcNow,
                ResultedByUserId = userId,
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            _dbContext.DiagnosticResults.Add(item.Result);
        }
        else
        {
            item.Result.ResultText = request.ResultText.Trim();
            item.Result.Conclusion = request.Conclusion?.Trim();
            item.Result.ReferenceRange = request.ReferenceRange?.Trim();
            item.Result.Unit = request.Unit?.Trim();
            item.Result.ResultedAtUtc = DateTime.UtcNow;
            item.Result.ResultedByUserId = userId;
            item.Result.RowVersion = Guid.NewGuid().ToByteArray();
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

    public async Task<DiagnosticOrderDto> CompleteOrderAsync(long orderId, TransitionDiagnosticOrderRequest? request)
    {
        var userId = GetUserId();

        var order = await _dbContext.DiagnosticOrders
            .Include(o => o.Items)
                .ThenInclude(i => i.Result)
            .Include(o => o.Appointment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException("Phiếu chỉ định không tồn tại.");

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

        order.Status = DiagnosticOrderStatus.Completed;
        order.CompletedAtUtc = DateTime.UtcNow;
        order.CompletedByUserId = userId;
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
                Route = $"/doctor/appointments/{order.AppointmentId}/examination",
                RelatedEntityType = "DiagnosticOrder",
                RelatedEntityId = order.Id.ToString(),
                DedupeKey = $"diag_completed_doc_{order.Id}_{doctorUser.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        // Notify patient
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
                Title = "Kết quả cận lâm sàng đã sẵn sàng",
                Message = $"Phiếu chỉ định #{order.OrderCode} đã có kết quả. Bạn có thể xem kết quả trực tuyến.",
                Route = "/patient/diagnostic-results",
                RelatedEntityType = "DiagnosticOrder",
                RelatedEntityId = order.Id.ToString(),
                DedupeKey = $"diag_completed_pat_{order.Id}",
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

        var items = new List<DiagnosticOrderDto>();
        foreach (var id in orderIds)
        {
            var dto = await GetOrderDtoByIdAsync(id);
            if (dto != null) items.Add(dto);
        }

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

        return (await GetOrderDtoByIdAsync(orderId))!;
    }

    private async Task<DiagnosticOrderDto?> GetOrderDtoByIdAsync(long orderId)
    {
        var order = await _dbContext.DiagnosticOrders
            .AsNoTracking()
            .Include(o => o.Appointment)
            .Include(o => o.Patient)
            .Include(o => o.OrderingDoctor)
                .ThenInclude(d => d.DoctorSpecialties)
                    .ThenInclude(ds => ds.Specialty)
            .Include(o => o.ReviewedByDoctor)
            .Include(o => o.Items)
                .ThenInclude(i => i.DiagnosticService)
            .Include(o => o.Items)
                .ThenInclude(i => i.Result)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return null;

        var patientUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.Patient.UserId);
        var orderingDocUser = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.OrderingDoctor.UserId);
        var reviewedDocUser = order.ReviewedByDoctor != null
            ? await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.ReviewedByDoctor.UserId)
            : null;

        var startedUser = order.StartedByUserId.HasValue
            ? await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.StartedByUserId.Value)
            : null;

        var completedUser = order.CompletedByUserId.HasValue
            ? await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.CompletedByUserId.Value)
            : null;

        var resultRecorderIds = order.Items
            .Where(i => i.Result != null)
            .Select(i => i.Result!.ResultedByUserId)
            .Distinct()
            .ToList();

        var resultRecorders = await _dbContext.Users
            .AsNoTracking()
            .Where(u => resultRecorderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

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

        var itemDtos = order.Items.Select(i =>
        {
            DiagnosticResultDto? resDto = null;
            if (i.Result != null)
            {
                resultRecorders.TryGetValue(i.Result.ResultedByUserId, out var recorderName);
                resDto = new DiagnosticResultDto
                {
                    Id = i.Result.Id,
                    DiagnosticOrderItemId = i.Result.DiagnosticOrderItemId,
                    ResultText = i.Result.ResultText,
                    Conclusion = i.Result.Conclusion,
                    ReferenceRange = i.Result.ReferenceRange,
                    Unit = i.Result.Unit,
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
                Status = i.Status.ToString(),
                RowVersion = i.RowVersion != null ? Convert.ToBase64String(i.RowVersion) : null,
                Result = resDto
            };
        }).ToList();

        return new DiagnosticOrderDto
        {
            Id = order.Id,
            OrderCode = order.OrderCode,
            AppointmentId = order.AppointmentId,
            AppointmentCode = order.Appointment?.AppointmentCode ?? string.Empty,
            AppointmentDate = order.Appointment?.AppointmentDate ?? DateOnly.FromDateTime(order.OrderedAtUtc),
            PatientId = order.PatientId,
            PatientName = patientUser?.FullName ?? "Bệnh nhân",
            PatientPhone = patientUser?.PhoneNumber ?? string.Empty,
            PatientGender = order.Patient.Gender.HasValue ? order.Patient.Gender.Value.ToString() : string.Empty,
            PatientDob = order.Patient.DateOfBirth,
            PatientAge = patientAge,
            OrderingDoctorId = order.OrderingDoctorId,
            OrderingDoctorName = orderingDocUser != null ? (string.IsNullOrWhiteSpace(order.OrderingDoctor.AcademicTitle) ? orderingDocUser.FullName : $"{order.OrderingDoctor.AcademicTitle}. {orderingDocUser.FullName}") : "Bác sĩ",
            SpecialtyName = specialtyName,
            ClinicalIndication = order.ClinicalIndication,
            Note = order.Note,
            Status = order.Status.ToString(),
            OrderedAtUtc = order.OrderedAtUtc,
            StartedAtUtc = order.StartedAtUtc,
            CompletedAtUtc = order.CompletedAtUtc,
            CancelledAtUtc = order.CancelledAtUtc,
            StartedByUserName = startedUser?.FullName,
            CompletedByUserName = completedUser?.FullName,
            ReviewedAtUtc = order.ReviewedAtUtc,
            ReviewedByDoctorName = reviewedDocUser?.FullName,
            RowVersion = order.RowVersion != null ? Convert.ToBase64String(order.RowVersion) : null,
            Items = itemDtos
        };
    }
}
