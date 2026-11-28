using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientChatSafetyGeneralizationTests(TypedWizardFactory factory)
    : IntegrationTestBase(factory), IClassFixture<TypedWizardFactory>
{
    private const string Chat = "/api/v1/ai/chat";
    private const string Wizard = "/api/v1/ai/booking-wizard";
    private const string Overdose = "con tôi uống hết nửa chai siro ho của người lớn";
    private const string Unresponsive = "chồng tôi say rượu nặng, nôn liên tục và lơ mơ không tỉnh táo";
    private const string Choking = "bé ngậm đồng xu rồi lỡ nuốt, giờ ho sặc sụa";

    [Theory]
    [InlineData(Overdose)]
    [InlineData(Unresponsive)]
    [InlineData(Choking)]
    public async Task Reported_incidents_preempt_passive_chat_and_booking_drafts(string message)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await PostAsync<AiChatResponseDto>(client, Chat, new { message, hasOpenBookingWizard = true });
        AssertEmergency(response);
    }

    [Theory]
    [InlineData("Gọi mãi không tỉnh dù trời sáng rồi.")]
    [InlineData("Sau khi vấp ngã, chị không tỉnh táo.")]
    [InlineData("Người bên cạnh tôi thở rít.")]
    [InlineData("Con bị ngưng thở lúc tôi đang thay áo.")]
    [InlineData("Người nhà tôi nghẹn sau khi ăn một mẩu bánh.")]
    [InlineData("Chi toi tim moi trong luc dung cho xe.")]
    [InlineData("Ngoài sân có người đang sùi bọt mép.")]
    [InlineData("Bố tôi đi tiêu ra máu.")]
    [InlineData("Máu chảy không ngừng ở chỗ tay bị đứt.")]
    [InlineData("Bà tôi đột nhiên liệt chân phải.")]
    [InlineData("Anh vừa ăn xong thì môi lưỡi sưng và khó thở.")]
    public void Danger_signs_trigger_without_a_named_cause(string message) =>
        Assert.True(new AiSafetyGuard().Inspect(message).IsEmergency, message);

    [Theory]
    [InlineData("Cô vừa uống nguyên chai si rô trong ngăn tủ.")]
    [InlineData("Vợ tôi uống thuốc gấp đôi lượng thường lệ trong một lần.")]
    [InlineData("Anh đã uống quá nhiều bia trong giờ vừa rồi.")]
    [InlineData("Thằng bé ngậm một viên bi nhỏ trong lúc chơi.")]
    [InlineData("Chau hít một hạt cườm lúc đang xâu vòng.")]
    [InlineData("Em bé nuốt một cái kẹp tóc.")]
    [InlineData("Con em hít một hạt khi chơi.")]
    [InlineData("Bé nuốt tiền xu nhưng chưa ho.")]
    public void Exposure_rules_trigger_even_without_a_current_danger_sign(string message)
    {
        var result = new AiSafetyGuard().Inspect(message);
        Assert.True(result.IsEmergency, message);
        Assert.Equal("phơi nhiễm chất độc hoặc dị vật nguy hiểm", result.MatchedCategory);
    }

    [Theory]
    [InlineData("Chị không bị lơ mơ, đang nói rõ ràng.")]
    [InlineData("Hỏi cách bảo quản nửa chai si rô đã dùng.")]
    [InlineData("Thuốc còn nguyên lọ và tôi chưa uống.")]
    [InlineData("Bé ăn hạt đậu đã luộc mềm.")]
    [InlineData("Con chó ngậm đồng xu trong miệng.")]
    [InlineData("Bé không hít hạt, hạt còn nguyên trong hộp.")]
    [InlineData("Ông từng bị đau ngực, đã điều trị ổn định và đến hẹn khám.")]
    [InlineData("Bé đã được khám sau lần nuốt dị vật năm ngoái.")]
    [InlineData("Chị uống hết thuốc theo đơn, hẹn tái khám tuần tới.")]
    public void Ordinary_and_resolved_context_pass_the_rules(string message) =>
        Assert.False(new AiSafetyGuard().Inspect(message).IsEmergency, message);

    [Theory]
    [InlineData("trời nóng muốn chết, phòng khám có máy lạnh không")]
    [InlineData("lanh chet mat, co cho doi trong nha khong")]
    [InlineData("chán ăn muốn chết, cho hỏi khám khoa nào")]
    [InlineData("đói chết đi được, phòng khám mấy giờ đóng cửa")]
    [InlineData("buồn ngủ muốn chết mà tối mới có hẹn")]
    [InlineData("buồn cười muốn chết, có hẹn chiều mai không")]
    public void Physical_states_can_intensify_without_implying_self_harm(string message)
    {
        var guard = new AiSafetyGuard();
        Assert.False(guard.Inspect(message).IsSelfHarm);
        Assert.True(AiSafetyGuard.HasDistressIdiom(message));
        Assert.NotEqual(message, AiSafetyGuard.RemoveDistressIdioms(message));
    }

    [Theory]
    [InlineData("tôi buồn muốn chết")]
    [InlineData("chan doi qua muon chet")]
    [InlineData("cô đơn quá, tôi muốn chết")]
    [InlineData("tôi bế tắc và tuyệt vọng muốn chết")]
    [InlineData("trời lạnh, nhưng tôi muốn chết")]
    [InlineData("tôi mệt muốn chết, thật sự tôi không muốn sống")]
    [InlineData("mệt mỏi quá, tôi đang định tự làm hại mình")]
    [InlineData("dù trời nóng tôi muốn chết")]
    public void Emotional_distress_and_explicit_intent_keep_the_safety_response(string message) =>
        Assert.True(new AiSafetyGuard().Inspect(message).IsSelfHarm, message);

    [Theory]
    [InlineData("ngứa da, nên khám ở đâu", "SP04")]
    [InlineData("ngứa da muốn chết, nên khám ở đâu", "SP04")]
    [InlineData("ngua da, nen di kham cho nao", "SP04")]
    [InlineData("ngứa da nên khám khoa nào", "SP04")]
    [InlineData("đau lưng muốn chết luôn, nên khám khoa nào", "SP07")]
    [InlineData("phòng khám ở đâu", null)]
    public async Task Symptom_guidance_includes_the_facility_while_address_only_keeps_the_existing_route(string message, string? specialty)
    {
        using var scope = Factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var address = specialty is null
            ? await db.Facilities.Where(facility => facility.IsActive).OrderBy(facility => facility.Id).Select(facility => facility.Address).FirstAsync()
            : await db.Departments.Where(department => department.Specialty!.SpecialtyCode == specialty && department.IsActive &&
                    department.Facility.IsActive).OrderBy(department => department.FacilityId)
                .Select(department => department.Facility.Address).FirstAsync();

        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var chat = await PostAsync<AiChatResponseDto>(client, Chat, new { message, hasOpenBookingWizard = true });
        Assert.False(chat.ProviderWasCalled);
        Assert.Contains(address!, chat.Message);
        if (specialty is null)
        {
            Assert.Equal("FacilityInquiryResolved", chat.DialogueOutcome);
            Assert.Empty(chat.SpecialtySuggestions);
        }
        else
        {
            Assert.Equal("SymptomLocationResolved", chat.DialogueOutcome);
            Assert.Contains(chat.SpecialtySuggestions, item => item.SpecialtyCode == specialty);
        }
    }

    [Fact]
    public async Task Weather_idiom_stays_on_the_ordinary_chat_route()
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var chat = await PostAsync<AiChatResponseDto>(client, Chat, new { message = "trời nóng muốn chết, phòng khám có máy lạnh không" });
        var plain = await PostAsync<AiChatResponseDto>(client, Chat, new { message = "trời nóng, phòng khám có máy lạnh không" });
        Assert.DoesNotContain("người bạn tin cậy", chat.Message);
        Assert.Equal(plain.DialogueOutcome, chat.DialogueOutcome);
    }

    [Theory]
    [InlineData("bác sĩ Lý Kim Dung chủ nhật có khám không", "06:00,12:00,18:00,00:00")]
    [InlineData("bác sĩ Lý Kim Dung chủ nhật có khám buổi chiều không", "12:00,13:30")]
    [InlineData("bac si Ly Kim Dung toi chu nhat co kham khong", "18:00,21:30")]
    [InlineData("chủ nhật bác sĩ Lý Kim Dung có khám ban đêm không", "00:00,01:00,22:00")]
    public async Task Date_specific_doctor_reads_group_by_day_part_or_filter_to_the_part_requested(string message, string times)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
        var doctorId = await (from doctor in db.Doctors
                              join user in db.Users on doctor.UserId equals user.Id
                              where user.FullName == "Lý Kim Dung"
                              select doctor.Id).SingleAsync();
        factory.Clock.UtcNow = new DateTime(2026, 11, 28, 5, 0, 0, DateTimeKind.Utc);
        var day = new DateOnly(2026, 11, 29);
        factory.Slots.Clear();
        foreach (var at in new[] { new TimeOnly(21, 30), new(0, 0), new(6, 0), new(12, 0), new(18, 0),
                     new(13, 30), new(9, 0), new(22, 0), new(1, 0), new(6, 0) })
            factory.Slots.Add(new() { SlotId = factory.Slots.Count + 1, DoctorId = doctorId, SlotDate = day,
                StartTime = at, EndTime = at.AddMinutes(30) });

        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var chat = await PostAsync<AiChatResponseDto>(client, Chat, new { message });
        Assert.Equal("DoctorInformation", chat.DialogueOutcome);
        Assert.Contains("29/11/2026", chat.Message);
        Assert.Equal(times.Split(','), chat.Message.Split('\n').Where(line => line.StartsWith("- ")).Select(line => line[2..7]));
    }

    [Fact]
    public async Task Semantic_and_rule_safety_preempt_an_open_wizard_and_a_NoShow_lock()
    {
        using var handler = new PatientEmergencySemanticGateTests.SafetyEmbeddingHandler();
        using var embedClient = new HttpClient(handler);
        using var gate = PatientEmergencySemanticGateTests.Gate(embedClient, "http://127.0.0.1");
        Assert.True(await gate.BuildIndexAsync(CancellationToken.None));
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiSafetyGuard>();
            services.AddSingleton<IAiSafetyGuard>(new AiSafetyGuard(gate));
        }));
        using var scope = Factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var offset in new[] { 1, 2, 3 })
        {
            var day = factory.Clock.VietnamToday.AddDays(-offset);
            var slot = await CreateAvailableSlotAsync(DoctorEntityId, day, new TimeOnly(18, 0), new TimeOnly(18, 30));
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = $"NOSHOW-SEMANTIC-{offset}", PatientId = Patient2EntityId, DoctorId = DoctorEntityId,
                SpecialtyId = SpecialtyEntityId, AppointmentSlotId = slot.Id, AppointmentDate = day,
                StartTime = slot.StartTime, EndTime = slot.EndTime, Status = AppointmentStatus.NoShow,
                Histories = [new AppointmentHistory
                {
                    Action = AppointmentHistoryAction.NoShow, NewStatus = AppointmentStatus.NoShow,
                    CreatedAt = factory.Clock.UtcNow.AddDays(-offset)
                }]
            });
        }
        await db.SaveChangesAsync();

        using var originalRegular = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var originalLocked = await CreateAuthenticatedClientAsync("pat2@test.com");
        using var regular = app.CreateClient();
        using var locked = app.CreateClient();
        regular.DefaultRequestHeaders.Authorization = originalRegular.DefaultRequestHeaders.Authorization;
        locked.DefaultRequestHeaders.Authorization = originalLocked.DefaultRequestHeaders.Authorization;

        var sessionId = $"sess_{Guid.NewGuid():N}";
        var lockedSession = $"sess_{Guid.NewGuid():N}";
        var open = await PostAsync<AiBookingWizardResponseDto>(regular, Wizard, new { sessionId, step = "start" });
        var blocked = await PostAsync<AiBookingWizardResponseDto>(locked, Wizard, new { sessionId = lockedSession, step = "start" });
        Assert.NotNull(open.TextToken);
        Assert.Equal("ONLINE_BOOKING_BLOCKED", blocked.ErrorCode);
        using var copilotResponse = await locked.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message = PatientEmergencySemanticGateTests.SemanticOnly, isTypedInput = true, suggestionCode = "patient.my_appointments"
        });
        copilotResponse.EnsureSuccessStatusCode();
        var copilot = (await copilotResponse.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
        Assert.Equal(AiChatIntentTypes.EmergencyEscalation, copilot.GetProperty("intent").GetString());
        Assert.False(copilot.GetProperty("providerWasCalled").GetBoolean());
        Assert.Empty(copilot.GetProperty("executedToolNames").EnumerateArray());
        foreach (var message in new[] { PatientEmergencySemanticGateTests.SemanticOnly, Overdose, Unresponsive, Choking })
        {
            var interrupted = await PostAsync<AiBookingWizardResponseDto>(regular, Wizard,
                new { sessionId, step = "text", optionToken = open.TextToken, text = message });
            var lockedStart = await PostAsync<AiBookingWizardResponseDto>(locked, Wizard,
                new { sessionId = lockedSession, step = "start", typedMessage = message });
            foreach (var response in new[] { interrupted, lockedStart })
            {
                Assert.Equal("EMERGENCY", response.ErrorCode);
                Assert.Equal("tel:115", Assert.Single(response.Actions).Payload.TargetUrl);
                Assert.False(response.ProviderWasCalled);
            }
            var chat = await PostAsync<AiChatResponseDto>(locked, Chat, new { message, hasOpenBookingWizard = true });
            AssertEmergency(chat);
        }
    }

    private static void AssertEmergency(AiChatResponseDto response)
    {
        Assert.Equal("SafetyEmergency", response.DialogueOutcome);
        Assert.Equal("EMERGENCY", response.Urgency);
        Assert.Equal("tel:115", Assert.Single(response.Actions).Payload.TargetUrl);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.False(response.ProviderWasCalled);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request) where T : class
    {
        using var response = await client.PostAsJsonAsync(url, request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return Assert.IsType<T>((await response.Content.ReadFromJsonAsync<ApiResponse<T>>())?.Data);
    }
}
