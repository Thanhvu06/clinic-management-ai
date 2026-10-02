using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.AI.Training;

namespace ClinicManagement.IntegrationTests;

public sealed class RoleIntentDatasetTests
{
    private const string SeedsSha256 = "e28a34f6e4aab23af9ec414024b7897518eb74b9bedb9ac528a45b6aa26e45fc";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] SharedLabels = { "Greeting", "Help", "ClinicKnowledge", "ActionRequest", "OutOfScope" };
    private static readonly string[] Roles = { "Patient", "Receptionist", "Doctor", "DiagnosticTechnician", "Pharmacist", "Admin" };

    [Fact]
    public void SeedsHavePinnedHashCountsLabelsAndAuthorizedRoles()
    {
        var path = Path.Combine(DataDirectory, RoleIntentDatasetGenerator.SeedsFile);
        Assert.Equal(SeedsSha256, Hash(path));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(11734, bytes.Length);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal(195, File.ReadAllLines(path).Length);
        var seeds = RoleIntentDatasetGenerator.ReadSeeds(DataDirectory);
        var labels = RoleIntentDatasetGenerator.ReadLabels(DataDirectory);
        Assert.Equal(194, seeds.Count);
        Assert.Equal(24, labels.Count);
        Assert.Equal(24, seeds.Select(x => x.Label).Distinct().Count());
        Assert.Equal(9, seeds.Count(x => x.Source == "claude_v1_sua"));
        Assert.Equal(labels.Select(x => x.Label).Order(), seeds.Select(x => x.Label).Distinct().Order());
        foreach (var label in labels)
        {
            Assert.Equal(label.Label == "ActionRequest" ? 10 : 8, seeds.Count(x => x.Label == label.Label));
            if (SharedLabels.Contains(label.Label))
            {
                Assert.Equal("Shared", label.Group);
                Assert.Equal(Roles, label.Roles);
            }
        }
        foreach (var seed in seeds)
        {
            var label = Assert.Single(labels, x => x.Label == seed.Label);
            if (seed.Role == "Chung") Assert.Contains(seed.Label, SharedLabels);
            else Assert.Contains(RoleIntentDatasetGenerator.RoleCodes[seed.Role], label.Roles);
        }
    }

    [Fact]
    public void RegenerationMatchesAllThreeCommittedFilesByteForByte()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "clinic-role-intent-" + Path.GetRandomFileName());
        try
        {
            RoleIntentDatasetGenerator.Generate(DataDirectory, temporary);
            foreach (var file in new[] { RoleIntentDatasetGenerator.TrainFile, RoleIntentDatasetGenerator.ValidationFile, RoleIntentDatasetGenerator.ManifestFile })
            {
                var expected = File.ReadAllBytes(Path.Combine(DataDirectory, file));
                Assert.Equal(expected, File.ReadAllBytes(Path.Combine(temporary, file)));
                Assert.DoesNotContain((byte)'\r', expected);
                Assert.False(expected.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            }
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void SeedFamiliesStayInOneSplitAndEveryLabelHasTwoValidationSeeds()
    {
        var train = Read<RoleIntentRecord[]>(RoleIntentDatasetGenerator.TrainFile);
        var validation = Read<RoleIntentRecord[]>(RoleIntentDatasetGenerator.ValidationFile);
        Assert.Empty(train.Select(x => x.SeedId).Intersect(validation.Select(x => x.SeedId)));
        foreach (var label in RoleIntentDatasetGenerator.ReadLabels(DataDirectory))
        {
            var labelTrain = train.Where(x => x.Label == label.Label).ToArray();
            var labelValidation = validation.Where(x => x.Label == label.Label).ToArray();
            Assert.NotEmpty(labelTrain);
            Assert.NotEmpty(labelValidation);
            Assert.Equal(2, labelValidation.Select(x => x.SeedId).Distinct().Count());
            Assert.Equal(label.Label == "ActionRequest" ? 8 : 6, labelTrain.Select(x => x.SeedId).Distinct().Count());
        }
        Assert.All(train, x => Assert.Equal("train", x.Split));
        Assert.All(validation, x => Assert.Equal("validation", x.Split));
    }

    [Fact]
    public void NormalizedTextsNeverCrossLabels()
    {
        foreach (var group in Records.GroupBy(x => NormalizeForAudit(x.Text)))
            Assert.Single(group.Select(x => x.Label).Distinct());
    }

    [Fact]
    public void LookupAppointmentAlwaysHasAnIntactCode()
    {
        var records = Records.Where(x => x.Label == "LookupAppointment").ToArray();
        Assert.NotEmpty(records);
        Assert.All(records, x => Assert.Matches(@"(?i)lh[- ]?\d{3,}", x.Text));
        foreach (var record in records.Where(x => x.Variant != "original"))
            Assert.Matches(@"(?:LH-\d{3,5}|LH\d{3,5}|lh \d{3,5})", record.Text);
    }

    [Fact]
    public void VariantsAndManifestCountsHashesAndProvenanceAreConsistent()
    {
        var records = Records;
        var manifest = Read<RoleIntentManifest>(RoleIntentDatasetGenerator.ManifestFile);
        Assert.Equal("role-intent-gen-v1", manifest.GeneratorVersion);
        Assert.Equal(20261002, manifest.RngSeed);
        Assert.Equal(RoleIntentDatasetGenerator.Provenance, manifest.Provenance);
        Assert.Equal(records.Length, records.Select(x => x.Id).Distinct().Count());
        var seeds = RoleIntentDatasetGenerator.ReadSeeds(DataDirectory);
        Assert.Equal(194, records.Select(x => x.SeedId).Distinct().Count());
        foreach (var seed in seeds)
        {
            var variants = records.Where(x => x.SeedId == seed.SeedId).ToArray();
            Assert.InRange(variants.Length, 1, 12);
            Assert.Equal(variants.Length, variants.Select(x => x.Text).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(seed.Text, Assert.Single(variants, x => x.Variant == "original").Text);
            Assert.All(variants, x =>
            {
                Assert.Equal(seed.Label, x.Label);
                Assert.Equal(seed.Source, x.Source);
                Assert.Equal(seed.Role == "Chung" ? "Chung" : RoleIntentDatasetGenerator.RoleCodes[seed.Role], x.Role);
                if (seed.Label is "Greeting" or "OutOfScope") Assert.DoesNotContain("affix", x.Variant);
            });
        }
        Assert.Equal(records.Length, manifest.Totals.Total);
        Assert.Equal(records.Count(x => x.Split == "train"), manifest.Totals.Train);
        Assert.Equal(records.Count(x => x.Split == "validation"), manifest.Totals.Validation);
        Assert.Equal(24, manifest.CountsByLabelAndSplit.Length);
        foreach (var count in manifest.CountsByLabelAndSplit)
        {
            Assert.Equal(records.Count(x => x.Label == count.Label && x.Split == "train"), count.Train);
            Assert.Equal(records.Count(x => x.Label == count.Label && x.Split == "validation"), count.Validation);
        }
        Assert.InRange(manifest.DroppedCrossLabelCandidates, 0, int.MaxValue);
        Assert.Equal(4, manifest.Sha256.Count);
        foreach (var (file, hash) in manifest.Sha256) Assert.Equal(Hash(Path.Combine(DataDirectory, file)), hash);
    }

    [Fact]
    public void GeneratedTextsDoNotOverlapFrozenBlindInputs()
    {
        using var blind = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory, "phase5_blind_holdout.json")));
        var inputs = blind.RootElement.EnumerateArray().Select(x => NormalizeForAudit(x.GetProperty("inputVi").GetString()!)).ToHashSet();
        Assert.All(Records, record => Assert.DoesNotContain(NormalizeForAudit(record.Text), inputs));
    }

    [Fact]
    public void CrossLabelOriginalCollisionIsRejectedBeforeWritingOutputs()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "clinic-role-intent-collision-" + Path.GetRandomFileName());
        Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(Path.Combine(DataDirectory, RoleIntentDatasetGenerator.LabelsFile), Path.Combine(temporary, RoleIntentDatasetGenerator.LabelsFile));
            File.WriteAllText(Path.Combine(temporary, RoleIntentDatasetGenerator.SeedsFile),
                "vai_tro\tcau\tnhan\tnguon\nChung\tXin chào!\tGreeting\tfixture\nChung\txin chao\tHelp\tfixture\n", new UTF8Encoding(false));
            var error = Assert.Throws<InvalidDataException>(() => RoleIntentDatasetGenerator.Generate(temporary));
            Assert.Contains("Original seeds collide", error.Message);
            Assert.False(File.Exists(Path.Combine(temporary, RoleIntentDatasetGenerator.TrainFile)));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static RoleIntentRecord[] Records => Read<RoleIntentRecord[]>(RoleIntentDatasetGenerator.TrainFile)
        .Concat(Read<RoleIntentRecord[]>(RoleIntentDatasetGenerator.ValidationFile)).ToArray();
    private static T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DataDirectory, file)), JsonOptions)!;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    // Independent audit normalization, not a call back into the generator being tested.
    private static string NormalizeForAudit(string text)
    {
        var characters = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !char.IsPunctuation(c));
        return Regex.Replace(new string(characters.ToArray()), @"\s+", " ").Trim();
    }

    private static string DataDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "tools", "ClinicManagement.AI.Training", "data");
                if (File.Exists(Path.Combine(candidate, RoleIntentDatasetGenerator.SeedsFile))) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Role intent dataset directory not found.");
        }
    }
}
