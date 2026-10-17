using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

public class Icd10CatalogTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("J02")]
    [InlineData("j02")]
    [InlineData("viem hong")]
    [InlineData("Viêm họng")]
    public async Task Search_matches_code_and_Vietnamese_names(string search)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync($"/api/v1/icd10?search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var codes = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Contains(codes.EnumerateArray(), x => x.GetProperty("code").GetString() == "J02.9");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?search=")]
    [InlineData("?search=%20%20")]
    public async Task Empty_search_returns_empty_list(string query)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync("/api/v1/icd10" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray());
    }

    [Theory]
    [InlineData("pat1@test.com", HttpStatusCode.Forbidden)]
    [InlineData("rec@test.com", HttpStatusCode.OK)]
    [InlineData("admin@test.com", HttpStatusCode.OK)]
    public async Task Search_enforces_roles(string email, HttpStatusCode status)
    {
        await AuthenticateAsync(email);
        Assert.Equal(status, (await Client.GetAsync("/api/v1/icd10?search=J02")).StatusCode);
    }

    [Fact]
    public async Task Search_requires_authentication()
    {
        using var client = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/icd10?search=J02")).StatusCode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(2, 2)]
    [InlineData(999, 50)]
    public async Task Search_limits_results(int limit, int maximum)
    {
        await AuthenticateAsync("doc@test.com");
        var response = await Client.GetAsync($"/api/v1/icd10?search=a&limit={limit}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var count = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetArrayLength();
        Assert.InRange(count, 1, maximum);
        if (limit is 0 or -5 or 2 or 999) Assert.Equal(maximum, count);
    }

    [Fact]
    public async Task Search_prioritizes_code_matches_and_excludes_inactive_codes()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Icd10Codes.AddRange(
            new Icd10Code { Code = "Z99.8", NameVi = "J02 inactive fixture", SearchKey = "j02 inactive fixture", ChapterCode = "Z00-Z99", IsActive = false },
            new Icd10Code { Code = "Z99.9", NameVi = "J02 name fixture", SearchKey = "j02 name fixture", ChapterCode = "Z00-Z99", IsActive = true });
        await db.SaveChangesAsync();
        try
        {
            await AuthenticateAsync("doc@test.com");
            var response = await Client.GetAsync("/api/v1/icd10?search=J02");
            var codes = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray()
                .Select(x => x.GetProperty("code").GetString()).ToArray();
            Assert.Equal(new[] { "J02.0", "J02.9", "Z99.9" }, codes);
        }
        finally
        {
            db.Icd10Codes.RemoveRange(await db.Icd10Codes.Where(x => x.Code == "Z99.8" || x.Code == "Z99.9").ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Seed_has_unique_codes_and_precomputed_search_keys()
    {
        using var scope = Factory.Services.CreateScope();
        var codes = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Icd10Codes.AsNoTracking().ToListAsync();
        Assert.InRange(codes.Count, 150, 200);
        Assert.Equal(codes.Count, codes.Select(x => x.Code).Distinct().Count());
        Assert.Contains(codes, x => x.Code == "J06.9");
        Assert.All(codes, code =>
        {
            Assert.Matches("^[A-Z][0-9]{2}(\\.[0-9A-Z]{1,4})?$", code.Code);
            Assert.InRange(code.NameVi.Length, 1, 300);
            Assert.InRange(code.SearchKey.Length, 1, 400);
            Assert.Equal(VietnameseText.ToSearchKey($"{code.Code} {code.NameVi}"), code.SearchKey);
            Assert.True(code.IsActive);
        });
        Assert.Equal("dai thao duong", VietnameseText.ToSearchKey("  Đái tháo đường "));
        Assert.Equal("viem hong", VietnameseText.ToSearchKey("viêm họng".Normalize(System.Text.NormalizationForm.FormD)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Save_encounter_validates_and_normalizes_codes(bool visit, bool existing)
    {
        var (url, summaryId, email) = await CreateEncounterAsync(visit, existing);
        await AuthenticateAsync(email);
        var invalid = await Client.PutAsJsonAsync(url + "/encounter", new { diagnosis = "Viêm họng", diagnosisCode = "ZZZ.9" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_ICD10", error.GetProperty("errorCode").GetString());
        Assert.Equal("Mã ICD-10 không có trong danh mục.", error.GetProperty("message").GetString());

        var response = await Client.PutAsJsonAsync(url + "/encounter", new { diagnosis = "Viêm họng", diagnosisCode = " j02.9 ", rowVersion = await RowVersionAsync(summaryId) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("J02.9", saved.GetProperty("diagnosisCode").GetString());
        summaryId = saved.GetProperty("id").GetInt64();
        Assert.Equal("J02.9", (await ReadSummaryAsync(summaryId.Value)).DiagnosisCode);

        var empty = await Client.PutAsJsonAsync(url + "/encounter", new { diagnosis = "Viêm họng", diagnosisCode = "  ", rowVersion = await RowVersionAsync(summaryId) });
        Assert.True(empty.IsSuccessStatusCode, await empty.Content.ReadAsStringAsync());
        Assert.Null((await ReadSummaryAsync(summaryId.Value)).DiagnosisCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Complete_consultation_normalizes_codes(bool visit, bool existing)
    {
        var (url, summaryId, email) = await CreateEncounterAsync(visit, existing);
        await AuthenticateAsync(email);
        var response = await Client.PostAsJsonAsync(url + "/complete", new
        {
            diagnosis = "Viêm họng", summary = "Ổn định", diagnosisCode = " j02.9 ",
            encounterRowVersion = await RowVersionAsync(summaryId)
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = long.Parse(url.Split('/').Last());
        var saved = await db.VisitSummaries.SingleAsync(x => visit ? x.PatientVisitId == id : x.AppointmentId == id);
        Assert.Equal("J02.9", saved.DiagnosisCode);
    }

    [Fact]
    public async Task Inactive_code_is_rejected_when_saving()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = await db.Icd10Codes.FindAsync("J02.0");
        code!.IsActive = false;
        await db.SaveChangesAsync();
        try
        {
            var (url, _, email) = await CreateEncounterAsync(true, false);
            await AuthenticateAsync(email);
            var response = await Client.PutAsJsonAsync(url + "/encounter", new { diagnosis = "Viêm họng", diagnosisCode = "J02.0" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("INVALID_ICD10", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        }
        finally { code.IsActive = true; await db.SaveChangesAsync(); }
    }

    private async Task<(string Url, long? SummaryId, string Email)> CreateEncounterAsync(bool visit, bool existing)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"icd-doctor-{Guid.NewGuid():N}@test.com";
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "ICD Doctor", PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}", IsActive = true };
        Assert.True((await users.CreateAsync(user, "Pass@123")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Doctor")).Succeeded);
        var doctor = new Doctor { UserId = user.Id, IsActive = true };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        VisitSummary? summary = existing ? new VisitSummary { DoctorId = doctor.Id, Diagnosis = "Existing", Summary = "Existing", RowVersion = Guid.NewGuid().ToByteArray() } : null;
        long id;
        if (visit)
        {
            var department = await db.Departments.FirstAsync();
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment { UserId = user.Id, FacilityId = department.FacilityId, DepartmentId = department.Id, Role = "Doctor", IsActive = true });
            var entity = new PatientVisit { VisitCode = $"ICD-{Guid.NewGuid():N}", PatientId = Patient1EntityId, AssignedDoctorId = doctor.Id,
                FacilityId = department.FacilityId, DepartmentId = department.Id, VisitDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
                Status = VisitStatus.InConsultation, QueueNumber = TestQueueNumbers.Next(), CreatedByUserId = ReceptionistId, VisitSummary = summary };
            db.PatientVisits.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }
        else
        {
            var entity = new Appointment { AppointmentCode = $"ICD-{Guid.NewGuid():N}", PatientId = Patient1EntityId, DoctorId = doctor.Id,
                SpecialtyId = SpecialtyEntityId, AppointmentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
                StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), Status = AppointmentStatus.InConsultation, VisitSummary = summary,
                AppointmentSlot = new AppointmentSlot { DoctorId = doctor.Id, SlotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 30), IsBooked = true } };
            db.Appointments.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }
        return ($"/api/v1/doctor/{(visit ? "visits" : "appointments")}/{id}", summary?.Id, email);
    }

    private async Task<VisitSummary> ReadSummaryAsync(long id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().VisitSummaries.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<string?> RowVersionAsync(long? id) => id.HasValue && (await ReadSummaryAsync(id.Value)).RowVersion is { } version
        ? Convert.ToBase64String(version) : null;
}
