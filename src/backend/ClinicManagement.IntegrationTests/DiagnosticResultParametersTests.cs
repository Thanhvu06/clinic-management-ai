using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DiagnosticResultParametersTests : IntegrationTestBase
{
    public DiagnosticResultParametersTests(CustomWebApplicationFactory factory) : base(factory) { }
    private static int _offset = 180;

    private async Task<DiagnosticOrderDto> CreateOrderAsync(Gender gender = Gender.Male)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facilityId = await db.StaffFacilityAssignments.Where(a => a.UserId == TechnicianId && a.IsActive)
            .Select(a => a.FacilityId).FirstAsync();
        var patient = await db.Patients.FindAsync(Patient1EntityId);
        patient!.Gender = gender;
        var service = new DiagnosticService
        {
            Code = $"LIS-{Guid.NewGuid():N}"[..25], Name = "Bộ chỉ số kiểm thử", Category = DiagnosticCategory.Laboratory,
            Parameters = new List<DiagnosticServiceParameter>
            {
                new() { Code = "WBC", Name = "Bạch cầu", Unit = "10^9/L", RefLow = 4, RefHigh = 10, CriticalLow = 2, CriticalHigh = 30, SortOrder = 0 },
                new() { Code = "HGB", Name = "Huyết sắc tố", Unit = "g/L", RefLow = 130, RefHigh = 170, RefLowFemale = 120, RefHighFemale = 150, CriticalLow = 70, CriticalHigh = 200, SortOrder = 1 },
                new() { Code = "CHOL", Name = "Cholesterol", Unit = "mmol/L", RefHigh = 5.2m, SortOrder = 2 },
                new() { Code = "HDL", Name = "HDL-C", Unit = "mmol/L", RefLow = 1, SortOrder = 3 },
                new() { Code = "OLD", Name = "Chỉ số ngừng dùng", IsActive = false, SortOrder = 4 }
            }
        };
        db.DiagnosticServices.Add(service);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, GetFutureWorkingDate(Interlocked.Increment(ref _offset)), new TimeOnly(8, 0), new TimeOnly(8, 30));
        var appointment = new Appointment
        {
            AppointmentCode = $"LIS-{Guid.NewGuid():N}"[..25], PatientId = Patient1EntityId, DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId, FacilityId = facilityId, AppointmentSlotId = slot.Id,
            AppointmentDate = slot.SlotDate, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.InConsultation
        };
        db.Appointments.Add(appointment);
        var order = new DiagnosticOrder
        {
            OrderCode = $"DX-LIS-{Guid.NewGuid():N}", PatientId = Patient1EntityId, OrderingDoctorId = DoctorEntityId,
            FacilityId = facilityId, Appointment = appointment, ClinicalIndication = "Kiểm thử chỉ số",
            Status = DiagnosticOrderStatus.InProgress,
            Items = new List<DiagnosticOrderItem> { new() { DiagnosticService = service, Status = DiagnosticItemStatus.InProgress } }
        };
        db.DiagnosticOrders.Add(order);
        await db.SaveChangesAsync();
        await AuthenticateAsync("tech@test.com");
        return await ReadOrderAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
    }

    private static RecordDiagnosticResultRequest Values(DiagnosticOrderDto order, decimal wbc = 7, decimal hgb = 140, decimal chol = 4, decimal hdl = 1.2m) => new()
    {
        RowVersion = order.Items[0].RowVersion,
        Values = order.Items[0].Parameters.Select(p => new DiagnosticResultValueInput
        {
            ParameterId = p.Id, Value = p.Code switch { "WBC" => wbc, "HGB" => hgb, "CHOL" => chol, _ => hdl }
        }).ToList()
    };

    private Task<HttpResponseMessage> RecordAsync(DiagnosticOrderDto order, RecordDiagnosticResultRequest request) =>
        Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{order.Items[0].Id}/result", request);

    private static async Task<DiagnosticOrderDto> ReadOrderAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<DiagnosticOrderDto>(JsonDocument.Parse(body).RootElement.GetProperty("data").GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public async Task SavesCompleteValuesAndSnapshots_AllFlagsAndGeneratedText()
    {
        var order = await CreateOrderAsync();
        Assert.Equal(4, order.Items[0].Parameters.Count);
        Assert.Equal("4.0 – 10.0", order.Items[0].Parameters[0].ReferenceRangeText);
        Assert.Equal("≤ 5.2", order.Items[0].Parameters[2].ReferenceRangeText);
        Assert.Equal("≥ 1.0", order.Items[0].Parameters[3].ReferenceRangeText);
        var saved = await ReadOrderAsync(await RecordAsync(order, Values(order, wbc: 3, hgb: 210, chol: 6)));
        var result = saved.Items[0].Result!;
        Assert.Equal(new[] { "Low", "CriticalHigh", "High", "Normal" }, result.Values.Select(v => v.Flag));
        Assert.Contains("Bạch cầu (WBC): 3 10^9/L [Thấp]", result.ResultText);
        Assert.Contains("[Cao – NGUY HIỂM]", result.ResultText);
        Assert.Contains("[Bình thường]", result.ResultText);
        Assert.Null(result.ReferenceRange);
        Assert.Null(result.Unit);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parameter = await db.DiagnosticServiceParameters.FindAsync(result.Values[0].ParameterId);
        parameter!.Name = "Tên mới"; parameter.RefLow = 99;
        await db.SaveChangesAsync();
        var reread = await ReadOrderAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
        Assert.Equal("Bạch cầu", reread.Items[0].Result!.Values[0].ParameterName);
        Assert.Equal(4m, reread.Items[0].Result!.Values[0].RefLow);
    }

    [Theory]
    [InlineData(Gender.Female, "Normal", 120)]
    [InlineData(Gender.Male, "Low", 130)]
    [InlineData(Gender.Other, "Low", 130)]
    public async Task ChoosesPatientGenderReferenceRange(Gender gender, string flag, int refLow)
    {
        var order = await CreateOrderAsync(gender);
        Assert.Equal((decimal)refLow, order.Items[0].Parameters.Single(p => p.Code == "HGB").RefLow);
        var saved = await ReadOrderAsync(await RecordAsync(order, Values(order, hgb: 125)));
        Assert.Equal(flag, saved.Items[0].Result!.Values.Single(v => v.ParameterCode == "HGB").Flag);
    }

    [Theory]
    [InlineData(2, "CriticalLow")]
    [InlineData(30, "CriticalHigh")]
    [InlineData(4, "Normal")]
    [InlineData(10, "Normal")]
    [InlineData(11, "High")]
    public async Task AppliesInclusiveCriticalAndExclusiveReferenceBoundaries(int wbc, string flag)
    {
        var order = await CreateOrderAsync();
        var saved = await ReadOrderAsync(await RecordAsync(order, Values(order, wbc: wbc)));
        Assert.Equal(flag, saved.Items[0].Result!.Values[0].Flag);
    }

    [Theory]
    [InlineData("missing", "Thiếu chỉ số: Bạch cầu")]
    [InlineData("foreign", "Chỉ số không thuộc dịch vụ này.")]
    [InlineData("duplicate", "Không được nhập trùng chỉ số.")]
    [InlineData("negative", "Giá trị chỉ số phải")]
    [InlineData("million", "Giá trị chỉ số phải")]
    public async Task RejectsInvalidValuesWithoutWritingResults(string scenario, string message)
    {
        var order = await CreateOrderAsync();
        var request = Values(order);
        switch (scenario)
        {
            case "missing": request.Values!.RemoveAt(0); break;
            case "foreign": request.Values![0].ParameterId = -1; break;
            case "duplicate": request.Values!.Add(request.Values[0]); break;
            case "negative": request.Values![0].Value = -1; break;
            case "million": request.Values![0].Value = 1_000_000; break;
        }
        var response = await RecordAsync(order, request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("VALIDATION_ERROR", body);
        Assert.Contains(message, JsonDocument.Parse(body).RootElement.GetProperty("message").GetString());
        var reread = await ReadOrderAsync(await Client.GetAsync($"/api/v1/diagnostics/orders/{order.Id}"));
        Assert.Null(reread.Items[0].Result);
    }

    [Fact]
    public async Task CriticalNotifiesOrderingDoctorOnce_ChangedCriticalRenotifies_ValuesReplaceAtomically()
    {
        var order = await CreateOrderAsync();
        var saved = await ReadOrderAsync(await RecordAsync(order, Values(order, wbc: 2)));
        Assert.True(saved.Items[0].Result!.HasCriticalValue);
        Assert.NotNull(saved.Items[0].Result!.CriticalNotifiedAtUtc);
        var notified = saved.Items[0].Result!.CriticalNotifiedAtUtc;
        var repeated = await ReadOrderAsync(await RecordAsync(saved, Values(saved, wbc: 2)));
        Assert.Equal(notified, repeated.Items[0].Result!.CriticalNotifiedAtUtc);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notices = await db.Notifications.Where(n => n.RelatedEntityId == order.Id.ToString() && n.Title.StartsWith("CẢNH BÁO")).ToListAsync();
        var notice = Assert.Single(notices);
        Assert.Equal(DoctorId, notice.UserId);
        Assert.Equal(NotificationType.Diagnostic, notice.Type);
        Assert.Contains(order.OrderCode, notice.Message);
        Assert.Contains("Bạch cầu", notice.Message);
        Assert.Equal($"/doctor/appointments/{order.AppointmentId}/examination", notice.Route);
        Assert.StartsWith($"diag_critical_{order.Items[0].Id}_", notice.DedupeKey);
        Assert.Equal(1, await db.SystemAuditLogs.CountAsync(a => a.EntityId == order.Items[0].Id.ToString() && a.Action == "DiagnosticCriticalValueNotified"));
        var changed = await ReadOrderAsync(await RecordAsync(repeated, Values(repeated, wbc: 1)));
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.RelatedEntityId == order.Id.ToString() && n.Title.StartsWith("CẢNH BÁO")));
        Assert.Equal(4, await db.DiagnosticResultValues.CountAsync(v => v.DiagnosticResultId == changed.Items[0].Result!.Id));
        var normal = await ReadOrderAsync(await RecordAsync(changed, Values(changed)));
        Assert.False(normal.Items[0].Result!.HasCriticalValue);
    }

    [Fact]
    public async Task LegacyTextRequestStillSavesAndClearsNumericValues()
    {
        var order = await CreateOrderAsync();
        var saved = await ReadOrderAsync(await RecordAsync(order, Values(order)));
        var response = await Client.PutAsJsonAsync($"/api/v1/diagnostics/orders/{order.Id}/items/{order.Items[0].Id}/result", new
        {
            resultText = "Bạch cầu trong giới hạn", conclusion = "Bình thường", referenceRange = "4–10", unit = "G/L", rowVersion = saved.Items[0].RowVersion
        });
        var legacy = await ReadOrderAsync(response);
        Assert.Empty(legacy.Items[0].Result!.Values);
        Assert.Equal("Bạch cầu trong giới hạn", legacy.Items[0].Result!.ResultText);
        Assert.Equal("4–10", legacy.Items[0].Result!.ReferenceRange);
        Assert.Equal("G/L", legacy.Items[0].Result!.Unit);
        Assert.False(legacy.Items[0].Result!.HasCriticalValue);
        var blank = await RecordAsync(legacy, new RecordDiagnosticResultRequest { Values = new() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Contains("Kết quả cận lâm sàng không được để trống.", JsonDocument.Parse(await blank.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PreservesCommentAndLimitsGeneratedText_StaleVersionCannotOverwrite()
    {
        var order = await CreateOrderAsync();
        var request = Values(order); request.ResultText = new string('x', 3950);
        var saved = await ReadOrderAsync(await RecordAsync(order, request));
        Assert.StartsWith("xxx", saved.Items[0].Result!.ResultText);
        Assert.True(saved.Items[0].Result!.ResultText.Length <= 4000);
        Assert.Contains("HDL-C (HDL)", saved.Items[0].Result!.ResultText);
        var stale = await RecordAsync(saved, Values(order, wbc: 2));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task BatchedDoctorAndPatientDtosRetainValues_VisibilityStillRequiresReview()
    {
        var order = await CreateOrderAsync();
        await ReadOrderAsync(await RecordAsync(order, Values(order, wbc: 2)));
        await AuthenticateAsync("pat1@test.com");
        var hidden = await ReadOrderAsync(await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{order.Id}"));
        Assert.Null(hidden.Items[0].Result);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.DiagnosticOrders.FindAsync(order.Id);
            entity!.Status = DiagnosticOrderStatus.Completed; entity.ReviewedAtUtc = DateTime.UtcNow; entity.ReviewedByDoctorId = DoctorEntityId;
            await db.SaveChangesAsync();
        }
        var visible = await ReadOrderAsync(await Client.GetAsync($"/api/v1/patients/me/diagnostic-orders/{order.Id}"));
        Assert.Equal(4, visible.Items[0].Result!.Values.Count);
        var batchBody = JsonDocument.Parse(await (await Client.GetAsync("/api/v1/patients/me/diagnostic-orders?pageSize=50")).Content.ReadAsStringAsync());
        var batched = batchBody.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetInt64() == order.Id);
        Assert.Equal(4, batched.GetProperty("items")[0].GetProperty("result").GetProperty("values").GetArrayLength());
        Assert.Equal(4, batched.GetProperty("items")[0].GetProperty("parameters").GetArrayLength());
        await AuthenticateAsync("doc@test.com");
        var doctor = await ReadOrderAsync(await Client.GetAsync($"/api/v1/doctor/diagnostic-orders/{order.Id}"));
        Assert.Equal("CriticalLow", doctor.Items[0].Result!.Values[0].Flag);
    }

    [Fact]
    public async Task ParameterSeedIsIdempotent_UpdatesCatalogWithoutRemovingOtherParameters()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var code in new[] { "LAB-CBC", "LAB-GLU", "LAB-LIPID", "LAB-LFT", "LAB-RFT", "US-ABD" })
            db.DiagnosticServices.Add(new DiagnosticService { Code = code, Name = code, Category = code == "US-ABD" ? DiagnosticCategory.Ultrasound : DiagnosticCategory.Laboratory });
        await db.SaveChangesAsync();
        await DevelopmentDataSeeder.SeedDiagnosticParametersAsync(db);
        var initial = await db.DiagnosticServiceParameters.Where(p => p.DiagnosticService.Code.StartsWith("LAB-") && !p.DiagnosticService.Code.StartsWith("LAB-TEST"))
            .Select(p => p.Id).ToListAsync();
        Assert.Equal(16, initial.Count);
        var hgb = await db.DiagnosticServiceParameters.SingleAsync(p => p.DiagnosticService.Code == "LAB-CBC" && p.Code == "HGB");
        hgb.Name = "Tên cũ"; hgb.RefLowFemale = 1;
        await db.SaveChangesAsync();
        await DevelopmentDataSeeder.SeedDiagnosticParametersAsync(db);
        var after = await db.DiagnosticServiceParameters.Where(p => initial.Contains(p.Id)).ToListAsync();
        Assert.Equal(16, after.Count);
        Assert.Equal("Huyết sắc tố", hgb.Name);
        Assert.Equal(120m, hgb.RefLowFemale);
        Assert.Empty(await db.DiagnosticServiceParameters.Where(p => p.DiagnosticService.Code == "US-ABD").ToListAsync());
    }
}
