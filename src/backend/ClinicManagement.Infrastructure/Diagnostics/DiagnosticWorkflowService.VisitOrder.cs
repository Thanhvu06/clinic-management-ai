using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
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

        var clinicalIndication = await ResolveClinicalIndicationAsync(request.ClinicalIndication,
            visit.Id, visit.AppointmentId, visit.ChiefComplaint ?? visit.Appointment?.Reason);

        var cleanServiceIds = request.ServiceIds?.Distinct().ToList() ?? new List<long>();
        if (cleanServiceIds.Count == 0)
            throw new BusinessException("VALIDATION_ERROR", "Cần chọn ít nhất một dịch vụ cận lâm sàng.");

        var services = await _dbContext.DiagnosticServices
            .Where(s => cleanServiceIds.Contains(s.Id) && s.IsActive)
            .ToListAsync();

        if (services.Count != cleanServiceIds.Count)
            throw new BusinessException("INVALID_SERVICE", "Một hoặc nhiều dịch vụ chỉ định không tồn tại hoặc đã ngừng hoạt động.");

        await EnsureNoDuplicateServicesAsync(visit.Id, visit.AppointmentId, cleanServiceIds);
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
}
