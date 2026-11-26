using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ChatbotLockCancelFeeRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string WizardUrl = "/api/v1/ai/booking-wizard";
    private const string NoShowRuleSentence = "3 lần Vắng mặt trong 90 ngày";
    private const string StrokeSentence = "ba tôi tự nhiên méo miệng, nói ngọng, tay trái yếu hẳn từ nửa tiếng trước";

    [Theory]
    [InlineData("nhập sai mật khẩu mấy lần thì bị khóa tài khoản")]
    [InlineData("quên mật khẩu đăng nhập sai nhiều lần có bị khóa không")]
    public async Task G1_login_lock_question_uses_the_sign_in_lockout_not_the_no_show_rule(string message)
    {
        var response = await ChatAsync(message);
        Assert.Contains("nhập sai mật khẩu 5 lần liên tiếp thì tài khoản bị tạm khóa đăng nhập 15 phút", response.Message);
        Assert.Contains("Quên mật khẩu?", response.Message);
        Assert.Contains("liên hệ lễ tân", response.Message);
        Assert.DoesNotContain(NoShowRuleSentence, response.Message);
        Assert.Contains(response.Sources, source => source.DocId == "accounts");
        Assert.DoesNotContain(response.Sources, source => source.DocId == "appointments");
    }

    [Fact]
    public void G1_without_a_lockout_configuration_the_answer_says_there_is_no_information()
    {
        var text = LoginLockPolicyAnswer.Compose(null);
        Assert.Contains("chưa có thông tin", text);
        Assert.Contains("Quên mật khẩu?", text);
        Assert.Contains("liên hệ lễ tân", text);
        Assert.False(NoShowPolicyAnswer.IsLockQuestion("nhập sai mật khẩu mấy lần thì bị khóa tài khoản"));
        Assert.True(NoShowPolicyAnswer.IsLockQuestion("tài khoản bị khóa đặt lịch online vì vắng mặt thì sao"));
    }

    [Theory]
    [InlineData("hủy lịch có bị mất phí không")]
    [InlineData("hủy lịch có mất tiền không")]
    [InlineData("hủy hẹn có bị phạt không")]
    public async Task G2_cancellation_fee_question_says_the_document_names_no_fee(string message)
    {
        var response = await ChatAsync(message);
        Assert.Contains("không nêu khoản phí hay tiền phạt nào khi hủy lịch", response.Message);
        Assert.Contains("Hủy lịch", response.Message);
        Assert.Contains("liên hệ lễ tân", response.Message);
        Assert.DoesNotContain(NoShowRuleSentence, response.Message);
        Assert.Contains(response.Sources, source => source.DocId == "appointments" && source.Heading == "Gửi yêu cầu đổi hoặc hủy");
        Assert.NotEqual(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
        Assert.False(NoShowPolicyAnswer.IsLockQuestion(message));
    }

    [Fact]
    public async Task G2_a_cancellation_question_that_names_the_lock_still_gets_the_lock_rule()
    {
        var response = await ChatAsync("hủy lịch sát giờ có bị khóa đặt lịch không");
        Assert.Contains(NoShowRuleSentence, response.Message);
        Assert.Contains(response.Sources, source => source.DocId == "appointments");
    }

    [Theory]
    [InlineData("khám nội tổng quát bao nhiêu tiền nhỉ", "Nội tổng quát", "150.000")]
    [InlineData("nhi bn tiền vậy", "Nhi khoa", "180.000")]
    [InlineData("nhi khoa giá sao", "Nhi khoa", "180.000")]
    public async Task G3_consultation_fee_questions_still_resolve_the_named_specialty(string message, string specialty, string fee)
    {
        var response = await ChatAsync(message);
        Assert.Equal(AiChatIntentTypes.PricingInquiry, response.PrimaryIntent);
        Assert.Contains($"Chi phí khám chuyên khoa **{specialty}**", response.Message);
        Assert.Contains(fee, response.Message);
    }

    [Theory]
    [InlineData("thuốc paracetamol bao nhiu tiền nhỉ")]
    [InlineData("thuốc ho bn tiền nhỉ")]
    public async Task G3_the_particle_nhi_is_not_pediatrics_and_medicine_questions_never_get_consultation_fees(string message)
    {
        var response = await ChatAsync(message);
        Assert.DoesNotContain("Chi phí khám", response.Message);
        Assert.DoesNotContain("Bảng giá chi phí khám", response.Message);
        Assert.StartsWith("MedicinePrice", response.DialogueOutcome);
        Assert.Contains(response.Actions, action => action.Payload.TargetUrl == SafeRoutes.Medicines);
    }

    [Fact]
    public void G4_only_no_show_lock_sentences_are_checked_against_the_rule()
    {
        var rule = new ExpiryRulesOptions();
        Assert.False(NoShowPolicyAnswer.Contradicts("Tài khoản đăng nhập bị khóa 15 phút sau 5 lần nhập sai mật khẩu.", rule));
        Assert.False(NoShowPolicyAnswer.Contradicts("Khóa học tiền sản kéo dài 4 ngày. Phiếu hẹn giữ trong 7 ngày.", rule, "khóa học tiền sản mấy ngày"));
        Assert.True(NoShowPolicyAnswer.Contradicts("Khóa đăng nhập 15 phút. Ba lần vắng mặt trong 60 ngày sẽ bị khóa đặt lịch online.", rule));
        Assert.True(NoShowPolicyAnswer.Contradicts("Bạn sẽ bị khóa sau 2 lần.", rule, "lỡ hẹn mấy lần thì bị khóa đặt lịch"));
        Assert.False(NoShowPolicyAnswer.Contradicts("Bạn sẽ bị khóa sau 2 lần.", rule));
    }

    [Theory]
    [InlineData("hủy hẹn trước mấy tiếng thì không sao")]
    [InlineData("hủy lịch trước bao lâu thì được")]
    [InlineData("hủy hẹn sát giờ có được không")]
    public async Task G5_cancellation_deadline_question_is_answered_from_the_appointments_document(string message)
    {
        var response = await ChatAsync(message);
        Assert.Contains("không quy định phải hủy trước bao nhiêu giờ", response.Message);
        Assert.Contains("hết hạn xử lý", response.Message);
        Assert.DoesNotContain("Không thể xác định yêu cầu đặt lịch cần hủy", response.Message);
        Assert.Contains(response.Sources, source => source.DocId == "appointments" && source.Heading == "Hạn xử lý và hạn chế online");
        Assert.NotEqual(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
    }

    [Theory]
    [InlineData("hủy lịch hẹn ngày mai của tôi")]
    [InlineData("tôi muốn hủy lịch khám")]
    public async Task G5_a_real_cancel_command_keeps_the_cancellation_flow(string message)
    {
        Assert.False(CancellationPolicyAnswer.IsQuestion(message));
        var response = await ChatAsync(message);
        Assert.Equal(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
        Assert.DoesNotContain(response.Sources, source => source.DocId == "appointments");
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("day")]
    public async Task G6_a_new_booking_sentence_typed_on_an_open_step_starts_a_new_wizard_with_day_and_part(string openStep)
    {
        var (phrase, day) = TomorrowAfternoon();
        var afternoon = await CreateAvailableSlotAsync(DoctorEntityId, day, new TimeOnly(15, 11), new TimeOnly(15, 41));
        try
        {
            using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
            var session = NewSession();
            var open = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
                new { sessionId = session, step = "start", typedMessage = "đặt lịch khám nội tổng quát", hint = "nội tổng quát" });
            if (openStep == "day") open = await PickAsync(client, session, open.Options.Single(option => option.Label == DoctorName()).Token);
            Assert.Equal(openStep, open.Step);

            var restarted = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
                new { sessionId = session, step = "text", optionToken = open.TextToken, text = $"tôi muốn đặt lịch khám tim mạch {phrase}" });
            Assert.Equal("doctor", restarted.Step);
            Assert.Equal("Tim mạch", restarted.Summary?.SpecialtyName);
            Assert.DoesNotContain("chưa xác định được lựa chọn", restarted.Message);

            var days = await PickAsync(client, session, restarted.Options.Single(option => option.Label == DoctorName()).Token);
            Assert.Equal("day", days.Step);
            Assert.Contains("Đã chọn sẵn ngày", days.Message);
            Assert.Equal(day.ToString("dd/MM/yyyy"), days.Options[0].Label);
            Assert.Equal("buổi chiều", days.Options[0].Hint);
        }
        finally
        {
            await RemoveAsync([afternoon]);
        }
    }

    [Fact]
    public async Task G6_the_reproduction_sentence_is_a_new_request_and_a_step_answer_is_not()
    {
        var today = new DateOnly(2026, 11, 26);
        Assert.True(AiBookingWizardRestart.IsNewRequest("tôi muốn đặt lịch khám nhi chiều mai", today, BookingWizardStage.Doctor));
        Assert.True(AiBookingWizardRestart.IsNewRequest("tôi muốn đặt lịch khám nhi chiều mai", today, BookingWizardStage.Day));
        Assert.Equal("nhi", AiBookingWizardRestart.Hint("tôi muốn đặt lịch khám nhi chiều mai"));
        Assert.False(AiBookingWizardRestart.IsNewRequest("bác sĩ Lan", today, BookingWizardStage.Doctor));
        Assert.False(AiBookingWizardRestart.IsNewRequest("ngày mai", today, BookingWizardStage.Day));
        Assert.False(AiBookingWizardRestart.IsNewRequest("đặt lịch khám ngày 22/10/2026", today, BookingWizardStage.Day));
        Assert.False(AiBookingWizardRestart.IsNewRequest("đặt lịch chiều mai", today, BookingWizardStage.Day));
        Assert.True(AiBookingWizardRestart.IsNewRequest("đặt lịch chiều mai", today, BookingWizardStage.Specialty));

        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var session = NewSession();
        var doctors = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "start", typedMessage = "đặt lịch khám nội tổng quát", hint = "nội tổng quát" });
        var answer = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "text", optionToken = doctors.TextToken, text = DoctorName() });
        Assert.Equal("day", answer.Step);
        Assert.Equal(DoctorName(), answer.Summary?.DoctorName);
    }

    [Fact]
    public async Task G6_emergency_and_no_show_lock_still_run_before_a_restarted_wizard()
    {
        using (var client = await CreateAuthenticatedClientAsync("pat1@test.com"))
        {
            var session = NewSession();
            var doctors = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
                new { sessionId = session, step = "start", typedMessage = "đặt lịch khám nội tổng quát", hint = "nội tổng quát" });
            var emergency = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
                new { sessionId = session, step = "text", optionToken = doctors.TextToken, text = $"{StrokeSentence}, đặt lịch khám thần kinh chiều mai" });
            Assert.Equal("EMERGENCY", emergency.ErrorCode);
            Assert.Contains(emergency.Actions, action => action.Type == AiActionTypes.CallEmergency);
        }

        using var locked = await CreateAuthenticatedClientAsync("pat2@test.com");
        var lockedSession = NewSession();
        var open = await PostAsync<AiBookingWizardResponseDto>(locked, WizardUrl,
            new { sessionId = lockedSession, step = "start", typedMessage = "đặt lịch khám nội tổng quát", hint = "nội tổng quát" });
        var created = await AddNoShowsAsync(Patient2EntityId);
        try
        {
            var blocked = await PostAsync<AiBookingWizardResponseDto>(locked, WizardUrl,
                new { sessionId = lockedSession, step = "text", optionToken = open.TextToken, text = "tôi muốn đặt lịch khám tim mạch chiều mai" });
            Assert.Equal("ONLINE_BOOKING_BLOCKED", blocked.ErrorCode);
            Assert.Contains("liên hệ lễ tân", blocked.Message);
        }
        finally
        {
            await RemoveAsync(created);
        }
    }

    [Fact]
    public async Task G7_medicine_in_the_price_list_is_answered_with_its_price_and_a_link()
    {
        var response = await ChatAsync("thuốc paracetamol bao nhiu tiền");
        Assert.Equal("MedicinePriceResolved", response.DialogueOutcome);
        Assert.Contains("**Paracetamol 500mg**: 2.000", response.Message);
        Assert.DoesNotContain("Chi phí khám", response.Message);
        Assert.DoesNotContain("chưa hiểu rõ", response.Message);
        var link = Assert.Single(response.Actions);
        Assert.Equal("Mở Bảng giá thuốc", link.Label);
        Assert.Equal(SafeRoutes.Medicines, link.Payload.TargetUrl);
        Assert.True(SafeRoutes.IsSafeRoute(link.Payload.TargetUrl));
    }

    [Fact]
    public async Task G7_medicine_missing_from_the_price_list_is_reported_with_a_link()
    {
        var response = await ChatAsync("thuốc zyxotamin bao nhiêu tiền");
        Assert.Equal("MedicinePriceNotFound", response.DialogueOutcome);
        Assert.Contains("Không tìm thấy thuốc \"zyxotamin\" trong Bảng giá thuốc", response.Message);
        Assert.DoesNotContain("Chi phí khám", response.Message);
        Assert.Equal(SafeRoutes.Medicines, Assert.Single(response.Actions).Payload.TargetUrl);
    }

    [Fact]
    public async Task G7_dose_questions_stay_out_of_the_price_answer()
    {
        Assert.False(MedicinePriceQuestion.IsPriceQuestion("thuốc hạ sốt cho bé nhi uống bao nhiu viên"));
        Assert.False(MedicinePriceQuestion.IsPriceQuestion("thuốc ho uống bn ngày thì ngưng"));
        var response = await ChatAsync("uống thuốc hạ sốt bao nhiu viên một ngày");
        Assert.DoesNotContain("MedicinePrice", response.DialogueOutcome ?? string.Empty);
    }

    [Theory]
    [InlineData(StrokeSentence)]
    [InlineData(StrokeSentence + ", đặt lịch khám thần kinh giúp tôi")]
    public async Task Regression_stroke_sentence_in_chat_is_an_emergency(string message)
    {
        var response = await ChatAsync(message);
        Assert.Equal("EMERGENCY", response.Urgency);
        Assert.Contains(response.Actions, action => action.Type == AiActionTypes.CallEmergency);
    }

    private async Task<AiChatResponseDto> ChatAsync(string message)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        return await PostAsync<AiChatResponseDto>(client, "/api/v1/ai/chat", new { message, sessionId = NewSession() });
    }

    private (string Phrase, DateOnly Day) TomorrowAfternoon()
    {
        using var scope = Factory.Services.CreateScope();
        var tomorrow = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday.AddDays(1);
        return tomorrow.DayOfWeek == DayOfWeek.Sunday ? ("chiều ngày kia", tomorrow.AddDays(1)) : ("chiều mai", tomorrow);
    }

    private string DoctorName()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Doctors.Where(doctor => doctor.Id == DoctorEntityId)
            .Join(db.Users, doctor => doctor.UserId, user => user.Id, (_, user) => user.FullName).Single();
    }

    private async Task<List<object>> AddNoShowsAsync(long patientId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var created = new List<object>();
        for (var i = 1; i <= 3; i++)
        {
            var date = clock.VietnamToday.AddDays(-i);
            var slot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = new TimeOnly(20, i), EndTime = new TimeOnly(20, i + 30) };
            var appointment = new Appointment
            {
                AppointmentCode = "APT-G6-" + Guid.NewGuid().ToString("N"), PatientId = patientId,
                DoctorId = DoctorEntityId, SpecialtyId = SpecialtyEntityId, AppointmentSlot = slot,
                AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.NoShow,
                Histories = [new AppointmentHistory { Action = AppointmentHistoryAction.NoShow, NewStatus = AppointmentStatus.NoShow, CreatedAt = clock.UtcNow.AddDays(-i) }]
            };
            db.Appointments.Add(appointment);
            created.Add(appointment);
            created.Add(slot);
        }
        await db.SaveChangesAsync();
        return created;
    }

    private async Task RemoveAsync(IEnumerable<object> entities)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case Appointment appointment:
                    await db.AppointmentHistories.Where(history => history.AppointmentId == appointment.Id).ExecuteDeleteAsync();
                    await db.Appointments.Where(item => item.Id == appointment.Id).ExecuteDeleteAsync();
                    break;
                case AppointmentSlot slot:
                    await db.AppointmentSlots.Where(item => item.Id == slot.Id).ExecuteDeleteAsync();
                    break;
            }
        }
    }

    private static async Task<AiBookingWizardResponseDto> PickAsync(HttpClient client, string session, string token) =>
        await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl, new { sessionId = session, step = "pick", optionToken = token });

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static string NewSession() => $"sess_{Guid.NewGuid():N}";
}
