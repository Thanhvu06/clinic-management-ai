using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicManagement.IntegrationTests;

public class DemoPolishRound4SeedTests
{
    [Fact]
    public async Task SundayAfternoonResetMovesDiagnosticsToSaturdayAndCancelsOnlyUnstartedOrdersOlderThan24Hours()
    {
        using var factory = new FixedSeedWebApplicationFactory(new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<IDateTimeProvider>();
        Assert.Equal(DayOfWeek.Sunday, clock.VietnamToday.DayOfWeek);
        Assert.Equal(new TimeOnly(16, 30), clock.VietnamTime);
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(DemoSeedCalendar.WithAnchoredClock(services), 30, true);
        var db = services.GetRequiredService<AppDbContext>();
        var orders = await db.DiagnosticOrders.Include(o => o.PatientVisit).Include(o => o.Items)
            .Where(o => o.PatientVisit != null && o.PatientVisit.QueueNumber >= 200 && o.PatientVisit.QueueNumber < 205).ToListAsync();
        Assert.Equal(5, orders.Count);
        Assert.All(orders, order =>
        {
            Assert.Equal(new DateOnly(2026, 10, 24), order.PatientVisit!.VisitDate);
            Assert.Equal(new DateOnly(2026, 10, 24), DateOnly.FromDateTime(clock.ConvertUtcToVietnam(order.OrderedAtUtc)));
        });
        var expired = Assert.Single(orders, order => order.PatientVisit!.QueueNumber == 200);
        Assert.True(clock.UtcNow - expired.OrderedAtUtc > TimeSpan.FromHours(24));
        Assert.Null(expired.StartedAtUtc);
        Assert.Equal(DiagnosticOrderStatus.Cancelled, expired.Status);
        Assert.Equal(clock.UtcNow, expired.CancelledAtUtc);
        Assert.Contains("Quá hạn thực hiện", expired.Note);
        Assert.All(expired.Items, item => Assert.Equal(DiagnosticItemStatus.Cancelled, item.Status));
        var pending = Assert.Single(orders, order => order.PatientVisit!.QueueNumber == 201);
        Assert.True(clock.UtcNow - pending.OrderedAtUtc < TimeSpan.FromHours(24));
        Assert.Equal(DiagnosticOrderStatus.Ordered, pending.Status);
        Assert.Null(pending.CancelledAtUtc);
        Assert.All(orders.Where(order => order.StartedAtUtc != null), order => Assert.NotEqual(DiagnosticOrderStatus.Cancelled, order.Status));
    }

    [Theory]
    [InlineData("2026-10-27T00:30:00Z")]
    [InlineData("2026-10-27T08:00:00Z")]
    [InlineData("2026-10-27T23:30:00Z")]
    [InlineData("2026-10-26T23:30:00Z")]
    [InlineData("2026-10-25T02:00:00Z")]
    public async Task ResetKeepsEventsInPastAndPatientsPackagesContactsAndRoomsConsistent(string utc)
    {
        var clock = new FixedClock(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        using var factory = new CustomWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(clock);
        }));
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(DemoSeedCalendar.WithAnchoredClock(services), 30, true);
        var db = services.GetRequiredService<AppDbContext>();

