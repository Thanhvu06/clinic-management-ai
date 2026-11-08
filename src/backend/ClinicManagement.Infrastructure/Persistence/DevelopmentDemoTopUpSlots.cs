using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DevelopmentDemoTopUpSlots
{
    internal static async Task EnsureAsync(AppDbContext db, long[] doctorIds, DateOnly today, DateOnly lastDay, CancellationToken ct)
    {
        var schedules = await db.DoctorWorkSchedules.Where(x => doctorIds.Contains(x.DoctorId) && x.WorkDate >= today && x.WorkDate <= lastDay).ToListAsync(ct);
        var slots = await db.AppointmentSlots.Where(x => doctorIds.Contains(x.DoctorId) && x.SlotDate >= today && x.SlotDate <= lastDay).ToListAsync(ct);
        var keys = slots.Select(x => (x.DoctorId, x.SlotDate, x.StartTime)).ToHashSet();
        foreach (var doctorId in doctorIds)
        for (var date = today; date <= lastDay; date = date.AddDays(1))
        foreach (var (start, end) in db.OperatingHours.GetSeedShifts(date))
        {
            var overlapping = schedules.Where(x => x.DoctorId == doctorId && x.WorkDate == date && x.StartTime < end && x.EndTime > start).ToList();
            if (overlapping.Count == 0)
            {
                var schedule = new DoctorWorkSchedule { DoctorId = doctorId, WorkDate = date, StartTime = start, EndTime = end, IsActive = true };
                schedules.Add(schedule);
                db.DoctorWorkSchedules.Add(schedule);
            }
            for (var time = start; time.AddMinutes(30) <= end && time < end; time = time.AddMinutes(30))
            {
                if (keys.Contains((doctorId, date, time)) || !schedules.Any(x => x.DoctorId == doctorId && x.WorkDate == date && x.IsActive && x.StartTime <= time && x.EndTime >= time.AddMinutes(30))) continue;
                db.AppointmentSlots.Add(new AppointmentSlot { DoctorId = doctorId, SlotDate = date, StartTime = time, EndTime = time.AddMinutes(30) });
                keys.Add((doctorId, date, time));
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
