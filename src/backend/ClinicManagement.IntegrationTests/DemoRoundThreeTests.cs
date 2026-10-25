using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public sealed class DemoRoundThreeFixture : IAsyncLifetime
{
    public CustomWebApplicationFactory Factory { get; } = new FixedSeedWebApplicationFactory();
    public async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedDoctorSchedulesAndSlotsAsync(scope.ServiceProvider, 7, true);
    }
    public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }

    public async Task<HttpClient> LoginAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await manager.FindByEmailAsync(email))!;
        var password = $"Aa1!{Guid.NewGuid():N}";
        var token = await manager.GeneratePasswordResetTokenAsync(user);
        Assert.True((await manager.ResetPasswordAsync(user, token, password)).Succeeded);
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = email, password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("data").GetProperty("accessToken").GetString());
        return client;
    }
}

public class DemoRoundThreeTests(DemoRoundThreeFixture fixture) : IClassFixture<DemoRoundThreeFixture>
{
    [Fact]
    public async Task Seed_HasNoPatientTimeConflicts_AndCompleteClinicalRecords()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointments = await db.Appointments.Where(a => a.Status != AppointmentStatus.Cancelled).ToListAsync();
        foreach (var group in appointments.GroupBy(a => (a.PatientId, a.AppointmentDate)))
        {
            var ordered = group.OrderBy(a => a.StartTime).ToList();
            for (var i = 1; i < ordered.Count; i++) Assert.True(ordered[i - 1].EndTime <= ordered[i].StartTime);
        }
        var visits = await db.PatientVisits.Include(v => v.VisitSummary).Include(v => v.VitalSigns).Where(v => v.CompletedAtUtc != null).ToListAsync();
        Assert.NotEmpty(visits);
        Assert.All(visits, v => { Assert.False(string.IsNullOrWhiteSpace(v.VisitSummary?.Diagnosis)); Assert.False(string.IsNullOrWhiteSpace(v.VisitSummary?.Summary)); Assert.NotNull(v.VitalSigns); });
        var completed = await db.Appointments.Include(a => a.VisitSummary).Include(a => a.VitalSigns).Include(a => a.Prescription).Where(a => a.Status == AppointmentStatus.Completed).ToListAsync();
        Assert.All(completed, a => { Assert.False(string.IsNullOrWhiteSpace(a.VisitSummary?.DiagnosisCode)); Assert.NotNull(a.VitalSigns); Assert.NotNull(a.Prescription); });
    }

    [Fact]
    public async Task Results_ArePublishedOnlyAfterReview_WithOrderedSpecimenTimes_AndVitals()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = await db.DiagnosticOrders.Include(o => o.Patient).Include(o => o.Items).ThenInclude(i => i.Result).ToListAsync();
        foreach (var order in orders)
        {
            Assert.True(order.StartedAtUtc == null || order.OrderedAtUtc <= order.StartedAtUtc);
            foreach (var item in order.Items.Where(i => i.CollectedAtUtc != null))
            {
                Assert.True(order.StartedAtUtc <= item.CollectedAtUtc);
                Assert.True(item.CollectedAtUtc <= item.ReceivedAtUtc);
                Assert.True(item.Result == null || item.ReceivedAtUtc <= item.Result.ResultedAtUtc);
            }
            Assert.All(order.Items.Where(i => i.Result != null && order.VerifiedAtUtc != null), i => Assert.True(i.Result!.ResultedAtUtc <= order.VerifiedAtUtc));
        }
        var published = orders.First(o => o.ReviewedAtUtc != null);
        using var patient = await fixture.LoginAsync(published.Patient.Email!);
        using var result = JsonDocument.Parse(await patient.GetStringAsync($"/api/v1/patients/me/diagnostic-orders/{published.Id}"));
        Assert.Contains(result.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(), i => i.GetProperty("result").ValueKind == JsonValueKind.Object);
        using var vitals = JsonDocument.Parse(await patient.GetStringAsync("/api/v1/patients/me/vitals"));
        Assert.True(vitals.RootElement.GetProperty("data").GetArrayLength() > 0);
        var unpublished = orders.First(o => o.ReviewedAtUtc == null && o.Items.Any(i => i.Result != null));
        using var other = await fixture.LoginAsync(unpublished.Patient.Email!);
        using var hidden = JsonDocument.Parse(await other.GetStringAsync($"/api/v1/patients/me/diagnostic-orders/{unpublished.Id}"));
        Assert.All(hidden.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(), i => Assert.Equal(JsonValueKind.Null, i.GetProperty("result").ValueKind));
    }

    [Fact]
    public async Task Prescriptions_UseEncounterDate_AndPatientCodeUsesVietnamDate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var prescriptions = await db.Prescriptions.Include(p => p.Appointment).Include(p => p.PatientVisit).ToListAsync();
        Assert.All(prescriptions, p => Assert.Equal(p.Appointment?.AppointmentDate ?? p.PatientVisit!.VisitDate, DateOnly.FromDateTime(clock.ConvertUtcToVietnam(p.CreatedAt))));
        using var patient = await fixture.LoginAsync("patient@cliniccare.local");
        using var result = JsonDocument.Parse(await patient.GetStringAsync("/api/v1/patients/me/prescriptions"));
        foreach (var item in result.RootElement.GetProperty("data").EnumerateArray())
        {
            var date = clock.ConvertUtcToVietnam(item.GetProperty("createdAt").GetDateTime());
            Assert.Equal($"RX-{date:yyMMdd}-{item.GetProperty("id").GetInt64():D4}", item.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task WalkInOnlyPatient_SeesTheirOwnVitalsWithoutAnAppointment()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patient = await db.Patients.FirstAsync(p =>
            db.AppointmentVitalSigns.Any(v => v.PatientVisit != null && v.PatientVisit.PatientId == p.Id) &&
            !db.AppointmentVitalSigns.Any(v => v.Appointment != null && v.Appointment.PatientId == p.Id));
        var ownVisitIds = await db.PatientVisits.Where(v => v.PatientId == patient.Id).Select(v => v.Id).ToListAsync();
        using var client = await fixture.LoginAsync(patient.Email!);
        using var result = JsonDocument.Parse(await client.GetStringAsync("/api/v1/patients/me/vitals"));
        var items = result.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Contains(item.GetProperty("patientVisitId").GetInt64(), ownVisitIds));
    }

    [Fact]
    public async Task PrescriptionCode_UsesVietnamCalendarDayAtUtcDateBoundary()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var patient = await db.Patients.SingleAsync(p => p.Email == "patient@cliniccare.local");
        var prescription = await db.Prescriptions.FirstAsync(p => p.PatientId == patient.Id);
        var original = prescription.CreatedAt;
        var date = DateOnly.FromDateTime(clock.ConvertUtcToVietnam(original));
        try
        {
            prescription.CreatedAt = clock.ConvertVietnamToUtc(date.ToDateTime(new TimeOnly(0, 30)));
            await db.SaveChangesAsync();
            using var client = await fixture.LoginAsync(patient.Email!);
            using var result = JsonDocument.Parse(await client.GetStringAsync("/api/v1/patients/me/prescriptions"));
            var item = Assert.Single(result.RootElement.GetProperty("data").EnumerateArray(), i => i.GetProperty("id").GetInt64() == prescription.Id);
            Assert.Equal($"RX-{date:yyMMdd}-{prescription.Id:D4}", item.GetProperty("code").GetString());
        }
        finally { prescription.CreatedAt = original; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task ReceptionKpis_IncludeCompletedWalkIns_AndAllOutstandingInvoices()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
        using var reception = await fixture.LoginAsync("reception@cliniccare.local");
        using var stats = JsonDocument.Parse(await reception.GetStringAsync("/api/v1/reception/stats"));
        var visits = await db.PatientVisits.Where(v => v.VisitDate == today && v.CompletedAtUtc != null).ToListAsync();
        var linked = visits.Where(v => v.AppointmentId != null).Select(v => v.AppointmentId!.Value).ToList();
        var expected = visits.Count + await db.Appointments.CountAsync(a => a.AppointmentDate == today && a.Status == AppointmentStatus.Completed && !linked.Contains(a.Id));
        Assert.Equal(expected, stats.RootElement.GetProperty("data").GetProperty("completedAppointmentsToday").GetInt32());
        using var kpi = JsonDocument.Parse(await reception.GetStringAsync("/api/v1/reception/billing/kpi"));
        using var list = JsonDocument.Parse(await reception.GetStringAsync("/api/v1/reception/billing/invoices?status=Unpaid&pageSize=100"));
        Assert.Equal(list.RootElement.GetProperty("data").GetProperty("totalItems").GetInt32(), kpi.RootElement.GetProperty("data").GetProperty("todayUnpaidInvoices").GetInt32());
    }

    [Fact]
    public async Task PackageRegistration_RejectsMaleForFemalePackage_AndAllowsFemale()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var packageId = (await db.HealthPackages.SingleAsync(p => p.Code == "PKG04")).Id;
        var date = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday.AddDays(3);
        using var male = await fixture.LoginAsync("patient@cliniccare.local");
        var response = await male.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new { healthPackageId = packageId, preferredDate = date, contactPhone = "0901234567" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("PACKAGE_GENDER_MISMATCH", await response.Content.ReadAsStringAsync());
        using var female = await fixture.LoginAsync("patient.01@cliniccare.local");
        Assert.True((await female.PostAsJsonAsync("/api/v1/patient/health-package-registrations", new { healthPackageId = packageId, preferredDate = date, contactPhone = "0901234567" })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Activities_MatchSeededActorsAndTimes_AndRevenueHasTwoToSixPaymentsPerOpenDay()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var notifications = await db.Notifications.ToListAsync();
        foreach (var patient in await db.Patients.ToListAsync())
            Assert.Contains(notifications, n => n.UserId == patient.UserId);
        Assert.Contains(notifications, n => n.IsRead); Assert.Contains(notifications, n => !n.IsRead);
        var audit = await db.SystemAuditLogs.ToListAsync();
        foreach (var invoice in await db.Invoices.ToListAsync())
            Assert.Contains(audit, a => a.Action == "CreateInvoice" && a.EntityId == invoice.Id.ToString() && a.UserId == invoice.CreatedByUserId && a.CreatedAt == invoice.CreatedAtUtc);
        var payments = await db.Payments.Where(p => p.Status == PaymentStatus.Succeeded).ToListAsync();
        foreach (var group in payments.GroupBy(p => DateOnly.FromDateTime(clock.ConvertUtcToVietnam(p.ReceivedAtUtc))))
        { Assert.InRange(group.Count(), group.Key.DayOfWeek == DayOfWeek.Sunday ? 0 : 2, group.Key.DayOfWeek == DayOfWeek.Sunday ? 1 : 6); }
        Assert.Equal((await db.Invoices.Where(i => i.Status == InvoiceStatus.Paid).ToListAsync()).Sum(i => i.TotalAmount), payments.Sum(p => p.Amount));
        Assert.All(await db.Users.ToListAsync(), u => Assert.DoesNotMatch("Quản trị viên|Dược sĩ|Kỹ thuật viên|Lễ tân|Trưởng khoa", u.FullName));
    }

    [Fact]
    public async Task SupervisorCanReadVerificationQueue_AndScheduleSlotCountsRespectEachShift()
    {
        using var supervisor = await fixture.LoginAsync("labsupervisor@cliniccare.local");
        Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync("/api/v1/diagnostic-verification/queue")).StatusCode);
        using var reception = await fixture.LoginAsync("reception@cliniccare.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync("/api/v1/diagnostic-verification/queue")).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = await db.Doctors.FirstAsync();
        var service = scope.ServiceProvider.GetRequiredService<ClinicManagement.Application.Schedules.Interfaces.IScheduleService>();
        foreach (var shift in await service.GetDoctorWorkSchedulesAsync(doctor.Id))
            Assert.Equal(await db.AppointmentSlots.CountAsync(s => s.DoctorId == doctor.Id && s.SlotDate == shift.WorkDate && s.StartTime >= shift.StartTime && s.EndTime <= shift.EndTime), shift.SlotCount);
    }
}
