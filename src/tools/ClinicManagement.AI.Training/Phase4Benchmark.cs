using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Infrastructure.AI.Tools;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicManagement.AI.Training;

public static class Phase4BenchmarkRunner
{
    public const string EvaluatorVersion = "phase4-independent-v1";
    public const int Seed = 42024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Generate(string path)
    {
        var cases = Phase4DatasetFactory.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(cases, JsonOptions);
        File.WriteAllText(path, json, Encoding.UTF8);
        return JsonSerializer.Serialize(new
        {
            generated = true,
            path = Path.GetFullPath(path),
            cases = cases.Count,
            checksumSha256 = Sha256(json),
            seed = Seed,
            datasetVersion = Phase4DatasetFactory.Version
        }, JsonOptions);
    }

    public static string ValidateOnly(string path, string trainPath)
    {
        var report = Validate(path, trainPath);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string Evaluate(string path, string trainPath, string head)
    {
        var validation = Validate(path, trainPath);
        var report = new Phase4BenchmarkReport
        {
            EvaluatorVersion = EvaluatorVersion,
            TimestampUtc = DateTimeOffset.UtcNow,
            Head = head,
            LiveGeminiExecuted = false,
            Dataset = validation
        };

        report.SelfTestPassed = SelfTest(out var selfTestDetail);
        report.SelfTestDetail = selfTestDetail;
        if (!validation.IsValid)
        {
            report.Failures.AddRange(validation.Errors.Select(error => new Phase4Failure
            {
                CaseId = "dataset",
                Layer = "dataset_validation",
                Expected = "valid",
                Actual = "invalid",
                Detail = error
            }));
            return JsonSerializer.Serialize(report, JsonOptions);
        }

        var cases = LoadCases(path);
        var safetyFailures = new List<Phase4Failure>();
        var intentFailures = new List<Phase4Failure>();
        var plannerFailures = new List<Phase4Failure>();
        var mustNotContainFailures = new List<Phase4Failure>();
        var safetyGuard = new AiSafetyGuard();
        var classifier = new VietnameseIntentClassifier(IntentClassificationMode.Off);
        var planner = new AiDeterministicPlanner();
        var intentClasses = cases.Select(item => item.ExpectedIntent).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToList();
        var rulePairs = new List<Phase4IntentPair>();

        foreach (var item in cases)
        {
            var safety = safetyGuard.Inspect(item.InputVi);
            var actualSafety = safety.IsEmergency ? "emergency" : safety.IsPromptInjection ? "prompt_injection" : "none";
            if (!string.Equals(actualSafety, item.Safety, StringComparison.OrdinalIgnoreCase))
            {
                safetyFailures.Add(Failure(item, "safety", item.Safety, actualSafety, safety.MatchedCategory ?? "no-match"));
            }

            var intent = classifier.Classify(item.InputVi, new IntentClassificationContext());
            rulePairs.Add(new Phase4IntentPair(item.ExpectedIntent, intent.Intent));
            if (!string.Equals(intent.Intent, item.ExpectedIntent, StringComparison.OrdinalIgnoreCase))
            {
                intentFailures.Add(Failure(item, "intent_rule", item.ExpectedIntent, intent.Intent, intent.Method));
            }

            var actualTool = "none";
            var plannerDetail = actualSafety == "none" ? "blocked_by_authorization" : "blocked_by_safety";
            if (actualSafety == "none" && !string.Equals(item.ExpectedOutcome, "authorization_denied", StringComparison.OrdinalIgnoreCase))
            {
                var analysis = new AiConversationAnalysis
                {
                    NormalizedText = item.InputVi,
                    Intent = intent,
                    Safety = safety,
                    ProviderStatus = "NotCalled"
                };
                var decision = planner.Plan(new AiCopilotPlanningContext
                {
                    Role = ParseRole(item.Actor),
                    NormalizedMessage = item.InputVi,
                    Analysis = analysis,
                    Resource = ToResource(item.Context)
                });
                actualTool = decision.ToolCalls.Count == 0 ? "none" : string.Join(",", decision.ToolCalls.Select(call => call.Name));
                plannerDetail = decision.RequiresProvider ? "provider_required" : decision.PlannerMode;
            }
            if (!string.Equals(actualTool, item.ExpectedPlannerTool, StringComparison.OrdinalIgnoreCase))
            {
                plannerFailures.Add(Failure(item, "planner", item.ExpectedPlannerTool, actualTool, plannerDetail));
            }

            foreach (var forbidden in item.ForbiddenTools)
            {
                if (actualTool.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(tool => string.Equals(tool, forbidden, StringComparison.OrdinalIgnoreCase)))
                {
                    mustNotContainFailures.Add(Failure(item, "must_not_contain", "forbidden tool absent", forbidden, "planner output contained a forbidden tool"));
                }
            }
        }

        report.Safety = Metric("safety", cases.Count - safetyFailures.Count, cases.Count, safetyFailures);
        report.IntentRuleBaseline = Metric("intent_rule", cases.Count - intentFailures.Count, cases.Count, intentFailures);
        var ruleDetails = ComputeSimpleIntentMetrics(rulePairs, intentClasses);
        report.IntentRuleBaseline.MacroPrecision = ruleDetails.MacroPrecision;
        report.IntentRuleBaseline.MacroRecall = ruleDetails.MacroRecall;
        report.IntentRuleBaseline.MacroF1 = ruleDetails.MacroF1;
        report.IntentRuleBaseline.PerIntent = ruleDetails.PerIntent;
        report.IntentRuleBaseline.ConfusionMatrix = ruleDetails.ConfusionMatrix;
        report.PlannerRouting = Metric("planner_routing", cases.Count - plannerFailures.Count, cases.Count, plannerFailures);
        report.MustNotContain = Metric("must_not_contain", cases.Count - mustNotContainFailures.Count, cases.Count, mustNotContainFailures);
        report.Failures.AddRange(safetyFailures);
        report.Failures.AddRange(intentFailures);
        report.Failures.AddRange(plannerFailures);
        report.Failures.AddRange(mustNotContainFailures);
        report.Grounding = NotEvaluated("grounding", "Requires HTTP/tool response fixtures and persisted source records.");
        report.Authorization = NotEvaluated("authorization", "Requires HTTP gateway plus persisted user/session/facility/resource fixtures.");
        report.Outcome = NotEvaluated("outcome", "Requires HTTP confirmation and persistence side-effect assertions.");
        report.MlNet = EvaluateMlNet(cases, trainPath);
        report.Summary = BuildSummary(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static bool SelfTest(out string detail)
    {
        var cases = Phase4DatasetFactory.Build().Take(2).ToList();
        cases[1].CaseId = cases[0].CaseId;
        var report = ValidateCases(cases, Array.Empty<IntentDatasetRecord>(), "{}");
        var passed = !report.IsValid && report.Errors.Any(error => error.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        detail = passed ? "invalid duplicate CaseId was rejected" : "self-test failed to reject duplicate CaseId";
        return passed;
    }

    private static Phase4ValidationReport Validate(string path, string trainPath)
    {
        if (!File.Exists(path))
            return InvalidValidation($"Dataset file not found: {path}");

        var raw = File.ReadAllText(path);
        List<Phase4BenchmarkCase>? cases;
        try
        {
            cases = JsonSerializer.Deserialize<List<Phase4BenchmarkCase>>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            return InvalidValidation($"Dataset JSON is invalid: {ex.Message}");
        }

        var train = LoadIntentDataset(trainPath);
        return ValidateCases(cases ?? new List<Phase4BenchmarkCase>(), train, raw);
    }

    private static Phase4ValidationReport ValidateCases(
        IReadOnlyList<Phase4BenchmarkCase> cases,
        IReadOnlyList<IntentDatasetRecord> train,
        string rawJson)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var canonicalIntents = new HashSet<string>(AiChatIntentTypes.All, StringComparer.OrdinalIgnoreCase);
        var definitions = PatientCopilotToolHandler.Definitions()
            .Concat(AiRoleToolCatalog.Definitions)
            .Concat(AiRoleActionCatalog.Definitions)
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var expectedActors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Patient"] = 120,
            ["Receptionist"] = 30,
            ["Doctor"] = 30,
            ["DiagnosticTechnician"] = 20,
            ["Pharmacist"] = 20,
            ["Admin"] = 20
        };
        var actualActors = cases.GroupBy(item => item.Actor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var expected in expectedActors)
        {
            if (!actualActors.TryGetValue(expected.Key, out var count) || count < expected.Value)
                errors.Add($"actor {expected.Key} requires at least {expected.Value} cases, found {count}");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedInputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            if (string.IsNullOrWhiteSpace(item.CaseId)) errors.Add("case has empty CaseId");
            else if (!ids.Add(item.CaseId)) errors.Add($"duplicate CaseId: {item.CaseId}");
            if (string.IsNullOrWhiteSpace(item.InputVi)) errors.Add($"{item.CaseId}: InputVi is required");
            if (!expectedActors.ContainsKey(item.Actor ?? string.Empty)) errors.Add($"{item.CaseId}: unknown actor {item.Actor}");
            if (!canonicalIntents.Contains(item.ExpectedIntent ?? string.Empty)) errors.Add($"{item.CaseId}: unknown canonical intent {item.ExpectedIntent}");
            if (!AllowedOutcomes.Contains(item.ExpectedOutcome ?? string.Empty)) errors.Add($"{item.CaseId}: unknown outcome {item.ExpectedOutcome}");
            if (!AllowedSafety.Contains(item.Safety ?? string.Empty)) errors.Add($"{item.CaseId}: unknown safety {item.Safety}");
            if (!AllowedGrounding.Contains(item.Grounding ?? string.Empty)) errors.Add($"{item.CaseId}: unknown grounding {item.Grounding}");
            if (!string.Equals(item.Split, "holdout", StringComparison.OrdinalIgnoreCase)) errors.Add($"{item.CaseId}: split must be holdout");
            if (!item.Synthetic) errors.Add($"{item.CaseId}: synthetic must be true");
            if (string.IsNullOrWhiteSpace(item.Rationale)) errors.Add($"{item.CaseId}: rationale is required");

            var allowed = new HashSet<string>(item.AllowedTools ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var forbidden = new HashSet<string>(item.ForbiddenTools ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (allowed.Overlaps(forbidden)) errors.Add($"{item.CaseId}: allowed and forbidden tools overlap");
            foreach (var tool in allowed.Concat(forbidden).Append(item.ExpectedPlannerTool).Where(tool => !string.IsNullOrWhiteSpace(tool) && tool != "none"))
            {
                if (!definitions.ContainsKey(tool))
                    errors.Add($"{item.CaseId}: unknown tool {tool}");
            }
            foreach (var tool in allowed.Append(item.ExpectedPlannerTool).Where(tool => !string.IsNullOrWhiteSpace(tool) && tool != "none"))
            {
                if (definitions.TryGetValue(tool, out var definition) && !IsRoleAllowed(definition, ParseRole(item.Actor)))
                    errors.Add($"{item.CaseId}: actor {item.Actor} is not allowed to use {tool}");
            }

            var normalized = NormalizeForLeakage(item.InputVi);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                if (normalizedInputs.TryGetValue(normalized, out var prior))
                    warnings.Add($"near/exact duplicate input inside holdout: {prior} and {item.CaseId}");
                else
                    normalizedInputs[normalized] = item.CaseId;
            }
        }

        var trainTexts = train.Where(record => record.Approved && string.Equals(record.Split, "train", StringComparison.OrdinalIgnoreCase))
            .Select(record => NormalizeForLeakage(record.Text))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        var exactLeakage = cases.Count(item => trainTexts.Contains(NormalizeForLeakage(item.InputVi), StringComparer.Ordinal));
        if (exactLeakage > 0) errors.Add($"exact train/holdout leakage detected: {exactLeakage} case(s)");
        var nearExamples = new List<string>();
        foreach (var item in cases)
        {
            var input = Tokenize(item.InputVi);
            if (input.Count == 0) continue;
            var nearest = trainTexts.Select(text => Jaccard(input, Tokenize(text))).DefaultIfEmpty(0).Max();
            if (nearest >= .92)
            {
                nearExamples.Add($"{item.CaseId}:{nearest.ToString("F3", CultureInfo.InvariantCulture)}");
                warnings.Add($"{item.CaseId}: near-duplicate train similarity {nearest:F3}");
            }
        }

        var existingSplits = train.Where(record => record.Approved &&
                (string.Equals(record.Split, "train", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(record.Split, "val", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(record.Split, "test", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(record => record.Split, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(record => NormalizeForLeakage(record.Text)).Where(text => !string.IsNullOrWhiteSpace(text)).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var existingExactLeakage = 0;
        var existingNearLeakage = 0;
        var existingLeakageExamples = new List<string>();
        var splitNames = existingSplits.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        for (var leftIndex = 0; leftIndex < splitNames.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < splitNames.Count; rightIndex++)
            {
                var leftName = splitNames[leftIndex];
                var rightName = splitNames[rightIndex];
                foreach (var left in existingSplits[leftName])
                {
                    if (existingSplits[rightName].Contains(left, StringComparer.Ordinal))
                        existingExactLeakage++;
                    var similarity = existingSplits[rightName].Select(right => Jaccard(Tokenize(left), Tokenize(right))).DefaultIfEmpty(0).Max();
                    if (similarity >= .92)
                    {
                        existingNearLeakage++;
                        if (existingLeakageExamples.Count < 20)
                            existingLeakageExamples.Add($"{leftName}/{rightName}:{similarity:F3}");
                    }
                }
            }
        }
        if (existingExactLeakage > 0)
            errors.Add($"exact leakage exists between ML.NET train/val/test splits: {existingExactLeakage} pair(s)");
        if (existingNearLeakage > 0)
            warnings.Add($"near leakage exists between ML.NET train/val/test splits: {existingNearLeakage} pair(s)");

        return new Phase4ValidationReport
        {
            IsValid = errors.Count == 0,
            TotalCases = cases.Count,
            ByActor = cases.GroupBy(item => item.Actor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            BySafety = cases.GroupBy(item => item.Safety ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            ByIntent = cases.GroupBy(item => item.ExpectedIntent ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            Split = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["holdout"] = cases.Count },
            Errors = errors,
            Warnings = warnings,
            ExactTrainLeakage = exactLeakage,
            NearTrainLeakage = nearExamples.Count,
            NearLeakageExamples = nearExamples,
            ExistingSplitExactLeakage = existingExactLeakage,
            ExistingSplitNearLeakage = existingNearLeakage,
            ExistingSplitLeakageExamples = existingLeakageExamples,
            DatasetSha256 = Sha256(rawJson),
            DatasetVersion = Phase4DatasetFactory.Version,
            Seed = Seed
        };
    }

    private static Phase4MlNetReport EvaluateMlNet(IReadOnlyList<Phase4BenchmarkCase> holdout, string trainPath)
    {
        var training = LoadIntentDataset(trainPath);
        var trainRecords = training.Where(record => record.Approved && string.Equals(record.Split, "train", StringComparison.OrdinalIgnoreCase)).ToList();
        var validationRecords = training.Where(record => record.Approved && string.Equals(record.Split, "val", StringComparison.OrdinalIgnoreCase)).ToList();
        if (trainRecords.Count == 0 || validationRecords.Count == 0 || holdout.Count == 0)
            return new Phase4MlNetReport { Status = "not_run", Detail = "train, validation, and independent holdout are required" };

        var ml = new MLContext(Seed);
        var trainView = ml.Data.LoadFromEnumerable(trainRecords.Select(record => new IntentInput { Text = record.Text, Label = record.Intent }));
        var valView = ml.Data.LoadFromEnumerable(validationRecords.Select(record => new IntentInput { Text = record.Text, Label = record.Intent }));
        var holdoutInputs = holdout.Select(item => new IntentInput { Text = item.InputVi, Label = item.ExpectedIntent }).ToList();
        var holdoutView = ml.Data.LoadFromEnumerable(holdoutInputs);
        var pipeline = ml.Transforms.Text.FeaturizeText("Features", nameof(IntentInput.Text))
            .Append(ml.Transforms.Conversion.MapValueToKey("KeyLabel", nameof(IntentInput.Label)))
            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("KeyLabel", "Features"))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var model = pipeline.Fit(trainView);
        var valPredictions = ml.Data.CreateEnumerable<IntentEvaluationResult>(model.Transform(valView), false).ToList();
        var classes = trainRecords.Select(record => record.Intent).Concat(holdout.Select(item => item.ExpectedIntent))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToList();
        var threshold = SelectThreshold(valPredictions);
        var transformed = model.Transform(holdoutView);
        var predictions = ml.Data.CreateEnumerable<IntentEvaluationResult>(transformed, false).ToList();
        VBuffer<ReadOnlyMemory<char>> scoreSlots = default;
        transformed.Schema["Score"].Annotations.GetValue("SlotNames", ref scoreSlots);
        var scoreLabels = scoreSlots.DenseValues().Select(value => value.ToString()).ToArray();
        var correct = predictions.Zip(holdoutInputs, (prediction, expected) => ApplyThreshold(prediction, threshold) == expected.Label).Count(value => value);
        var rawCorrect = predictions.Zip(holdoutInputs, (prediction, expected) => prediction.PredictedLabel == expected.Label).Count(value => value);
        var perIntent = ComputePerIntent(predictions, holdoutInputs, threshold, classes);
        var matrix = ComputeMatrix(predictions, holdoutInputs, threshold, classes);
        var probabilities = predictions.Select((prediction, index) => ProbabilityFor(prediction, holdoutInputs[index].Label, scoreLabels)).ToList();
        var logLoss = probabilities.Count == 0 ? 0 : -probabilities.Select(value => Math.Log(Math.Max(value, 1e-7))).Average();
        var ece = ComputeEce(predictions, holdoutInputs, threshold);
        var latency = MeasureLatency(model, ml, holdout.Select(item => item.InputVi).ToList());
        return new Phase4MlNetReport
        {
            Status = "completed",
            ScoreSemantics = "SdcaMaximumEntropy probability scores; no second softmax applied",
            TrainSamples = trainRecords.Count,
            ValidationSamples = validationRecords.Count,
            HoldoutSamples = holdout.Count,
            LockedThreshold = threshold,
            ValidationMacroF1AtThreshold = ValidationMacroF1(valPredictions, validationRecords, threshold, classes),
            Correct = correct,
            RawModelCorrect = rawCorrect,
            Accuracy = (double)correct / holdout.Count,
            MacroPrecision = perIntent.Count == 0 ? 0 : perIntent.Values.Average(value => value.Precision),
            MacroRecall = perIntent.Count == 0 ? 0 : perIntent.Values.Average(value => value.Recall),
            MacroF1 = perIntent.Count == 0 ? 0 : perIntent.Values.Average(value => value.F1),
            LogLoss = logLoss,
            ExpectedCalibrationError = ece,
            PerIntent = perIntent,
            ConfusionMatrix = new Phase4ConfusionMatrix { Classes = classes, Matrix = matrix },
            CoverageByThreshold = new[] { .15, .25, .35, .45, .55, .65 }.ToDictionary(
                value => value.ToString("F2", CultureInfo.InvariantCulture),
                value => Coverage(predictions, value)),
            Latency = latency
        };
    }

    private static double SelectThreshold(IReadOnlyList<IntentEvaluationResult> predictions)
    {
        var bestThreshold = .35;
        var best = double.MinValue;
        foreach (var threshold in new[] { .15, .20, .25, .30, .35, .40, .45, .50, .55, .60 })
        {
            var score = predictions.Count == 0 ? 0 : predictions.Average(item =>
                ApplyThreshold(item, threshold) == item.Label ? 1d : 0d);
            if (score > best)
            {
                best = score;
                bestThreshold = threshold;
            }
        }
        return bestThreshold;
    }

    private static string ApplyThreshold(IntentEvaluationResult prediction, double threshold)
    {
        var confidence = prediction.Score?.Length > 0 ? prediction.Score.Max() : 0;
        return confidence >= threshold ? prediction.PredictedLabel : AiChatIntentTypes.UnclearOrOutOfScope;
    }

    private static double ValidationMacroF1(
        IReadOnlyList<IntentEvaluationResult> predictions,
        IReadOnlyList<IntentDatasetRecord> expected,
        double threshold,
        IReadOnlyList<string> classes)
    {
        if (predictions.Count == 0) return 0;
        var perIntent = ComputePerIntent(predictions, expected.Select(record => new IntentInput { Text = record.Text, Label = record.Intent }).ToList(), threshold, classes);
        return perIntent.Count == 0 ? 0 : perIntent.Values.Average(item => item.F1);
    }

    private static Dictionary<string, Phase4ClassMetric> ComputePerIntent(
        IReadOnlyList<IntentEvaluationResult> predictions,
        IReadOnlyList<IntentInput> expected,
        double threshold,
        IReadOnlyList<string> classes)
    {
        var result = new Dictionary<string, Phase4ClassMetric>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < classes.Count; i++)
        {
            var label = classes[i];
            var tp = predictions.Select((prediction, index) => (prediction, index))
                .Count(pair => expected[pair.index].Label.Equals(label, StringComparison.OrdinalIgnoreCase) &&
                               ApplyThreshold(pair.prediction, threshold).Equals(label, StringComparison.OrdinalIgnoreCase));
            var fp = predictions.Select((prediction, index) => (prediction, index))
                .Count(pair => !expected[pair.index].Label.Equals(label, StringComparison.OrdinalIgnoreCase) &&
                               ApplyThreshold(pair.prediction, threshold).Equals(label, StringComparison.OrdinalIgnoreCase));
            var fn = predictions.Select((prediction, index) => (prediction, index))
                .Count(pair => expected[pair.index].Label.Equals(label, StringComparison.OrdinalIgnoreCase) &&
                               !ApplyThreshold(pair.prediction, threshold).Equals(label, StringComparison.OrdinalIgnoreCase));
            var precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            var recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            result[label] = new Phase4ClassMetric
            {
                Support = tp + fn,
                Precision = precision,
                Recall = recall,
                F1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall)
            };
        }
        return result;
    }

    private static List<List<int>> ComputeMatrix(
        IReadOnlyList<IntentEvaluationResult> predictions,
        IReadOnlyList<IntentInput> expected,
        double threshold,
        IReadOnlyList<string> classes)
    {
        return classes.Select(actual => classes.Select(predicted => predictions.Select((item, index) => (item, index))
            .Count(pair => expected[pair.index].Label.Equals(actual, StringComparison.OrdinalIgnoreCase) &&
                           ApplyThreshold(pair.item, threshold).Equals(predicted, StringComparison.OrdinalIgnoreCase))).ToList()).ToList();
    }

    private static double ProbabilityFor(IntentEvaluationResult prediction, string label, IReadOnlyList<string> scoreLabels)
    {
        if (prediction.Score is null || prediction.Score.Length == 0) return 0;
        var match = scoreLabels.Select((value, slot) => (value, slot))
            .FirstOrDefault(pair => pair.value.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(match.value) || match.slot >= prediction.Score.Length) return 0;
        return prediction.Score[match.slot];
    }

    private static double ComputeEce(IReadOnlyList<IntentEvaluationResult> predictions, IReadOnlyList<IntentInput> expected, double threshold)
    {
        if (predictions.Count == 0) return 0;
        return predictions.Select((prediction, index) =>
        {
            var confidence = prediction.Score?.Length > 0 ? prediction.Score.Max() : 0;
            var predicted = ApplyThreshold(prediction, threshold);
            return Math.Abs(confidence - (predicted.Equals(expected[index].Label, StringComparison.OrdinalIgnoreCase) ? 1 : 0));
        }).Average();
    }

    private static Phase4Latency MeasureLatency(ITransformer model, MLContext ml, IReadOnlyList<string> inputs)
    {
        var engine = ml.Model.CreatePredictionEngine<IntentInput, IntentOutput>(model);
        var values = new List<double>();
        foreach (var input in inputs.Take(10)) _ = engine.Predict(new IntentInput { Text = input });
        foreach (var input in inputs)
        {
            var timer = Stopwatch.StartNew();
            _ = engine.Predict(new IntentInput { Text = input });
            timer.Stop();
            values.Add(timer.Elapsed.TotalMilliseconds);
        }
        values.Sort();
        return new Phase4Latency
        {
            Iterations = values.Count,
            WarmupIterations = Math.Min(10, inputs.Count),
            P50Ms = Percentile(values, .50),
            P95Ms = Percentile(values, .95),
            MinMs = values.Count == 0 ? 0 : values[0],
            MaxMs = values.Count == 0 ? 0 : values[^1],
            AverageMs = values.Count == 0 ? 0 : values.Average(),
            Environment = $"{Environment.OSVersion}; {Environment.ProcessorCount} logical processors"
        };
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(values.Count * percentile) - 1, 0, values.Count - 1);
        return values[index];
    }

    private static double Coverage(IReadOnlyList<IntentEvaluationResult> predictions, double threshold) =>
        predictions.Count == 0 ? 0 : predictions.Count(item => item.Score?.Length > 0 && item.Score.Max() >= threshold) / (double)predictions.Count;

    private static Phase4Metric Metric(string name, int numerator, int denominator, IReadOnlyList<Phase4Failure> failures) => new()
    {
        Name = name,
        Numerator = numerator,
        Denominator = denominator,
        Rate = denominator == 0 ? 0 : (double)numerator / denominator,
        Status = failures.Count == 0 ? "pass" : "fail",
        FailureCount = failures.Count
    };

    private static Phase4IntentDetails ComputeSimpleIntentMetrics(
        IReadOnlyList<Phase4IntentPair> pairs,
        IReadOnlyList<string> classes)
    {
        var perIntent = new Dictionary<string, Phase4ClassMetric>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in classes)
        {
            var tp = pairs.Count(pair => pair.Expected.Equals(label, StringComparison.OrdinalIgnoreCase) && pair.Actual.Equals(label, StringComparison.OrdinalIgnoreCase));
            var fp = pairs.Count(pair => !pair.Expected.Equals(label, StringComparison.OrdinalIgnoreCase) && pair.Actual.Equals(label, StringComparison.OrdinalIgnoreCase));
            var fn = pairs.Count(pair => pair.Expected.Equals(label, StringComparison.OrdinalIgnoreCase) && !pair.Actual.Equals(label, StringComparison.OrdinalIgnoreCase));
            var precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            var recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            perIntent[label] = new Phase4ClassMetric
            {
                Support = tp + fn,
                Precision = precision,
                Recall = recall,
                F1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall)
            };
        }
        return new Phase4IntentDetails
        {
            MacroPrecision = perIntent.Count == 0 ? 0 : perIntent.Values.Average(metric => metric.Precision),
            MacroRecall = perIntent.Count == 0 ? 0 : perIntent.Values.Average(metric => metric.Recall),
            MacroF1 = perIntent.Count == 0 ? 0 : perIntent.Values.Average(metric => metric.F1),
            PerIntent = perIntent,
            ConfusionMatrix = new Phase4ConfusionMatrix
            {
                Classes = classes.ToList(),
                Matrix = classes.Select(expected => classes.Select(actual =>
                    pairs.Count(pair => pair.Expected.Equals(expected, StringComparison.OrdinalIgnoreCase) &&
                                        pair.Actual.Equals(actual, StringComparison.OrdinalIgnoreCase))).ToList()).ToList()
            }
        };
    }

    private static Phase4Metric NotEvaluated(string name, string detail) => new()
    {
        Name = name,
        Numerator = 0,
        Denominator = 0,
        Rate = null,
        Status = "not_evaluated_http_required",
        Detail = detail
    };

    private static string BuildSummary(Phase4BenchmarkReport report) =>
        $"Independent holdout={report.Dataset.TotalCases}; rule intent={report.IntentRuleBaseline.Status}; " +
        $"ML.NET={report.MlNet.Status}; safety={report.Safety.Status}; planner={report.PlannerRouting.Status}; " +
        $"liveGeminiExecuted={report.LiveGeminiExecuted}.";

    private static Phase4Failure Failure(Phase4BenchmarkCase item, string layer, string expected, string actual, string detail) => new()
    {
        CaseId = item.CaseId,
        Layer = layer,
        Expected = expected,
        Actual = actual,
        Detail = detail
    };

    private static Phase4ValidationReport InvalidValidation(string error) => new()
    {
        IsValid = false,
        Errors = new List<string> { error },
        DatasetVersion = Phase4DatasetFactory.Version,
        Seed = Seed
    };

    private static List<Phase4BenchmarkCase> LoadCases(string path) =>
        JsonSerializer.Deserialize<List<Phase4BenchmarkCase>>(File.ReadAllText(path), JsonOptions) ?? new();

    private static List<IntentDatasetRecord> LoadIntentDataset(string path)
    {
        if (!File.Exists(path))
        {
            var alternate = Path.Combine("src", "tools", "ClinicManagement.AI.Training", path);
            if (File.Exists(alternate)) path = alternate;
        }
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<List<IntentDatasetRecord>>(File.ReadAllText(path), JsonOptions) ?? new();
    }

    private static AiActorRole ParseRole(string? actor) =>
        Enum.TryParse<AiActorRole>(actor, true, out var role) ? role : AiActorRole.Patient;

    private static AiResolvedResourceContext ToResource(IReadOnlyDictionary<string, string>? context)
    {
        long? ReadLong(string key) => context is not null && context.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : null;
        return new AiResolvedResourceContext
        {
            AppointmentId = ReadLong("appointmentId"),
            DiagnosticOrderId = ReadLong("diagnosticOrderId"),
            PrescriptionId = ReadLong("prescriptionId"),
            ResourceVersion = context is not null && context.TryGetValue("resourceVersion", out var version) ? version : null,
            CurrentRoute = context is not null && context.TryGetValue("route", out var route) ? route : null
        };
    }

    private static bool IsRoleAllowed(AiToolDefinition definition, AiActorRole role) =>
        definition.AccessMode == AiToolAccessMode.Public || definition.AllowedRoles.Count == 0 || definition.AllowedRoles.Contains(role);

    private static string NormalizeForLeakage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return AiTextNormalizer.NormalizeForComparison(value).Trim();
    }

    private static HashSet<string> Tokenize(string? value) =>
        NormalizeForLeakage(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 && right.Count == 0) return 1;
        return left.Union(right).Count() == 0 ? 0 : left.Intersect(right).Count() / (double)left.Union(right).Count();
    }

    private static string Sha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static readonly HashSet<string> AllowedOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed", "clarification", "safety_blocked", "authorization_denied",
        "pending_confirmation", "stale_rejected", "replay_rejected", "fail_closed", "grounded_read"
    };

    private static readonly HashSet<string> AllowedSafety = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "emergency", "prompt_injection"
    };

