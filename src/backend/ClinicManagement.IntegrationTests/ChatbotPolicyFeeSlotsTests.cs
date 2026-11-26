using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Appointments.Interfaces;
using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ChatbotPolicyFeeSlotsTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string WizardUrl = "/api/v1/ai/booking-wizard";

    [Theory]
    [InlineData("lỡ hẹn mấy lần thì bị khóa đặt lịch, mở lại kiểu gì")]
    [InlineData("bị khóa đặt lịch online thì mở lại sao")]
    [InlineData("hủy lịch sát giờ có bị khóa đặt lịch không")]
    public async Task F1_booking_policy_question_is_answered_from_the_policy_with_a_source(string message)
    {
        var response = await ChatAsync(message);
        Assert.Contains(response.Sources, source => source.DocId == "appointments");
        Assert.Contains("3 lần Vắng mặt trong 90 ngày", response.Message);
        Assert.Contains("liên hệ lễ tân", response.Message);
        Assert.DoesNotContain("chưa xác định được chuyên khoa", response.Message);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Null(response.BookingDraft);
        Assert.DoesNotContain(response.Actions, action => action.Type == AiActionTypes.StartBooking);
    }

    [Theory]
    [InlineData("lỡ hẹn 2 lần có sao không", "Vắng mặt 2 lần thì chưa bị khóa đặt lịch online")]
    [InlineData("lỡ hẹn 1 lần có bị gì không", "Vắng mặt 1 lần thì chưa bị khóa đặt lịch online")]
    [InlineData("trễ hẹn 3 lần thì sao", "Nếu đủ 3 lần bị ghi nhận Vắng mặt trong vòng 90 ngày thì tài khoản bị tạm khóa đặt lịch online 30 ngày")]
    public async Task F2_lock_answers_use_the_rule_figures(string message, string expected)
    {
        var response = await ChatAsync(message);
        Assert.Contains(expected, response.Message);
        Assert.Contains(response.Sources, source => source.DocId == "appointments");
        Assert.DoesNotContain("hai lần có thể dẫn đến khóa", response.Message);
        if (message.Contains("lỡ hẹn")) Assert.Contains("Ngưỡng khóa là 3 lần Vắng mặt trong 90 ngày", response.Message);
        if (message.Contains("trễ")) Assert.Contains("30 phút", response.Message);
    }

    [Fact]
    public void F2_contradicting_lock_figures_are_detected_and_the_policy_document_matches_the_rule()
    {
        var rule = new ExpiryRulesOptions();
        Assert.True(NoShowPolicyAnswer.Contradicts("Lỡ hẹn hai lần có thể dẫn đến khóa đặt lịch online trong 30 ngày.", rule));
        Assert.True(NoShowPolicyAnswer.Contradicts("Ba lần vắng mặt trong 60 ngày sẽ bị khóa đặt lịch online.", rule));
        Assert.False(NoShowPolicyAnswer.Contradicts("Ba lần vắng mặt trong 90 ngày có thể dẫn đến khóa đặt lịch online trong 30 ngày.", rule));
        Assert.False(NoShowPolicyAnswer.Contradicts("Hai lần đến trễ dưới 30 phút vẫn được tiếp nhận.", rule));
        var policy = File.ReadAllText(Path.Combine(RepoRoot(), "src", "backend", "ClinicManagement.Infrastructure", "AI", "Knowledge", "Documents", "appointments.md"));
        Assert.Contains($"ba lần Vắng mặt trong {rule.NoShowWindowDays} ngày dẫn tới khóa đặt lịch online {rule.OnlineBookingBlockDays} ngày", policy);
        Assert.Equal(3, rule.NoShowThreshold);
        Assert.Contains($"Quá {rule.AppointmentGraceMinutes} phút", policy);
    }

    [Theory]
    [InlineData("nhi bn tiền vậy", "Nhi khoa", "180.000")]
    [InlineData("nhi khoa giá sao", "Nhi khoa", "180.000")]
    [InlineData("da liễu hết nhiêu", "Da liễu", "200.000")]
    [InlineData("tim mạch bao nhiu tiền", "Tim mạch", "250.000")]
    public async Task F3_specialty_name_with_colloquial_price_is_a_fee_question(string message, string specialty, string fee)
    {
        var response = await ChatAsync(message);
        Assert.Equal(AiChatIntentTypes.PricingInquiry, response.PrimaryIntent);
        Assert.Contains($"Chi phí khám chuyên khoa **{specialty}**", response.Message);
        Assert.Contains(fee, response.Message);
        Assert.Empty(response.SpecialtySuggestions);
    }

    [Theory]
    [InlineData("thuốc hạ sốt cho bé nhi uống bao nhiu viên")]
    [InlineData("thuốc ho uống bn ngày thì ngưng")]
    public async Task F3_dose_or_duration_questions_are_not_fee_questions(string message)
    {
        var response = await ChatAsync(message);
        Assert.NotEqual(AiChatIntentTypes.PricingInquiry, response.PrimaryIntent);
    }

    [Fact]
    public async Task F4_chosen_doctor_without_slots_keeps_the_doctor_step_with_other_doctors()
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var session = $"sess_{Guid.NewGuid():N}";
        var doctors = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "start", typedMessage = "đặt lịch khám nội tổng quát", hint = "nội tổng quát" });
        var (busy, idle) = (DoctorName(DoctorEntityId), DoctorName(Doctor2EntityId));
        var response = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "pick", optionToken = doctors.Options.Single(option => option.Label == idle).Token });
        Assert.Equal("doctor", response.Step);
        Assert.Equal("NO_AVAILABLE_SLOTS", response.ErrorCode);
        Assert.Contains($"Bác sĩ {idle} không có lịch trống trong 14 ngày tới", response.Message);
        Assert.DoesNotContain(response.Options, option => option.Label == idle);
        Assert.Contains(response.Options, option => option.Label == busy);
        Assert.Contains(response.Options, option => option.Label == "Bác sĩ nào cũng được (lịch gần nhất)");
        Assert.True(response.CanGoBack);
        Assert.Equal("/locations", Assert.Single(response.Actions, action => action.Type == AiActionTypes.ContactReception).Payload.TargetUrl);
        var next = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "pick", optionToken = response.Options.Single(option => option.Label == busy).Token });
        Assert.Equal("day", next.Step);
    }

    [Fact]
    public async Task F5_demo_seed_gives_every_active_specialty_a_bookable_doctor_for_14_days_idempotently()
    {
        using var seeded = new FixedSeedWebApplicationFactory(new DateTime(2026, 11, 26, 3, 0, 0, DateTimeKind.Utc));
        using var scope = seeded.Services.CreateScope();
        var services = scope.ServiceProvider;
        await DevelopmentDataSeeder.SeedMasterDataAsync(services);
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var appointmentsBefore = await db.Appointments.CountAsync();

        Assert.True(await DevelopmentSpecialtyCoverageSeed.EnsureAsync(db, clock) > 0);
        Assert.Equal(0, await DevelopmentSpecialtyCoverageSeed.EnsureAsync(db, clock));
        Assert.Equal(appointmentsBefore, await db.Appointments.CountAsync());

        var today = clock.VietnamToday;
        var availability = services.GetRequiredService<IAppointmentAvailabilityPolicy>();
        var specialties = await db.Specialties.Where(s => s.IsActive && s.DoctorSpecialties.Any()).ToListAsync();
        Assert.Equal(11, specialties.Count);
        foreach (var specialty in specialties)
        {
            var slots = await availability.GetAvailableSlotsAsync(new BatchSlotAvailabilityRequest
            {
                SpecialtyId = specialty.Id, FromDate = today, ToDate = today.AddDays(DevelopmentSpecialtyCoverageSeed.CoverageDays - 1)
            });
            Assert.True(slots.Count > 0, $"{specialty.Name} has no bookable slot");
            Assert.Contains(slots, slot => slot.SlotDate == today.AddDays(DevelopmentSpecialtyCoverageSeed.CoverageDays - 1));
            Assert.True(await db.DoctorWorkSchedules.AnyAsync(schedule => schedule.IsActive && schedule.WorkDate == today.AddDays(1) &&
                slots.Select(slot => slot.DoctorId).Contains(schedule.DoctorId)), $"{specialty.Name} has no schedule");
        }
    }

    private async Task<AiChatResponseDto> ChatAsync(string message)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        return await PostAsync<AiChatResponseDto>(client, "/api/v1/ai/chat", new { message, sessionId = $"sess_{Guid.NewGuid():N}" });
    }

    private string DoctorName(long doctorId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Doctors.Where(doctor => doctor.Id == doctorId)
            .Join(db.Users, doctor => doctor.UserId, user => user.Id, (_, user) => user.FullName).Single();
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "backend"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }
}
