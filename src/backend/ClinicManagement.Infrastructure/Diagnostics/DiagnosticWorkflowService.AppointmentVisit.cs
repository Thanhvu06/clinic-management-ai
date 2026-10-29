using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
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

        var clinicalIndication = await ResolveClinicalIndicationAsync(request.ClinicalIndication,
            appointment.PatientVisit?.Id, appointment.Id, appointment.PatientVisit?.ChiefComplaint ?? appointment.Reason);

        var cleanServiceIds = request.ServiceIds?.Distinct().ToList() ?? new List<long>();
        if (cleanServiceIds.Count == 0)
            throw new BusinessException("VALIDATION_ERROR", "Cần chọn ít nhất một dịch vụ cận lâm sàng.");

        var services = await _dbContext.DiagnosticServices
            .Where(s => cleanServiceIds.Contains(s.Id) && s.IsActive)
            .ToListAsync();

        if (services.Count != cleanServiceIds.Count)
            throw new BusinessException("INVALID_SERVICE", "Một hoặc nhiều dịch vụ chỉ định không tồn tại hoặc đã ngừng hoạt động.");

        var visitId = await FindActiveAppointmentVisitIdAsync(appointment.Id);
        await EnsureNoDuplicateServicesAsync(visitId, appointment.Id, cleanServiceIds);
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
                    PatientVisitId = visitId,
                    FacilityId = resolvedFacilityId,
                    SourceAiActionId = sourceAiActionId,
                    PatientId = appointment.PatientId,
                    OrderingDoctorId = doctor.Id,
                    ClinicalIndication = clinicalIndication,
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

    private Task<long?> FindActiveAppointmentVisitIdAsync(long appointmentId) =>
        _dbContext.PatientVisits.AsNoTracking()
            .Where(v => v.AppointmentId == appointmentId && v.Status != VisitStatus.Cancelled)
            .Select(v => (long?)v.Id)
            .FirstOrDefaultAsync();
}
