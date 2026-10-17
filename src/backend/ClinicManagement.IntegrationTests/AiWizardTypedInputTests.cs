using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Doctors.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class TypedWizardFactory : CustomWebApplicationFactory
{
    public List<AvailableSlotDto> Slots { get; } = [];
    public TypedWizardClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(Clock);
            var policy = new Mock<IAppointmentAvailabilityPolicy>();
            policy.Setup(x => x.GetAvailableSlotsAsync(It.IsAny<BatchSlotAvailabilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BatchSlotAvailabilityRequest request, CancellationToken _) => Slots
                    .Where(slot => !request.DoctorId.HasValue || slot.DoctorId == request.DoctorId).ToList());
            policy.Setup(x => x.EvaluateSlotAvailabilityAsync(It.IsAny<SlotAvailabilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SlotAvailabilityRequest request, CancellationToken _) => Slots.Any(slot => slot.SlotId == request.SlotId)
                    ? new SlotAvailabilityResult { IsAvailable = true, SlotId = request.SlotId,
                        StartTime = Slots.First(slot => slot.SlotId == request.SlotId).StartTime,
                        EndTime = Slots.First(slot => slot.SlotId == request.SlotId).EndTime }
                    : SlotAvailabilityResult.Failure("SLOT_UNAVAILABLE", "Khung giờ không còn trống."));
            services.RemoveAll<IAppointmentAvailabilityPolicy>();
            services.AddSingleton(policy.Object);
        });
    }
}

public sealed class TypedWizardClock : IDateTimeProvider
{
    private readonly DateTimeProvider _zone = new();
    public DateTime UtcNow { get; set; } = new(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
    public DateTime VietnamNow => _zone.ConvertUtcToVietnam(UtcNow);
    public DateOnly VietnamToday => DateOnly.FromDateTime(VietnamNow);
    public TimeOnly VietnamTime => TimeOnly.FromDateTime(VietnamNow);
    public TimeZoneInfo VietnamTimeZone => _zone.VietnamTimeZone;
    public DateTime ConvertUtcToVietnam(DateTime value) => _zone.ConvertUtcToVietnam(value);
    public DateTime ConvertVietnamToUtc(DateTime value) => _zone.ConvertVietnamToUtc(value);
}

[Collection(AiPhase12AcceptanceCollection.Name)]
public class AiWizardTypedInputTests : IntegrationTestBase, IClassFixture<TypedWizardFactory>
{
    private const string Url = "/api/v1/ai/booking-wizard";
    private readonly TypedWizardFactory _typed;
    private readonly string _session = $"typed_{Guid.NewGuid():N}";
    public AiWizardTypedInputTests(TypedWizardFactory factory) : base(factory)
    {
        _typed = factory;
        factory.Clock.UtcNow = new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
        factory.Slots.Clear();
        factory.MockAiProvider.Invocations.Clear();
        AddSlot(new DateOnly(2026, 10, 19), new TimeOnly(9, 30));
        AddSlot(new DateOnly(2026, 10, 22), new TimeOnly(9, 30));
    }

    [Theory]
    [InlineData("khám ngày 22/10/2026")]
    [InlineData("22-10-2026")]
    [InlineData("22/10")]
    [InlineData("ngày 22")]
    [InlineData("thứ 5")]
    [InlineData("thứ 5 tuần sau")]
    public async Task Typed_available_day_selects_exact_rendered_day(string text)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        Assert.NotNull(day.TextToken);
        var result = await StepAsync(patient, "text", day.TextToken, text);
        Assert.Equal("slot", result.Step);
        Assert.Equal("2026-10-22", result.Summary!.SlotDate);
        Assert.Contains(result.Options, option => option.Label.StartsWith("09:30"));
    }

