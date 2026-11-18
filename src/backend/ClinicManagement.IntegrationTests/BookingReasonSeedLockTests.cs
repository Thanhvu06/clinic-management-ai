using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Authentication.DTOs;
using ClinicManagement.Application.Appointments.DTOs;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Expiry;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Schedules;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ClinicManagement.IntegrationTests;

public sealed class BookingReasonSeedLockTests
{
    [Fact]
    public async Task Cleanup_keeps_internal_audit_notes_out_of_patient_prescriptions_and_history()
    {
        using var parent = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc));
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<ClinicOperatingHoursOptions>(options => options.Mode = "TwentyFourSeven")));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(services, 30, true);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var patient = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var appointment = await db.Appointments.FirstAsync(a => a.PatientId == patient.Id);
        var order = await db.DiagnosticOrders.FirstAsync(o => o.PatientId == patient.Id);
        order.Note = "Theo dõi ho kéo dài.\nDọn dữ liệu demo cũ";
        db.AppointmentHistories.Add(new AppointmentHistory
        {
            AppointmentId = appointment.Id, Action = AppointmentHistoryAction.Cancelled,
            NewStatus = AppointmentStatus.Cancelled, Note = "Dọn dữ liệu demo cũ", CreatedAt = clock.UtcNow
        });
        var prescription = new Prescription
        {
            PatientId = patient.Id, DoctorId = appointment.DoctorId, CreatedAt = clock.UtcNow.AddDays(-3),
            Status = PrescriptionStatus.Issued, Notes = "Nghỉ ngơi, uống đủ nước."
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        var environment = services.GetRequiredService<IHostEnvironment>();
        environment.EnvironmentName = "Development";
        using var worker = new DevelopmentDemoTopUpWorker(services.GetRequiredService<IServiceScopeFactory>(), environment,
            Options.Create(new DemoTopUpOptions { Enabled = true }), NullLogger<DevelopmentDemoTopUpWorker>.Instance);
        await worker.RunOnceAsync();
        db.ChangeTracker.Clear();
        prescription = await db.Prescriptions.SingleAsync(p => p.Id == prescription.Id);
        Assert.Equal(PrescriptionStatus.Expired, prescription.Status);
        Assert.Contains("Vui lòng tái khám", prescription.Notes);
        Assert.DoesNotContain("demo", prescription.Notes!, StringComparison.OrdinalIgnoreCase);

        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { EmailOrPhone = patient.Email!, Password = "Demo@12345" });
        var auth = await login.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Data!.AccessToken);
        var history = await client.GetFromJsonAsync<ApiResponse<List<AppointmentHistoryDto>>>($"/api/v1/appointments/{appointment.Id}/history");
        Assert.Contains(history!.Data!, h => h.Note != null && h.Note.Contains("Vui lòng chọn lịch khám mới"));
        Assert.DoesNotContain(history.Data!, h => h.Note?.Contains("demo", StringComparison.OrdinalIgnoreCase) == true);
        var diagnostic = await client.GetFromJsonAsync<ApiResponse<DiagnosticOrderDto>>($"/api/v1/patients/me/diagnostic-orders/{order.Id}");
        Assert.StartsWith("Theo dõi ho kéo dài.", diagnostic!.Data!.Note);
        Assert.Contains("Vui lòng tái khám", diagnostic.Data.Note);
        Assert.DoesNotContain("demo", diagnostic.Data.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task Reseed_repairs_demo_lock_and_patient_notes_only_in_development(string environmentName, bool remainsBlocked)
    {
        using var factory = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc));
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var demo = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var real = new Patient { FullName = "Nguyễn An", Email = "patient@example.org", PhoneNumber = "0912345678", MedicalRecordNumber = "BN-REAL-LOCK", DateOfBirth = new(1990, 1, 1) };
        db.Patients.Add(real);
        var doctor = await db.Doctors.Include(d => d.DoctorSpecialties).FirstAsync();
        var specialtyId = doctor.DoctorSpecialties.First().SpecialtyId;
        foreach (var patient in new[] { demo, real })
        for (var index = 0; index < 3; index++)
        {
            var date = clock.VietnamToday.AddDays(-index - 1);
            var start = new TimeOnly(patient == demo ? 8 : 9, 0);
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"LOCK-{patient.Email}-{index}", Patient = patient, DoctorId = doctor.Id,
                SpecialtyId = specialtyId, AppointmentDate = date, StartTime = start, EndTime = start.AddMinutes(30),
                Status = AppointmentStatus.Confirmed, Reason = "Ho khan kéo dài 3 ngày",
                AppointmentSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(30), IsBooked = true }
            });
        }
        var prescription = new Prescription
        {
            PatientId = demo.Id, DoctorId = doctor.Id, CreatedAt = clock.UtcNow.AddDays(-14), Status = PrescriptionStatus.Expired,
            Notes = "Nghỉ ngơi, uống đủ nước.\nDọn dữ liệu demo cũ"
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
        await services.GetRequiredService<ExpiryWorker>().RunOnceAsync(throwOnFailure: true);
        db.ChangeTracker.Clear();
        var options = new ExpiryRulesOptions();
        Assert.NotNull(await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options, demo.Id));
        Assert.NotNull(await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options, real.Id));
        services.GetRequiredService<IHostEnvironment>().EnvironmentName = environmentName;

        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        db.ChangeTracker.Clear();
        Assert.Equal(remainsBlocked, await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options, demo.Id) != null);
        Assert.NotNull(await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options, real.Id));
        var notes = (await db.Prescriptions.SingleAsync(p => p.Id == prescription.Id)).Notes!;
        Assert.Equal(remainsBlocked, notes.Contains("demo", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Nghỉ ngơi, uống đủ nước.", notes);
        if (!remainsBlocked)
        {
            Assert.Contains("Vui lòng tái khám", notes);
            Assert.False(await db.Appointments.AnyAsync(a => a.PatientId == demo.Id && a.Status == AppointmentStatus.NoShow));
        }
        var historyCount = await db.AppointmentHistories.CountAsync();
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        Assert.Equal(historyCount, await db.AppointmentHistories.CountAsync());
        Assert.Equal(notes, (await db.Prescriptions.SingleAsync(p => p.Id == prescription.Id)).Notes);

        var repeatDate = clock.VietnamToday.AddDays(-4);
        var repeated = new Appointment
        {
            AppointmentCode = "LOCK-REPEAT", PatientId = demo.Id, DoctorId = doctor.Id, SpecialtyId = specialtyId,
            AppointmentDate = repeatDate, StartTime = new(11, 0), EndTime = new(11, 30), Status = AppointmentStatus.Confirmed,
            Reason = "Ho khan kéo dài 3 ngày",
            AppointmentSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = repeatDate, StartTime = new(11, 0), EndTime = new(11, 30), IsBooked = true }
        };
        db.Appointments.Add(repeated);
        await db.SaveChangesAsync();
        await services.GetRequiredService<ConfirmedAppointmentExpiryRule>().RunAsync(default);
        db.ChangeTracker.Clear();
        Assert.Equal(remainsBlocked ? AppointmentStatus.NoShow : AppointmentStatus.Cancelled,
            (await db.Appointments.SingleAsync(a => a.Id == repeated.Id)).Status);
        Assert.Equal(remainsBlocked, await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options, demo.Id) != null);
        Assert.NotNull(await NoShowRestrictionRule.GetBlockedUntilAsync(db, clock, options, real.Id));
    }
}
