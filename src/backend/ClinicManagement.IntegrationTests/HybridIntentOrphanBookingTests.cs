using System.Text.Json;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class HybridIntentOrphanBookingTests
{
    private static readonly string[] Steps = [AiChatIntentTypes.SelectDoctor, AiChatIntentTypes.SelectSlot,
        AiChatIntentTypes.ProvideReason, AiChatIntentTypes.ReviewDraft, AiChatIntentTypes.ConfirmBooking,
        AiChatIntentTypes.ModifyDraft, AiChatIntentTypes.CancelDraft];

    [Theory]
    [InlineData(.75, "model")]
    [InlineData(.749, "fallback")]
    public void OrphanStepsUseModelThresholdInsteadOfClearAnalyzerOrOverrideThreshold(double confidence, string source)
    {
        foreach (var step in Steps)
        {
            var model = HybridIntentRouterTests.Fake("MyBills", confidence);
            var router = Router(model);
            var context = Context(step);
            var decision = router.Plan(context);
            Assert.Equal(source, router.LastRoute!.Source);
            if (source == "model") Assert.Equal("patient.get_my_bills", Assert.Single(decision.ToolCalls).Name);
            else Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(decision));
            model.Verify(x => x.Predict(It.IsAny<string>(), AiActorRole.Patient), Times.Once);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyOrUnrelatedMemoryDoesNotOpenBooking(int kind)
    {
        var memory = kind switch
        {
            0 => new AiConversationMemoryState(),
            1 => new AiConversationMemoryState { LastIntent = AiChatIntentTypes.ViewAppointments },
            _ => new AiConversationMemoryState { LastSubIntent = "MyBills", ConfirmedEntities = new Dictionary<string, string> { ["unrelated"] = "1" } }
        };
        foreach (var step in Steps)
        {
            var model = HybridIntentRouterTests.Fake("MyBills", .75);
            var router = Router(model);
            router.Plan(Context(step, memory));
            Assert.Equal("model", router.LastRoute!.Source);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OpenBookingMemoryPreservesExactRuleAndNeverCallsModel(int kind)
    {
        var memory = kind switch
        {
            0 => new AiConversationMemoryState { LastIntent = AiChatIntentTypes.StartBooking },
            1 => new AiConversationMemoryState { LastIntent = AiChatIntentTypes.ConfirmBooking },
            2 => new AiConversationMemoryState { LastSubIntent = "BookingWizard" },
            3 => new AiConversationMemoryState { LastSubIntent = AiChatIntentTypes.SelectSlot },
            4 => new AiConversationMemoryState { PendingClarification = "synthetic-pending" },
            _ => new AiConversationMemoryState { MissingFields = ["doctorId"] }
        };
        foreach (var step in Steps) AssertProtected(Context(step, memory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmergencyPromptInjectionAndGuardKeepAbsolutePriority(int kind)
    {
        var context = new AiCopilotPlanningContext
        {
            Role = AiActorRole.Patient, NormalizedMessage = "synthetic-orphan-booking-marker",
            Analysis = new AiConversationAnalysis
            {
                Intent = new() { Intent = AiChatIntentTypes.ProvideReason, IsClear = true, Method = kind == 2 ? "SyntheticGuard" : "RuleBased" },
                Safety = new() { IsEmergency = kind == 0, IsPromptInjection = kind == 1 }
            }
        };
        AssertProtected(context);
    }

    [Fact]
    public void StartBookingAndResolvedDeterministicHandlersRemainProtected()
    {
        AssertProtected(Context(AiChatIntentTypes.StartBooking));
        AssertProtected(new AiCopilotPlanningContext
        {
            Role = AiActorRole.Patient, NormalizedMessage = "Xem lịch hẹn của tôi",
            Analysis = new() { Intent = new() { Intent = AiChatIntentTypes.ModifyDraft, IsClear = true } }
        });
    }

    [Theory]
    [InlineData("ActionRequest", true)]
    [InlineData("OutOfScope", true)]
    [InlineData("DoctorQueue", false)]
    public void UnsupportedOrForeignModelLabelsRetainFallback(string label, bool allowed)
    {
        var model = HybridIntentRouterTests.Fake(label, .99);
        model.Setup(x => x.IsLabelAllowed(label, AiActorRole.Patient)).Returns(allowed);
        var router = Router(model);
        var context = Context(AiChatIntentTypes.ConfirmBooking);
        Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        Assert.Equal("fallback", router.LastRoute!.Source);
    }

    private static void AssertProtected(AiCopilotPlanningContext context)
    {
        var model = HybridIntentRouterTests.Fake("MyBills", .99);
        var router = Router(model);
        Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        Assert.Equal("rule", router.LastRoute!.Source);
        model.Verify(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>()), Times.Never);
    }

    private static HybridIntentRouter Router(Mock<IRoleIntentModel> model) => new(new(), model.Object,
        NullLogger<HybridIntentRouter>.Instance, Options.Create(new RoleIntentModelOptions { OverrideEnabled = true, OverrideThreshold = .8 }));

    private static AiCopilotPlanningContext Context(string intent, AiConversationMemoryState? memory = null) => new()
    {
        Role = AiActorRole.Patient, NormalizedMessage = "synthetic-orphan-booking-marker", Memory = memory,
        Analysis = new() { Intent = new() { Intent = intent, IsClear = true } }
    };
}
