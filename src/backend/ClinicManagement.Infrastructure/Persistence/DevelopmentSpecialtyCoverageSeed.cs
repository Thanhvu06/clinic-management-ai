using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Gives every active specialty one bookable doctor for the booking window, so a patient who asks
/// about a specialty never reaches an empty booking flow in the demo. Adds missing shifts and slots,
/// and moves a doctor's home department to their primary specialty when the demo left it on another one.
/// Nothing is deleted.
/// </summary>
public static class DevelopmentSpecialtyCoverageSeed
{
    public const int CoverageDays = 14;

    public static async Task<int> EnsureAsync(AppDbContext db, IDateTimeProvider clock, CancellationToken ct = default)
    {
        var memberships = await db.DoctorSpecialties.AsNoTracking()
            .Where(x => x.Specialty.IsActive && x.Doctor.IsActive && db.Users.Any(user => user.Id == x.Doctor.UserId && user.IsActive))
            .Select(x => new { x.SpecialtyId, x.DoctorId, x.IsPrimary, x.Doctor.UserId })
            .ToListAsync(ct);
        var representatives = memberships
            .GroupBy(x => x.SpecialtyId)
            .Select(group => group.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DoctorId).First())
            .ToList();
        if (representatives.Count == 0) return 0;

        var changes = 0;
        // The demo master seed assigns every doctor to the first department, which the booking
        // facility check rejects for any primary specialty other than that department's.
        foreach (var doctor in representatives.Where(x => x.IsPrimary))
        {
            if ((await AppointmentFacilityResolver.GetEligibleAsync(db, doctor.DoctorId, doctor.SpecialtyId, ct)).Count > 0) continue;
            var assignment = await db.StaffFacilityAssignments
                .FirstOrDefaultAsync(a => a.UserId == doctor.UserId && a.Role == "Doctor" && a.IsActive, ct);
            if (assignment is null) continue;
            var department = await db.Departments.Where(d => d.FacilityId == assignment.FacilityId && d.SpecialtyId == doctor.SpecialtyId && d.IsActive)
                .OrderBy(d => d.Id).FirstOrDefaultAsync(ct);
            if (department is null || assignment.DepartmentId == department.Id) continue;
            assignment.DepartmentId = department.Id;
            changes++;
        }
        await db.SaveChangesAsync(ct);

        var today = clock.VietnamToday;
        var before = await db.AppointmentSlots.CountAsync(ct);
        await DevelopmentDemoTopUpSlots.EnsureAsync(db, representatives.Select(x => x.DoctorId).Distinct().ToArray(), today, today.AddDays(CoverageDays - 1), ct);
        return changes + await db.AppointmentSlots.CountAsync(ct) - before;
    }
}
