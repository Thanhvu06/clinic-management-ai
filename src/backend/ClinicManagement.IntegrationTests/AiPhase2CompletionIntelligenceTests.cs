using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiPhase2CompletionIntelligenceTests : IntegrationTestBase
{
    public AiPhase2CompletionIntelligenceTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public void Vietnamese_pipeline_normalizes_and_understands_required_paraphrases_without_interrogative_names()
    {
        var classifier = new VietnameseIntentClassifier(IntentClassificationMode.Off);
        var cases = new[]
        {
            (Text: "  ho mấy hôm nay thì khám khoa gì\\\u0001  ", Intent: AiChatIntentTypes.SpecialtyRecommendation, Reason: "Ho mấy hôm nay", Doctor: (string?)null),
            (Text: "bác nào chữa đau bụng", Intent: AiChatIntentTypes.FindDoctorForSymptom, Reason: "Đau bụng", Doctor: (string?)null),
            (Text: "đau bao tử thì gặp ai", Intent: AiChatIntentTypes.FindDoctorForSymptom, Reason: "Đau bao tử", Doctor: (string?)null),
            (Text: "tìm bác Khải giúp mình", Intent: AiChatIntentTypes.DoctorSearch, Reason: (string?)null, Doctor: "Khải"),
            (Text: "xem lịch bác Nguyễn Minh Khải", Intent: AiChatIntentTypes.DoctorSearch, Reason: (string?)null, Doctor: "Nguyễn Minh Khải")
        };

        foreach (var item in cases)
        {
            var result = classifier.Classify(AiTextNormalizer.Normalize(item.Text));
            Assert.Equal(item.Intent, result.Intent);
            Assert.Equal(item.Reason, result.ExtractedReason);
            Assert.Equal(item.Doctor, result.ExtractedDoctorName);
        }

        Assert.Equal("xin chào", AiTextNormalizer.Normalize("  xin\u0001   chào\\  "));
        Assert.False(VietnameseIntentClassifier.IsValidBookingReason("Người đầu tiên"));
        Assert.False(VietnameseIntentClassifier.IsValidBookingReason("Cho tôi"));
        Assert.False(VietnameseIntentClassifier.IsValidBookingReason("kkkkkkkkkkkk"));
    }

    [Fact]
    public async Task Greeting_and_unknown_text_never_fall_through_to_a_role_default_tool()
    {
        var client = await CreateAuthenticatedClientAsync("tech@test.com");
        var greetingResponse = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "xin chào", sessionId = $"greet-{Guid.NewGuid():N}" });
        Assert.True(greetingResponse.IsSuccessStatusCode, await greetingResponse.Content.ReadAsStringAsync());
        using var greeting = JsonDocument.Parse(await greetingResponse.Content.ReadAsStringAsync());
        var greetingData = greeting.RootElement.GetProperty("data");
        Assert.Equal(AiPlannerModes.Deterministic, greetingData.GetProperty("plannerMode").GetString());
        Assert.Equal(AiProviderStatusContract.NotCalled, greetingData.GetProperty("providerState").GetString());
        Assert.Empty(greetingData.GetProperty("cards").EnumerateArray());

        var unknownResponse = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "tình hình hôm nay thế nào", sessionId = $"unknown-{Guid.NewGuid():N}" });
        Assert.True(unknownResponse.IsSuccessStatusCode, await unknownResponse.Content.ReadAsStringAsync());
        using var unknown = JsonDocument.Parse(await unknownResponse.Content.ReadAsStringAsync());
        var unknownData = unknown.RootElement.GetProperty("data");
        Assert.Equal(AiPlannerModes.Fallback, unknownData.GetProperty("plannerMode").GetString());
        Assert.Equal(AiAssistantModes.Degraded, unknownData.GetProperty("assistantMode").GetString());
        Assert.Empty(unknownData.GetProperty("cards").EnumerateArray());
    }

    [Fact]
    public void Deterministic_planner_requires_context_and_can_create_a_bounded_multi_read_plan()
    {
        var planner = new AiDeterministicPlanner();
        var analysis = new AiConversationAnalysis { NormalizedText = "tóm tắt bệnh nhân hiện tại và xem các chỉ định đang chờ" };
        var missing = planner.Plan(new AiCopilotPlanningContext { Role = AiActorRole.Doctor, NormalizedMessage = analysis.NormalizedText, Analysis = analysis });
        Assert.Empty(missing.ToolCalls);
        Assert.NotNull(missing.Clarification);

        var planned = planner.Plan(new AiCopilotPlanningContext
        {
            Role = AiActorRole.Doctor,
            NormalizedMessage = analysis.NormalizedText,
            Analysis = analysis,
            Resource = new AiResolvedResourceContext { AppointmentId = 42 }
        });
        Assert.Equal(2, planned.ToolCalls.Count);
        Assert.Equal(new[] { "doctor.get_patient_summary", "doctor.get_diagnostic_orders" }, planned.ToolCalls.Select(x => x.Name));
        Assert.All(planned.ToolCalls, call => Assert.True(AiPlannerPolicy.IsAllowed(call.Name)));
    }

    [Fact]
    public async Task Structured_planner_maps_valid_output_and_rejects_mixed_invalid_or_failed_provider_output()
    {
        var provider = new Mock<IAiSpecialtySuggestionProvider>();
        var validCalls = new List<AiPlannerToolCall>
        {
            new() { Name = "doctor.get_my_queue", Version = "1.0", Arguments = JsonSerializer.SerializeToElement(new { }) }
        };
        provider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { PlannerSchemaVersion = "1.0", PlannerConfidence = .91m, IsSuccess = true, Status = "Success", IsClear = true, PrimaryIntent = AiChatIntentTypes.QueueLookup, Reply = "Đã hiểu yêu cầu.", ToolCalls = validCalls });
        var planner = new GeminiStructuredPlanner(provider.Object, new AiProviderHealth(), NullLogger<GeminiStructuredPlanner>.Instance);
        var request = new AiStructuredPlannerRequest { Role = AiActorRole.Doctor, Message = "xem bệnh nhân đang chờ", AllowedToolNames = new[] { "doctor.get_my_queue" } };
        var valid = await planner.PlanAsync(request);
        Assert.True(valid.IsSuccess);
        Assert.True(valid.ProviderCalled);
        Assert.Equal(AiProviderStatusContract.Online, valid.ProviderState);
        Assert.Single(valid.Decision.ToolCalls);

        provider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                IsSuccess = true,
                Status = "Success",
                PlannerSchemaVersion = "1.0",
                PlannerConfidence = .9m,
                PrimaryIntent = AiChatIntentTypes.QueueLookup,
                IsClear = true,
                ToolCalls = new List<AiPlannerToolCall>
                {
                    validCalls[0],
                    new() { Name = "patient.execute_confirmed_action", Version = "1.0", Arguments = JsonSerializer.SerializeToElement(new { confirm = true }) }
                }
            });
        var invalid = await planner.PlanAsync(request);
        Assert.False(invalid.IsSuccess);
        Assert.Empty(invalid.Decision.ToolCalls);
        Assert.Equal(AiProviderStatusContract.Degraded, invalid.ProviderState);

        provider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("test timeout"));
        var timeout = await planner.PlanAsync(request);
        Assert.False(timeout.IsSuccess);
        Assert.Equal(AiPlannerModes.Fallback, timeout.Decision.PlannerMode);
        Assert.Equal(AiProviderStatusContract.Degraded, timeout.ProviderState);
    }

    [Fact]
    public async Task Conversation_memory_persists_across_scopes_isolates_accounts_and_serializes_concurrent_turns()
    {
        var sessionId = $"memory-{Guid.NewGuid():N}";
        var conversationId = $"conversation-{Guid.NewGuid():N}";
        async Task<AiConversationMemoryState> Save(IServiceScope scope, string intent) =>
            await scope.ServiceProvider.GetRequiredService<IAiConversationMemoryStore>().SaveTurnAsync(new AiConversationMemoryWriteRequest
            {
                UserId = DoctorId,
                SessionId = sessionId,
                ConversationId = conversationId,
                Role = AiActorRole.Doctor,
                Intent = intent,
                CurrentResource = new AiResolvedResourceContext { AppointmentId = 123 }
            });

        using (var first = Factory.Services.CreateScope())
            await Save(first, AiChatIntentTypes.PatientSummary);

        using (var second = Factory.Services.CreateScope())
        {
            var store = second.ServiceProvider.GetRequiredService<IAiConversationMemoryStore>();
            var loaded = await store.LoadAsync(sessionId, DoctorId, AiActorRole.Doctor);
            Assert.NotNull(loaded);
            Assert.Equal(123, loaded.CurrentResource!.AppointmentId);
            Assert.Null(await store.LoadAsync(sessionId, Doctor2UserId, AiActorRole.Doctor));
            Assert.Null(await store.LoadAsync(sessionId, DoctorId, AiActorRole.Receptionist));
        }

        using var scopeA = Factory.Services.CreateScope();
        using var scopeB = Factory.Services.CreateScope();
        var writes = await Task.WhenAll(Save(scopeA, AiChatIntentTypes.PatientSummary), Save(scopeB, AiChatIntentTypes.DiagnosticLookup));
        Assert.All(writes, x => Assert.True(x.Version >= 2));
        using var finalScope = Factory.Services.CreateScope();
        var final = await finalScope.ServiceProvider.GetRequiredService<IAiConversationMemoryStore>().LoadAsync(sessionId, DoctorId, AiActorRole.Doctor);
        Assert.NotNull(final);
        Assert.True(final.Version >= 3);
        Assert.DoesNotContain("patient", final.SanitizedSummary!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Doctor_context_is_assignment_and_version_checked_and_multi_plan_uses_real_tools()
    {
        var ownAppointment = await CreateAppointmentAsync(DoctorEntityId, "AI-OWN");
        var otherAppointment = await CreateAppointmentAsync(Doctor2EntityId, "AI-OTHER");
        var client = await CreateAuthenticatedClientAsync("doc@test.com");

        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message = "tóm tắt bệnh nhân hiện tại và xem các chỉ định đang chờ",
            sessionId = $"doctor-plan-{Guid.NewGuid():N}",
            resourceContext = new { appointmentId = ownAppointment }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("cards").GetArrayLength());

        var denied = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message = "tóm tắt bệnh nhân hiện tại",
            sessionId = $"doctor-denied-{Guid.NewGuid():N}",
            resourceContext = new { appointmentId = otherAppointment }
        });
        using var deniedDocument = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.Empty(deniedDocument.RootElement.GetProperty("data").GetProperty("cards").EnumerateArray());

        var stale = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message = "tóm tắt bệnh nhân hiện tại",
            sessionId = $"doctor-stale-{Guid.NewGuid():N}",
            resourceContext = new { appointmentId = ownAppointment },
            resourceVersion = "stale-version"
        });
        using var staleDocument = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal("RESOURCE_SCOPE_DENIED", staleDocument.RootElement.GetProperty("data").GetProperty("subIntent").GetString());
        Assert.Empty(staleDocument.RootElement.GetProperty("data").GetProperty("cards").EnumerateArray());
    }

    [Fact]
    public async Task Reception_card_uses_real_doctor_display_name_not_identity_guid()
    {
        await CreateAppointmentAsync(DoctorEntityId, "AI-NAME", DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)));
        var client = await CreateAuthenticatedClientAsync("rec@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message = "Xem lịch hẹn hôm nay", sessionId = $"doctor-name-{Guid.NewGuid():N}" });
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, json);
        Assert.Contains("Doctor 1", json, StringComparison.Ordinal);
        Assert.DoesNotContain(DoctorId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<long> CreateAppointmentAsync(long doctorId, string prefix, DateOnly? date = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appointmentDate = date ?? GetFutureWorkingDate(4);
        var discriminator = Guid.NewGuid().ToString("N")[..8];
        var slot = new AppointmentSlot
        {
            DoctorId = doctorId,
            SlotDate = appointmentDate,
            StartTime = new TimeOnly(15, 0),
            EndTime = new TimeOnly(15, 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(slot);
        await db.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentCode = $"{prefix}-{discriminator}",
            PatientId = Patient1EntityId,
            DoctorId = doctorId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            AppointmentDate = appointmentDate,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Status = AppointmentStatus.Confirmed,
            Reason = "Kiểm tra context AI"
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }
}
