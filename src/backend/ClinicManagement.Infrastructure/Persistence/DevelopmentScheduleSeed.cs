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
        var sundayDoctors = doctors.Where(d => d.DoctorSpecialties.Any(ds => ds.IsPrimary && new[] { "SP01", "SP02", "SP03", "SP05" }.Contains(ds.Specialty.SpecialtyCode))).Take(4).Select(d => d.Id).ToHashSet();
        foreach (var doc in doctors)
        {
            for (int i = 0; i < days; i++)
            {
                var date = today.AddDays(i);
                var sunday = date.DayOfWeek == DayOfWeek.Sunday;
                if (sunday && !sundayDoctors.Contains(doc.Id)) continue;
                var morningStart = sunday ? new TimeOnly(7, 0) : new TimeOnly(8, 0);

                var morningKey = (doc.Id, date, morningStart, new TimeOnly(11, 30));
                if (!scheduleLookup.TryGetValue(morningKey, out var morningSchedule))
                {
                    morningSchedule = new DoctorWorkSchedule
                    {
                        DoctorId = doc.Id,
                        WorkDate = date,
                        StartTime = morningStart,
                        EndTime = new TimeOnly(11, 30),
                        IsActive = true
                    };
                    db.DoctorWorkSchedules.Add(morningSchedule);
                    scheduleLookup[morningKey] = morningSchedule;
                }
                else if (!morningSchedule.IsActive)
                {
                    morningSchedule.IsActive = true;
                }

                var hasAfternoonShift = !sunday && date.DayOfWeek != DayOfWeek.Saturday;
                var afternoonKey = (doc.Id, date, new TimeOnly(13, 30), new TimeOnly(17, 0));
                if (hasAfternoonShift)
                {
                    if (!scheduleLookup.TryGetValue(afternoonKey, out var afternoonSchedule))
                    {
                        afternoonSchedule = new DoctorWorkSchedule
                        {
                            DoctorId = doc.Id,
                            WorkDate = date,
                            StartTime = new TimeOnly(13, 30),
                            EndTime = new TimeOnly(17, 0),
                            IsActive = true
                        };
                        db.DoctorWorkSchedules.Add(afternoonSchedule);
                        scheduleLookup[afternoonKey] = afternoonSchedule;
                    }
                    else if (!afternoonSchedule.IsActive)
                    {
                        afternoonSchedule.IsActive = true;
                    }
                }

                var mStart = morningStart;
                for (int s = 0; s < (sunday ? 9 : 7); s++)
                {
                    var slotKey = (doc.Id, date, mStart);
                    if (!slotLookup.TryGetValue(slotKey, out var slot))
                    {
                        slot = new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            SlotDate = date,
                            StartTime = mStart,
                            EndTime = mStart.AddMinutes(30),
                            IsBooked = false
                        };
                        db.AppointmentSlots.Add(slot);
                        slotLookup[slotKey] = slot;
                    }
                    mStart = mStart.AddMinutes(30);
                }

                var aStart = new TimeOnly(13, 30);
                for (int s = 0; hasAfternoonShift && s < (sunday ? 9 : 7); s++)
                {
                    var slotKey = (doc.Id, date, aStart);
                    if (!slotLookup.TryGetValue(slotKey, out var slot))
                    {
                        slot = new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            SlotDate = date,
                            StartTime = aStart,
                            EndTime = aStart.AddMinutes(30),
                            IsBooked = false
                        };
                        db.AppointmentSlots.Add(slot);
                        slotLookup[slotKey] = slot;
                    }
                    aStart = aStart.AddMinutes(30);
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
