using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI.Tools;

public sealed partial class RoleCopilotToolHandler
{
    private async Task<AiToolExecutionResult> LookupAppointmentAsync(AiToolExecutionContext context, string json, CancellationToken cancellationToken)
    {
        var facilities = await ResolveFacilityScopeAsync(context, nameof(AiActorRole.Receptionist), cancellationToken);
        if (facilities.Count == 0) return ScopeDenied();
        using var document = JsonDocument.Parse(json);
        var code = DisplayCodeSearch.Canonical(document.RootElement.GetProperty("appointmentCode").GetString()!);
        var appointment = await _db.Appointments.AsNoTracking()
            .Where(a => (a.ShortCode == code || a.AppointmentCode.ToUpper() == code) && a.FacilityId.HasValue && facilities.Contains(a.FacilityId.Value))
            .Select(a => new { a.Id, AppointmentCode = (a.ShortCode ?? a.AppointmentCode), a.AppointmentDate, a.StartTime, a.EndTime, status = a.Status.ToString(), patientName = a.Patient.FullName })
            .SingleOrDefaultAsync(cancellationToken);
        return appointment is null ? AiToolExecutionResult.Failed("NOT_FOUND", "Không tìm thấy lịch hẹn trong phạm vi cơ sở được phân quyền.") : Completed(appointment, "appointment_lookup", "Đã tra cứu lịch hẹn từ hệ thống.");
    }
}
