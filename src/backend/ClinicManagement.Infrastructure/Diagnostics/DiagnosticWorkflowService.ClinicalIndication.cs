using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
    private async Task<string> ResolveClinicalIndicationAsync(string? indication, long? visitId, long? appointmentId, string? reason)
    {
        if (!string.IsNullOrWhiteSpace(indication)) return indication.Trim();
        var summary = await _dbContext.VisitSummaries.AsNoTracking()
            .Where(summary => (visitId.HasValue && summary.PatientVisitId == visitId) ||
                (appointmentId.HasValue && summary.AppointmentId == appointmentId))
            .OrderByDescending(summary => summary.Id)
            .Select(summary => new { summary.Diagnosis, summary.ChiefComplaint }).FirstOrDefaultAsync();
        var fallback = new[] { summary?.Diagnosis, summary?.ChiefComplaint, reason }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        return fallback[..Math.Min(fallback.Length, 1000)];
    }
}
