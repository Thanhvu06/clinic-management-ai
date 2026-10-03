using System.Text.Json;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class HybridIntentCalibrationTests
{
    [Theory]
    [InlineData(.95, "override", "patient.get_my_bills")]
    [InlineData(.949, "rule", "patient.get_my_appointments")]
    public void DifferentHighConfidenceLabelCanReplaceRuleAtConfiguredBoundary(double score, string source, string tool)
    {
        var model = HybridIntentRouterTests.Fake("MyBills", score);
        var router = Router(model);
        var result = router.Plan(Context());
        Assert.Equal(source, router.LastRoute!.Source);
        Assert.Equal(tool, Assert.Single(result.ToolCalls).Name);
        Assert.DoesNotContain(result.ToolCalls, x => x.Name.Contains("execute") || x.Name.Contains("prepare"));
        model.Verify(x => x.Predict(It.IsAny<string>(), AiActorRole.Patient), Times.Once);
    }

    [Theory]
    [InlineData("MyAppointments", .99, true)]
    [InlineData("ActionRequest", .99, true)]
    [InlineData("OutOfScope", .99, true)]
    [InlineData("DoctorQueue", .99, false)]
    [InlineData("MyBills", double.NaN, true)]
    [InlineData("MyBills", 1.01, true)]
    public void SameUnsupportedForeignOrInvalidPredictionKeepsOriginalRule(string label, double confidence, bool allowed)
    {
        var model = HybridIntentRouterTests.Fake(label, confidence);
        model.Setup(x => x.IsLabelAllowed(label, AiActorRole.Patient)).Returns(allowed);
        var context = Context();
        var router = Router(model);
        Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        Assert.Equal("rule", router.LastRoute!.Source);
    }

    [Theory]
    [InlineData(AiChatIntentTypes.StartBooking)]
    [InlineData(AiChatIntentTypes.SelectDoctor)]
    [InlineData(AiChatIntentTypes.SelectSlot)]
    [InlineData(AiChatIntentTypes.ProvideReason)]
    [InlineData(AiChatIntentTypes.ReviewDraft)]
    [InlineData(AiChatIntentTypes.ConfirmBooking)]
    [InlineData(AiChatIntentTypes.ModifyDraft)]
    [InlineData(AiChatIntentTypes.CancelDraft)]
    [InlineData(AiChatIntentTypes.FindEarliestAvailableSlot)]
    [InlineData(AiChatIntentTypes.SpecialtyRecommendation)]
    [InlineData(AiChatIntentTypes.FindDoctorForSymptom)]
    [InlineData(AiChatIntentTypes.DoctorSearch)]
    public void EveryLegacyBookingIntentNeverCallsOverrideModel(string intent)
    {
        AssertProtected(Context(intent: intent));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void SafetyGuardsAndContextNeverCallOverrideModel(int kind)
    {
        var memory = kind switch {
            3 => new AiConversationMemoryState { LastIntent = "ViewAppointments" },
            4 => new AiConversationMemoryState { PendingClarification = "which appointment?" },
            5 => new AiConversationMemoryState { MissingFields = new[] { "appointmentId" } },
            6 => new AiConversationMemoryState { ConfirmedEntities = new Dictionary<string, string> { ["case"] = "7" } },
            7 => new AiConversationMemoryState { CurrentResource = new() { VisitId = 7 } },
            8 => new AiConversationMemoryState { SanitizedSummary = "prior turn" }, _ => null };
        var context = Context(memory: memory);
        context.Analysis.Intent.Method = kind == 2 ? "ExplicitOutOfScopeGuard" : "RuleBased";
        if (kind is 0 or 1) context = new AiCopilotPlanningContext {
            Role = context.Role, NormalizedMessage = context.NormalizedMessage, Memory = memory,
            Analysis = new() { Intent = context.Analysis.Intent, Safety = new() { IsEmergency = kind == 0, IsPromptInjection = kind == 1 } } };
        AssertProtected(context);
    }

    [Fact]
    public void CorrectionAndRelativeSelectionNeverCallOverrideModel()
    {
        var context = Context(); context.Analysis.Intent.IsCorrection = true; AssertProtected(context);
        context = Context(); context.Analysis.Intent.ExtractedRelativeDoctorIndex = 1; AssertProtected(context);
        context = Context(); context.Analysis.Intent.ExtractedRelativeSlotIndex = 1; AssertProtected(context);
    }

    [Theory]
    [InlineData("xem chỉ định của ca đang mở", AiActorRole.Doctor)]
    [InlineData("đối chiếu thanh toán toa hiện tại", AiActorRole.Pharmacist)]
    [InlineData("chuan bi giu cho thuoc", AiActorRole.Pharmacist)]
    [InlineData("tra cuu ma lich hen", AiActorRole.Receptionist)]
    public void ResourceAndConfirmationClarificationsStayProtected(string text, AiActorRole role)
    {
        AssertProtected(new() { Role = role, NormalizedMessage = text,
            Analysis = new() { Intent = new() { IsClear = false, Intent = AiChatIntentTypes.UnclearOrOutOfScope } } });
    }

    [Fact]
    public void DisabledOverridePreservesPr13RuleAndItsNoModelCallContract()
    {
        var model = HybridIntentRouterTests.Fake("MyBills", 1);
        var context = Context();
        var router = Router(model, enabled: false);
        Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        Assert.Equal("rule", router.LastRoute!.Source);
        model.Verify(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>()), Times.Never);
        Assert.False(new RoleIntentModelOptions().OverrideEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void MissingOrInvalidOverrideThresholdFailsClosed(double? threshold)
    {
        var model = HybridIntentRouterTests.Fake("MyBills", .99);
        var router = Router(model, threshold: threshold);
        Assert.Equal("patient.get_my_appointments", Assert.Single(router.Plan(Context()).ToolCalls).Name);
        model.Verify(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>()), Times.Never);
    }

    [Theory]
    [InlineData(null, .75)]
    [InlineData("0.8", .8)]
    public void ConfigurationThresholdTakesPriorityAndAbsentThresholdUsesFrozenMeta(string? threshold, double expected)
    {
        var values = new Dictionary<string, string?> {
            ["RoleIntentModel:Directory"] = HybridIntentRouterTests.ModelDirectory,
            ["RoleIntentModel:LabelsPath"] = Path.Combine(HybridIntentRouterTests.DataDirectory, "role_intent_labels_v1.json") };
        if (threshold is not null) values["RoleIntentModel:Threshold"] = threshold;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddRoleIntentRuntime(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<RoleIntentModel>();
        Assert.True(model.IsAvailable); Assert.Equal(expected, model.Threshold);
        Assert.False(provider.GetRequiredService<IOptions<RoleIntentModelOptions>>().Value.OverrideEnabled);
    }

    private static void AssertProtected(AiCopilotPlanningContext context)
    {
        var model = HybridIntentRouterTests.Fake("MyBills", 1);
        var router = Router(model);
        Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        Assert.Equal("rule", router.LastRoute!.Source);
        model.Verify(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>()), Times.Never);
    }
    private static HybridIntentRouter Router(Mock<IRoleIntentModel> model, bool enabled = true, double? threshold = .95) =>
        new(new(), model.Object, NullLogger<HybridIntentRouter>.Instance, Options.Create(new RoleIntentModelOptions {
            OverrideEnabled = enabled, OverrideThreshold = threshold }));
    private static AiCopilotPlanningContext Context(string intent = AiChatIntentTypes.ViewAppointments, AiConversationMemoryState? memory = null) =>
        new() { Role = AiActorRole.Patient, NormalizedMessage = "Xem lịch hẹn của tôi", Memory = memory,
            Analysis = new() { Intent = new() { Intent = intent, IsClear = true } } };
}
