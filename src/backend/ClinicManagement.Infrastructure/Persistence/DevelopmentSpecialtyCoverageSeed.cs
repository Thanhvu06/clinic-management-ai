using ClinicManagement.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Gives every active specialty one bookable doctor for the booking window, so a patient who asks
/// about a specialty never reaches an empty booking flow in the demo. Adds missing shifts and slots only.
/// </summary>
public static class DevelopmentSpecialtyCoverageSeed
{
    public const int CoverageDays = 14;

    public static async Task<int> EnsureAsync(AppDbContext db, IDateTimeProvider clock, CancellationToken ct = default)
    {
        var memberships = await db.DoctorSpecialties.AsNoTracking()
            .Where(x => x.Specialty.IsActive && x.Doctor.IsActive && db.Users.Any(user => user.Id == x.Doctor.UserId && user.IsActive))
            .Select(x => new { x.SpecialtyId, x.DoctorId, x.IsPrimary })
            .ToListAsync(ct);
        var doctorIds = memberships
            .GroupBy(x => x.SpecialtyId)
            .Select(group => group.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DoctorId).First().DoctorId)
            .Distinct().ToArray();
        if (doctorIds.Length == 0) return 0;
        var today = clock.VietnamToday;
        var before = await db.AppointmentSlots.CountAsync(ct);
        await DevelopmentDemoTopUpSlots.EnsureAsync(db, doctorIds, today, today.AddDays(CoverageDays - 1), ct);
        return await db.AppointmentSlots.CountAsync(ct) - before;
    }
}