    [Theory]
    [InlineData("ngày mai", "2026-10-19")]
    [InlineData("mai", "2026-10-19")]
    [InlineData("thứ 2 tuần sau", "2026-10-19")]
    [InlineData("thứ 2", "2026-10-19")]
    public async Task Relative_dates_follow_Vietnam_today_rather_than_UTC(string text, string date)
    {
        Assert.Equal(new DateOnly(2026, 10, 18), _typed.Clock.VietnamToday);
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        var result = await StepAsync(patient, "text", day.TextToken, text);
        Assert.Equal("slot", result.Step);
        Assert.Equal(date, result.Summary!.SlotDate);
    }

    [Theory]
    [InlineData("mốt", 20)]
    [InlineData("ngày kia", 20)]
    [InlineData("thứ 3", 20)]
    [InlineData("thứ 4", 21)]
    [InlineData("thứ 6", 23)]
    [InlineData("thứ 7", 24)]
    public async Task Other_relative_days_select_only_existing_options(string text, int dayOfMonth)
    {
        AddSlot(new DateOnly(2026, 10, dayOfMonth), new TimeOnly(11, 0));
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        var result = await StepAsync(patient, "text", day.TextToken, text);
        Assert.Equal("slot", result.Step);
        Assert.Equal($"2026-10-{dayOfMonth}", result.Summary!.SlotDate);
    }

    [Theory]
    [InlineData("22/10/2026", "không còn lịch trống")]
    [InlineData("17/10/2026", "đã qua")]
    [InlineData("01/11/2026", "14 ngày tới")]
    [InlineData("không rõ ngày", "chưa xác định")]
    [InlineData("31/02/2026", "chưa xác định")]
    [InlineData("hôm nay", "18/10/2026")]
    [InlineData("chủ nhật", "25/10/2026")]
    public async Task Unavailable_invalid_past_and_outside_days_keep_current_buttons(string text, string message)
    {
        _typed.Slots.RemoveAll(slot => slot.SlotDate == new DateOnly(2026, 10, 22));
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        var result = await StepAsync(patient, "text", day.TextToken, text);
        Assert.Equal("day", result.Step);
        Assert.Contains(message, result.Message);
        Assert.Equal(day.Options.Select(option => option.Label), result.Options.Select(option => option.Label));
        Assert.NotNull(result.TextToken);
        if (text == "22/10/2026") Assert.Contains("19/10/2026", result.Message);
    }

    [Fact]
    public async Task Yearless_and_day_only_dates_roll_forward_at_year_boundary()
    {
        _typed.Clock.UtcNow = new DateTime(2026, 12, 30, 18, 0, 0, DateTimeKind.Utc); // December 31 in Vietnam.
        _typed.Slots.Clear();
        AddSlot(new DateOnly(2027, 1, 2), new TimeOnly(9, 30));
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        foreach (var text in new[] { "2/1", "ngày 2", "mốt" })
        {
            var result = await StepAsync(patient, "text", day.TextToken, text);
            Assert.Equal("slot", result.Step);
            Assert.Equal("2027-01-02", result.Summary!.SlotDate);
        }
    }

    [Fact]
    public async Task Typed_day_cannot_bypass_the_seven_rendered_day_limit()
    {
        _typed.Slots.Clear();
        for (var date = new DateOnly(2026, 10, 19); date <= new DateOnly(2026, 10, 31); date = date.AddDays(1))
            AddSlot(date, new TimeOnly(9, 30));
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        Assert.Equal(7, day.Options.Count);
        var result = await StepAsync(patient, "text", day.TextToken, "31/10/2026");
        Assert.Equal("day", result.Step);
        Assert.Contains("không còn lịch trống", result.Message);
        Assert.Equal(day.Options.Select(option => option.Label), result.Options.Select(option => option.Label));
    }

