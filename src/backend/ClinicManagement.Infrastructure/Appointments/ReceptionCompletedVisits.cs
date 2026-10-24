using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Appointments;

internal static class ReceptionCompletedVisits
{
    internal static async Task<int> CountAsync(AppDbContext db, IQueryable<Appointment> appointments, DateOnly today,
        long? facilityId, bool globalAdmin, IReadOnlyCollection<long> allowedFacilities)
    {
        var visits = db.PatientVisits.Where(v => v.VisitDate == today && v.CompletedAtUtc != null);
        if (facilityId is > 0) visits = visits.Where(v => v.FacilityId == facilityId);
        else if (!globalAdmin) visits = visits.Where(v => allowedFacilities.Contains(v.FacilityId));
        var completedVisitIds = visits.Select(v => v.AppointmentId);
        return await visits.CountAsync() + await appointments.CountAsync(a => a.AppointmentDate == today &&
            a.Status == AppointmentStatus.Completed && !completedVisitIds.Contains(a.Id));
    }
}
