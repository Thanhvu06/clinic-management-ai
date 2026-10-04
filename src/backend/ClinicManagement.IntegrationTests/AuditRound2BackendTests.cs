using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClinicManagement.Application.Admin.DTOs;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Patients.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Admin;
using ClinicManagement.Infrastructure.Appointments;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

public class AuditRound2BackendTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task R3_Anonymous_lookup_is_rate_limited_without_changing_success_response()
    {
        using var app = Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["AppointmentLookupRateLimiting:TestingPermitLimit"] = "2" })));
        using var client = app.CreateClient();
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/appointments/lookup?query=0123456785")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/v1/appointments/lookup?query=0123456785")).StatusCode);
    }

    [Theory]
    [InlineData("Pharmacist")] [InlineData("DiagnosticTechnician")]
    public async Task R4_Admin_can_create_pharmacy_and_diagnostic_staff(string role)
    {
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var email = $"r2-{Guid.NewGuid():N}@test.com";
        var response = await admin.PostAsJsonAsync("/api/v1/admin/users", new { email, phoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", fullName = "Audit staff", password = "Pass@1234", role });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await manager.IsInRoleAsync((await manager.FindByEmailAsync(email))!, role));
    }

    [Fact]
    public async Task R4_Duplicate_phone_does_not_create_staff()
    {
        using var admin = await CreateAuthenticatedClientAsync("admin@test.com");
        var email = $"r2-{Guid.NewGuid():N}@test.com";
        var response = await admin.PostAsJsonAsync("/api/v1/admin/users", new { email, phoneNumber = "0123456783", fullName = "Audit staff", password = "Pass@1234", role = "Receptionist" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.Contains("Số điện thoại đã được sử dụng.", errors.GetProperty("PhoneNumber").EnumerateArray().Select(x => x.GetString()));
        using var scope = Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task R4_Failed_role_assignment_removes_the_created_user()
    {
        using var scope = Factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var mock = new Mock<UserManager<ApplicationUser>>(scope.ServiceProvider.GetRequiredService<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).Returns((string email) => real.FindByEmailAsync(email));
        mock.SetupGet(m => m.Users).Returns(real.Users);
        mock.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).Returns((ApplicationUser user, string password) => real.CreateAsync(user, password));
        mock.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "AuditRoleFailure", Description = "Role assignment rejected" }));
        mock.Setup(m => m.DeleteAsync(It.IsAny<ApplicationUser>())).Returns((ApplicationUser user) => real.DeleteAsync(user));
        var email = $"r2-{Guid.NewGuid():N}@test.com";
        var service = new AdminUserService(mock.Object, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<ICurrentUserService>());
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.CreateStaffUserAsync(new CreateStaffUserDto { Email = email, PhoneNumber = "", FullName = "Audit staff", Password = "Pass@1234", Role = "Receptionist" }));
        Assert.Contains("Role assignment rejected", error.Message);
        Assert.Null(await real.FindByEmailAsync(email));
        mock.Verify(m => m.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    [Theory]
    [InlineData("plain-db-update", false)] [InlineData("text", false)]
    [InlineData("sqlite-fk", false)] [InlineData("sqlite-check", false)]
    [InlineData("sqlite-unique-unrelated", false)] [InlineData("sqlite-busy", true)]
    [InlineData("sqlite-locked", true)] [InlineData("concurrency", true)]
    public void R5_Booking_conflicts_use_typed_errors_not_messages(string kind, bool expected)
    {
        Exception error = kind switch
        {
            "plain-db-update" => new DbUpdateException("Database write failed"),
            "text" => new Exception("concurrency conflict busy snapshot unique constraint deadlock"),
            "sqlite-fk" => new DbUpdateException("failed", new SqliteException("foreign key", 19, 787)),
            "sqlite-check" => new DbUpdateException("failed", new SqliteException("check", 19, 275)),
            "sqlite-unique-unrelated" => new DbUpdateException("failed", new SqliteException("unique constraint", 19, 2067)),
            "sqlite-busy" => new DbUpdateException("failed", new SqliteException("", 5)),
            "sqlite-locked" => new SqliteException("", 6),
            _ => new DbUpdateConcurrencyException()
        };
        var method = typeof(AppointmentService).GetMethod("IsConcurrencyOrConflictException", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, (bool)method.Invoke(null, new object[] { error })!);
    }

    [Fact]
    public async Task R5_Actual_appointment_unique_violation_is_a_booking_conflict()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = $"R2-{Guid.NewGuid():N}"[..18];
        Appointment Row() => new() { AppointmentCode = code, PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, AppointmentDate = GetFutureWorkingDate(900), StartTime = new(9, 0), EndTime = new(9, 30), Status = AppointmentStatus.Completed };
        db.Appointments.Add(Row());
        await db.SaveChangesAsync();
        db.Appointments.Add(Row());
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var method = typeof(AppointmentService).GetMethod("IsConcurrencyOrConflictException", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, new object[] { error })!);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task R6_Inactive_doctor_account_cannot_be_booked_and_reactivation_allows_booking(bool cached)
    {
        var slot = await CreateAvailableSlotAsync(Doctor2EntityId, GetFutureWorkingDate(901), new(10, 0), new(10, 30));
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = (await db.Users.FindAsync(Doctor2UserId))!;
        var body = new { doctorId = Doctor2EntityId, specialtyId = SpecialtyEntityId, appointmentSlotId = slot.Id, reason = "Đau đầu kéo dài cần bác sĩ khám", idempotencyKey = cached ? Guid.NewGuid().ToString() : null };
        var first = await patient.PostAsJsonAsync("/api/v1/appointments", body);
        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        user.IsActive = false;
        await db.SaveChangesAsync();
        try
        {
            var response = await patient.PostAsJsonAsync("/api/v1/appointments", body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
            Assert.Equal(1, await db.Appointments.CountAsync(a => a.AppointmentSlotId == slot.Id));
            user.IsActive = true;
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Created, (await patient.PostAsJsonAsync("/api/v1/appointments", body)).StatusCode);
        }
        finally { user.IsActive = true; await db.SaveChangesAsync(); }
    }

    [Theory]
    [InlineData("admin/users", "admin@test.com", 0, 1000, 1, 100)]
    [InlineData("admin/users", "admin@test.com", -1, 0, 1, 10)]
    [InlineData("admin/audit-logs", "admin@test.com", 0, 1000, 1, 100)]
    [InlineData("admin/audit-logs", "admin@test.com", -1, 0, 1, 10)]
    [InlineData("reception/appointments", "rec@test.com", 0, 1000, 1, 100)]
    [InlineData("reception/appointments", "rec@test.com", -1, 0, 1, 10)]
    [InlineData("notifications", "pat1@test.com", 0, 1000, 1, 100)]
    public async Task R4_R7_Pagination_is_bounded(string path, string email, int page, int size, int expectedPage, int expectedSize)
    {
        using var client = await CreateAuthenticatedClientAsync(email);
        var response = await client.GetAsync($"/api/v1/{path}?page={page}&pageSize={size}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal(expectedPage, data.GetProperty("page").GetInt32());
        Assert.Equal(expectedSize, data.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData(-1, 1)] [InlineData(1000, 100)]
    public async Task R7_Vitals_already_bound_limits_on_main(int limit, int expected)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < 101; i++)
            db.Appointments.Add(new Appointment { AppointmentCode = $"R2-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, AppointmentDate = GetFutureWorkingDate(1000 + i), StartTime = new(9, 0), EndTime = new(9, 30), Status = AppointmentStatus.Completed, VitalSigns = new AppointmentVitalSigns { RecordedByUserId = DoctorId, HeartRate = 70 } });
        await db.SaveChangesAsync();
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await patient.GetAsync($"/api/v1/patients/me/vitals?limit={limit}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task R8_Admin_today_uses_vietnam_date_at_0010()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.SpecifyKind(today.ToDateTime(new(0, 10)).AddHours(-7), DateTimeKind.Utc));
        clock.SetupGet(x => x.VietnamToday).Returns(today);
        using var app = Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(clock.Object)));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var date in new[] { today, today, today.AddDays(-1) })
            db.Appointments.Add(new Appointment { AppointmentCode = $"R2-{Guid.NewGuid():N}"[..18], PatientId = Patient1EntityId, DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlotId = SlotEntityId, AppointmentDate = date, StartTime = new(9, 0), EndTime = new(9, 30), Status = AppointmentStatus.Completed });
        await db.SaveChangesAsync();
        using var client = app.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = "admin@test.com", password = "Pass@123" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("accessToken").GetString());
        var data = JsonDocument.Parse(await (await client.GetAsync("/api/v1/admin/stats")).Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal(await db.Appointments.CountAsync(a => a.AppointmentDate == today), data.GetProperty("totalAppointmentsToday").GetInt32());
    }

    [Theory]
    [InlineData(null)] [InlineData("pat1@test.com")] [InlineData("rec@test.com")]
    [InlineData("doc@test.com")] [InlineData("pharm@test.com")] [InlineData("tech@test.com")] [InlineData("admin@test.com")]
    public async Task R9_Catalog_only_exposes_public_or_actor_roles_and_capabilities(string? email)
    {
        using var client = email == null ? Factory.CreateClient() : await CreateAuthenticatedClientAsync(email);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = email == null ? null : await db.Users.SingleAsync(u => u.Email == email);
        var roles = new HashSet<AiActorRole>();
        if (actor != null)
            foreach (var role in await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(actor)) roles.Add(Enum.Parse<AiActorRole>(role));
        var caps = await scope.ServiceProvider.GetRequiredService<IAiCapabilityResolver>().ResolveAsync(new AiToolExecutionContext { ActorId = actor?.Id, IsAuthenticated = actor != null, Roles = roles });
        var expected = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>().GetDefinitions().Where(d => actor == null ? d.AccessMode == AiToolAccessMode.Public : (d.AccessMode != AiToolAccessMode.RoleRestricted || d.AllowedRoles.Any(roles.Contains)) && d.Capabilities.All(caps.Contains)).Select(d => d.Name).Order().ToArray();
        var response = await client.GetAsync("/api/v1/ai/tools");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("name").GetString()).Order().ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task R9_Reception_role_without_facility_capability_does_not_expose_reception_tools()
    {
        var email = $"r2-{Guid.NewGuid():N}@test.com";
        using (var scope = Factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", FullName = "Unassigned reception", IsActive = true };
            Assert.True((await manager.CreateAsync(user, "Pass@123")).Succeeded);
            Assert.True((await manager.AddToRoleAsync(user, "Receptionist")).Succeeded);
        }
        using var client = await CreateAuthenticatedClientAsync(email);
        var response = await client.GetAsync("/api/v1/ai/tools");
        var names = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray();
        Assert.Contains("clinic.search_specialties", names);
        Assert.DoesNotContain("reception.get_today_appointments", names);
        Assert.DoesNotContain("reception.prepare_confirm_appointment", names);
    }
}
