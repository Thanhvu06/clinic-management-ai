using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DevelopmentScheduleSeed
{
    internal static async Task SeedAsync(AppDbContext db, List<Doctor> doctors, IDateTimeProvider dateTimeProvider, ILogger logger, int scheduleDays)
    {
        var today = dateTimeProvider.VietnamToday;
        var existingSchedules = await db.DoctorWorkSchedules.ToListAsync();
        var scheduleLookup = existingSchedules
            .GroupBy(s => (s.DoctorId, s.WorkDate, s.StartTime, s.EndTime))
            .ToDictionary(g => g.Key, g => g.First());

        var existingSlots = await db.AppointmentSlots.ToListAsync();
        var slotLookup = existingSlots
            .GroupBy(s => (s.DoctorId, s.SlotDate, s.StartTime))
            .ToDictionary(g => g.Key, g => g.First());

        var days = Math.Clamp(scheduleDays, 1, 30);
        foreach (var doc in doctors)
        {
            for (int i = 0; i < days; i++)
            {
                var date = today.AddDays(i);
                foreach (var (start, end) in db.OperatingHours.GetSeedShifts(date))
                {
                    var key = (doc.Id, date, start, end);
                    if (!scheduleLookup.TryGetValue(key, out var schedule))
                    {
                        schedule = new DoctorWorkSchedule { DoctorId = doc.Id, WorkDate = date, StartTime = start, EndTime = end };
                        db.DoctorWorkSchedules.Add(schedule);
                        scheduleLookup[key] = schedule;
                    }
                    schedule.IsActive = true;
                    var close = end.ToTimeSpan();
                    for (var minute = start.ToTimeSpan(); minute < close; minute += TimeSpan.FromMinutes(30))
                    {
                        var slotStart = TimeOnly.FromTimeSpan(minute);
                        var slotKey = (doc.Id, date, slotStart);
                        if (slotLookup.ContainsKey(slotKey)) continue;
                        var slot = new AppointmentSlot
                        {
                            DoctorId = doc.Id, SlotDate = date, StartTime = slotStart,
                            EndTime = slotStart.AddMinutes(30), IsBooked = false
                        };
                        db.AppointmentSlots.Add(slot);
                        slotLookup[slotKey] = slot;
                    }
                }
            }
        }
        await db.SaveChangesAsync();

        var activeApptSlotIds = await db.Appointments
            .Where(a => AppointmentStatusExtensions.HoldingSlotStatuses.Contains(a.Status))
            .Select(a => a.AppointmentSlotId)
            .Distinct()
            .ToListAsync();
        var activeSlotIdSet = new HashSet<long>(activeApptSlotIds);

        var allDoctorSlots = await db.AppointmentSlots.ToListAsync();
        foreach (var slot in allDoctorSlots)
        {
            var shouldBeBooked = activeSlotIdSet.Contains(slot.Id);
            if (slot.IsBooked != shouldBeBooked)
            {
                slot.IsBooked = shouldBeBooked;
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Work schedules and slots seeded and synchronized for {Days} days [VietnamToday..+{DaysMinusOne}].", days, days - 1);
    }
}
