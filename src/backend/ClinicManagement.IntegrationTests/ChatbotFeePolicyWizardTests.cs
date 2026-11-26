using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ChatbotFeePolicyWizardTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string WizardUrl = "/api/v1/ai/booking-wizard";

    [Theory]
    [InlineData("tui muốn bik khám nhi thì mất bn tiền v", "Nhi khoa", "180.000")]
    [InlineData("khám da liễu bao nhiu vậy", "Da liễu", "200.000")]
    [InlineData("khám tai mũi họng giá sao ạ", "Tai mũi họng", "180.000")]
    [InlineData("khám tim mạch hết nhiêu", "Tim mạch", "250.000")]
    [InlineData("khám nhãn khoa tính bn", "Nhãn khoa", "180.000")]
    [InlineData("cho hỏi khám nội tiết bao nhiu tiền", "Nội tiết", "220.000")]
    public async Task Colloquial_fee_question_with_specialty_returns_database_fee(string message, string specialty, string fee)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await PostAsync<AiChatResponseDto>(client, "/api/v1/ai/chat", new { message, sessionId = NewSession() });
        Assert.Equal(AiChatIntentTypes.PricingInquiry, response.PrimaryIntent);
        Assert.Contains($"Chi phí khám chuyên khoa **{specialty}**", response.Message);
        Assert.Contains(fee, response.Message);
        Assert.DoesNotContain("triệu chứng", response.Message);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Contains(response.Actions, action => action.Type == AiActionTypes.StartBooking);
    }

    [Theory]
    [InlineData("uống thuốc hạ sốt bao nhiu viên một ngày")]
    [InlineData("khám xong bao nhiu ngày thì có kết quả")]
    public async Task Dose_or_duration_question_is_not_a_fee_question(string message)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await PostAsync<AiChatResponseDto>(client, "/api/v1/ai/chat", new { message, sessionId = NewSession() });
        Assert.NotEqual(AiChatIntentTypes.PricingInquiry, response.PrimaryIntent);
    }

    [Theory]
    [InlineData("lỡ hẹn mấy lần thì bị khóa đặt lịch online vậy, mở lại kiểu gì")]
    [InlineData("vắng mặt mấy lần thì bị khóa đặt lịch online")]
    [InlineData("bị khóa đặt lịch online thì mở lại kiểu gì")]
    [InlineData("quy định khóa đặt lịch online khi vắng mặt là sao")]
    [InlineData("tại sao bị khóa đặt lịch online vậy")]
    public async Task Booking_policy_question_is_answered_from_appointment_policy(string message)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await PostAsync<AiChatResponseDto>(client, "/api/v1/ai/chat", new { message, sessionId = NewSession() });
        Assert.True(response.Sources.Any(source => source.DocId == "appointments"),
            $"{response.DialogueOutcome} | {response.PrimaryIntent} | {response.Message}");
        Assert.Null(response.BookingDraft);
        Assert.DoesNotContain(response.Actions, action => action.Type == AiActionTypes.ConfirmBooking);
    }

    [Fact]
    public async Task Locked_patient_is_told_at_wizard_start_with_reception_contact()
    {
        var created = await AddNoShowsAsync(Patient2EntityId);
        try
        {
            using var client = await CreateAuthenticatedClientAsync("pat2@test.com");
            var response = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl, new
            {
                sessionId = NewSession(), step = "start", typedMessage = "đặt lịch khám nội tổng quát chiều mai giúp tôi", hint = "nội tổng quát"
            });
            Assert.Equal("stopped", response.Step);
            Assert.Equal("ONLINE_BOOKING_BLOCKED", response.ErrorCode);
            Assert.Contains("Tạm khóa đặt lịch online đến", response.Message);
            Assert.Contains("liên hệ lễ tân", response.Message);
            Assert.Empty(response.Options);
            Assert.Null(response.TextToken);
            Assert.Null(response.Summary);
            var contact = Assert.Single(response.Actions);
            Assert.Equal(AiActionTypes.ContactReception, contact.Type);
            Assert.Equal("/locations", contact.Payload.TargetUrl);

            using var eligibility = await client.GetAsync("/api/v1/appointments/online-eligibility");
            var page = (await eligibility.Content.ReadFromJsonAsync<ApiResponse<EligibilityDto>>())!.Data!;
            Assert.Equal(page.Message, response.Message);
        }
        finally
        {
            await RemoveAsync(created);
        }
    }

    [Fact]
    public async Task Tomorrow_afternoon_is_preselected_and_slots_are_filtered_but_changeable()
    {
        var tomorrow = Today().AddDays(1);
        var phrase = tomorrow.DayOfWeek == DayOfWeek.Sunday ? "chiều ngày kia" : "chiều mai";
        var day = tomorrow.DayOfWeek == DayOfWeek.Sunday ? tomorrow.AddDays(1) : tomorrow;
        var morning = await CreateAvailableSlotAsync(DoctorEntityId, day, new TimeOnly(8, 7), new TimeOnly(8, 37));
        var afternoon = await CreateAvailableSlotAsync(DoctorEntityId, day, new TimeOnly(14, 7), new TimeOnly(14, 37));
        try
        {
            using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
            var session = NewSession();
            var doctors = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl, new
            {
                sessionId = session, step = "start", typedMessage = $"đặt lịch khám nội tổng quát {phrase} giúp tôi", hint = "nội tổng quát"
            });
            Assert.Equal("doctor", doctors.Step);
            var days = await PickAsync(client, session, doctors.Options.Single(option => option.Label == DoctorName()).Token);
            Assert.Equal("day", days.Step);
            Assert.Contains("Đã chọn sẵn ngày", days.Message);
            Assert.Equal(new[] { day.ToString("dd/MM/yyyy"), "Chọn ngày khác" }, days.Options.Select(option => option.Label));
            Assert.Equal("buổi chiều", days.Options[0].Hint);

            var slots = await PickAsync(client, session, days.Options[0].Token);
            Assert.Equal("slot", slots.Step);
            Assert.Contains("buổi chiều (12:00–18:00)", slots.Message);
            var times = slots.Options.Where(option => option.Label != "Xem mọi khung giờ trong ngày").Select(option => TimeOnly.Parse(option.Label[..5])).ToList();
            Assert.Contains(new TimeOnly(14, 7), times);
            Assert.All(times, time => Assert.InRange(time, new TimeOnly(12, 0), new TimeOnly(17, 59)));

            var all = await PickAsync(client, session, slots.Options.Single(option => option.Label == "Xem mọi khung giờ trong ngày").Token);
            Assert.Contains(all.Options, option => option.Label.StartsWith("08:07"));

            var otherDays = await PickAsync(client, session, days.Options[1].Token);
            Assert.Equal("day", otherDays.Step);
            Assert.DoesNotContain(otherDays.Options, option => option.Label == "Chọn ngày khác");
            Assert.Contains(otherDays.Options, option => option.Label == day.ToString("dd/MM/yyyy"));
        }
        finally
        {
            await RemoveAsync([morning, afternoon]);
        }
    }

    [Theory]
    [InlineData("sáng mai", BookingDayPart.Morning, 1)]
    [InlineData("chiều mai", BookingDayPart.Afternoon, 1)]
    [InlineData("tối nay", BookingDayPart.Evening, 0)]
    [InlineData("mai", BookingDayPart.None, 1)]
    [InlineData("đặt lịch giúp tôi với bác sĩ Mai", BookingDayPart.None, -1)]
    [InlineData("toi muon dat lich sang mai", BookingDayPart.Morning, 1)]
    public void Start_preference_reads_day_and_part_without_confusing_toi_or_names(string text, BookingDayPart part, int offset)
    {
        var today = new DateOnly(2026, 11, 26);
        var parsed = AiBookingWizardPreference.Parse(text, today);
        Assert.Equal(part, parsed.Part);
        Assert.Equal(offset < 0 ? 0 : today.AddDays(offset).DayNumber, parsed.Day);
    }

    [Fact]
    public async Task Review_prompt_and_button_are_the_same_single_confirmation()
    {
        var day = WorkingDateAfter(Today(), 2);
        await CreateAvailableSlotAsync(DoctorEntityId, day, new TimeOnly(16, 13), new TimeOnly(16, 43));
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var session = NewSession();
        var doctors = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "start", typedMessage = "đặt lịch khám nội tổng quát", hint = "nội tổng quát" });
        var days = await PickAsync(client, session, doctors.Options.Single(option => option.Label == DoctorName()).Token);
        var slots = await PickAsync(client, session, days.Options.Single(option => option.Label == day.ToString("dd/MM/yyyy")).Token);
        var next = await PickAsync(client, session, slots.Options.Single(option => option.Label.StartsWith("16:13")).Token);
        if (next.Step == "facility") next = await PickAsync(client, session, next.Options[0].Token);
        Assert.Equal("reason", next.Step);
        var review = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId = session, step = "reason", optionToken = next.ReasonToken, reason = "Khám tổng quát định kỳ" });
        Assert.Equal("review", review.Step);
        Assert.Contains("\"Xác nhận đặt lịch\"", review.Message);
        Assert.NotNull(review.ConfirmAction);
        Assert.Equal(AiActionTypes.ConfirmBooking, review.ConfirmAction.Type);
        Assert.Equal("Xác nhận đặt lịch", review.ConfirmAction.Label);
        Assert.True(review.ConfirmAction.RequiresConfirmation);
        Assert.Equal(review.ReviewAction!.Payload.ConfirmationId, review.ConfirmAction.Payload.ConfirmationId);
    }

    [Fact]
    public async Task Any_doctor_without_slots_stays_on_doctor_step_with_ways_out()
    {
        var membership = await AddNeurologyMembershipAsync(Doctor2EntityId);
        try
        {
            using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
            var session = NewSession();
            var doctors = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
                new { sessionId = session, step = "start", typedMessage = "đặt lịch khám thần kinh", hint = "thần kinh" });
            Assert.Equal("doctor", doctors.Step);
            var response = await PickAsync(client, session, doctors.Options.Single(option => option.Label == "Bác sĩ nào cũng được (lịch gần nhất)").Token);
            Assert.Equal("doctor", response.Step);
            Assert.Equal("NO_AVAILABLE_SLOTS", response.ErrorCode);
            Assert.Contains("không có lịch trống trong 14 ngày tới", response.Message);
            Assert.Empty(response.Options);
            Assert.True(response.CanGoBack);
            Assert.Equal("specialty", (await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
                new { sessionId = session, step = "back", optionToken = response.BackToken })).Step);
            Assert.Equal("/locations", Assert.Single(response.Actions, action => action.Type == AiActionTypes.ContactReception).Payload.TargetUrl);
        }
        finally
        {
            using var scope = Factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().DoctorSpecialties
                .Where(item => item.DoctorId == membership.DoctorId && item.SpecialtyId == membership.SpecialtyId).ExecuteDeleteAsync();
        }
    }

    private async Task<DoctorSpecialty> AddNeurologyMembershipAsync(long doctorId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var neurology = await db.Specialties.SingleAsync(item => item.SpecialtyCode == "SP08");
        var membership = new DoctorSpecialty { DoctorId = doctorId, SpecialtyId = neurology.Id, IsPrimary = false };
        db.DoctorSpecialties.Add(membership);
        await db.SaveChangesAsync();
        return membership;
    }

    private sealed record EligibilityDto(bool IsBlocked, string? Message);

    private DateOnly Today()
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().VietnamToday;
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
            var slot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = new TimeOnly(19, i), EndTime = new TimeOnly(19, i + 30) };
            var appointment = new Appointment
            {
                AppointmentCode = "APT-E5A-" + Guid.NewGuid().ToString("N"), PatientId = patientId,
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
