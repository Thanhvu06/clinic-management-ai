using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Admin;

public static class AuditBusinessReferences
{
    public static IQueryable<SystemAuditLog> FindByCode(AppDbContext db, IQueryable<SystemAuditLog> logs, string search)
    {
        var entered = DisplayCodeSearch.Compact(search);
        var code = DisplayCodeSearch.Canonical(entered);
        var legacyPrescriptionId = DisplayCodeSearch.LegacyPrescriptionId(entered);
        var appointments = db.Appointments.Where(x => x.ShortCode == code || x.AppointmentCode.ToUpper() == entered);
        var registrations = db.HealthPackageRegistrations.Where(x => x.ShortCode == code || x.RegistrationCode.ToUpper() == entered);
        return logs.Where(log =>
            log.Description.ToUpper().Contains(entered) || log.Description.ToUpper().Contains(code) ||
            log.EntityId.ToUpper() == entered || log.EntityId.ToUpper() == code ||
            (log.EntityName == nameof(Appointment) && appointments.Any(x => x.Id.ToString() == log.EntityId)) ||
            (log.EntityName == nameof(AppointmentChangeRequest) && db.AppointmentChangeRequests.Any(r =>
                r.Id.ToString() == log.EntityId && appointments.Any(a => a.Id == r.AppointmentId))) ||
            (log.EntityName == nameof(RevisitRequest) && db.RevisitRequests.Any(r =>
                r.Id.ToString() == log.EntityId && appointments.Any(a => a.Id == r.AppointmentId))) ||
            (log.EntityName == nameof(Patient) && db.Patients.Any(x =>
                x.Id.ToString() == log.EntityId && (x.ShortCode == code || x.MedicalRecordNumber.ToUpper() == entered))) ||
            (log.EntityName == nameof(PatientVisit) && db.PatientVisits.Any(x =>
                x.Id.ToString() == log.EntityId && (x.ShortCode == code || x.VisitCode.ToUpper() == entered))) ||
            (log.EntityName == nameof(Invoice) && db.Invoices.Any(x =>
                x.Id.ToString() == log.EntityId && (x.ShortCode == code || x.InvoiceCode.ToUpper() == entered))) ||
            (log.EntityName == nameof(Payment) && db.Payments.Any(x =>
                x.Id.ToString() == log.EntityId && (x.ShortCode == code || x.PaymentCode.ToUpper() == entered))) ||
            (log.EntityName == nameof(Prescription) && db.Prescriptions.Any(x =>
                x.Id.ToString() == log.EntityId && (x.ShortCode == code || x.Id == legacyPrescriptionId))) ||
            (log.EntityName == nameof(DiagnosticOrder) && db.DiagnosticOrders.Any(x =>
                x.Id.ToString() == log.EntityId && (x.ShortCode == code || x.OrderCode.ToUpper() == entered))) ||
            (log.EntityName == nameof(HealthPackageRegistration) && registrations.Any(x =>
                x.Id.ToString() == log.EntityId || x.RegistrationCode == log.EntityId)));
    }

    public static async Task PopulateAsync(AppDbContext db, IDateTimeProvider clock, IReadOnlyList<SystemAuditLogDto> logs)
    {
        var ids = logs.Select(l => long.TryParse(l.EntityId, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
        var references = new Dictionary<(string Entity, long Id), string>();
        foreach (var item in await db.Appointments.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.AppointmentCode) }).ToListAsync())
            references[("Appointment", item.Id)] = item.Code;
        foreach (var item in await db.PatientVisits.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.VisitCode) }).ToListAsync())
            references[("PatientVisit", item.Id)] = item.Code;
        foreach (var item in await db.DiagnosticOrders.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.OrderCode) }).ToListAsync())
            references[("DiagnosticOrder", item.Id)] = item.Code;
        foreach (var item in await db.Invoices.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.InvoiceCode) }).ToListAsync())
            references[("Invoice", item.Id)] = item.Code;
        foreach (var item in await db.Payments.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.PaymentCode) }).ToListAsync())
            references[("Payment", item.Id)] = item.Code;
        foreach (var item in await db.HealthPackageRegistrations.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.RegistrationCode) }).ToListAsync())
            references[("HealthPackageRegistration", item.Id)] = item.Code;
        foreach (var item in await db.Prescriptions.Where(x => ids.Contains(x.Id)).ToListAsync())
            references[("Prescription", item.Id)] = BusinessDisplay.PrescriptionCode(item, clock);
        foreach (var item in await db.Patients.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.ShortCode ?? x.MedicalRecordNumber) }).ToListAsync())
            references[("Patient", item.Id)] = item.Code;
        foreach (var item in await db.Specialties.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = x.SpecialtyCode }).ToListAsync())
            references[("Specialty", item.Id)] = item.Code;
        foreach (var item in await db.AppointmentChangeRequests.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.Appointment.ShortCode ?? x.Appointment.AppointmentCode) }).ToListAsync())
            references[("AppointmentChangeRequest", item.Id)] = $"YC{item.Id} / {item.Code}";
        foreach (var item in await db.RevisitRequests.Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Code = (x.OriginalAppointment.ShortCode ?? x.OriginalAppointment.AppointmentCode) }).ToListAsync())
            references[("RevisitRequest", item.Id)] = $"TK{item.Id} / {item.Code}";
        foreach (var item in await db.DoctorLeaveRequests.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync())
            references[("DoctorLeaveRequest", item)] = $"NP{item}";
        foreach (var log in logs)
            log.ReferenceCode = long.TryParse(log.EntityId, out var id)
                ? references.GetValueOrDefault((log.EntityName, id), "")
                : Guid.TryParse(log.EntityId, out _) ? "" : log.EntityId;
        await AuditDescriptionCodes.ApplyAsync(db, clock, logs);
    }
}
