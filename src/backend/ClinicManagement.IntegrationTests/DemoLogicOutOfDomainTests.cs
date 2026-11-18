using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Authentication.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class DemoLogicOutOfDomainTests
{
    private const string LegacyNote = "Dọn dữ liệu demo cũ";

    [Theory]
    [InlineData("2026-11-09T17:01:00Z", 60)]
    [InlineData("2026-11-09T02:45:00Z", 60)]
    [InlineData("2026-11-09T02:45:00Z", 1440)]
    [InlineData("2026-11-09T02:45:00Z", 2000)]
    [InlineData("2026-12-31T16:45:00Z", 60)]
    public async Task Periodic_top_up_books_demo_from_tomorrow_after_the_next_tick(string utc, int intervalMinutes)
    {
        var clock = new TopUpClock(DateTime.Parse(utc).ToUniversalTime());
        using var parent = new FixedSeedWebApplicationFactory();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = "TwentyFourSeven");
            services.AddSingleton<IDateTimeProvider>(clock);
        }));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var environment = services.GetRequiredService<IHostEnvironment>();
        environment.EnvironmentName = "Development";
        var db = services.GetRequiredService<AppDbContext>();
        var before = clock.VietnamNow;
        using var worker = new DevelopmentDemoTopUpWorker(services.GetRequiredService<IServiceScopeFactory>(), environment,
            Options.Create(new DemoTopUpOptions { Enabled = true, TopUpIntervalMinutes = intervalMinutes }),
            NullLogger<DevelopmentDemoTopUpWorker>.Instance);

        await worker.RunOnceAsync();
        db.ChangeTracker.Clear();
        var appointment = Assert.Single(await db.Appointments.Include(a => a.Patient)
            .Where(a => a.Patient.Email == "patient@cliniccare.local").ToListAsync());
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.True(appointment.AppointmentDate > DateOnly.FromDateTime(before));
        Assert.True(appointment.AppointmentDate.ToDateTime(appointment.StartTime) > before.AddMinutes(Math.Clamp(intervalMinutes, 1, 1440)));
        Assert.InRange(appointment.StartTime, new TimeOnly(7, 0), new TimeOnly(19, 59));
        await worker.RunOnceAsync();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.PatientId == appointment.PatientId));

        clock.Utc = clock.Utc.AddMinutes(Math.Clamp(intervalMinutes, 1, 1440));
        await services.GetRequiredService<ConfirmedAppointmentExpiryRule>().RunAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status);
    }

    [Theory]
    [InlineData("Development", "patient@cliniccare.local")]
    [InlineData("Production", "patient@cliniccare.local")]
    [InlineData("Development", "patient.01@cliniccare.local")]
    public async Task Expiry_marks_every_overdue_confirmed_patient_as_no_show(string environment, string email)
    {
        using var factory = new FixedSeedWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        services.GetRequiredService<IHostEnvironment>().EnvironmentName = environment;
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var patient = await db.Patients.SingleAsync(p => p.Email == email);
        var doctor = await db.Doctors.Include(d => d.DoctorSpecialties).FirstAsync();
        var appointment = Appointment(patient, doctor, clock.VietnamToday.AddDays(-1), AppointmentStatus.Confirmed);
        appointment.AppointmentSlot.IsBooked = true;
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var rule = services.GetRequiredService<ConfirmedAppointmentExpiryRule>();
        Assert.Equal(1, await rule.RunAsync());
        db.ChangeTracker.Clear();
        var expired = await db.Appointments.Include(a => a.Histories).Include(a => a.AppointmentSlot).SingleAsync(a => a.Id == appointment.Id);
        Assert.Equal(AppointmentStatus.NoShow, expired.Status);
        Assert.Contains(expired.Histories, h => h.NewStatus == AppointmentStatus.NoShow && h.Note == "Vắng mặt");
        Assert.False(expired.AppointmentSlot.IsBooked);
        Assert.Equal(0, await rule.RunAsync());
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task Patient_reads_stored_notes_and_only_development_seed_repairs_them(string environment, bool remainsInternal)
    {
        using var factory = new FixedSeedWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var patient = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var doctor = await db.Doctors.Include(d => d.DoctorSpecialties).FirstAsync();
        var appointment = Appointment(patient, doctor, clock.VietnamToday.AddDays(-1), AppointmentStatus.Cancelled);
        appointment.Histories.Add(new AppointmentHistory
        {
            Action = AppointmentHistoryAction.Cancelled, NewStatus = AppointmentStatus.Cancelled,
            Note = LegacyNote, CreatedAt = clock.UtcNow
        });
        var order = new DiagnosticOrder
        {
            OrderCode = "DX-STORED-NOTE", PatientId = patient.Id, OrderingDoctorId = doctor.Id,
            Status = DiagnosticOrderStatus.Cancelled, Note = $"Theo dõi ho kéo dài.\n{LegacyNote}",
            OrderedAtUtc = clock.UtcNow.AddDays(-1), VerificationNote = "Ghi chú kiểm duyệt nội bộ"
        };
        db.AddRange(appointment, order, new Prescription
        {
            PatientId = patient.Id, DoctorId = doctor.Id, Status = PrescriptionStatus.Expired,
            CreatedAt = clock.UtcNow.AddDays(-2), Notes = LegacyNote
        });
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { EmailOrPhone = patient.Email!, Password = "Demo@12345" });
        var auth = await login.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);
        var historyPath = $"/api/v1/appointments/{appointment.Id}/history";
        var orderPath = $"/api/v1/patients/me/diagnostic-orders/{order.Id}";
        var beforeHistory = await client.GetFromJsonAsync<ApiResponse<List<AppointmentHistoryDto>>>(historyPath);
        var beforeOrder = await client.GetFromJsonAsync<ApiResponse<DiagnosticOrderDto>>(orderPath);
        Assert.Equal(LegacyNote, Assert.Single(beforeHistory!.Data!).Note);
        Assert.Equal(order.Note, beforeOrder!.Data!.Note);
        Assert.Null(beforeOrder.Data.VerificationNote);

        services.GetRequiredService<IHostEnvironment>().EnvironmentName = environment;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        db.ChangeTracker.Clear();
        Assert.Equal(remainsInternal, await db.AppointmentHistories.AnyAsync(h => h.Note != null && h.Note.Contains(LegacyNote)));
        Assert.Equal(remainsInternal, await db.DiagnosticOrders.AnyAsync(o => o.Note != null && o.Note.Contains(LegacyNote)));
        Assert.Equal(remainsInternal, await db.Prescriptions.AnyAsync(p => p.Notes != null && p.Notes.Contains(LegacyNote)));
        var afterHistory = await client.GetFromJsonAsync<ApiResponse<List<AppointmentHistoryDto>>>(historyPath);
        var afterOrder = await client.GetFromJsonAsync<ApiResponse<DiagnosticOrderDto>>(orderPath);
        Assert.Equal((await db.AppointmentHistories.SingleAsync(h => h.AppointmentId == appointment.Id)).Note,
            Assert.Single(afterHistory!.Data!).Note);
        Assert.Equal((await db.DiagnosticOrders.SingleAsync(o => o.Id == order.Id)).Note, afterOrder!.Data!.Note);
        Assert.Null(afterOrder.Data.VerificationNote);
    }

    private static Appointment Appointment(Patient patient, Doctor doctor, DateOnly date, AppointmentStatus status) => new()
    {
        AppointmentCode = $"APT-DOMAIN-{Guid.NewGuid():N}", PatientId = patient.Id, DoctorId = doctor.Id,
        SpecialtyId = doctor.DoctorSpecialties.First().SpecialtyId, AppointmentDate = date,
        StartTime = new(9, 0), EndTime = new(9, 30), Status = status, Reason = "Ho khan kéo dài 3 ngày",
        AppointmentSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = new(9, 0), EndTime = new(9, 30) }
    };

    private sealed class TopUpClock(DateTime utc) : IDateTimeProvider
    {
        private readonly DateTimeProvider converter = new();
        public DateTime Utc { get; set; } = utc;
        public DateTime UtcNow => Utc;
        public DateTime VietnamNow => ConvertUtcToVietnam(Utc);
        public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
        public TimeZoneInfo VietnamTimeZone => converter.VietnamTimeZone;
        public DateTime ConvertUtcToVietnam(DateTime value) => converter.ConvertUtcToVietnam(value);
        public DateTime ConvertVietnamToUtc(DateTime value) => converter.ConvertVietnamToUtc(value);
    }
}
