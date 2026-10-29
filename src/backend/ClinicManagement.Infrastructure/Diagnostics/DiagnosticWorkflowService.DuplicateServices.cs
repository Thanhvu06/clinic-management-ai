using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
    private async Task EnsureNoDuplicateServicesAsync(long? visitId, long? appointmentId, List<long> serviceIds)
    {
        var duplicateName = await _dbContext.DiagnosticOrderItems.AsNoTracking()
            .Where(item => item.Status != DiagnosticItemStatus.Cancelled &&
                item.DiagnosticOrder.Status != DiagnosticOrderStatus.Cancelled &&
                ((visitId.HasValue && item.DiagnosticOrder.PatientVisitId == visitId) ||
                 (appointmentId.HasValue && item.DiagnosticOrder.AppointmentId == appointmentId)) &&
                serviceIds.Contains(item.DiagnosticServiceId))
            .OrderBy(item => item.Id)
            .Select(item => item.DiagnosticService.Name)
            .FirstOrDefaultAsync();
        if (duplicateName != null)
            throw new BusinessException("DUPLICATE_DIAGNOSTIC_SERVICE", $"Dịch vụ '{duplicateName}' đã được chỉ định trong lượt khám này.");
    }
}
