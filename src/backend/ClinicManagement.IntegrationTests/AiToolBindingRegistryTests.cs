using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.Authentication.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;

namespace ClinicManagement.IntegrationTests;

public sealed class AiToolBindingRegistryTests
{
    [Fact]
    public void Doctor_provider_resource_b_is_rejected_before_any_tool_can_execute()
    {
        var result = Preflight(
            new[] { Call("doctor.get_patient_summary", new { visitId = 202L }) },
            AiRoleToolCatalog.Definitions,
            new AiResolvedResourceContext { VisitId = 101, ResourceVersion = "v-a" });

        Assert.False(result.IsValid);
        Assert.Equal("PROVIDER_RESOURCE_MISMATCH", result.Code);
        Assert.Empty(result.BoundCalls);
        Assert.DoesNotContain("101", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("202", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_exact_or_missing_provider_binding_is_canonicalized_to_the_current_resource()
    {
        var definitions = AiRoleToolCatalog.Definitions;
        var current = new AiResolvedResourceContext { AppointmentId = 41, VisitId = 101, ResourceVersion = "v-a" };

        var exact = Preflight(new[] { Call("doctor.get_patient_summary", new { appointmentId = 41L }) }, definitions, current);
        Assert.True(exact.IsValid, exact.Message);
        Assert.Equal(101, exact.BoundCalls[0].Arguments.GetProperty("visitId").GetInt64());
        Assert.False(exact.BoundCalls[0].Arguments.TryGetProperty("appointmentId", out _));

        var missing = Preflight(new[] { Call("doctor.get_patient_summary", new { }) }, definitions, current);
        Assert.True(missing.IsValid, missing.Message);
        Assert.Equal(101, missing.BoundCalls[0].Arguments.GetProperty("visitId").GetInt64());
    }

    [Fact]
    public void Resource_bound_tool_without_current_context_is_a_clarification_boundary()
    {
        var result = Preflight(
            new[] { Call("doctor.get_patient_summary", new { }) },
            AiRoleToolCatalog.Definitions,
            new AiResolvedResourceContext());

        Assert.False(result.IsValid);
        Assert.Equal("RESOURCE_CONTEXT_REQUIRED", result.Code);
        Assert.Empty(result.BoundCalls);
    }

    [Fact]
    public void Pharmacist_and_patient_detail_bind_only_their_current_resource()
    {
        var pharmacist = Preflight(
            new[] { Call("pharmacist.get_prescription_payment_status", new { prescriptionId = 77L }) },
            AiRoleToolCatalog.Definitions,
            new AiResolvedResourceContext { PrescriptionId = 77, ResourceVersion = "p-1" });
        Assert.True(pharmacist.IsValid, pharmacist.Message);

        var wrongPatientResource = Preflight(
            new[] { Call("patient.get_appointment_detail", new { appointmentId = 88L }) },
            PatientCopilotToolHandler.Definitions(),
            new AiResolvedResourceContext { AppointmentId = 89, ResourceVersion = "a-1" });
        Assert.False(wrongPatientResource.IsValid);
        Assert.Equal("PROVIDER_RESOURCE_MISMATCH", wrongPatientResource.Code);
    }

    [Fact]
    public void Mixed_plan_is_atomic_and_nested_authority_arguments_are_rejected()
    {
        var mixed = Preflight(
            new[]
            {
                Call("doctor.get_my_queue", new { }),
                Call("doctor.get_patient_summary", new { visitId = 202L })
            },
            AiRoleToolCatalog.Definitions,
            new AiResolvedResourceContext { VisitId = 101 });
        Assert.False(mixed.IsValid);
        Assert.Empty(mixed.BoundCalls);

        var nested = Preflight(
            new[] { Call("clinic.search_knowledge", new { query = new { nested = new { userId = "actor" } } }) },
            AiRoleToolCatalog.Definitions,
            new AiResolvedResourceContext());
        Assert.False(nested.IsValid);
        Assert.Equal("FORBIDDEN_TOOL_ARGUMENT", nested.Code);
    }

    [Fact]
    public void Resource_change_and_version_change_are_not_the_same_server_context()
    {
        var original = new AiResolvedResourceContext { VisitId = 101, ResourceVersion = "version-a" };
        Assert.True(AiToolBindingRegistry.IsSameResourceContext(original, new AiResolvedResourceContext { VisitId = 101, ResourceVersion = "version-a" }));
        Assert.False(AiToolBindingRegistry.IsSameResourceContext(original, new AiResolvedResourceContext { VisitId = 102, ResourceVersion = "version-a" }));
        Assert.False(AiToolBindingRegistry.IsSameResourceContext(original, new AiResolvedResourceContext { VisitId = 101, ResourceVersion = "version-b" }));
    }

    [Fact]
    public async Task Orchestrator_rejects_a_provider_plan_when_the_current_resource_changes_while_waiting()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(x => x.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Doctor") }, "test"))
        });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var pipeline = new Mock<IAiConversationPipeline>();
        pipeline.Setup(x => x.Analyze(It.IsAny<string>(), It.IsAny<IntentClassificationContext>()))
            .Returns(new AiConversationAnalysis { NormalizedText = "yêu cầu mơ hồ" });
        var deterministic = new Mock<IAiDeterministicPlanner>();
        deterministic.Setup(x => x.Plan(It.IsAny<AiCopilotPlanningContext>()))
            .Returns(new AiPlannerDecision { RequiresProvider = true });
        var structured = new Mock<IAiStructuredPlanner>();
        structured.Setup(x => x.PlanAsync(It.IsAny<AiStructuredPlannerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiStructuredPlannerResult
            {
                IsSuccess = true,
                ProviderCalled = true,
                ProviderState = AiProviderStatusContract.Online,
                Decision = new AiPlannerDecision
                {
                    PlannerMode = AiPlannerModes.Gemini,
                    Intent = AiChatIntentTypes.PatientSummary,
                    ToolCalls = new[] { Call("doctor.get_patient_summary", new { visitId = 101L }) }
                }
            });
        var resolver = new Mock<IAiCopilotContextResolver>();
        resolver.SetupSequence(x => x.ResolveAsync(It.IsAny<AiCopilotRequestDto>(), It.IsAny<AiConversationMemoryState?>(), AiActorRole.Doctor, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiContextResolutionResult.Valid(new AiResolvedResourceContext { CurrentRoute = "/doctor", VisitId = 101, ResourceVersion = "v-a" }))
            .ReturnsAsync(AiContextResolutionResult.Valid(new AiResolvedResourceContext { CurrentRoute = "/doctor", VisitId = 102, ResourceVersion = "v-b" }));
        var memory = new Mock<IAiConversationMemoryStore>();
        memory.Setup(x => x.LoadAsync(It.IsAny<string>(), It.IsAny<Guid?>(), AiActorRole.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiConversationMemoryState?)null);
        memory.Setup(x => x.SaveTurnAsync(It.IsAny<AiConversationMemoryWriteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiConversationMemoryState { Version = 1 });
        var composer = new Mock<IAiGroundedResponseComposer>();
        composer.Setup(x => x.Compose(It.IsAny<AiPlannerDecision>(), It.IsAny<IReadOnlyList<AiToolExecutionResult>>()))
            .Returns(new AiGroundedResponse { Message = "Resource đã thay đổi." });
        var executor = new Mock<IAiToolExecutor>();
        var audit = new Mock<IAiAuditService>();
        audit.Setup(x => x.LogActionAsync(It.IsAny<AiAuditLogEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiAuditWriteResult.Success());

        var orchestrator = new RoleAwareCopilotOrchestrator(
            http.Object, currentUser.Object, pipeline.Object, deterministic.Object, structured.Object,
            resolver.Object, memory.Object, composer.Object, executor.Object, audit.Object);

        var response = await orchestrator.ChatAsync(new AiCopilotRequestDto
        {
            Message = "xem ca hiện tại",
            SessionId = "sess_resource_change"
        });

        Assert.Equal("RESOURCE_CONTEXT_CHANGED", response.ErrorCode);
        executor.Verify(x => x.ExecutePlannerPlanAsync(It.IsAny<IReadOnlyList<AiPlannerToolCall>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Public_catalog_planner_contract_does_not_expose_internal_resource_ids()
    {
        var definitions = PatientCopilotToolHandler.Definitions();
        var searchDoctors = Assert.Single(definitions, x => x.Name == "clinic.search_doctors");
        var slots = Assert.Single(definitions, x => x.Name == "clinic.get_available_slots");

        Assert.DoesNotContain(searchDoctors.ArgumentSchema, x => x.Name is "doctorId" or "specialtyId" or "slotId");
        Assert.DoesNotContain(slots.ArgumentSchema, x => x.Name is "doctorId" or "specialtyId" or "slotId");
    }

    [Fact]
    public async Task Structured_planner_returns_resource_mismatch_without_a_bound_call()
    {
        var provider = new Mock<IAiSpecialtySuggestionProvider>();
        provider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                IsSuccess = true,
                Status = "Success",
                PlannerSchemaVersion = "1.0",
                PlannerConfidence = .95m,
                PrimaryIntent = AiChatIntentTypes.PatientSummary,
                IsClear = true,
                ToolCalls = new List<AiPlannerToolCall> { Call("doctor.get_patient_summary", new { visitId = 202L }) }
            });

        var planner = new GeminiStructuredPlanner(provider.Object, new AiProviderHealth(), NullLogger<GeminiStructuredPlanner>.Instance);
        var result = await planner.PlanAsync(new AiStructuredPlannerRequest
        {
            Role = AiActorRole.Doctor,
            Message = "tóm tắt ca hiện tại",
            Resource = new AiResolvedResourceContext { VisitId = 101 },
            AllowedTools = AiRoleToolCatalog.Definitions.Where(x => x.AllowedRoles.Contains(AiActorRole.Doctor)).ToArray(),
            AllowedToolNames = AiRoleToolCatalog.Definitions.Where(x => x.AllowedRoles.Contains(AiActorRole.Doctor)).Select(x => x.Name).ToArray()
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("PROVIDER_RESOURCE_MISMATCH", result.FailureReason);
        Assert.Equal("PROVIDER_RESOURCE_MISMATCH", result.Decision.ErrorCode);
        Assert.Empty(result.Decision.ToolCalls);
    }

    [Fact]
    public async Task Structured_planner_does_not_disclose_server_bound_argument_names_to_provider()
    {
        var provider = new Mock<IAiSpecialtySuggestionProvider>();
        var capturedPolicy = string.Empty;
        provider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<ChatMessageDto>, List<WhitelistItemDto>, string, CancellationToken>((_, _, _, policy, _) => capturedPolicy = policy)
            .ReturnsAsync(new AiChatProviderResult
            {
                IsSuccess = true,
                Status = "Success",
                PlannerSchemaVersion = "1.0",
                PlannerConfidence = .95m,
                PrimaryIntent = AiChatIntentTypes.QueueLookup,
                IsClear = true,
                ToolCalls = new List<AiPlannerToolCall> { Call("doctor.get_my_queue", new { }) }
            });

        var planner = new GeminiStructuredPlanner(provider.Object, new AiProviderHealth(), NullLogger<GeminiStructuredPlanner>.Instance);
        var doctorTools = AiRoleToolCatalog.Definitions.Where(x => x.AllowedRoles.Contains(AiActorRole.Doctor)).ToArray();
        var result = await planner.PlanAsync(new AiStructuredPlannerRequest
        {
            Role = AiActorRole.Doctor,
            Message = "xem hàng đợi",
            Resource = new AiResolvedResourceContext { VisitId = 101 },
            AllowedTools = doctorTools,
            AllowedToolNames = doctorTools.Select(x => x.Name).ToArray()
        });

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.DoesNotContain("visitId", capturedPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("appointmentId", capturedPolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hasServerBoundResource", capturedPolicy, StringComparison.Ordinal);
    }

    private static AiToolPlanPreflightResult Preflight(
        IReadOnlyList<AiPlannerToolCall> calls,
        IReadOnlyList<AiToolDefinition> definitions,
        AiResolvedResourceContext resource) =>
        AiToolBindingRegistry.ValidateAndBindPlan(calls, definitions, resource);

    private static AiPlannerToolCall Call(string name, object args) => new()
    {
        Name = name,
        Version = "1.0",
        Arguments = JsonSerializer.SerializeToElement(args)
    };
}
