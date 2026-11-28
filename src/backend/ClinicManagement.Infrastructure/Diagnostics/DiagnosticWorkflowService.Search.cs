using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Common;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
    private IQueryable<DiagnosticOrder> FilterOrdersBySearch(IQueryable<DiagnosticOrder> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;

        var term = search.Trim().ToLowerInvariant();
        var code = DisplayCodeSearch.Canonical(search);
        var prefix = DisplayCodeSearch.Compact(search);
        return query.Where(o =>
            o.OrderCode.ToUpper().Contains(prefix) || o.ShortCode == code || (o.ShortCode != null && o.ShortCode.StartsWith(prefix)) ||
            (o.Appointment != null && (o.Appointment.AppointmentCode.ToUpper().Contains(prefix) ||
                o.Appointment.ShortCode == code || (o.Appointment.ShortCode != null && o.Appointment.ShortCode.StartsWith(prefix)))) ||
            (o.PatientVisit != null && (o.PatientVisit.VisitCode.ToUpper().Contains(prefix) ||
                o.PatientVisit.ShortCode == code || (o.PatientVisit.ShortCode != null && o.PatientVisit.ShortCode.StartsWith(prefix)))) ||
            (o.Patient.FullName != null && o.Patient.FullName.ToLower().Contains(term)) ||
            (o.Patient.PhoneNumber != null && o.Patient.PhoneNumber.Contains(term)) ||
            o.Patient.MedicalRecordNumber.ToUpper().Contains(prefix) ||
            o.Patient.ShortCode == code || (o.Patient.ShortCode != null && o.Patient.ShortCode.StartsWith(prefix)) ||
            _dbContext.Users.Any(u => o.Patient.UserId != null && u.Id == o.Patient.UserId &&
                (u.FullName.ToLower().Contains(term) || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)))));
    }
}
