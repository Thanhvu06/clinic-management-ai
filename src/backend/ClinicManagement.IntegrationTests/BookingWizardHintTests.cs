using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

/// <summary>S5: an optional typed hint can preselect a doctor or specialty on the wizard start step only.</summary>
[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class BookingWizardHintTests : IntegrationTestBase
{
    private const string Url = "/api/v1/ai/booking-wizard";
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    public BookingWizardHintTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData("bác sĩ Lan")]
    [InlineData("bs lan")]
    [InlineData("Bác sĩ Trần Thị Lan")]
    [InlineData("doctor Lan")]
    public async Task Unique_doctor_hint_starts_on_day_with_that_doctor_and_primary_specialty(string hint)
    {
        await SeedAsync();
        var (client, session) = await PatientAsync();
        var state = await StepAsync(client, new { sessionId = session, step = "start", hint });
        Assert.Equal("day", state.GetProperty("step").GetString());
        Assert.Equal("Trần Thị Lan", state.GetProperty("summary").GetProperty("doctorName").GetString());
        Assert.Equal("Da liễu", state.GetProperty("summary").GetProperty("specialtyName").GetString());
        Assert.True(state.GetProperty("canGoBack").GetBoolean());
    }

    [Theory]
    [InlineData("tim mạch")]
    [InlineData("tim mach")]
    [InlineData("khoa Tim Mạch")]
    public async Task Unique_specialty_hint_starts_on_doctor_step_of_that_specialty(string hint)
    {
        await SeedAsync();
        var (client, session) = await PatientAsync();
        var state = await StepAsync(client, new { sessionId = session, step = "start", hint });
        Assert.Equal("doctor", state.GetProperty("step").GetString());
        Assert.Equal("Tim mạch", state.GetProperty("summary").GetProperty("specialtyName").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("summary").GetProperty("doctorName").ValueKind);
    }

    [Theory]
    [InlineData("bác sĩ Zyxw")]
    [InlineData("bác sĩ Hoa")]
    [InlineData("phục hồi chức năng")]
    [InlineData("bác sĩ Khoi")]
    [InlineData("   ")]
    public async Task Unknown_ambiguous_or_non_ai_hint_falls_back_to_specialty_without_error(string hint)
    {
        await SeedAsync();
        var (client, session) = await PatientAsync();
        // Control: the same session can be prefilled by a matching hint.
        Assert.Equal("day", (await StepAsync(client, new { sessionId = session, step = "start", hint = "bác sĩ Lan" })).GetProperty("step").GetString());
        var state = await StepAsync(client, new { sessionId = session, step = "start", hint });
        Assert.Equal("specialty", state.GetProperty("step").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("errorCode").ValueKind);
        Assert.NotEmpty(state.GetProperty("options").EnumerateArray());
    }

    [Fact]
    public async Task Hint_on_pick_step_is_ignored()
    {
        await SeedAsync();
        var (client, session) = await PatientAsync();
        Assert.Equal("day", (await StepAsync(client, new { sessionId = session, step = "start", hint = "bác sĩ Lan" })).GetProperty("step").GetString());
        var start = await StepAsync(client, new { sessionId = session, step = "start" });
        Assert.Equal("specialty", start.GetProperty("step").GetString());
        var token = start.GetProperty("options")[0].GetProperty("token").GetString();
        var picked = await StepAsync(client, new { sessionId = session, step = "pick", optionToken = token, hint = "bác sĩ Lan" });
        Assert.Equal("doctor", picked.GetProperty("step").GetString());
        Assert.NotEqual("Da liễu", picked.GetProperty("summary").GetProperty("specialtyName").GetString());
    }

    [Fact]
    public async Task Back_from_prefilled_day_goes_to_doctor_then_specialty_and_hint_is_not_audited()
    {
        await SeedAsync();
        var (client, session) = await PatientAsync();
        var day = await StepAsync(client, new { sessionId = session, step = "start", hint = "bác sĩ Lan" });
        Assert.Equal("day", day.GetProperty("step").GetString());
        var doctor = await StepAsync(client, new { sessionId = session, step = "back", optionToken = day.GetProperty("backToken").GetString() });
        Assert.Equal("doctor", doctor.GetProperty("step").GetString());
        Assert.Equal("Da liễu", doctor.GetProperty("summary").GetProperty("specialtyName").GetString());
        Assert.Contains(doctor.GetProperty("options").EnumerateArray(), option => option.GetProperty("label").GetString() == "Trần Thị Lan");
        var specialty = await StepAsync(client, new { sessionId = session, step = "back", optionToken = doctor.GetProperty("backToken").GetString() });
        Assert.Equal("specialty", specialty.GetProperty("step").GetString());

        var prefilledDoctor = await StepAsync(client, new { sessionId = session, step = "start", hint = "tim mạch" });
        var back = await StepAsync(client, new { sessionId = session, step = "back", optionToken = prefilledDoctor.GetProperty("backToken").GetString() });
        Assert.Equal("specialty", back.GetProperty("step").GetString());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AiAuditLogs.Where(x => x.SessionId == session).Select(x => x.MetadataJson ?? "").ToListAsync();
        Assert.NotEmpty(logs);
        Assert.All(logs, log => { Assert.DoesNotContain("Lan", log); Assert.DoesNotContain("tim", log, StringComparison.OrdinalIgnoreCase); });
    }

    [Fact]
    public async Task Oversized_hint_is_rejected_by_validation()
    {
        await SeedAsync();
        var (client, session) = await PatientAsync();
        var response = await client.PostAsJsonAsync(Url, new { sessionId = session, step = "start", hint = new string('a', 121) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SeedAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dermatology = await db.Specialties.SingleAsync(x => x.SpecialtyCode == "SP04");
            if (await db.Specialties.AnyAsync(x => x.SpecialtyCode == "SPX-HINT")) { _seeded = true; return; }
            var rehab = new Specialty { SpecialtyCode = "SPX-HINT", Name = "Phục hồi chức năng", Description = "Không mở cho trợ lý", IsActive = true, AiEnabled = false, ConsultationFee = 100000m };
            db.Specialties.Add(rehab);
            await db.SaveChangesAsync();
            await DoctorAsync(db, "Trần Thị Lan", dermatology.Id);
            await DoctorAsync(db, "Nguyễn Thị Hoa", dermatology.Id);
            await DoctorAsync(db, "Lê Văn Hoa", dermatology.Id);
            await DoctorAsync(db, "Phạm Văn Khoi", rehab.Id);
            _seeded = true;
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task DoctorAsync(AppDbContext db, string fullName, long specialtyId)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = id, UserName = $"hint-{id:N}@test.com", Email = $"hint-{id:N}@test.com", FullName = fullName, PhoneNumber = "09" + Random.Shared.Next(10_000_000, 99_999_999), IsActive = true });
        var doctor = new Doctor { UserId = id, IsActive = true, AcademicTitle = "BS", ExperienceYears = 3 };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialtyId, IsPrimary = true });
        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, string Session)> PatientAsync() =>
        (await CreateAuthenticatedClientAsync("pat1@test.com"), $"wizhint_{Guid.NewGuid():N}");

    private static async Task<JsonElement> StepAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Url, body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }
}
