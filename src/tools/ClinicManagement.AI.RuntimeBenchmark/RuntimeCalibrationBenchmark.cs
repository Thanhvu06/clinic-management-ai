using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

// Calibration reads validation only. Evaluation is a separate, explicitly frozen,
// once-only operation; archived PR #13 eval rows supply the baseline without rerunning eval.
internal static class RuntimeCalibrationBenchmark
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string Data = "";
    private static Dictionary<string, string> ToolLabels = new();
    private static HashSet<string> Catalog = new();
    private static readonly VietnameseIntentClassifier Classifier = new();

    public static async Task RunAsync(string root, string[] args)
    {
        CheckMeasurementDefinition();
        Data = Path.Combine(root, "src/tools/ClinicManagement.AI.Training/data");
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "role_intent_labels_v1.json")));
        Catalog = catalog.RootElement.EnumerateArray().Select(x => x.GetProperty("label").GetString()!).ToHashSet();
        ToolLabels = catalog.RootElement.EnumerateArray().Where(x => x.GetProperty("targetTool").ValueKind == JsonValueKind.String)
            .ToDictionary(x => x.GetProperty("targetTool").GetString()!, x => x.GetProperty("label").GetString()!);
        using var model = new RoleIntentModel(Options.Create(new RoleIntentModelOptions {
            Directory = Path.Combine(root, "src/tools/ClinicManagement.AI.Training/models/role-intent-v2"),
            LabelsPath = Path.Combine(Data, "role_intent_labels_v1.json") }), NullLogger<RoleIntentModel>.Instance);
        if (!model.IsAvailable) throw new InvalidOperationException("Frozen v2 must be available; never train an artifact here.");
        var validation = ExpandValidation();
        var selectionPath = Path.Combine(Path.GetTempPath(), "clinic-runtime-calibration-selection.json");
        if (args.Contains("calibrate"))
        {
            // Same fixed grid/criterion as v2, with exactly RoleIntentModel's authenticated-role mask.
            // Every candidate is also exercised through the actual router/orchestrator; both
            // masked-model acceptance and conditional router acceptance are reported separately.
            var predictions = validation.Select(x => model.Predict(AiTextNormalizer.Normalize(x.Text), x.Role)
                ?? throw new InvalidOperationException("Missing masked validation prediction.")).ToArray();
            var baseline = await Batch(validation, model, .75, false, null);
            var trials = new List<ThresholdTrial>();
            for (var index = 0; index < 20; index++)
            {
                var threshold = Math.Round(index * .05, 2);
                var accepted = validation.Select((x, i) => (Case: x, Prediction: predictions[i]))
                    .Where(x => x.Prediction.Confidence >= threshold).ToArray();
                var weight = accepted.Sum(x => x.Case.Weight);
                var correct = accepted.Where(x => x.Prediction.Label == x.Case.Expected).Sum(x => x.Case.Weight);
                var routed = await Batch(validation, model, threshold, false, null);
                var modelRows = routed.Where(x => x.Source == "model").ToArray();
                trials.Add(new(threshold, accepted.Length, weight, weight == 0 ? 0 : correct / weight,
                    weight / validation.Sum(x => x.Weight), modelRows.Length, modelRows.Sum(x => x.Weight),
                    modelRows.Length == 0 ? 0 : modelRows.Where(x => x.LabelCorrect).Sum(x => x.Weight) / modelRows.Sum(x => x.Weight)));
            }
            var chosen = trials.Where(x => x.AcceptedWeight > 0 && x.AcceptedAccuracy + 1e-12 >= .9)
                .OrderByDescending(x => x.Coverage).ThenBy(x => x.Threshold).FirstOrDefault()
                ?? throw new InvalidOperationException("No grid threshold meets 90% accepted accuracy; stop rather than weaken the criterion.");
            var thresholdOnly = await Batch(validation, model, chosen.Threshold, false, null);
            var ruleCorrect = baseline.Where(x => x.Source == "rule" && x.LabelCorrect).Sum(x => x.Weight);
            var overrideTrials = new List<OverrideTrial>();
            foreach (var threshold in new[] { .80, .85, .90, .95, .99 })
            {
                var after = await Batch(validation, model, chosen.Threshold, true, threshold);
                var events = after.Select((row, index) => (After: row, Before: baseline[index])).Where(x => x.After.Source == "override").ToArray();
                var repaired = events.Where(x => !x.Before.LabelCorrect && x.After.LabelCorrect).ToArray();
                var damaged = events.Where(x => x.Before.LabelCorrect && !x.After.LabelCorrect).ToArray();
                var damage = damaged.Sum(x => x.After.Weight);
                overrideTrials.Add(new(threshold, events.Length, events.Sum(x => x.After.Weight), repaired.Length,
                    repaired.Sum(x => x.After.Weight), damaged.Length, damage, ruleCorrect,
                    repaired.Sum(x => x.After.Weight) - damage, damage <= .01 * ruleCorrect + 1e-12));
            }
            // Ties prefer the safer higher threshold; no eligible trial leaves override disabled.
            var overrideChoice = overrideTrials.Where(x => x.Eligible)
                .OrderByDescending(x => x.NetGain).ThenByDescending(x => x.Threshold).FirstOrDefault();
            var afterSelected = await Batch(validation, model, chosen.Threshold, overrideChoice is not null, overrideChoice?.Threshold);
            var selection = new Selection(chosen.Threshold, overrideChoice is not null, overrideChoice?.Threshold,
                trials, overrideTrials, Summary(baseline), Summary(thresholdOnly), Summary(afterSelected), baseline, afterSelected,
                "Validation only. Fixed 0.00-0.95 grid, step 0.05; highest role-masked model coverage with accepted label accuracy >=90%, tie lower threshold. Router-conditioned accepted accuracy is separately reported. Override maximizes weighted repairs minus damage with damage <=1% of baseline rule-label-correct weight; tie higher threshold; no eligible trial stays disabled.");
            File.WriteAllText(selectionPath, JsonSerializer.Serialize(selection, Json));
            Console.WriteLine(JsonSerializer.Serialize(new { selectionPath, chosen.Threshold, selection.OverrideEnabled,
                selection.OverrideThreshold, ValidationCases = validation.Select(x => x.Id).Distinct().Count(), RuntimeTurns = validation.Count,
                Before = Summary(baseline), After = Summary(afterSelected), overrideTrials }, Json));
            return;
        }

        var frozenSelection = JsonSerializer.Deserialize<Selection>(File.ReadAllText(selectionPath), Json)!;
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/backend/ClinicManagement.Api/appsettings.json")));
        var settings = config.RootElement.GetProperty("RoleIntentModel");
        if (settings.GetProperty("Threshold").GetDouble() != frozenSelection.Threshold ||
            settings.GetProperty("OverrideEnabled").GetBoolean() != frozenSelection.OverrideEnabled ||
            settings.GetProperty("OverrideThreshold").GetDouble() != (frozenSelection.OverrideThreshold ?? .99))
            throw new InvalidOperationException("Configuration differs from validation selection; do not run eval.");
        var frozenPaths = new[] { "src/backend/ClinicManagement.Infrastructure/AI/HybridIntentRouter.cs",
            "src/backend/ClinicManagement.Infrastructure/AI/RoleIntentModel.cs", "src/backend/ClinicManagement.Api/appsettings.json",
            "src/tools/ClinicManagement.AI.RuntimeBenchmark/RuntimeCalibrationBenchmark.cs", "src/tools/ClinicManagement.AI.RuntimeBenchmark/Program.cs" };
        var hashes = frozenPaths.ToDictionary(x => x, x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, x)))));
        var archived = ReadArchivedEval(root);
        var phase = ReadPhase();
        var eval = ExpandEval();
        if (eval.Count != 490 || eval.Select(x => x.Id).Distinct().Count() != 240 ||
            Math.Abs(eval.Sum(x => x.Weight) - 240) > 1e-8) throw new InvalidDataException("Eval protocol mismatch; do not execute.");
        var receipt = Path.Combine(Path.GetTempPath(), "clinic-runtime-calibration-eval-once.json");
        // CreateNew prevents a second eval in this task, including after an interrupted invocation.
        using (var once = new FileStream(receipt, FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(once, new { StartedUtc = DateTime.UtcNow, EvalRuntimeInvocations = 1, Selection = frozenSelection.Threshold,
                frozenSelection.OverrideEnabled, frozenSelection.OverrideThreshold, FrozenBeforeEval = hashes }, Json);
        var evalAfter = await Batch(eval, model, frozenSelection.Threshold, frozenSelection.OverrideEnabled, frozenSelection.OverrideThreshold,
            Path.Combine(Path.GetTempPath(), "clinic-runtime-calibration-eval-frozen-rows.jsonl"));
        var phaseBefore = await Batch(phase, model, .75, false, null);
        var phaseAfter = await Batch(phase, model, frozenSelection.Threshold, frozenSelection.OverrideEnabled, frozenSelection.OverrideThreshold);
        var report = new {
            Provenance = "AI-written eval already seen in v1/v2/PR13. Thresholds/policy frozen from validation before exactly one new eval runtime pass. PR13 eval baseline is recomputed from archived rows, not rerun. No tuning from eval.",
            NewDefinition = "Correct if legacy handler-label proxy is correct OR task label is correct and handler asks for missing fixture resource OR expected ActionRequest/OutOfScope has fallback by design. ProviderDisabled alone is still wrong. Phase5 additionally reports exact canonical-intent match separately.",
            MaskCorrection = "Frozen training code treats Chung as UNION of roles (all labels), not only common labels. Runtime expands each Chung validation/eval case across six authenticated roles with weight 1/6. Pipelines/metadata are unchanged.",
            Selection = frozenSelection, EvalRuntimeInvocations = 1, FrozenBeforeEval = hashes,
            EvalBefore = Summary(archived), EvalAfter = Summary(evalAfter), EvalBeforeRows = archived, EvalAfterRows = evalAfter,
            Phase5Before = Summary(phaseBefore), Phase5After = Summary(phaseAfter), Phase5BeforeRows = phaseBefore, Phase5AfterRows = phaseAfter,
            Phase5StaffStartBooking = new { Staff = phase.Count(x => x.Expected == "StartBooking" && x.Role != AiActorRole.Patient),
                Total = phase.Count(x => x.Expected == "StartBooking") }
        };
        var reportPath = Path.Combine(Path.GetTempPath(), "clinic-runtime-calibration-final.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, Json));
        Console.WriteLine(JsonSerializer.Serialize(new { reportPath, report.EvalBefore, report.EvalAfter, report.Phase5Before, report.Phase5After }, Json));
    }

    private static List<Case> ExpandValidation()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "role_intent_validation_v1.json")));
        return json.RootElement.EnumerateArray().SelectMany(x => Expand(x.GetProperty("id").GetString()!,
            x.GetProperty("role").GetString()!, x.GetProperty("label").GetString()!, x.GetProperty("text").GetString()!)).ToList();
    }
    private static List<Case> ExpandEval() => File.ReadAllLines(Path.Combine(Data, "role_intent_eval_ai_v1.tsv")).Skip(1)
        .SelectMany((line, index) => { var c = line.Split('\t'); return Expand("eval-" + (index + 1), c[0], c[2], c[1]); }).ToList();
    private static IEnumerable<Case> Expand(string id, string role, string label, string text)
    {
        if (role == "Chung") return Enum.GetValues<AiActorRole>().Select(r => new Case(id, r, label, text, 1d / 6));
        role = ClinicManagement.AI.Training.RoleIntentDatasetGenerator.RoleCodes.GetValueOrDefault(role, role);
        return new[] { new Case(id, Enum.Parse<AiActorRole>(role), label, text, 1) };
    }
    private static List<Case> ReadPhase()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "phase5_blind_holdout.json")));
        return json.RootElement.EnumerateArray().Select(x => new Case(x.GetProperty("caseId").GetString()!,
            Enum.Parse<AiActorRole>(x.GetProperty("actor").GetString()!), x.GetProperty("expectedIntent").GetString()!,
            x.GetProperty("inputVi").GetString()!, 1, false)).ToList();
    }
    private static async Task<List<Row>> Batch(List<Case> cases, RoleIntentModel model, double threshold, bool enabled, double? overrideThreshold,
        string? journalPath = null)
    {
        var rows = new List<Row>();
        using var journal = journalPath is null ? null : new StreamWriter(new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write));
        foreach (var item in cases)
        {
            var row = await Run(item, model, threshold, enabled, overrideThreshold);
            rows.Add(row);
            if (journal is not null) { await journal.WriteLineAsync(JsonSerializer.Serialize(row)); await journal.FlushAsync(); }
        }
        if (rows.Select(x => (x.Id, x.Role)).Distinct().Count() != rows.Count) throw new InvalidDataException("Duplicate runtime keys.");
        return rows;
    }

    private static async Task<Row> Run(Case item, RoleIntentModel realModel, double threshold, bool overrideEnabled, double? overrideThreshold)
    {
        var model = new ThresholdModel(realModel, threshold);
        var router = new HybridIntentRouter(new(), model, NullLogger<HybridIntentRouter>.Instance,
            Options.Create(new RoleIntentModelOptions { OverrideEnabled = overrideEnabled, OverrideThreshold = overrideThreshold }));
        var trace = new PlannerTrace(router);
        var pipeline = new PipelineTrace(new AiConversationPipeline(Classifier, new AiSafetyGuard()));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, item.Role.ToString()) }, "offline")) } };
        var current = new Mock<ICurrentUserService>(); current.SetupGet(x => x.UserId).Returns(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var resolver = new Mock<IAiCopilotContextResolver>();
        resolver.Setup(x => x.ResolveAsync(It.IsAny<AiCopilotRequestDto>(), It.IsAny<AiConversationMemoryState?>(), It.IsAny<AiActorRole>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(AiContextResolutionResult.Valid(new()));
        var memory = new Mock<IAiConversationMemoryStore>();
        memory.Setup(x => x.SaveTurnAsync(It.IsAny<AiConversationMemoryWriteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiConversationMemoryState());
        var structured = new Mock<IAiStructuredPlanner>();
        structured.Setup(x => x.PlanAsync(It.IsAny<AiStructuredPlannerRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiStructuredPlannerResult {
            ProviderState = AiProviderStatusContract.Disabled, Decision = new() { Intent = AiChatIntentTypes.ClarificationRequired,
                PlannerMode = AiPlannerModes.Fallback, Message = "Offline provider disabled", Clarification = "Offline provider disabled" } });
        var executor = new Mock<IAiToolExecutor>();
        executor.Setup(x => x.ExecutePlannerPlanAsync(It.IsAny<IReadOnlyList<AiPlannerToolCall>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AiPlannerToolCall> calls, string? session, CancellationToken ct) => (IReadOnlyList<AiToolExecutionResult>)calls
                .Select(c => new AiToolExecutionResult { Status = "completed", ResultType = c.Name, DisplayText = "Synthetic read result" }).ToArray());
        var audit = new Mock<IAiAuditService>();
        audit.Setup(x => x.LogActionAsync(It.IsAny<AiAuditLogEntry>(), It.IsAny<CancellationToken>())).ReturnsAsync(AiAuditWriteResult.Success());
        var orchestrator = new RoleAwareCopilotOrchestrator(http, current.Object, pipeline, trace, structured.Object, resolver.Object,
            memory.Object, new AiGroundedResponseComposer(), executor.Object, audit.Object);
        var response = await orchestrator.ChatAsync(new() { Message = item.Text, SessionId = "offline-" + item.Id,
            ConversationId = "offline-" + item.Id, ClientTurnId = item.Id });
        var decision = trace.Last;
        var tool = decision?.ToolCalls.FirstOrDefault()?.Name;
        var oldLabel = tool is not null && ToolLabels.TryGetValue(tool, out var mapped) ? mapped : decision?.SubIntent switch {
            "BookingWizard" => "StartBooking", "Greeting" => "Greeting", "RoleHelp" => "Help", "ClinicKnowledge" => "ClinicKnowledge",
            "WriteRequiresExplicitActionConfirmation" or "MixedReadWritePlan" => "ActionRequest", _ => decision?.Intent switch {
                AiChatIntentTypes.StartBooking => "StartBooking", AiChatIntentTypes.Greeting => "Greeting", AiChatIntentTypes.Help => "Help", _ => "Unresolved" } };
        var source = router.LastRoute?.Source ?? (decision is null || decision.RequiresProvider ? "fallback" : "rule");
        if (response.AssistantMode == AiAssistantModes.SafetyBlocked) source = "rule";
        if (source is "model" or "override" && decision is not null && response.Intent == decision.Intent) oldLabel = router.LastRoute!.Label;
        var selected = source == "fallback" ? null : source is "model" or "override" ? router.LastRoute?.Label : oldLabel == "Unresolved" ? router.LastRoute?.Label : oldLabel;
        var missing = decision?.SubIntent is "MissingAssignedCase" or "MissingPrescriptionForPayment" or "MissingAppointmentCode" ||
            response.ErrorCode == AiPlannerErrorCodes.ResourceContextRequired;
        if (source == "rule" && missing && trace.Context is {} context)
        {
            var sentinel = new AiResolvedResourceContext { VisitId = long.MaxValue, PrescriptionId = long.MaxValue };
            var probe = new AiDeterministicPlanner().Plan(new() { Role = context.Role, NormalizedMessage = context.NormalizedMessage,
                Analysis = context.Analysis, Resource = sentinel, CurrentTurnResource = sentinel, Memory = context.Memory });
            var probeTool = probe.ToolCalls.FirstOrDefault()?.Name;
            if (probeTool is not null && ToolLabels.TryGetValue(probeTool, out var target)) selected = target;
            else if (decision?.SubIntent == "MissingAppointmentCode") selected = "LookupAppointment";
            else if (Catalog.Contains(probe.Intent)) selected = probe.Intent;
        }
        if (selected is not null && !Catalog.Contains(selected)) selected = "Unresolved";
        var oldCorrect = item.LabelExpected ? oldLabel == item.Expected : response.Intent == item.Expected;
        var labelCorrect = item.LabelExpected ? selected == item.Expected : decision?.Intent == item.Expected;
        var designedFallback = source == "fallback" && item.Expected is "ActionRequest" or "OutOfScope" or AiChatIntentTypes.UnclearOrOutOfScope;
        var correct = CorrectOutcome(oldCorrect, labelCorrect, missing, designedFallback);
        var blocked = missing ? "MissingFixtureResource" : decision?.RequiresProvider == true && response.ProviderState == AiProviderStatusContract.Disabled ? "ProviderDisabled" : null;
        return new(item.Id, item.Role.ToString(), item.Expected, item.Text, item.Weight, response.Intent, oldLabel, source, oldCorrect,
            correct, selected, labelCorrect, router.LastRoute?.Label, router.LastRoute?.Score, decision?.SubIntent, blocked,
            Group(oldCorrect, source, labelCorrect, designedFallback), Group(correct, source, labelCorrect, designedFallback));
    }

    private static string? Group(bool correct, string source, bool labelCorrect, bool designedFallback) => correct ? null : source switch {
        "rule" => labelCorrect ? "b" : "a", "model" or "override" => labelCorrect ? "d" : "c", "fallback" => designedFallback ? "f" : "e",
        _ => throw new InvalidDataException("Unknown source.") };
    private static List<Row> ReadArchivedEval(string root)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/ai/RUNTIME_HYBRID_MEASUREMENTS.json")));
        var rows = json.RootElement.GetProperty("SupplementalDiagnostics").GetProperty("Rows").EnumerateArray().Select(x => {
            var d = x.GetProperty("Diagnostic");
            var oldCorrect = x.GetProperty("Correct").GetBoolean(); var source = x.GetProperty("Source").GetString()!;
            var labelCorrect = d.GetProperty("LabelCorrect").GetBoolean(); var expected = x.GetProperty("Expected").GetString()!;
            string? S(JsonElement element, string key) => element.GetProperty(key).ValueKind == JsonValueKind.Null ? null : element.GetProperty(key).GetString();
            var blocked = S(d, "BlockingReason"); var fallback = source == "fallback" && expected is "ActionRequest" or "OutOfScope";
            var correct = CorrectOutcome(oldCorrect, labelCorrect, blocked == "MissingFixtureResource", fallback);
            return new Row(x.GetProperty("Id").GetString()!, x.GetProperty("Role").GetString()!, expected, d.GetProperty("Text").GetString()!,
                x.GetProperty("Weight").GetDouble(), x.GetProperty("Intent").GetString()!, x.GetProperty("Label").GetString()!, source,
                oldCorrect, correct, S(d, "SelectedLabel"), labelCorrect, S(d, "RawRouteLabel"),
                d.GetProperty("RouteScore").ValueKind == JsonValueKind.Null ? null : d.GetProperty("RouteScore").GetDouble(),
                S(d, "PlannerSubIntent"), blocked, Group(oldCorrect, source, labelCorrect, fallback), Group(correct, source, labelCorrect, fallback));
        }).ToList();
        if (rows.Count != 490 || Math.Abs(rows.Sum(x => x.Weight) - 240) > 1e-8) throw new InvalidDataException("Archived baseline coverage mismatch.");
        return rows;
    }

    private static SplitSummary Summary(List<Row> rows)
    {
        var weight = rows.Sum(x => x.Weight);
        Dictionary<string, GroupCount> Groups(IEnumerable<Row> values, bool legacy, double denominator) => "abcdef".ToDictionary(c => c.ToString(), c => {
            var members = values.Where(x => (legacy ? x.LegacyErrorGroup : x.ErrorGroup) == c.ToString()).ToArray();
            return new GroupCount(members.Length, members.Sum(x => x.Weight), denominator == 0 ? 0 : members.Sum(x => x.Weight) / denominator); });
        return new(rows.Select(x => x.Id).Distinct().Count(), rows.Count, weight, rows.Where(x => x.LegacyCorrect).Sum(x => x.Weight),
            rows.Where(x => x.LegacyCorrect).Sum(x => x.Weight) / weight, rows.Where(x => x.Correct).Sum(x => x.Weight),
            rows.Where(x => x.Correct).Sum(x => x.Weight) / weight,
            new[] { "rule", "model", "override", "fallback" }.ToDictionary(s => s, s => {
                var members = rows.Where(x => x.Source == s).ToArray(); return new GroupCount(members.Length, members.Sum(x => x.Weight), members.Sum(x => x.Weight) / weight); }),
            Groups(rows, true, weight), Groups(rows, false, weight), rows.GroupBy(x => x.Expected).Select(g => new LabelSummary(g.Key,
                g.Count(), g.Sum(x => x.Weight), g.Where(x => x.LegacyCorrect).Sum(x => x.Weight) / g.Sum(x => x.Weight),
                g.Where(x => x.Correct).Sum(x => x.Weight) / g.Sum(x => x.Weight), Groups(g, true, g.Sum(x => x.Weight)), Groups(g, false, g.Sum(x => x.Weight)))).ToArray());
    }
    private static bool CorrectOutcome(bool legacyCorrect, bool labelCorrect, bool missingResource, bool designedFallback) =>
        legacyCorrect || (labelCorrect && missingResource) || designedFallback;

    private static void CheckMeasurementDefinition()
    {
        // Behavioral examples independent of observed validation/eval values.
        if (!CorrectOutcome(true, false, false, false) || !CorrectOutcome(false, true, true, false) ||
            !CorrectOutcome(false, false, false, true) || CorrectOutcome(false, false, true, false) ||
            CorrectOutcome(false, true, false, false))
            throw new InvalidOperationException("Measurement definition regression: missing resource is correct only with correct task; provider-disabled alone is not correct.");
    }
    internal sealed record Case(string Id, AiActorRole Role, string Expected, string Text, double Weight, bool LabelExpected = true);
    internal sealed record Row(string Id, string Role, string Expected, string Text, double Weight, string ResponseIntent, string LegacyLabel,
        string Source, bool LegacyCorrect, bool Correct, string? SelectedLabel, bool LabelCorrect, string? RawRouteLabel, double? Score,
        string? PlannerSubIntent, string? BlockingReason, string? LegacyErrorGroup, string? ErrorGroup);
    internal sealed record GroupCount(int RuntimeTurns, double Weight, double Rate);
    internal sealed record LabelSummary(string Label, int RuntimeTurns, double Weight, double LegacyAccuracy, double Accuracy,
        Dictionary<string, GroupCount> LegacyGroups, Dictionary<string, GroupCount> Groups);
    internal sealed record SplitSummary(int OriginalCases, int RuntimeTurns, double TotalWeight, double LegacyCorrect, double LegacyAccuracy,
        double Correct, double Accuracy, Dictionary<string, GroupCount> Sources, Dictionary<string, GroupCount> LegacyGroups,
        Dictionary<string, GroupCount> Groups, LabelSummary[] PerLabel);
    internal sealed record ThresholdTrial(double Threshold, int AcceptedTurns, double AcceptedWeight, double AcceptedAccuracy, double Coverage,
        int RouterAcceptedTurns, double RouterAcceptedWeight, double RouterAcceptedAccuracy);
    internal sealed record OverrideTrial(double Threshold, int OverrideTurns, double OverrideWeight, int RepairedTurns, double RepairedWeight,
        int DamagedTurns, double DamagedWeight, double RuleCorrectWeight, double NetGain, bool Eligible);
    internal sealed record Selection(double Threshold, bool OverrideEnabled, double? OverrideThreshold, List<ThresholdTrial> ThresholdTrials,
        List<OverrideTrial> OverrideTrials, SplitSummary ValidationBefore, SplitSummary ValidationThresholdOnly, SplitSummary ValidationAfter,
        List<Row> ValidationBeforeRows, List<Row> ValidationAfterRows, string Criterion);
    private sealed class ThresholdModel(RoleIntentModel model, double threshold) : IRoleIntentModel
    {
        public bool IsAvailable => model.IsAvailable;
        public double Threshold => threshold;
        public bool IsLabelAllowed(string label, AiActorRole role) => model.IsLabelAllowed(label, role);
        public RoleIntentModelPrediction? Predict(string text, AiActorRole role) => model.Predict(text, role);
    }
    private sealed class PipelineTrace(IAiConversationPipeline inner) : IAiConversationPipeline
    {
        public AiConversationAnalysis? Last { get; private set; }
        public AiConversationAnalysis Analyze(string? text, IntentClassificationContext? context = null) => Last = inner.Analyze(text, context);
    }
    private sealed class PlannerTrace(IAiDeterministicPlanner inner) : IAiDeterministicPlanner
    {
        public AiPlannerDecision? Last { get; private set; }
        public AiCopilotPlanningContext? Context { get; private set; }
        public AiPlannerDecision Plan(AiCopilotPlanningContext context) { Context = context; return Last = inner.Plan(context); }
        public AiPlannerDecision PlanSuggestion(ClinicManagement.Application.AI.Suggestions.AiSuggestionDefinition? suggestion,
            AiResolvedResourceContext resource) => Last = inner.PlanSuggestion(suggestion, resource);
    }
}
