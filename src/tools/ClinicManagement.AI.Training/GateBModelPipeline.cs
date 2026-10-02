using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;

namespace ClinicManagement.AI.Training;

/// <summary>
/// Reproducible Gate B pipeline.  It deliberately keeps candidate selection,
/// threshold fitting and frozen-blind scoring as separate steps.
/// </summary>
public static class GateBModelPipeline
{
    public const string PipelineVersion = "gateb-intent-pipeline-v1";
    public const int Seed = 42042;
    private const double NearDuplicateThreshold = .92;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly string[] CriticalIntents =
    {
        AiChatIntentTypes.StartBooking,
        AiChatIntentTypes.ViewAppointments,
        AiChatIntentTypes.CancelDraft,
        AiChatIntentTypes.ModifyDraft,
        AiChatIntentTypes.UnclearOrOutOfScope
    };

    public static GateBRunReport Run(
        string legacyPath,
        string additionsPath,
        string independentPath,
        string blindPath,
        string manifestPath,
        string productionModelPath,
        string productionMetadataPath,
        string sourceHead,
        bool workingTreeDirty,
        string? candidateOutputDirectory = null)
    {
        var legacy = LoadLegacy(legacyPath);
        var additions = Load(additionsPath);
        var registry = legacy.Concat(additions).ToList();
        var blindRaw = File.Exists(blindPath) ? File.ReadAllText(blindPath) : string.Empty;
        var manifestRaw = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : string.Empty;
        var independentRaw = File.Exists(independentPath) ? File.ReadAllText(independentPath) : string.Empty;
        var validation = ValidateRegistry(registry, blindRaw, manifestRaw);
        var report = new GateBRunReport
        {
            PipelineVersion = PipelineVersion,
            TimestampUtc = DateTimeOffset.UtcNow,
            SourceHeadAtGeneration = sourceHead,
            WorkingTreeDirty = workingTreeDirty,
            Seed = Seed,
            LegacyDatasetPath = legacyPath,
            AdditionsDatasetPath = additionsPath,
            IndependentDatasetPath = independentPath,
            BlindDatasetPath = blindPath,
            Registry = validation,
            RegistryHashSha256 = Sha256(JsonSerializer.Serialize(registry.OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList(), JsonOptions)),
            LiveGeminiExecuted = false,
            ModelModeAfterRun = "Shadow/Off; no production activation"
        };

        if (!validation.IsValid)
        {
            report.Status = "VALIDATION_FAILED";
            report.Promotion = PromotionRules.NotPromoted("Dataset, leakage, or frozen manifest validation failed.");
            return report;
        }

        var train = registry.Where(r => r.Approved && r.Split.Equals("train", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        var development = registry.Where(r => r.Approved && r.Split.Equals("development", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        var legacyTest = registry.Where(r => r.Approved && r.Split.Equals("legacy_test", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        var blind = LoadBlind(blindPath);
        var independent = ParseBlind(independentRaw, new List<string>());
        var baselineClasses = LoadLegacy(legacyPath).Where(r => r.Approved && r.Split.Equals("train", StringComparison.OrdinalIgnoreCase)).Select(r => r.Intent).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var comparableBlind = blind.Where(r => r.Actor.Equals("Patient", StringComparison.OrdinalIgnoreCase) && baselineClasses.Contains(r.ExpectedIntent)).OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        var comparableIndependent = independent.Where(r => r.Actor.Equals("Patient", StringComparison.OrdinalIgnoreCase) && baselineClasses.Contains(r.ExpectedIntent)).OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();

        report.Baseline = EvaluateProductionBaseline(productionModelPath, productionMetadataPath, development, legacyTest, comparableIndependent, comparableBlind, baselineClasses);
        var candidateSpecs = new[]
        {
            new GateBCandidateSpec("candidate_sdca_word", "SdcaMaximumEntropy", "default_word", "current SDCA baseline options; deterministic single-thread/no shuffle"),
            new GateBCandidateSpec("candidate_sdca_word_char", "SdcaMaximumEntropy", "word_char_ngrams", "word 1-2 grams plus character 3-5 grams; diacritics retained"),
            new GateBCandidateSpec("candidate_lbfgs_word_char", "LbfgsMaximumEntropy", "word_char_ngrams", "L-BFGS maximum entropy with the same word/character representation")
        };

        var outputDir = candidateOutputDirectory ?? Path.Combine(Path.GetTempPath(), "cliniccare-gateb-candidates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        foreach (var spec in candidateSpecs)
        {
            try
            {
                var candidate = TrainCandidate(spec, train, development, legacyTest, comparableIndependent, comparableBlind, outputDir, report.RegistryHashSha256);
                report.Candidates.Add(candidate);
            }
            catch (Exception ex)
            {
                report.Candidates.Add(new GateBCandidateReport
                {
                    Name = spec.Name,
                    Trainer = spec.Trainer,
                    FeatureSettings = spec.FeatureSettings,
                    Description = spec.Description,
                    Status = "failed",
                    Failure = ex.GetType().Name + ": " + ex.Message
                });
            }
        }

        var eligible = report.Candidates.Where(c => c.Status == "completed" && c.Deterministic).OrderByDescending(c => c.Development.MacroF1).ThenByDescending(c => c.Development.CriticalRecall).ThenByDescending(c => c.Development.SelectiveAccuracy).FirstOrDefault();
        report.SelectedCandidate = eligible?.Name;
        report.Promotion = eligible is null
            ? PromotionRules.NotPromoted("No deterministic candidate completed the development gate.")
            : PromotionRules.Decide(report.Baseline, eligible, validation, fullRegressionPassed: false, explicitPromotionRequested: false);
        report.Status = report.Promotion.Decision;
        report.SelectionUsesBlindHoldout = false;
        report.BlindEvaluationWasAfterSelection = eligible is not null;
        return report;
    }

    public static GateBDatasetValidationReport ValidateRegistry(
        IReadOnlyList<IntentDatasetRecord> records,
        string blindRaw,
        string manifestRaw)
    {
        var errors = new List<string>();
        var canonical = AiChatIntentTypes.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var accentFolded = new Dictionary<string, string>(StringComparer.Ordinal);
        var byFamily = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var bySemantic = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var label = record.EffectiveLabel;
            if (string.IsNullOrWhiteSpace(record.CaseId) || !ids.Add(record.CaseId)) errors.Add($"duplicate or empty caseId: {record.CaseId}");
            if (string.IsNullOrWhiteSpace(record.Text)) errors.Add($"{record.CaseId}: text is required");
            if (string.IsNullOrWhiteSpace(record.Intent) || string.IsNullOrWhiteSpace(record.Label) || !record.Intent.Equals(label, StringComparison.OrdinalIgnoreCase)) errors.Add($"{record.CaseId}: intent and label must both be present and agree");
            if (!canonical.Contains(label)) errors.Add($"{record.CaseId}: label is outside canonical taxonomy: {label}");
            if (!record.Actor.Equals("Patient", StringComparison.OrdinalIgnoreCase)) errors.Add($"{record.CaseId}: actor must be Patient for this classifier");
            if (!record.Language.Equals("vi", StringComparison.OrdinalIgnoreCase) && !record.Language.StartsWith("vi-", StringComparison.OrdinalIgnoreCase)) errors.Add($"{record.CaseId}: language must be vi");
            if (string.IsNullOrWhiteSpace(record.SourceType) || string.IsNullOrWhiteSpace(record.TemplateFamilyId) || string.IsNullOrWhiteSpace(record.SemanticGroupId)) errors.Add($"{record.CaseId}: provenance/group fields are required");
            if (!new[] { "train", "development", "legacy_test" }.Contains(record.Split, StringComparer.OrdinalIgnoreCase)) errors.Add($"{record.CaseId}: invalid split {record.Split}");

            var normalized = GateBTextNormalizer.Normalize(record.Text);
            var folded = GateBTextNormalizer.AccentFold(record.Text);
            if (!string.IsNullOrWhiteSpace(normalized) && !exact.TryAdd(normalized, record.CaseId)) errors.Add($"normalized duplicate: {record.CaseId} and {exact[normalized]}");
            if (!string.IsNullOrWhiteSpace(folded) && !accentFolded.TryAdd(folded, record.CaseId)) errors.Add($"accent-folded duplicate: {record.CaseId} and {accentFolded[folded]}");
            AddSplit(byFamily, record.TemplateFamilyId, record.Split);
            AddSplit(bySemantic, record.SemanticGroupId, record.Split);
        }

        foreach (var pair in byFamily.Where(pair => pair.Value.Count > 1)) errors.Add($"templateFamilyId crosses splits: {pair.Key}");
        foreach (var pair in bySemantic.Where(pair => pair.Value.Count > 1)) errors.Add($"semanticGroupId crosses splits: {pair.Key}");

        var ordered = records.OrderBy(r => r.CaseId, StringComparer.Ordinal).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var left = GateBTextNormalizer.Tokenize(ordered[i].Text);
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (ordered[i].TemplateFamilyId.Equals(ordered[j].TemplateFamilyId, StringComparison.OrdinalIgnoreCase)) continue;
                var similarity = GateBTextNormalizer.Jaccard(left, GateBTextNormalizer.Tokenize(ordered[j].Text));
                if (similarity >= NearDuplicateThreshold) errors.Add($"near duplicate {similarity:F3}: {ordered[i].CaseId} and {ordered[j].CaseId}");
            }
        }

        var blind = ParseBlind(blindRaw, errors);
        var manifest = ParseManifest(manifestRaw, errors);
        var blindHash = Sha256(blindRaw);
        if (manifest is not null && !manifest.HoldoutSha256.Equals(blindHash, StringComparison.OrdinalIgnoreCase)) errors.Add("frozen blind manifest checksum mismatch");
        if (manifest is not null && (!manifest.Frozen || !manifest.EvaluationOnly || manifest.ClassifierPlannerTunedAfterFreeze)) errors.Add("frozen blind manifest flags are invalid");
        if (blind.Count > 0)
        {
            var trainTexts = records.Where(r => r.Approved && r.Split.Equals("train", StringComparison.OrdinalIgnoreCase)).Select(r => (GateBTextNormalizer.Normalize(r.Text), GateBTextNormalizer.AccentFold(r.Text))).ToList();
            foreach (var item in blind)
            {
                var normalized = GateBTextNormalizer.Normalize(item.InputVi);
                var folded = GateBTextNormalizer.AccentFold(item.InputVi);
                if (trainTexts.Any(x => x.Item1 == normalized || x.Item2 == folded)) errors.Add($"blind leakage: {item.CaseId}");
                var nearest = trainTexts.Select(x => GateBTextNormalizer.Jaccard(GateBTextNormalizer.Tokenize(item.InputVi), GateBTextNormalizer.Tokenize(x.Item1))).DefaultIfEmpty(0).Max();
                if (nearest >= NearDuplicateThreshold) errors.Add($"near blind leakage {nearest:F3}: {item.CaseId}");
            }
        }

        return new GateBDatasetValidationReport
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            TotalRecords = records.Count,
            IndependentSemanticGroups = records.Select(r => r.SemanticGroupId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TemplateFamilies = records.Select(r => r.TemplateFamilyId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Split = records.GroupBy(r => r.Split, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            Labels = records.GroupBy(r => r.EffectiveLabel, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            SourceTypes = records.GroupBy(r => r.SourceType, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            ExactDuplicateCount = errors.Count(e => e.Contains("normalized duplicate", StringComparison.OrdinalIgnoreCase)),
            AccentFoldedDuplicateCount = errors.Count(e => e.Contains("accent-folded duplicate", StringComparison.OrdinalIgnoreCase)),
            NearDuplicateCount = errors.Count(e => e.StartsWith("near duplicate", StringComparison.OrdinalIgnoreCase)),
            TemplateFamilyLeakageCount = errors.Count(e => e.Contains("templateFamilyId crosses", StringComparison.OrdinalIgnoreCase)),
            SemanticGroupLeakageCount = errors.Count(e => e.Contains("semanticGroupId crosses", StringComparison.OrdinalIgnoreCase)),
            BlindLeakageCount = errors.Count(e => e.Contains("blind leakage", StringComparison.OrdinalIgnoreCase) || e.Contains("near blind leakage", StringComparison.OrdinalIgnoreCase)),
            SplitPolicy = "Deterministic preassigned grouped split: templateFamilyId and semanticGroupId are exclusive to one split; legacy val/test are renamed development/legacy_test."
        };
    }

    public static IReadOnlyList<string> DeterministicGroupSplit(IReadOnlyList<IntentDatasetRecord> records, int seed = Seed)
    {
        // This is a deterministic assignment oracle used by tests and reports.
        // The checked-in registry keeps explicit splits so a reviewer can audit
        // them; no random per-row split is used by training.
        return records.GroupBy(r => r.TemplateFamilyId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => StableHash(g.Key, seed), StringComparer.Ordinal)
            .Select(g => $"{g.Key}:{g.First().Split}")
            .ToList();
    }

    private static GateBCandidateReport TrainCandidate(
        GateBCandidateSpec spec,
        IReadOnlyList<IntentDatasetRecord> trainRecords,
        IReadOnlyList<IntentDatasetRecord> developmentRecords,
        IReadOnlyList<IntentDatasetRecord> legacyTestRecords,
        IReadOnlyList<Phase4BenchmarkCase> independentRecords,
        IReadOnlyList<Phase4BenchmarkCase> blindRecords,
        string outputDirectory,
        string registryHash)
    {
        var stopwatch = Stopwatch.StartNew();
        var ml = new MLContext(Seed);
        var trainInputs = trainRecords.Select(r => new IntentInput { Text = r.Text, Label = r.EffectiveLabel }).OrderBy(r => r.Text, StringComparer.Ordinal).ThenBy(r => r.Label, StringComparer.Ordinal).ToList();
        var developmentInputs = developmentRecords.Select(r => new IntentInput { Text = r.Text, Label = r.EffectiveLabel }).ToList();
        var legacyTestInputs = legacyTestRecords.Select(r => new IntentInput { Text = r.Text, Label = r.EffectiveLabel }).ToList();
        var trainView = ml.Data.LoadFromEnumerable(trainInputs);
        var pipeline = BuildPipeline(ml, spec);
        var model = pipeline.Fit(trainView);
        var modelPath = Path.Combine(outputDirectory, spec.Name + ".zip");
        ml.Model.Save(model, trainView.Schema, modelPath);
        var artifactHash = Sha256File(modelPath);
        var metadataPath = Path.Combine(outputDirectory, spec.Name + ".metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(new
        {
            modelVersion = "gateb-candidate-v1",
            pipelineVersion = PipelineVersion,
            datasetHashSha256 = registryHash,
            trainer = spec.Trainer,
            featureSettings = spec.FeatureSettings,
            seed = Seed,
            artifactSha256 = artifactHash
        }, JsonOptions));
        var classes = GetScoreLabels(model.Transform(trainView));
        var development = Score(ml, model, developmentInputs, classes, SelectPolicy: true);
        var policy = SelectPolicy(development.RawPredictions, developmentInputs, classes);
        development = Score(ml, model, developmentInputs, classes, policy);
        var legacyTest = Score(ml, model, legacyTestInputs, classes, policy);
        var blindInputs = blindRecords.Select(r => new IntentInput { Text = r.InputVi, Label = r.ExpectedIntent }).ToList();
        var blind = Score(ml, model, blindInputs, classes, policy);
        var independentInputs = independentRecords.Select(r => new IntentInput { Text = r.InputVi, Label = r.ExpectedIntent }).ToList();
        var independent = Score(ml, model, independentInputs, classes, policy);

        var repeatMl = new MLContext(Seed);
        var repeatModel = BuildPipeline(repeatMl, spec).Fit(repeatMl.Data.LoadFromEnumerable(trainInputs));
        var repeatPredictions = repeatMl.Data.CreateEnumerable<IntentEvaluationResult>(repeatModel.Transform(repeatMl.Data.LoadFromEnumerable(developmentInputs)), false).ToList();
        var deterministic = development.RawPredictions.Count == repeatPredictions.Count && development.RawPredictions.Zip(repeatPredictions).All(pair => pair.First.PredictedLabel == pair.Second.PredictedLabel && ScoresClose(pair.First.Score, pair.Second.Score));
        stopwatch.Stop();
        return new GateBCandidateReport
        {
            Name = spec.Name,
            Trainer = spec.Trainer,
            FeatureSettings = spec.FeatureSettings,
            Description = spec.Description,
            Status = "completed",
            Seed = Seed,
            TrainSamples = trainRecords.Count,
            DevelopmentSamples = developmentRecords.Count,
            LegacyTestSamples = legacyTestRecords.Count,
            TrainingMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            ArtifactPath = modelPath,
            ArtifactSha256 = artifactHash,
            MetadataPath = metadataPath,
            MetadataSha256 = Sha256File(metadataPath),
            ArtifactMetadataHashMatch = JsonDocument.Parse(File.ReadAllText(metadataPath)).RootElement.GetProperty("artifactSha256").GetString() == artifactHash,
            ArtifactBytes = new FileInfo(modelPath).Length,
            Deterministic = deterministic,
            Development = development.Metrics,
            LegacyTest = legacyTest.Metrics,
            IndependentHoldout = independent.Metrics,
            FrozenBlind = blind.Metrics,
            Threshold = policy.Threshold,
            Margin = policy.Margin,
            AbstentionPolicy = "abstain when top1Score < threshold OR (top1Score - top2Score) < margin; abstention label is UnclearOrOutOfScope",
            ScoreSemantics = "Sdca/L-BFGS MaximumEntropy score vector treated as probability-like only for log-loss/ECE after finite/nonnegative/sum validation; top1 score and margin remain explicit model scores."
        };
    }

    private static IEstimator<ITransformer> BuildPipeline(MLContext ml, GateBCandidateSpec spec)
    {
        IEstimator<ITransformer> features = spec.FeatureSettings == "default_word"
            ? ml.Transforms.Text.FeaturizeText("Features", nameof(IntentInput.Text))
            : ml.Transforms.Text.FeaturizeText("Features", new TextFeaturizingEstimator.Options
            {
                KeepDiacritics = true,
                KeepPunctuations = false,
                WordFeatureExtractor = new WordBagEstimator.Options { NgramLength = 2, UseAllLengths = true },
                CharFeatureExtractor = new WordBagEstimator.Options { NgramLength = 5, UseAllLengths = true }
            }, nameof(IntentInput.Text));

        IEstimator<ITransformer> pipeline = features.Append(ml.Transforms.Conversion.MapValueToKey("KeyLabel", nameof(IntentInput.Label)));
        if (spec.Trainer == "LbfgsMaximumEntropy")
            pipeline = pipeline.Append(ml.MulticlassClassification.Trainers.LbfgsMaximumEntropy("KeyLabel", "Features"));
        else
            pipeline = pipeline.Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy(new Microsoft.ML.Trainers.SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "KeyLabel",
                FeatureColumnName = "Features",
                NumberOfThreads = 1,
                Shuffle = false
            }));
        return pipeline.Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
    }

    private static GateBMetricEvaluation Score(MLContext ml, ITransformer model, IReadOnlyList<IntentInput> inputs, IReadOnlyList<string> classes, bool SelectPolicy)
    {
        return Score(ml, model, inputs, classes, GateBDecisionPolicy.Default);
    }

    private static GateBMetricEvaluation Score(MLContext ml, ITransformer model, IReadOnlyList<IntentInput> inputs, IReadOnlyList<string> classes, GateBDecisionPolicy policy)
    {
        var data = ml.Data.LoadFromEnumerable(inputs);
        var transformed = model.Transform(data);
        var predictions = ml.Data.CreateEnumerable<IntentEvaluationResult>(transformed, false).ToList();
        return new GateBMetricEvaluation { RawPredictions = predictions, Metrics = ComputeMetrics(predictions, inputs, classes, policy) };
    }

    private static GateBDecisionPolicy SelectPolicy(IReadOnlyList<IntentEvaluationResult> predictions, IReadOnlyList<IntentInput> expected, IReadOnlyList<string> classes)
    {
        var best = GateBDecisionPolicy.Default;
        var bestObjective = double.MinValue;
        foreach (var threshold in new[] { .15, .20, .25, .30, .35, .40, .45, .50, .55, .60 })
        foreach (var margin in new[] { .00, .03, .05, .08, .12, .18 })
        {
            var metrics = ComputeMetrics(predictions, expected, classes, new GateBDecisionPolicy(threshold, margin));
            var objective = metrics.MacroF1 + metrics.CriticalRecall * .20 + metrics.SelectiveAccuracy * .05 - metrics.AbstentionRate * .03;
            if (objective > bestObjective || (Math.Abs(objective - bestObjective) < 1e-12 && threshold < best.Threshold))
            {
                bestObjective = objective;
                best = new GateBDecisionPolicy(threshold, margin);
            }
        }
        return best;
    }

    private static GateBMetricReport ComputeMetrics(IReadOnlyList<IntentEvaluationResult> predictions, IReadOnlyList<IntentInput> expected, IReadOnlyList<string> classes, GateBDecisionPolicy policy)
    {
        var decided = predictions.Select((p, i) => Decide(p, classes, policy)).ToList();
        var labels = classes.Concat(new[] { AiChatIntentTypes.UnclearOrOutOfScope }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var perClass = new Dictionary<string, GateBClassMetric>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var tp = expected.Select((e, i) => (e, i)).Count(x => x.e.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && decided[x.i].Equals(label, StringComparison.OrdinalIgnoreCase));
            var fp = expected.Select((e, i) => (e, i)).Count(x => !x.e.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && decided[x.i].Equals(label, StringComparison.OrdinalIgnoreCase));
            var fn = expected.Select((e, i) => (e, i)).Count(x => x.e.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && !decided[x.i].Equals(label, StringComparison.OrdinalIgnoreCase));
            var precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            var recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            perClass[label] = new GateBClassMetric { Support = tp + fn, Precision = precision, Recall = recall, F1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall) };
        }
        var scoredCount = expected.Count(x => !string.IsNullOrWhiteSpace(x.Label));
        var correct = expected.Select((e, i) => decided[i].Equals(e.Label, StringComparison.OrdinalIgnoreCase)).Count(x => x);
        var covered = decided.Count(x => !x.Equals(AiChatIntentTypes.UnclearOrOutOfScope, StringComparison.OrdinalIgnoreCase));
        var critical = CriticalIntents.Where(label => perClass.ContainsKey(label) && perClass[label].Support > 0).Select(label => perClass[label].Recall).DefaultIfEmpty(0).Average();
        var supported = perClass.Values.Where(value => value.Support > 0).ToList();
        var validProbability = predictions.All(p => p.Score is { Length: > 0 } && p.Score.All(x => !float.IsNaN(x) && !float.IsInfinity(x) && x >= 0) && Math.Abs(p.Score.Sum() - 1f) <= .05f);
        double? logLoss = validProbability && predictions.Count > 0 ? -predictions.Select((p, i) => Math.Log(Math.Max(ProbabilityFor(p, expected[i].Label, classes), 1e-7))).Average() : null;
        double? ece = validProbability && predictions.Count > 0 ? predictions.Select((p, i) => Math.Abs(Top1(p) - (decided[i].Equals(expected[i].Label, StringComparison.OrdinalIgnoreCase) ? 1 : 0))).Average() : null;
        return new GateBMetricReport
        {
                Samples = scoredCount,
                Correct = correct,
                Threshold = policy.Threshold,
                Margin = policy.Margin,
                Accuracy = scoredCount == 0 ? 0 : (double)correct / scoredCount,
                MacroPrecision = supported.Count == 0 ? 0 : supported.Average(x => x.Precision),
                MacroRecall = supported.Count == 0 ? 0 : supported.Average(x => x.Recall),
                MacroF1 = supported.Count == 0 ? 0 : supported.Average(x => x.F1),
                EvaluatedClassCount = supported.Count,
                ExcludedZeroSupportLabels = labels.Where(label => perClass[label].Support == 0).OrderBy(label => label, StringComparer.Ordinal).ToList(),
                CriticalRecall = critical,
                Coverage = scoredCount == 0 ? 0 : (double)covered / scoredCount,
                AbstentionRate = scoredCount == 0 ? 0 : (double)(scoredCount - covered) / scoredCount,
                SelectiveAccuracy = covered == 0 ? 0 : expected.Select((e, i) => (e, i)).Where(x => !decided[x.i].Equals(AiChatIntentTypes.UnclearOrOutOfScope, StringComparison.OrdinalIgnoreCase)).Count(x => decided[x.i].Equals(x.e.Label, StringComparison.OrdinalIgnoreCase)) / (double)covered,
                LogLoss = logLoss,
                ExpectedCalibrationError = ece,
                PerClass = perClass,
                ConfusionMatrix = BuildMatrix(expected, decided, labels),
                Top1ScoreAverage = predictions.Count == 0 ? 0 : predictions.Average(Top1),
                MarginAverage = predictions.Count == 0 ? 0 : predictions.Average(Margin)
        };
    }

    private static GateBBaselineReport EvaluateProductionBaseline(string modelPath, string metadataPath, IReadOnlyList<IntentDatasetRecord> development, IReadOnlyList<IntentDatasetRecord> legacyTest, IReadOnlyList<Phase4BenchmarkCase> independent, IReadOnlyList<Phase4BenchmarkCase> blind, IReadOnlySet<string> classes)
    {
        if (!File.Exists(modelPath)) return new GateBBaselineReport { Status = "missing_artifact" };
        var ml = new MLContext(Seed);
        var model = ml.Model.Load(modelPath, out _);
        var labels = classes.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var threshold = .15;
        if (File.Exists(metadataPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (document.RootElement.TryGetProperty("OptimalConfidenceThreshold", out var value) && value.TryGetDouble(out var parsed)) threshold = parsed;
            }
            catch { }
        }
        var policy = new GateBDecisionPolicy(threshold, 0);
        var developmentResult = Score(ml, model, development.Where(r => classes.Contains(r.EffectiveLabel)).Select(r => new IntentInput { Text = r.Text, Label = r.EffectiveLabel }).ToList(), labels, policy).Metrics;
        var testResult = Score(ml, model, legacyTest.Where(r => classes.Contains(r.EffectiveLabel)).Select(r => new IntentInput { Text = r.Text, Label = r.EffectiveLabel }).ToList(), labels, policy).Metrics;
        var independentResult = Score(ml, model, independent.Select(r => new IntentInput { Text = r.InputVi, Label = r.ExpectedIntent }).ToList(), labels, policy).Metrics;
        var blindResult = Score(ml, model, blind.Select(r => new IntentInput { Text = r.InputVi, Label = r.ExpectedIntent }).ToList(), labels, policy).Metrics;
        var artifactHash = Sha256File(modelPath);
        var metadataHash = File.Exists(metadataPath) ? Sha256File(metadataPath) : string.Empty;
        var artifactMetadataHashMatch = false;
        if (File.Exists(metadataPath))
        {
            try
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                artifactMetadataHashMatch = string.Equals(
                    metadata.RootElement.TryGetProperty("ArtifactSha256", out var value) ? value.GetString() : null,
                    artifactHash,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                artifactMetadataHashMatch = false;
            }
        }

        return new GateBBaselineReport
        {
            Status = "completed",
            Threshold = threshold,
            Development = developmentResult,
            LegacyTest = testResult,
            IndependentHoldout = independentResult,
            FrozenBlind = blindResult,
            ArtifactSha256 = artifactHash,
            MetadataSha256 = metadataHash,
            ArtifactMetadataHashMatch = artifactMetadataHashMatch,
            ArtifactBytes = new FileInfo(modelPath).Length
        };
    }

    private static List<IntentDatasetRecord> LoadLegacy(string path)
    {
        var records = Load(path);
        foreach (var record in records)
        {
            record.Label = record.Intent;
            record.Actor = "Patient";
            record.Language = "vi";
            record.SourceType = "legacy_curated";
            record.TemplateFamilyId = "legacy-family-" + record.CaseId;
            record.SemanticGroupId = "legacy-semantic-" + record.CaseId;
            record.IsSynthetic = true;
            record.Notes = "Legacy curated record; one authored case is treated as one audited group, not a generated-template variant.";
            record.Split = record.Split.Equals("val", StringComparison.OrdinalIgnoreCase) ? "development" : record.Split.Equals("test", StringComparison.OrdinalIgnoreCase) ? "legacy_test" : "train";
        }
        return records;
    }

    private static List<IntentDatasetRecord> Load(string path) => JsonSerializer.Deserialize<List<IntentDatasetRecord>>(File.ReadAllText(path), JsonOptions) ?? new();

    private static List<Phase4BenchmarkCase> LoadBlind(string path) => ParseBlind(File.ReadAllText(path), new List<string>());

    private static List<Phase4BenchmarkCase> ParseBlind(string raw, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) { errors.Add("frozen blind dataset is missing"); return new(); }
        try { return JsonSerializer.Deserialize<List<Phase4BenchmarkCase>>(raw, JsonOptions) ?? new(); }
        catch (Exception ex) { errors.Add("frozen blind dataset JSON is invalid: " + ex.Message); return new(); }
    }

    private static Phase5HoldoutManifest? ParseManifest(string raw, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) { errors.Add("frozen blind manifest is missing"); return null; }
        try { return JsonSerializer.Deserialize<Phase5HoldoutManifest>(raw, JsonOptions); }
        catch (Exception ex) { errors.Add("frozen blind manifest JSON is invalid: " + ex.Message); return null; }
    }

    private static void AddSplit(Dictionary<string, HashSet<string>> map, string key, string split)
    {
        if (!map.TryGetValue(key, out var values)) map[key] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        values.Add(split);
    }

    private static IReadOnlyList<string> GetScoreLabels(IDataView view)
    {
        VBuffer<ReadOnlyMemory<char>> slots = default;
        view.Schema["Score"].Annotations.GetValue("SlotNames", ref slots);
        return slots.DenseValues().Select(x => x.ToString()).ToArray();
    }

    private static string Decide(IntentEvaluationResult prediction, IReadOnlyList<string> classes, GateBDecisionPolicy policy) => Top1(prediction) >= policy.Threshold && Margin(prediction) >= policy.Margin ? prediction.PredictedLabel : AiChatIntentTypes.UnclearOrOutOfScope;
    private static float Top1(IntentEvaluationResult prediction) => prediction.Score is { Length: > 0 } ? prediction.Score.Max() : 0;
    private static float Margin(IntentEvaluationResult prediction)
    {
        if (prediction.Score is not { Length: > 1 }) return 0;
        var scores = prediction.Score.OrderByDescending(x => x).ToArray();
        return scores[0] - scores[1];
    }
    private static double ProbabilityFor(IntentEvaluationResult prediction, string label, IReadOnlyList<string> labels)
    {
        var index = labels.Select((value, index) => (value, index)).FirstOrDefault(x => x.value.Equals(label, StringComparison.OrdinalIgnoreCase)).index;
        return prediction.Score is not null && index < prediction.Score.Length ? prediction.Score[index] : 0;
    }
    private static bool ScoresClose(float[]? left, float[]? right) => left is not null && right is not null && left.Length == right.Length && left.Zip(right).All(x => Math.Abs(x.First - x.Second) <= 1e-5f);
    private static List<List<int>> BuildMatrix(IReadOnlyList<IntentInput> expected, IReadOnlyList<string> predictions, IReadOnlyList<string> labels) => labels.Select(actual => labels.Select(predicted => expected.Select((e, i) => (e, i)).Count(x => x.e.Label.Equals(actual, StringComparison.OrdinalIgnoreCase) && predictions[x.i].Equals(predicted, StringComparison.OrdinalIgnoreCase))).ToList()).ToList();
    private static string StableHash(string value, int seed) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(CultureInfo.InvariantCulture) + ":" + value))).ToLowerInvariant();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Sha256File(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record GateBCandidateSpec(string Name, string Trainer, string FeatureSettings, string Description);
    private sealed class GateBMetricEvaluation { public List<IntentEvaluationResult> RawPredictions { get; init; } = new(); public GateBMetricReport Metrics { get; init; } = new(); }
}

