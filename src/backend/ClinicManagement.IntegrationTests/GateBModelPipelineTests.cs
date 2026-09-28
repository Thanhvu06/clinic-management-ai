using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.AI.Training;
using ClinicManagement.Infrastructure.AI;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class GateBModelPipelineTests
{
    [Fact]
    public void DuplicateCaseIdIsRejected()
    {
        var records = new[] { Record("same", "xin chào", "Greeting"), Record("same", "chào phòng khám", "Greeting") };
        var report = GateBModelPipeline.ValidateRegistry(records, EmptyBlind, EmptyManifest);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("duplicate or empty caseId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalizedAndAccentFoldedDuplicatesAreRejectedAcrossSplits()
    {
        var records = new[]
        {
            Record("one", "Đặt lịch khám", "StartBooking", split: "train"),
            Record("two", "đặt   lịch khám!", "StartBooking", split: "development"),
            Record("three", "dat lich kham", "StartBooking", split: "legacy_test")
        };
        var report = GateBModelPipeline.ValidateRegistry(records, EmptyBlind, EmptyManifest);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("accent-folded duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Errors, error => error.Contains("normalized duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TemplateFamilyAndSemanticGroupLeakageAreRejected()
    {
        var first = Record("one", "xem lịch đã đặt", "ViewAppointments", split: "train");
        var second = Record("two", "mở danh sách lịch cũ", "ViewAppointments", split: "development");
        second.TemplateFamilyId = first.TemplateFamilyId;
        second.SemanticGroupId = first.SemanticGroupId;
        var report = GateBModelPipeline.ValidateRegistry(new[] { first, second }, EmptyBlind, EmptyManifest);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("templateFamilyId crosses", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Errors, error => error.Contains("semanticGroupId crosses", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidActorAndLabelAreRejected()
    {
        var record = Record("bad", "some text", "NotAnIntent");
        record.Actor = "Doctor";
        var report = GateBModelPipeline.ValidateRegistry(new[] { record }, EmptyBlind, EmptyManifest);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("outside canonical taxonomy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Errors, error => error.Contains("actor must be Patient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FrozenManifestChecksumMismatchIsRejected()
    {
        var manifest = EmptyManifest.Replace(Sha256(EmptyBlind), new string('0', 64), StringComparison.OrdinalIgnoreCase);
        var report = GateBModelPipeline.ValidateRegistry(new[] { Record("one", "xin chào", "Greeting") }, EmptyBlind, manifest);
        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, error => error.Contains("checksum mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroupSplitOracleIsDeterministic()
    {
        var records = new[] { Record("b", "một câu", "Greeting"), Record("a", "câu khác", "Greeting") };
        var first = GateBModelPipeline.DeterministicGroupSplit(records, 42);
        var second = GateBModelPipeline.DeterministicGroupSplit(records, 42);
        Assert.Equal(first, second);
    }

    [Fact]
    public void PromotionFailsClosedForCriticalRegressionAndMissingFullRegression()
    {
        var registry = new GateBDatasetValidationReport
        {
            IsValid = true,
            ExactDuplicateCount = 0,
            AccentFoldedDuplicateCount = 0,
            NearDuplicateCount = 0,
            TemplateFamilyLeakageCount = 0,
            SemanticGroupLeakageCount = 0,
            BlindLeakageCount = 0
        };
        var baseline = new GateBBaselineReport
        {
            Development = new GateBMetricReport { MacroF1 = .50, CriticalRecall = .90, ExpectedCalibrationError = .10 }
        };
        var candidate = new GateBCandidateReport
        {
            Name = "bad-candidate",
            Deterministic = true,
            ArtifactSha256 = new string('a', 64),
            Development = new GateBMetricReport { MacroF1 = .60, CriticalRecall = .70, ExpectedCalibrationError = .10, AbstentionRate = .20 }
        };
        var decision = PromotionRules.Decide(baseline, candidate, registry, fullRegressionPassed: false, explicitPromotionRequested: false);
        Assert.Equal("NOT_PROMOTED", decision.Decision);
        Assert.False(decision.Checks["criticalRecallNoSevereRegression"]);
        Assert.False(decision.Checks["fullRegression"]);
        Assert.False(decision.Checks["explicitPromotionRequested"]);
    }

    [Fact]
    public void CheckedInCuratedAdditionsHaveRequiredRegistryFieldsAndNoBlindLeakage()
    {
        var path = RepoFile(Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "gateb_intent_registry_additions.json"));
        var blindPath = RepoFile(Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase5_blind_holdout.json"));
        var manifestPath = RepoFile(Path.Combine("src", "tools", "ClinicManagement.AI.Training", "data", "phase5_blind_holdout_manifest.json"));
        var records = JsonSerializer.Deserialize<List<IntentDatasetRecord>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        var report = GateBModelPipeline.ValidateRegistry(records, File.ReadAllText(blindPath), File.ReadAllText(manifestPath));
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(51, report.TotalRecords);
        Assert.Equal(0, report.BlindLeakageCount);
    }

    [Fact]
    public void OfflineRunSelectsOnlyFromDevelopmentAndReportsCorrectIndependentDenominator()
    {
        var report = RunGateB();

        Assert.True(report.Registry.IsValid, string.Join(Environment.NewLine, report.Registry.Errors));
        Assert.False(report.SelectionUsesBlindHoldout);
        Assert.True(report.BlindEvaluationWasAfterSelection);
        Assert.True(report.Baseline.ArtifactMetadataHashMatch);
        Assert.Equal(108, report.Baseline.IndependentHoldout.Samples);
        Assert.Equal(108, report.Candidates.Single(x => x.Name == "candidate_sdca_word").IndependentHoldout.Samples);
        Assert.All(report.Candidates.Where(x => x.Status == "completed"), candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.ArtifactSha256));
            Assert.True(candidate.ArtifactMetadataHashMatch);
        });
    }

    [Fact]
    public void RepeatedOfflineRunKeepsTheDevelopmentWinnerStable()
    {
        var first = RunGateB();
        var second = RunGateB();
        var firstWinner = first.Candidates.Single(x => x.Name == first.SelectedCandidate);
        var secondWinner = second.Candidates.Single(x => x.Name == second.SelectedCandidate);

        Assert.Equal(first.SelectedCandidate, second.SelectedCandidate);
        Assert.Equal(firstWinner.Deterministic, secondWinner.Deterministic);
        Assert.Equal(firstWinner.Development.MacroF1, secondWinner.Development.MacroF1, precision: 10);
        Assert.Equal(firstWinner.Threshold, secondWinner.Threshold, precision: 10);
        Assert.Equal(firstWinner.Margin, secondWinner.Margin, precision: 10);
        Assert.False(first.SelectionUsesBlindHoldout);
        Assert.False(second.SelectionUsesBlindHoldout);
    }

    [Fact]
    public void RuntimeRejectsArtifactMetadataMismatchAndLoadsMatchingArtifact()
    {
        var modelPath = RepoFile(Path.Combine("src", "backend", "ClinicManagement.Infrastructure", "models", "vietnamese_intent_classifier_v1.zip"));
        var metadataPath = RepoFile(Path.Combine("src", "backend", "ClinicManagement.Infrastructure", "models", "intent_model_metadata.json"));
        var temp = Path.Combine(Path.GetTempPath(), "cliniccare-gateb-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var matching = new VietnameseIntentClassifier(IntentClassificationMode.Shadow, modelPath);
            Assert.True(matching.ArtifactMetadataMatches);
            Assert.Equal(AiChatIntentTypes.Greeting, matching.Classify("xin chào").Intent);

            var copiedModel = Path.Combine(temp, "vietnamese_intent_classifier_v1.zip");
            File.Copy(modelPath, copiedModel);
            var copiedMetadata = File.ReadAllText(metadataPath);
            File.WriteAllText(Path.Combine(temp, "intent_model_metadata.json"), copiedMetadata);
            var matchingCopy = new VietnameseIntentClassifier(IntentClassificationMode.Shadow, copiedModel);
            Assert.True(matchingCopy.ArtifactMetadataMatches);
            Assert.NotNull(matchingCopy.Classify("xin chào").ShadowIntent);

            var mismatchedMetadata = copiedMetadata.Replace(
                "9b6be85dfed3c8a6a0fb8a86ad4e2d1c071e48311376e5bfa079210be72f23d4",
                new string('0', 64),
                StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(temp, "intent_model_metadata.json"), mismatchedMetadata);

            var rejected = new VietnameseIntentClassifier(IntentClassificationMode.Shadow, copiedModel);
            Assert.False(rejected.ArtifactMetadataMatches);
            Assert.Null(rejected.LoadedModelPath);
            Assert.Null(rejected.Classify("xin chào").ShadowIntent);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static IntentDatasetRecord Record(string id, string text, string intent, string split = "train") => new()
    {
        CaseId = id,
        Text = text,
        Intent = intent,
        Label = intent,
        Actor = "Patient",
        Language = "vi",
        SourceType = "synthetic_curated",
        TemplateFamilyId = "family-" + id,
        SemanticGroupId = "semantic-" + id,
        IsSynthetic = true,
        Split = split,
        Approved = true,
        Notes = "test fixture"
    };

    private static string EmptyBlind => "[]";
    private static string EmptyManifest => $"{{\"holdoutSha256\":\"{Sha256(EmptyBlind)}\",\"frozen\":true,\"evaluationOnly\":true,\"classifierPlannerTunedAfterFreeze\":false}}";
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static GateBRunReport RunGateB()
    {
        var repo = RepoRoot();
        var output = Path.Combine(Path.GetTempPath(), "cliniccare-gateb-test-" + Guid.NewGuid().ToString("N"));
        return GateBModelPipeline.Run(
            Path.Combine(repo, "src", "tools", "ClinicManagement.AI.Training", "data", "vietnamese_intent_dataset.json"),
            Path.Combine(repo, "src", "tools", "ClinicManagement.AI.Training", "data", "gateb_intent_registry_additions.json"),
            Path.Combine(repo, "src", "tools", "ClinicManagement.AI.Training", "data", "phase4_independent_cases.json"),
            Path.Combine(repo, "src", "tools", "ClinicManagement.AI.Training", "data", "phase5_blind_holdout.json"),
            Path.Combine(repo, "src", "tools", "ClinicManagement.AI.Training", "data", "phase5_blind_holdout_manifest.json"),
            Path.Combine(repo, "src", "backend", "ClinicManagement.Infrastructure", "models", "vietnamese_intent_classifier_v1.zip"),
            Path.Combine(repo, "src", "backend", "ClinicManagement.Infrastructure", "models", "intent_model_metadata.json"),
            "test-head",
            workingTreeDirty: false,
            output);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