        async Task Past<T>() where T : class
        {
            var properties = db.Model.FindEntityType(typeof(T))!.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)).ToList();
            foreach (var entity in await db.Set<T>().ToListAsync())
            foreach (var property in properties)
                if (db.Entry(entity).Property(property.Name).CurrentValue is DateTime time)
                    Assert.True(time <= clock.UtcNow, $"{typeof(T).Name}.{property.Name}: {time:O} > {clock.UtcNow:O}");
        }
        await Past<Invoice>(); await Past<Payment>(); await Past<HealthPackageRegistration>();
        await Past<AppointmentHistory>(); await Past<AppointmentChangeRequest>(); await Past<PatientVisit>();
        await Past<DiagnosticOrder>(); await Past<DiagnosticOrderItem>(); await Past<DiagnosticResult>();
        await Past<VisitSummary>(); await Past<AppointmentVitalSigns>(); await Past<Prescription>();
        await Past<SystemAuditLog>(); await Past<Notification>(); await Past<MedicineStockTransaction>();
        await Past<MedicineBatch>(); await Past<ApplicationUser>(); await Past<Facility>();
        await Past<ClinicLocation>(); await Past<HealthPackage>(); await Past<Medicine>();

        var patients = await db.Patients.ToDictionaryAsync(p => p.Id);
        var packages = await db.HealthPackages.ToDictionaryAsync(p => p.Id);
        var registrations = await db.HealthPackageRegistrations.ToDictionaryAsync(r => r.Id);
        Assert.All(registrations.Values, r =>
        {
            var patient = patients[r.PatientId];
            Assert.Equal(patient.PhoneNumber, r.ContactPhone);
            Assert.Matches(@"^REG-\d{6}-[0-9A-F]{8}$", r.RegistrationCode);
            Assert.Equal(patient.DateOfBirth > r.PreferredDate.AddYears(-16), packages[r.HealthPackageId].Code == "PKG03");
        });
        foreach (var invoice in await db.Invoices.ToListAsync())
        {
            Assert.Matches(@"^INV-\d{6}-[0-9A-F]{8}$", invoice.InvoiceCode);
            if (invoice.HealthPackageRegistrationId is { } id)
                Assert.Equal(invoice.Status == InvoiceStatus.Cancelled, registrations[id].Status == HealthPackageRegistrationStatus.Cancelled);
        }
        var appointments = await db.Appointments.Include(a => a.Specialty).Include(a => a.Histories).ToListAsync();
        foreach (var appointment in appointments)
        {
            if (patients[appointment.PatientId].DateOfBirth > appointment.AppointmentDate.AddYears(-16))
                Assert.Equal("SP02", appointment.Specialty.SpecialtyCode);
            Assert.Equal(TimeSpan.FromMinutes(30), appointment.EndTime - appointment.StartTime);
        }
        foreach (var request in await db.AppointmentChangeRequests.ToListAsync())
        {
            var appointment = appointments.Single(a => a.Id == request.AppointmentId);
            Assert.True(request.CreatedAt >= appointment.Histories.Single(h => h.Action == AppointmentHistoryAction.Created).CreatedAt);
            Assert.True(request.CreatedAt < clock.ConvertVietnamToUtc(appointment.AppointmentDate.ToDateTime(appointment.StartTime)));
        }
        var visits = await db.PatientVisits.Include(v => v.AssignedDoctor).ThenInclude(d => d!.DoctorSpecialties).ThenInclude(s => s.Specialty).ToListAsync();
        foreach (var visit in visits)
            if (patients[visit.PatientId].DateOfBirth > visit.VisitDate.AddYears(-16))
                Assert.Equal("SP02", visit.AssignedDoctor!.DoctorSpecialties.Single(s => s.IsPrimary).Specialty.SpecialtyCode);
        var overdue = visits.Where(v => v.VisitDate < clock.VietnamToday && v.Status == VisitStatus.InConsultation).ToList();
        Assert.NotEmpty(overdue);
        Assert.All(overdue, v =>
        {
            Assert.Equal(DemoSeedCalendar.PreviousOpen(clock.VietnamToday.AddDays(-1)), v.VisitDate);
            Assert.InRange(v.QueueNumber, 1, 899);
            Assert.NotNull(v.AppointmentId);
        });
        if (clock.VietnamTime < new TimeOnly(7, 0))
        {
            Assert.DoesNotContain(visits, v => v.VisitDate == clock.VietnamToday);
            Assert.Contains(appointments, a => a.AppointmentDate == clock.VietnamToday);
        }
        var specialties = await db.DoctorSpecialties.Select(s => s.SpecialtyId).Distinct().ToListAsync();
        var rooms = await db.Rooms.Include(r => r.Department).Where(r => r.IsActive && r.RoomType == RoomType.Consultation).ToListAsync();
        Assert.All(specialties, id => Assert.Contains(rooms, r => r.Department.SpecialtyId == id));
        var texts = appointments.Select(a => a.Reason).Concat(visits.Select(v => v.ChiefComplaint))
            .Concat(await db.AppointmentHistories.Select(h => h.Note).ToListAsync())
            .Concat(await db.RevisitRequests.Select(r => r.Note).ToListAsync())
            .Concat(await db.Prescriptions.Select(p => p.Notes).ToListAsync())
            .Concat(await db.DoctorLeaveRequests.Select(l => l.Reason).ToListAsync())
            .Concat(await db.Notifications.Select(n => n.Title + n.Message).ToListAsync());
        Assert.All(texts, text => Assert.DoesNotMatch("(?i)minh họa|demo|test|mẫu", text ?? ""));
    }

    internal sealed class FixedClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime UtcNow => utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }
}
