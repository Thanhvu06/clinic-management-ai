using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class CopilotUiFollowupTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ServiceGuidance_JoinsStoredSentencesWithoutPeriodSemicolon()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.DiagnosticServices.FirstOrDefaultAsync(s => s.Code == "LAB-CBC");
        if (service is null) db.Add(service = new DiagnosticService { Code = "LAB-CBC", Name = "Tổng phân tích tế bào máu ngoại vi", IsActive = true });
        service.PreparationInstructions = "Không cần nhịn ăn đặc biệt.";
        service.SpecimenType = "Máu toàn phần";
        await db.SaveChangesAsync();

        var response = await ChatAsync(await CreateAuthenticatedClientAsync("tech@test.com"), "cách lấy mẫu xét nghiệm công thức máu");
        var expected = "Không cần nhịn ăn đặc biệt. Loại bệnh phẩm: Máu toàn phần.";
        Assert.Contains(expected, response.GetProperty("message").GetString());
        Assert.DoesNotContain(".;", response.GetProperty("message").GetString());
        var description = Assert.Single(response.GetProperty("cards").EnumerateArray()).GetProperty("description").GetString();
        Assert.Contains(expected, description);
        Assert.DoesNotContain(".;", description);
    }

    [Theory]
    [InlineData("Nguyễn Đình Thành")]
    [InlineData("0900000004")]
    [InlineData("BN-2026-000001")]
    public async Task ReceptionSearch_FindsSamePatientByNamePhoneAndExactMedicalRecord(string query)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = await db.StaffFacilityAssignments.Where(a => a.UserId == ReceptionistId && a.Role == "Receptionist" && a.IsActive).Select(a => a.FacilityId).FirstAsync();
        var patient = await db.Patients.SingleAsync(p => p.Id == Patient1EntityId);
        patient.FullName = "Nguyễn Đình Thành";
        patient.PhoneNumber = "0900000004";
        patient.MedicalRecordNumber = "BN-2026-000001";
        patient.PrimaryFacilityId = facility;
        await db.SaveChangesAsync();

        var response = await ChatAsync(await CreateAuthenticatedClientAsync("rec@test.com"), $"tìm bệnh nhân {query}");
        Assert.Contains(response.GetProperty("executedToolNames").EnumerateArray(), tool => tool.GetString() == "reception.search_patients");
        var card = Assert.Single(response.GetProperty("cards").EnumerateArray(), c => c.GetProperty("type").GetString() == "reception_patient_lookup");
        var row = Assert.Single(card.GetProperty("data").EnumerateArray());
        Assert.Equal("Nguyễn Đình Thành", row.GetProperty("fullName").GetString());
        Assert.Equal("BN-2026-000001", row.GetProperty("medicalRecordNumber").GetString());
        Assert.Equal("0900***004", row.GetProperty("phone").GetString());
        Assert.DoesNotContain("0900000004", response.GetRawText());
    }

    private static async Task<JsonElement> ChatAsync(HttpClient client, string message)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Clone();
    }
}
