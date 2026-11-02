using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

internal static class DemoSeedTimeline
{
    internal static DateOnly ActivityDate(IDateTimeProvider clock, IClinicOperatingHoursPolicy? policy = null) =>
        policy?.IsTwentyFourSeven != true && (DemoSeedCalendar.IsClosed(clock.VietnamToday, policy) || clock.VietnamTime < new TimeOnly(7, 0))
            ? DemoSeedCalendar.PreviousOpen(clock.VietnamToday.AddDays(-1), policy) : clock.VietnamToday;

    internal static DateTime ActivityNow(IDateTimeProvider clock, IClinicOperatingHoursPolicy? policy = null) => ActivityDate(clock, policy) == clock.VietnamToday
        ? clock.UtcNow : RealisticDevelopmentSeed.At(clock, ActivityDate(clock, policy), 17);

    internal static DateTime BeforeNow(IDateTimeProvider clock, DateOnly date, int hour, int minute = 0, IClinicOperatingHoursPolicy? policy = null)
    {
        var time = RealisticDevelopmentSeed.At(clock, date, hour, minute);
        var latest = ActivityNow(clock, policy).AddHours(-1);
        return time < latest ? time : latest;
    }

    internal static async Task BoundEventsAsync(AppDbContext db, IDateTimeProvider clock)
    {
        var eventTypes = new HashSet<Type>
        {
            typeof(AppointmentHistory), typeof(AppointmentChangeRequest), typeof(PatientVisit),
            typeof(VisitSummary), typeof(AppointmentVitalSigns), typeof(DiagnosticOrder),
            typeof(DiagnosticOrderItem), typeof(DiagnosticResult), typeof(Prescription),
            typeof(Invoice), typeof(Payment), typeof(HealthPackageRegistration), typeof(Notification),
            typeof(SystemAuditLog), typeof(MedicineStockTransaction), typeof(MedicineBatch),
            typeof(ApplicationUser), typeof(Facility), typeof(ClinicLocation), typeof(HealthPackage),
            typeof(Medicine), typeof(MedicineCategory), typeof(StaffFacilityAssignment)
        };
        var date = ActivityDate(clock, db.OperatingHours);
        var start = RealisticDevelopmentSeed.At(clock, date, db.OperatingHours.IsTwentyFourSeven ? 0 : 7);
        var end = ActivityNow(clock, db.OperatingHours);
        var duration = end - start;
        // Entity defaults use the system clock; a reset must also obey an injected clock.
        foreach (var entry in db.ChangeTracker.Entries().Where(e => eventTypes.Contains(e.Metadata.ClrType)).ToList())
        foreach (var property in entry.Properties)
        {
            if (property.CurrentValue is not DateTime value || value.Year <= 1900) continue;
            var local = clock.ConvertUtcToVietnam(value);
            if (DateOnly.FromDateTime(local) < clock.VietnamToday) continue;
            if (date != clock.VietnamToday)
                property.CurrentValue = start.AddTicks((long)(duration.Ticks * local.TimeOfDay.TotalSeconds / TimeSpan.FromDays(1).TotalSeconds));
            else if (value > clock.UtcNow)
                property.CurrentValue = clock.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}