public static class GateBTextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var value = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        value = Regex.Replace(value, "[\\p{P}\\p{S}]", " ");
        return Regex.Replace(value, "\\s+", " ").Trim();
    }
    public static string AccentFold(string? text)
    {
        var normalized = Normalize(text).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }
    public static HashSet<string> Tokenize(string? text) => Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    public static double Jaccard(HashSet<string> left, HashSet<string> right) => left.Count == 0 && right.Count == 0 ? 1 : left.Union(right).Count() == 0 ? 0 : left.Intersect(right).Count() / (double)left.Union(right).Count();
}

public static class PromotionRules
{
    public static GateBPromotionDecision NotPromoted(string reason) => new() { Decision = "NOT_PROMOTED", Reason = reason };

    public static GateBPromotionDecision Decide(GateBBaselineReport baseline, GateBCandidateReport candidate, GateBDatasetValidationReport registry, bool fullRegressionPassed, bool explicitPromotionRequested)
    {
        var checks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["datasetValidation"] = registry.IsValid,
            ["leakageZero"] = registry.ExactDuplicateCount == 0 && registry.AccentFoldedDuplicateCount == 0 && registry.NearDuplicateCount == 0 && registry.TemplateFamilyLeakageCount == 0 && registry.SemanticGroupLeakageCount == 0 && registry.BlindLeakageCount == 0,
            ["determinism"] = candidate.Deterministic,
            ["meaningfulDevelopmentMacroF1Gain"] = candidate.Development.MacroF1 >= baseline.Development.MacroF1 + .03,
            ["criticalRecallNoSevereRegression"] = candidate.Development.CriticalRecall >= baseline.Development.CriticalRecall - .05,
            ["calibrationAndAbstentionWithinPolicy"] = (candidate.Development.ExpectedCalibrationError ?? double.MaxValue) <= (baseline.Development.ExpectedCalibrationError ?? double.MaxValue) + .05 && candidate.Development.AbstentionRate <= .70,
            ["independentMacroF1NoSevereRegression"] = candidate.IndependentHoldout.MacroF1 >= baseline.IndependentHoldout.MacroF1 - .02,
            ["independentCriticalRecallNoSevereRegression"] = candidate.IndependentHoldout.CriticalRecall >= baseline.IndependentHoldout.CriticalRecall - .05,
            ["artifactMetadataHash"] = !string.IsNullOrWhiteSpace(candidate.ArtifactSha256) && candidate.ArtifactMetadataHashMatch,
            ["fullRegression"] = fullRegressionPassed,
            ["explicitPromotionRequested"] = explicitPromotionRequested
        };
        var passed = checks.Values.All(x => x);
        return new GateBPromotionDecision
        {
            Decision = passed ? "PROMOTED_TO_SHADOW" : "NOT_PROMOTED",
            Reason = passed ? "Candidate passed the fail-closed policy and is eligible for Shadow only." : "One or more promotion checks failed; production artifact and mode remain unchanged.",
            Checks = checks
        };
    }
}

