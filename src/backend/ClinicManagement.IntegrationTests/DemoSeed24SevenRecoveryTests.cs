using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class DemoSeed24SevenRecoveryTests
{
    [Theory]
    [InlineData("2026-11-01T14:00:00Z")]
    [InlineData("2026-11-03T20:00:00Z")]
    [InlineData("2026-11-02T02:00:00Z")]
    [InlineData("2026-11-02T16:45:00Z")]
    public async Task Reset24Seven_RestoresPastHistory_FutureRequestsAndNearbyConfirmedAppointment(string utc)
    {
        using var parent = new FixedSeedWebApplicationFactory(DateTime.Parse(utc).ToUniversalTime());
        var user = new DemoSeedUser();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = "TwentyFourSeven");
            services.AddSingleton<ICurrentUserService>(user);
        }));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var patient = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var invoices = await db.Invoices.Include(i => i.Payments).Where(i => i.PatientId == patient.Id).ToListAsync();
        var prescriptions = await db.Prescriptions.Where(p => p.PatientId == patient.Id).ToListAsync();
        var diagnostics = await db.DiagnosticOrders.Include(o => o.Items).ThenInclude(i => i.Result)
            .Where(o => o.PatientId == patient.Id && o.Status == DiagnosticOrderStatus.Completed && o.ReviewedAtUtc != null).ToListAsync();
        Assert.True(invoices.Count >= 11);
        Assert.True(prescriptions.Count >= 4);
        Assert.True(diagnostics.Count >= 5);
        Assert.All(invoices, invoice =>
        {
            Assert.True(invoice.CreatedAtUtc < clock.UtcNow);
            Assert.All(invoice.Payments, payment => Assert.True(payment.ReceivedAtUtc < clock.UtcNow));
        });
        Assert.All(prescriptions, prescription =>
        {
            Assert.True(prescription.CreatedAt < clock.UtcNow);
            if (prescription.DispensedAt.HasValue) Assert.True(prescription.DispensedAt < clock.UtcNow);
        });
        Assert.All(diagnostics, order =>
        {
            Assert.True(order.CompletedAtUtc < clock.UtcNow);
            Assert.True(order.ReviewedAtUtc < clock.UtcNow);
            Assert.All(order.Items, item => Assert.True(item.Result!.ResultedAtUtc < clock.UtcNow));
        });
        var upcoming = (await db.Appointments.Include(a => a.AppointmentSlot).Where(a => a.PatientId == patient.Id && a.Status == AppointmentStatus.Confirmed).ToListAsync())
            .Where(a => a.AppointmentDate.ToDateTime(a.StartTime) > clock.VietnamNow).OrderBy(a => a.AppointmentDate).ThenBy(a => a.StartTime).First();
        Assert.InRange(upcoming.AppointmentDate.ToDateTime(upcoming.StartTime), clock.VietnamNow.AddHours(1), clock.VietnamNow.AddHours(6));
        Assert.Equal(upcoming.StartTime, upcoming.AppointmentSlot.StartTime);
        Assert.Equal(upcoming.DoctorId, upcoming.AppointmentSlot.DoctorId);
        Assert.True(upcoming.AppointmentSlot.IsBooked);
        Assert.True(await db.DoctorWorkSchedules.AnyAsync(s => s.DoctorId == upcoming.DoctorId && s.WorkDate == upcoming.AppointmentDate &&
            s.IsActive && s.StartTime <= upcoming.StartTime && s.EndTime >= upcoming.EndTime));

        var requests = await db.AppointmentChangeRequests.Include(r => r.Appointment).Include(r => r.RequestedSlot).ToListAsync();
        Assert.Equal(new[] { AppointmentChangeRequestType.Reschedule, AppointmentChangeRequestType.Cancellation }.Order(), requests.Select(r => r.RequestType).Order());
        Assert.All(requests, request =>
        {
            Assert.Equal(AppointmentChangeRequestStatus.Pending, request.Status);
            Assert.True(request.Appointment.AppointmentDate.ToDateTime(request.Appointment.StartTime) >= clock.VietnamNow.AddDays(1));
            if (request.RequestedSlot is { } slot) Assert.True(slot.SlotDate.ToDateTime(slot.StartTime) >= clock.VietnamNow.AddDays(1));
        });
        var leaves = await db.DoctorLeaveRequests.ToListAsync();
        Assert.NotEmpty(leaves);
        Assert.All(leaves, leave =>
        {
            Assert.Equal(DoctorLeaveRequestStatus.Pending, leave.Status);
            Assert.True(leave.StartDateTime >= clock.VietnamNow.AddDays(1));
            Assert.True(leave.EndDateTime > leave.StartDateTime);
        });
        await services.GetRequiredService<ExpiryWorker>().RunOnceAsync(throwOnFailure: true);
        db.ChangeTracker.Clear();
        Assert.All(await db.AppointmentChangeRequests.ToListAsync(), request => Assert.Equal(AppointmentChangeRequestStatus.Pending, request.Status));
        Assert.All(await db.DoctorLeaveRequests.ToListAsync(), leave => Assert.Equal(DoctorLeaveRequestStatus.Pending, leave.Status));
        user.UserId = (await db.Users.SingleAsync(u => u.Email == "doctor@cliniccare.local")).Id;
        var dashboard = await services.GetRequiredService<IDoctorAppointmentService>().GetDoctorDashboardAsync(clock.VietnamToday);
        Assert.Equal(new[] { "Ca đêm", "Ca ngày", "Ca tối" }, dashboard.TodayShifts.Select(s => s.ShiftName));
        Assert.Equal("Ca đêm 00:00–08:00 · Ca ngày 08:00–16:00 · Ca tối 16:00–23:30",
            string.Join(" · ", dashboard.TodayShifts.Select(s => $"{s.ShiftName} {s.StartTime:HH:mm}–{s.EndTime:HH:mm}")));
        if (clock.VietnamTime < new TimeOnly(23, 30))
            Assert.StartsWith(clock.VietnamTime < new TimeOnly(8, 0) ? "Ca đêm" : clock.VietnamTime < new TimeOnly(16, 0) ? "Ca ngày" : "Ca tối", dashboard.CurrentShift);
        var counts = (invoices.Count, prescriptions.Count, diagnostics.Count);
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        Assert.Equal(counts, (await db.Invoices.CountAsync(i => i.PatientId == patient.Id), await db.Prescriptions.CountAsync(p => p.PatientId == patient.Id),
            await db.DiagnosticOrders.CountAsync(o => o.PatientId == patient.Id && o.Status == DiagnosticOrderStatus.Completed && o.ReviewedAtUtc != null)));
    }

    [Fact]
    public async Task StandardDashboard_KeepsOriginalDaytimeShiftLabels()
    {
        using var parent = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 2, 2, 0, 0, DateTimeKind.Utc));
        var user = new DemoSeedUser();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = "Standard");
            services.AddSingleton<ICurrentUserService>(user);
        }));
        using var scope = factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(scope.ServiceProvider, 30, true);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        user.UserId = (await db.Users.SingleAsync(u => u.Email == "doctor@cliniccare.local")).Id;
        var dashboard = await scope.ServiceProvider.GetRequiredService<IDoctorAppointmentService>().GetDoctorDashboardAsync(new DateOnly(2026, 11, 2));
        Assert.Equal(new[] { "Ca sáng", "Ca chiều" }, dashboard.TodayShifts.Select(s => s.ShiftName));
        Assert.StartsWith("Ca sáng", dashboard.CurrentShift);
    }

    private sealed class DemoSeedUser : ICurrentUserService
    {
        public Guid? UserId { get; set; }
    }
}
