using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.AI.Training;

namespace ClinicManagement.IntegrationTests;

public sealed class RoleIntentEvalSetTests
{
    private const string EvalFile = "role_intent_eval_ai_v1.tsv";
    private const string ManifestFile = "role_intent_eval_ai_manifest_v1.json";
    private const string ExpectedSha256 = "4104b49e57d2f82758602faa63c255cdddf5a6ec2e895d045c27e04382dd4686";
    private const string Provenance = "Written by AI (Claude) in a single pass on 2026-10-02 as a supplementary evaluation set. Not written by human authors and NOT a blind test set by independent people. Never used for training or model selection. Frozen: do not edit after commit.";
    private static readonly string[] SharedLabels = { "Greeting", "Help", "ClinicKnowledge", "ActionRequest", "OutOfScope" };

    [Fact]
    public void EvalHasPinnedHashCountsSourceAndAuthorizedRoles()
    {
        var bytes = File.ReadAllBytes(Path.Combine(DataDirectory, EvalFile));
        Assert.Equal(ExpectedSha256, Sha256(bytes));
        Assert.Equal(17253, bytes.Length);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.DoesNotContain((byte)'\r', bytes);
        var rows = ReadEval();
        var labels = RoleIntentDatasetGenerator.ReadLabels(DataDirectory);
        Assert.Equal(240, rows.Length);
        Assert.Equal(24, labels.Count);
        Assert.Equal(labels.Select(x => x.Label).Order(), rows.Select(x => x.Label).Distinct().Order());
        foreach (var label in labels) Assert.Equal(10, rows.Count(x => x.Label == label.Label));
        foreach (var row in rows)
        {
            Assert.Equal("claude_eval_v1", row.Source);
            var definition = Assert.Single(labels, x => x.Label == row.Label);
            if (row.Role == "Chung")
            {
                Assert.Contains(row.Label, SharedLabels);
                Assert.Equal("Shared", definition.Group);
                Assert.All(RoleIntentDatasetGenerator.RoleCodes.Values, role => Assert.Contains(role, definition.Roles));
            }
            else
            {
                Assert.True(RoleIntentDatasetGenerator.RoleCodes.TryGetValue(row.Role, out var role), $"Unknown role at row {row.Line}: {row.Role}");
                Assert.Contains(role!, definition.Roles);
            }
        }
    }

    [Fact]
    public void EvalDoesNotOverlapSeedsTrainOrValidation()
    {
        var reference = new List<(string File, string Text)>();
        reference.AddRange(RoleIntentDatasetGenerator.ReadSeeds(DataDirectory)
            .Select(x => (RoleIntentDatasetGenerator.SeedsFile, x.Text)));
        foreach (var file in new[] { RoleIntentDatasetGenerator.TrainFile, RoleIntentDatasetGenerator.ValidationFile })
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory, file)));
            reference.AddRange(json.RootElement.EnumerateArray().Select(x => (file, x.GetProperty("text").GetString()!)));
        }
        AssertNoOverlap(reference);
    }

    [Fact]
    public void EvalNormalizedTextsNeverCrossLabels()
    {
        var collisions = ReadEval().GroupBy(x => RoleIntentDatasetGenerator.Normalize(x.Text))
            .Where(group => group.Select(x => x.Label).Distinct().Count() > 1)
            .Select(group => string.Join(" | ", group.Select(x => $"row {x.Line} [{x.Label}] {x.Text}"))).ToArray();
        Assert.True(collisions.Length == 0, "Cross-label eval collisions:\n" + string.Join("\n", collisions));
    }

    [Fact]
    public void EvalDoesNotOverlapFrozenBlindInputs()
    {
        const string file = "phase5_blind_holdout.json";
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory, file)));
        AssertNoOverlap(json.RootElement.EnumerateArray().Select(x => (file, x.GetProperty("inputVi").GetString()!)));
    }

    [Fact]
    public void EvalLookupAppointmentAlwaysHasAnIntactCode()
    {
        var rows = ReadEval().Where(x => x.Label == "LookupAppointment").ToArray();
        Assert.Equal(10, rows.Length);
        Assert.All(rows, row => Assert.Matches(@"(?i)lh[- ]?\d{3,}", row.Text));
    }

    [Fact]
    public void EvalManifestMatchesFileHashSizeRowAndLabelCounts()
    {
        var manifestBytes = File.ReadAllBytes(Path.Combine(DataDirectory, ManifestFile));
        Assert.False(manifestBytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.DoesNotContain((byte)'\r', manifestBytes);
        using var manifest = JsonDocument.Parse(manifestBytes);
        var root = manifest.RootElement;
        var bytes = File.ReadAllBytes(Path.Combine(DataDirectory, EvalFile));
        var rows = ReadEval();
        Assert.Equal(EvalFile, root.GetProperty("file").GetString());
        Assert.Equal(Sha256(bytes), root.GetProperty("sha256").GetString());
        Assert.Equal(bytes.Length, root.GetProperty("byteSize").GetInt32());
        Assert.Equal(rows.Length, root.GetProperty("rowCount").GetInt32());
        Assert.Equal(Provenance, root.GetProperty("provenance").GetString());
        var counts = root.GetProperty("countsByLabel");
        Assert.Equal(24, counts.EnumerateObject().Count());
        foreach (var group in rows.GroupBy(x => x.Label))
            Assert.Equal(group.Count(), counts.GetProperty(group.Key).GetInt32());
    }

    private static void AssertNoOverlap(IEnumerable<(string File, string Text)> reference)
    {
        // Reuse the generator's exact normalization; no separate normalization rules.
        var index = reference.ToLookup(x => RoleIntentDatasetGenerator.Normalize(x.Text), StringComparer.Ordinal);
        var collisions = ReadEval().SelectMany(row => index[RoleIntentDatasetGenerator.Normalize(row.Text)]
            .Select(match => $"row {row.Line} [{row.Label}] eval: {row.Text} => {match.File}: {match.Text}"))
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(collisions.Length == 0, "STOP: normalized eval overlaps (do not edit the frozen TSV):\n" + string.Join("\n", collisions));
    }

    private static EvalRow[] ReadEval()
    {
        var lines = File.ReadAllLines(Path.Combine(DataDirectory, EvalFile), Encoding.UTF8);
        Assert.Equal("vai_tro\tcau\tnhan\tnguon", lines[0]);
        return lines.Skip(1).Select((line, index) =>
        {
            var columns = line.Split('\t');
            Assert.Equal(4, columns.Length);
            Assert.All(columns, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            return new EvalRow(index + 2, columns[0], columns[1], columns[2], columns[3]);
        }).ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed record EvalRow(int Line, string Role, string Text, string Label, string Source);

    private static string DataDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "tools", "ClinicManagement.AI.Training", "data");
                if (File.Exists(Path.Combine(candidate, EvalFile))) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Role intent eval data directory not found.");
        }
    }
}