public sealed class GateBRunReport
{
    public string PipelineVersion { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string SourceHeadAtGeneration { get; init; } = string.Empty;
    public bool WorkingTreeDirty { get; init; }
    public int Seed { get; init; }
    public string LegacyDatasetPath { get; init; } = string.Empty;
    public string AdditionsDatasetPath { get; init; } = string.Empty;
    public string IndependentDatasetPath { get; init; } = string.Empty;
    public string RegistryHashSha256 { get; init; } = string.Empty;
    public string BlindDatasetPath { get; init; } = string.Empty;
    public bool LiveGeminiExecuted { get; init; }
    public string ModelModeAfterRun { get; init; } = string.Empty;
    public string Status { get; set; } = "NOT_RUN";
    public GateBDatasetValidationReport Registry { get; init; } = new();
    public GateBBaselineReport Baseline { get; set; } = new();
    public List<GateBCandidateReport> Candidates { get; init; } = new();
    public string? SelectedCandidate { get; set; }
    public bool SelectionUsesBlindHoldout { get; set; }
    public bool BlindEvaluationWasAfterSelection { get; set; }
    public GateBPromotionDecision Promotion { get; set; } = new();
}

public sealed class GateBDatasetValidationReport
{
    public bool IsValid { get; init; }
    public int TotalRecords { get; init; }
    public int IndependentSemanticGroups { get; init; }
    public int TemplateFamilies { get; init; }
    public Dictionary<string, int> Split { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SourceTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int ExactDuplicateCount { get; init; }
    public int AccentFoldedDuplicateCount { get; init; }
    public int NearDuplicateCount { get; init; }
    public int TemplateFamilyLeakageCount { get; init; }
    public int SemanticGroupLeakageCount { get; init; }
    public int BlindLeakageCount { get; init; }
    public string SplitPolicy { get; init; } = string.Empty;
    public List<string> Errors { get; init; } = new();
}

public sealed class GateBCandidateReport
{
    public string Name { get; init; } = string.Empty;
    public string Trainer { get; init; } = string.Empty;
    public string FeatureSettings { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Failure { get; init; }
    public int Seed { get; init; }
    public int TrainSamples { get; init; }
    public int DevelopmentSamples { get; init; }
    public int LegacyTestSamples { get; init; }
    public double TrainingMilliseconds { get; init; }
    public string ArtifactPath { get; init; } = string.Empty;
    public string ArtifactSha256 { get; init; } = string.Empty;
    public string MetadataPath { get; init; } = string.Empty;
    public string MetadataSha256 { get; init; } = string.Empty;
    public bool ArtifactMetadataHashMatch { get; init; }
    public long ArtifactBytes { get; init; }
    public bool Deterministic { get; init; }
    public double Threshold { get; init; }
    public double Margin { get; init; }
    public string AbstentionPolicy { get; init; } = string.Empty;
    public string ScoreSemantics { get; init; } = string.Empty;
    public GateBMetricReport Development { get; init; } = new();
    public GateBMetricReport LegacyTest { get; init; } = new();
    public GateBMetricReport IndependentHoldout { get; init; } = new();
    public GateBMetricReport FrozenBlind { get; init; } = new();
}

public sealed class GateBBaselineReport
{
    public string Status { get; init; } = "completed";
    public double Threshold { get; init; }
    public string ArtifactSha256 { get; init; } = string.Empty;
    public string MetadataSha256 { get; init; } = string.Empty;
    public bool ArtifactMetadataHashMatch { get; init; }
    public long ArtifactBytes { get; init; }
    public GateBMetricReport Development { get; init; } = new();
    public GateBMetricReport LegacyTest { get; init; } = new();
    public GateBMetricReport IndependentHoldout { get; init; } = new();
    public GateBMetricReport FrozenBlind { get; init; } = new();
}

public sealed class GateBMetricReport
{
    public string Status { get; init; } = "completed";
    public int Samples { get; init; }
    public int Correct { get; init; }
    public double Accuracy { get; init; }
    public double MacroPrecision { get; init; }
    public double MacroRecall { get; init; }
    public double MacroF1 { get; init; }
    public double CriticalRecall { get; init; }
    public double Coverage { get; init; }
    public double AbstentionRate { get; init; }
    public double SelectiveAccuracy { get; init; }
    public double? LogLoss { get; init; }
    public double? ExpectedCalibrationError { get; init; }
    public double Top1ScoreAverage { get; init; }
    public double MarginAverage { get; init; }
    public double Threshold { get; init; }
    public double Margin { get; init; }
    public int EvaluatedClassCount { get; init; }
    public List<string> ExcludedZeroSupportLabels { get; init; } = new();
    public string ArtifactSha256 { get; init; } = string.Empty;
    public long ArtifactBytes { get; init; }
    public Dictionary<string, GateBClassMetric> PerClass { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<List<int>> ConfusionMatrix { get; init; } = new();
}

public sealed class GateBClassMetric
{
    public int Support { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1 { get; init; }
}

public sealed class GateBPromotionDecision
{
    public string Decision { get; init; } = "NOT_PROMOTED";
    public string Reason { get; init; } = string.Empty;
    public Dictionary<string, bool> Checks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record GateBDecisionPolicy(double Threshold, double Margin)
{
    public static GateBDecisionPolicy Default { get; } = new(.35, .05);
}
