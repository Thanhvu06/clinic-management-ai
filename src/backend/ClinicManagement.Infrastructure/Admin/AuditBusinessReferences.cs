using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

public static class AuditBusinessReferences
{
    public static async Task PopulateAsync(AppDbContext db, IDateTimeProvider clock, IReadOnlyList<SystemAuditLogDto> logs)
    {
        var ids = logs.Select(l => long.TryParse(l.EntityId, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
        var references = new Dictionary<(string Entity, long Id), string>();
        foreach (var item in await db.Appointments.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.AppointmentCode }).ToListAsync())
            references[("Appointment", item.Id)] = item.Code;
        foreach (var item in await db.PatientVisits.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.VisitCode }).ToListAsync())
            references[("PatientVisit", item.Id)] = item.Code;
        foreach (var item in await db.DiagnosticOrders.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.OrderCode }).ToListAsync())
            references[("DiagnosticOrder", item.Id)] = item.Code;
        foreach (var item in await db.Invoices.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.InvoiceCode }).ToListAsync())
            references[("Invoice", item.Id)] = item.Code;
        foreach (var item in await db.Payments.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.PaymentCode }).ToListAsync())
            references[("Payment", item.Id)] = item.Code;
        foreach (var item in await db.HealthPackageRegistrations.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.RegistrationCode }).ToListAsync())
            references[("HealthPackageRegistration", item.Id)] = item.Code;
        foreach (var item in await db.Prescriptions.Where(x => ids.Contains(x.Id)).ToListAsync())
            references[("Prescription", item.Id)] = BusinessDisplay.PrescriptionCode(item, clock);
        foreach (var item in await db.Patients.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.MedicalRecordNumber }).ToListAsync())
            references[("Patient", item.Id)] = item.Code;
        foreach (var item in await db.Specialties.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.SpecialtyCode }).ToListAsync())
            references[("Specialty", item.Id)] = item.Code;
        foreach (var log in logs)
            log.ReferenceCode = long.TryParse(log.EntityId, out var id)
                ? references.GetValueOrDefault((log.EntityName, id), "")
                : Guid.TryParse(log.EntityId, out _) ? "" : log.EntityId;
    }
}
