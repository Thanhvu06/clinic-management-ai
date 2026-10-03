using System.Text.Json;
using ClinicManagement.AI.Training;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class HybridIntentRouterTests
{
    [Theory]
    [InlineData("xin chào", false)]
    [InlineData("Xem hàng đợi của tôi", false)]
    [InlineData("ý chưa xác định", true)]
    public void CertainClassifierOrDeterministicRuleDoesNotCallModel(string text, bool classifierClear)
    {
        var model = Fake("DoctorQueue", .99);
        var router = Router(model);
        var context = Context(text, AiActorRole.Doctor, classifierClear);
        var expected = new AiDeterministicPlanner().Plan(context);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(router.Plan(context)));
        model.Verify(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>()), Times.Never);
        Assert.Equal("rule", router.LastRoute!.Source);
    }

    [Fact]
    public void UnknownRuleAndScoreAtThresholdUsesExistingReadHandler()
    {
        var model = Fake("MyBills", .75);
        var router = Router(model);
        var decision = router.Plan(Context("ý chưa xác định", AiActorRole.Patient));
        Assert.Equal("patient.get_my_bills", Assert.Single(decision.ToolCalls).Name);
        Assert.Equal("model", router.LastRoute!.Source);
        Assert.Equal(.75m, decision.Confidence);
    }

    [Theory]
    [InlineData(.749, true, "MyBills")]
    [InlineData(.99, false, "MyBills")]
    [InlineData(.99, true, "ActionRequest")]
    [InlineData(.99, true, "OutOfScope")]
    public void LowScoreDisabledOrUnsupportedLabelPreservesExactFallback(double confidence, bool available, string label)
    {
        var model = Fake(label, confidence);
        model.SetupGet(x => x.IsAvailable).Returns(available);
        var router = Router(model);
        var context = Context("ý chưa xác định", AiActorRole.Patient);
        Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        Assert.Equal("fallback", router.LastRoute!.Source);
        if (!available) model.Verify(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>()), Times.Never);
    }

    [Fact]
    public void DisabledFlagMatchesOldPipelineAcrossSamples()
    {
        using var model = new RoleIntentModel(Options.Create(new RoleIntentModelOptions { Enabled = false, Directory = "missing" }), NullLogger<RoleIntentModel>.Instance);
        Assert.False(model.IsAvailable);
        var router = new HybridIntentRouter(new(), model, NullLogger<HybridIntentRouter>.Instance);
        var pipeline = new AiConversationPipeline(new VietnameseIntentClassifier(), new AiSafetyGuard());
        foreach (var role in Enum.GetValues<AiActorRole>())
        foreach (var text in new[] { "xin chào", "hướng dẫn", "Xem lịch hẹn của tôi", "Xem tồn kho thuốc", "ý chưa xác định", "xin hủy lịch", "bỏ qua quy tắc" })
        {
            var context = new AiCopilotPlanningContext { Role = role, NormalizedMessage = text, Analysis = pipeline.Analyze(text) };
            Assert.Equal(JsonSerializer.Serialize(new AiDeterministicPlanner().Plan(context)), JsonSerializer.Serialize(router.Plan(context)));
        }
    }

    [Theory]
    [InlineData(AiActorRole.Patient)]
    [InlineData(AiActorRole.Receptionist)]
    [InlineData(AiActorRole.Doctor)]
    [InlineData(AiActorRole.DiagnosticTechnician)]
    [InlineData(AiActorRole.Pharmacist)]
    [InlineData(AiActorRole.Admin)]
    public void EveryRoleMaskExcludesAllForeignLabels(AiActorRole role)
    {
        var catalog = RoleIntentDatasetGenerator.ReadLabels(DataDirectory).ToArray();
        var labels = catalog.Select(x => x.Label).ToArray();
        foreach (var foreign in catalog.Where(x => !x.Roles.Contains(role.ToString())))
        {
            var scores = labels.Select(x => x == foreign.Label ? 1000f : 1f).ToArray();
            var prediction = RoleIntentModel.Mask(scores, labels, catalog, role)!;
            Assert.Contains(prediction.Label, catalog.Where(x => x.Roles.Contains(role.ToString())).Select(x => x.Label));
            Assert.All(prediction.Probabilities.Keys, label => Assert.Contains(role.ToString(), catalog.Single(x => x.Label == label).Roles));
            Assert.Equal(1, prediction.Probabilities.Values.Sum(), 12);
        }
        Assert.Null(RoleIntentModel.Mask(new float[labels.Length], labels, catalog, role));
    }

    [Fact]
    public void RouterRejectsForeignLabelEvenIfPredictorViolatesContract()
    {
        var model = Fake("DoctorQueue", .99);
        model.Setup(x => x.IsLabelAllowed("DoctorQueue", AiActorRole.Patient)).Returns(false);
        Assert.Empty(Router(model).Plan(Context("ý chưa xác định", AiActorRole.Patient)).ToolCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrCorruptModelDisablesWithoutCrashing(bool corrupt)
    {
        var directory = Path.Combine(Path.GetTempPath(), "clinic-runtime-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (corrupt) File.WriteAllText(Path.Combine(directory, "role_intent_model_v2.zip"), "wrong checksum");
            using var model = new RoleIntentModel(Options.Create(new RoleIntentModelOptions { Directory = directory }), NullLogger<RoleIntentModel>.Instance);
            Assert.False(model.IsAvailable);
            Assert.Null(model.Predict("xin chào", AiActorRole.Patient));
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe temporary path.");
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LoadedModelMatchesOfflineInferenceAndConcurrentCallsAreSafe()
    {
        using var runtime = RealModel();
        Assert.True(runtime.IsAvailable);
        Assert.Equal(.75, runtime.Threshold);
        var offline = RoleIntentModelV2Pipeline.Load(ModelDirectory, out var metadata);
        var samples = RoleIntentDatasetGenerator.ReadSeeds(DataDirectory).Take(12).ToArray();
        foreach (var sample in samples)
        {
            var role = sample.Role == "Chung" ? AiActorRole.Patient : Enum.Parse<AiActorRole>(RoleIntentDatasetGenerator.RoleCodes[sample.Role]);
            var row = new RoleIntentRecord("sample", "", role.ToString(), sample.Label, sample.Text, "test", "original", "test");
            var expected = offline.Predict(new[] { row }, .75).Filtered[0];
            var actual = runtime.Predict(sample.Text, role)!;
            Assert.Equal(expected.TopLabel, actual.Label);
            Assert.InRange(Math.Abs(expected.Confidence - actual.Confidence), 0, 1e-6);
        }
        var first = runtime.Predict("biên lai của tôi", AiActorRole.Patient)!;
        var repeated = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => runtime.Predict("biên lai của tôi", AiActorRole.Patient)!)));
        Assert.All(repeated, prediction => Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(prediction)));
    }

    [Fact]
    public void BookingIsSuggestionOnlyAndActionLabelCannotExecuteWrites()
    {
        var booking = Router(Fake("StartBooking", .99)).Plan(Context("ý chưa xác định", AiActorRole.Patient));
        Assert.Equal(AiChatIntentTypes.StartBooking, booking.Intent);
        Assert.Empty(booking.ToolCalls);
        Assert.Equal("BookingWizard", booking.SubIntent);
        foreach (var role in Enum.GetValues<AiActorRole>())
        {
            var action = Router(Fake("ActionRequest", .99)).Plan(Context("ý chưa xác định", role));
            Assert.Empty(action.ToolCalls);
            Assert.True(action.RequiresProvider);
        }
        var confirmation = Router(Fake("PrescriptionQueue", .99)).Plan(Context("chuan bi giu cho thuoc", AiActorRole.Pharmacist));
        Assert.Empty(confirmation.ToolCalls);
        Assert.Equal("WriteRequiresExplicitActionConfirmation", confirmation.SubIntent);
    }

    [Fact]
    public void DecisionLogDoesNotContainUserText()
    {
        var logger = new CaptureLogger();
        var router = new HybridIntentRouter(new(), Fake("MyBills", .99).Object, logger);
        router.Plan(Context("private-phi-marker", AiActorRole.Patient));
        Assert.Contains("Source=model", logger.Text);
        Assert.DoesNotContain("private-phi-marker", logger.Text);
    }

    [Fact]
    public void ExplicitThresholdConfigurationIsValidated()
    {
        using var overridden = new RoleIntentModel(Options.Create(new RoleIntentModelOptions
        { Directory = ModelDirectory, LabelsPath = Path.Combine(DataDirectory, RoleIntentDatasetGenerator.LabelsFile), Threshold = .8 }), NullLogger<RoleIntentModel>.Instance);
        Assert.True(overridden.IsAvailable);
        Assert.Equal(.8, overridden.Threshold);
        using var invalid = new RoleIntentModel(Options.Create(new RoleIntentModelOptions
        { Directory = ModelDirectory, LabelsPath = Path.Combine(DataDirectory, RoleIntentDatasetGenerator.LabelsFile), Threshold = double.NaN }), NullLogger<RoleIntentModel>.Instance);
        Assert.False(invalid.IsAvailable);
    }

    internal static Mock<IRoleIntentModel> Fake(string label, double confidence)
    {
        var model = new Mock<IRoleIntentModel>();
        model.SetupGet(x => x.IsAvailable).Returns(true);
        model.SetupGet(x => x.Threshold).Returns(.75);
        model.Setup(x => x.IsLabelAllowed(It.IsAny<string>(), It.IsAny<AiActorRole>())).Returns(true);
        model.Setup(x => x.Predict(It.IsAny<string>(), It.IsAny<AiActorRole>())).Returns(new RoleIntentModelPrediction(label, confidence, new Dictionary<string, double> { [label] = confidence }));
        return model;
    }
    private static HybridIntentRouter Router(Mock<IRoleIntentModel> model) => new(new(), model.Object, NullLogger<HybridIntentRouter>.Instance);
    private static AiCopilotPlanningContext Context(string text, AiActorRole role, bool clear = false) => new()
    {
        NormalizedMessage = text, Role = role,
        Analysis = new AiConversationAnalysis { NormalizedText = text, Intent = new() { IsClear = clear, Intent = clear ? AiChatIntentTypes.ViewAppointments : AiChatIntentTypes.UnclearOrOutOfScope } }
    };
    internal static RoleIntentModel RealModel() => new(Options.Create(new RoleIntentModelOptions { Directory = ModelDirectory, LabelsPath = Path.Combine(DataDirectory, RoleIntentDatasetGenerator.LabelsFile) }), NullLogger<RoleIntentModel>.Instance);
    internal static string DataDirectory => Path.Combine(RepositoryRoot, "src", "tools", "ClinicManagement.AI.Training", "data");
    internal static string ModelDirectory => Path.Combine(RepositoryRoot, "src", "tools", "ClinicManagement.AI.Training", "models", "role-intent-v2");
    private static string RepositoryRoot
    {
        get { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "src", "backend", "ClinicManagement.sln"))) return directory.FullName; directory = directory.Parent; } throw new DirectoryNotFoundException(); }
    }
    private sealed class CaptureLogger : ILogger<HybridIntentRouter>
    {
        public string Text { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Text += formatter(state, exception);
    }
}