    private static readonly HashSet<string> AllowedGrounding = new(StringComparer.OrdinalIgnoreCase)
    {
        "grounded", "not_applicable", "requires_persisted_resource", "must_not_invent"
    };
}

public sealed class Phase4BenchmarkCase
{
    public string CaseId { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string InputVi { get; set; } = string.Empty;
    public Dictionary<string, string> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ExpectedIntent { get; set; } = string.Empty;
    public string ExpectedOutcome { get; set; } = string.Empty;
    public string ExpectedPlannerTool { get; set; } = "none";
    public string[] AllowedTools { get; set; } = Array.Empty<string>();
    public string[] ForbiddenTools { get; set; } = Array.Empty<string>();
    public string Safety { get; set; } = "none";
    public string Grounding { get; set; } = "not_applicable";
    public string Rationale { get; set; } = string.Empty;
    public string Split { get; set; } = "holdout";
    public bool Synthetic { get; set; } = true;
}

public sealed class Phase4ValidationReport
{
    public bool IsValid { get; init; }
    public int TotalCases { get; init; }
    public Dictionary<string, int> ByActor { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> BySafety { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ByIntent { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Split { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int ExactTrainLeakage { get; init; }
    public int NearTrainLeakage { get; init; }
    public List<string> NearLeakageExamples { get; init; } = new();
    public int ExistingSplitExactLeakage { get; init; }
    public int ExistingSplitNearLeakage { get; init; }
    public List<string> ExistingSplitLeakageExamples { get; init; } = new();
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public string DatasetVersion { get; init; } = string.Empty;
    public int Seed { get; init; }
    public string DatasetSha256 { get; init; } = string.Empty;
}

public sealed class Phase4BenchmarkReport
{
    public string Benchmark { get; init; } = "ClinicCare AI Phase 4 independent benchmark";
    public string EvaluatorVersion { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string Head { get; init; } = string.Empty;
    public bool LiveGeminiExecuted { get; init; }
    public string EvaluationMode { get; init; } = "deterministic safety/rule/planner + offline ML.NET; HTTP/persistence layers explicitly not evaluated";
    public Phase4ValidationReport Dataset { get; init; } = new();
    public Phase4Metric Safety { get; set; } = new();
    public Phase4Metric IntentRuleBaseline { get; set; } = new();
    public Phase4Metric PlannerRouting { get; set; } = new();
    public Phase4Metric Grounding { get; set; } = new();
    public Phase4Metric Authorization { get; set; } = new();
    public Phase4Metric Outcome { get; set; } = new();
    public Phase4Metric MustNotContain { get; set; } = new();
    public Phase4MlNetReport MlNet { get; set; } = new();
    public List<Phase4Failure> Failures { get; init; } = new();
    public bool SelfTestPassed { get; set; }
    public string SelfTestDetail { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class Phase4Metric
{
    public string Name { get; init; } = string.Empty;
    public int Numerator { get; init; }
    public int Denominator { get; init; }
    public double? Rate { get; init; }
    public string Status { get; init; } = string.Empty;
    public int FailureCount { get; init; }
    public string? Detail { get; init; }
    public double? MacroPrecision { get; set; }
    public double? MacroRecall { get; set; }
    public double? MacroF1 { get; set; }
    public Dictionary<string, Phase4ClassMetric> PerIntent { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Phase4ConfusionMatrix ConfusionMatrix { get; set; } = new();
}

public sealed record Phase4IntentPair(string Expected, string Actual);

public sealed class Phase4IntentDetails
{
    public double MacroPrecision { get; init; }
    public double MacroRecall { get; init; }
    public double MacroF1 { get; init; }
    public Dictionary<string, Phase4ClassMetric> PerIntent { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Phase4ConfusionMatrix ConfusionMatrix { get; init; } = new();
}

public sealed class Phase4Failure
{
    public string CaseId { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class Phase4MlNetReport
{
    public string Status { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string ScoreSemantics { get; init; } = string.Empty;
    public int TrainSamples { get; init; }
    public int ValidationSamples { get; init; }
    public int HoldoutSamples { get; init; }
    public double LockedThreshold { get; init; }
    public double ValidationMacroF1AtThreshold { get; init; }
    public int Correct { get; init; }
    public int RawModelCorrect { get; init; }
    public double Accuracy { get; init; }
    public double MacroPrecision { get; init; }
    public double MacroRecall { get; init; }
    public double MacroF1 { get; init; }
    public double LogLoss { get; init; }
    public double ExpectedCalibrationError { get; init; }
    public Dictionary<string, Phase4ClassMetric> PerIntent { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Phase4ConfusionMatrix ConfusionMatrix { get; init; } = new();
    public Dictionary<string, double> CoverageByThreshold { get; init; } = new(StringComparer.Ordinal);
    public Phase4Latency Latency { get; init; } = new();
}

public sealed class Phase4ClassMetric
{
    public int Support { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1 { get; init; }
}

public sealed class Phase4ConfusionMatrix
{
    public List<string> Classes { get; init; } = new();
    public List<List<int>> Matrix { get; init; } = new();
}

public sealed class Phase4Latency
{
    public int Iterations { get; init; }
    public int WarmupIterations { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public double AverageMs { get; init; }
    public double P50Ms { get; init; }
    public double P95Ms { get; init; }
    public string Environment { get; init; } = string.Empty;
}