    [Theory]
    [InlineData("9h30", "09:30")]
    [InlineData("09:30", "09:30")]
    [InlineData("9 giờ 30", "09:30")]
    [InlineData("9 giờ rưỡi", "09:30")]
    [InlineData("9h", "09:00")]
    [InlineData("sáng 9h", "09:00")]
    [InlineData("chiều 2h", "14:00")]
    [InlineData("2h chiều", "14:00")]
    public async Task Typed_time_selects_exact_start_time(string text, string expected)
    {
        _typed.Slots.Clear();
        AddSlot(new DateOnly(2026, 10, 19), TimeOnly.ParseExact(expected, "HH:mm", CultureInfo.InvariantCulture));
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        var slot = await StepAsync(patient, "pick", day.Options.Single().Token);
        var result = await StepAsync(patient, "text", slot.TextToken, text);
        Assert.Equal("reason", result.Step);
        Assert.Equal(expected, result.Summary!.StartTime);
        Assert.Null(result.TextToken);
        Assert.NotNull(result.ReasonToken);
    }

    [Fact]
    public async Task Missing_slot_keeps_options_and_lists_available_times()
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var day = await DayAsync(patient);
        var slot = await StepAsync(patient, "pick", day.Options[0].Token);
        var result = await StepAsync(patient, "text", slot.TextToken, "10h45");
        Assert.Equal("slot", result.Step);
        Assert.Contains("Không có khung giờ 10:45", result.Message);
        Assert.Contains("09:30", result.Message);
        Assert.Equal(slot.Options.Select(option => option.Label), result.Options.Select(option => option.Label));
    }

    [Fact]
    public async Task Specialty_and_doctor_names_use_rendered_labels_and_preserve_buttons_on_miss()
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var start = await StepAsync(patient, "start");
        var chosen = start.Options[0];
        var failed = await StepAsync(patient, "text", start.TextToken, "tên không tồn tại");
        Assert.Equal("specialty", failed.Step);
        Assert.Equal(start.Options.Select(option => option.Label), failed.Options.Select(option => option.Label));
        var result = await StepAsync(patient, "text", start.TextToken, chosen.Label.ToUpperInvariant());
        Assert.Equal("doctor", result.Step);
        Assert.Equal(chosen.Label, result.Summary!.SpecialtyName);
        var doctor = result.Options[0];
        var next = await StepAsync(patient, "text", result.TextToken, doctor.Label);
        Assert.Equal("day", next.Step);
        Assert.Equal(doctor.Label, next.Summary!.DoctorName);
    }

    [Fact]
    public async Task Facility_text_uses_exact_eligible_list_and_refuses_ambiguous_names()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facility = new Facility { Code = $"TYPED-{Guid.NewGuid():N}", Name = "Cơ sở nhập chữ", Address = "Synthetic", IsActive = true };
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        var department = new Department { FacilityId = facility.Id, SpecialtyId = SpecialtyEntityId, Code = "TYPED", Name = "Khoa kiểm thử", IsActive = true };
        var assignment = new StaffFacilityAssignment { UserId = DoctorId, FacilityId = facility.Id, Role = "Doctor", IsActive = true };
        db.Departments.Add(department);
        db.StaffFacilityAssignments.Add(assignment);
        await db.SaveChangesAsync();
        try
        {
            using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
            var day = await DayAsync(patient);
            var slot = await StepAsync(patient, "pick", day.Options[0].Token);
            var facilities = await StepAsync(patient, "pick", slot.Options[0].Token);
            Assert.Equal("facility", facilities.Step);
            var ambiguous = await StepAsync(patient, "text", facilities.TextToken, string.Join(" và ", facilities.Options.Select(option => option.Label)));
            Assert.Equal("facility", ambiguous.Step);
            Assert.Contains("chưa xác định", ambiguous.Message);
            Assert.Equal(facilities.Options.Select(option => option.Label), ambiguous.Options.Select(option => option.Label));
            var reason = await StepAsync(patient, "text", facilities.TextToken, "co so nhap chu");
            Assert.Equal("reason", reason.Step);
            Assert.Equal(facility.Name, reason.Summary!.FacilityName);
        }
        finally
        {
            db.StaffFacilityAssignments.Remove(assignment);
            db.Departments.Remove(department);
            db.Facilities.Remove(facility);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Text_tokens_bind_user_session_operation_and_expiry()
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var other = await CreateAuthenticatedClientAsync("pat2@test.com");
        var start = await StepAsync(patient, "start");
        Assert.Equal("WIZARD_TOKEN_INVALID_OR_EXPIRED", (await StepAsync(other, "text", start.TextToken, "Nội khoa")).ErrorCode);
        var wrongSession = await patient.PostAsJsonAsync(Url, new { sessionId = "another_session", step = "text", optionToken = start.TextToken, text = "Nội khoa" });
        Assert.Contains("WIZARD_TOKEN_INVALID_OR_EXPIRED", await wrongSession.Content.ReadAsStringAsync());
        Assert.Equal("WIZARD_TOKEN_INVALID_OR_EXPIRED", (await StepAsync(patient, "pick", start.TextToken)).ErrorCode);
        Assert.Equal("WIZARD_TOKEN_INVALID_OR_EXPIRED", (await StepAsync(patient, "text", start.Options[0].Token, "Nội khoa")).ErrorCode);
        _typed.Clock.UtcNow = _typed.Clock.UtcNow.AddMinutes(16);
        Assert.Equal("WIZARD_TOKEN_INVALID_OR_EXPIRED", (await StepAsync(patient, "text", start.TextToken, "Nội khoa")).ErrorCode);
    }

    [Fact]
    public async Task Text_audit_records_only_length_and_HTTP_validation_enforces_200_characters()
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        var start = await StepAsync(patient, "start");
        const string text = "nội dung riêng tư chỉ dùng để kiểm tra nhật ký";
        await StepAsync(patient, "text", start.TextToken, text);
        using var scope = Factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiAuditLogs.Where(log => log.SessionId == _session).ToListAsync();
        var audit = Assert.Single(entries, log => JsonDocument.Parse(log.MetadataJson!).RootElement.GetProperty("operation").GetString() == "text");
        var metadata = JsonDocument.Parse(audit.MetadataJson!).RootElement;
        Assert.Equal(text.Length, metadata.GetProperty("textLength").GetInt32());
        Assert.Equal(new[] { "operation", "providerWasCalled", "source", "status", "textLength" }, metadata.EnumerateObject().Select(property => property.Name).Order());
        Assert.DoesNotContain(metadata.EnumerateObject(), property => property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == text);
        Assert.DoesNotContain(text, JsonSerializer.Serialize(entries));
        var tooLong = await patient.PostAsJsonAsync(Url, new { sessionId = _session, step = "text", optionToken = start.TextToken, text = new string('a', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    private void AddSlot(DateOnly date, TimeOnly start) => _typed.Slots.Add(new AvailableSlotDto {
        SlotId = _typed.Slots.Count + 90000, DoctorId = DoctorEntityId, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(30) });

    private async Task<AiBookingWizardResponseDto> DayAsync(HttpClient patient)
    {
        var start = await StepAsync(patient, "start");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var specialty = (await db.Specialties.FindAsync(SpecialtyEntityId))!.Name;
        var doctor = await db.Users.Where(user => user.Id == DoctorId).Select(user => user.FullName).SingleAsync();
        var doctors = await StepAsync(patient, "pick", start.Options.Single(option => option.Label == specialty).Token);
        var day = await StepAsync(patient, "pick", doctors.Options.Single(option => option.Label == doctor).Token);
        Assert.Equal("day", day.Step);
        return day;
    }

    private async Task<AiBookingWizardResponseDto> StepAsync(HttpClient client, string step, string? token = null, string? text = null)
    {
        var response = await client.PostAsJsonAsync(Url, new { sessionId = _session, step, optionToken = token, text });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .Deserialize<AiBookingWizardResponseDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(result.ProviderWasCalled);
        Assert.Empty(_typed.MockAiProvider.Invocations);
        return result;
    }
}
