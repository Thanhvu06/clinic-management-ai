using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ClinicManagement.AI.Training.HiddenEvalRunner;

namespace ClinicManagement.AI.Training;

public static class RoleIntentV3Tests
{
    private const string Marker = "synthetic-human-test-private-marker-q7p9";
    private static string Data => Path.Combine(RoleIntentV3Evaluation.Root(), "src", "tools", "ClinicManagement.AI.Training", "data");
    private static string Models => Path.Combine(RoleIntentV3Evaluation.Root(), "src", "tools", "ClinicManagement.AI.Training", "models", "role-intent-v3");

    public static int Run()
    {
        Action[] tests = [ReadsSharedAndInlineWorkbook, SkipsExamplesAndBlankRows, RejectsWrongHeaders, RejectsWrongSheet,
            RejectsDuplicateStt, RejectsMissingAndMultilineCells, ReadsTsv, RejectsMalformedTsv, ValidatesRolesAndCount,
            RejectsNormalizedDuplicates, ReadsManifestAndDistribution, LoadsSavedV3AndRejectsCorruption,
            RuntimeMatchesV3AndSupportsV2, SelectionUsesValidationOnly, ReportIsPrivateAndHasWilson, PreservesOldHiddenSelfTests, SupplementBatchesStayDisjoint,
            ScannerDetectsLeaksUsingSyntheticQuestions, ScannerUsesUtf8WhenConsoleUsesWindows1258];
        for (var i = 0; i < tests.Length; i++)
        {
            try { tests[i](); }
            catch (Exception exception) { throw new InvalidOperationException($"V3 self-test {i + 1} failed ({exception.GetType().Name})."); }
        }
        return tests.Length;
    }

    public static int RunScannerTests()
    {
        ScannerDetectsLeaksUsingSyntheticQuestions();
        ScannerUsesUtf8WhenConsoleUsesWindows1258();
        return 2;
    }

    private static void ReadsSharedAndInlineWorkbook()
    {
        foreach (var shared in new[] { false, true })
        {
            using var stream = Workbook([["1", "Bệnh nhân", Marker, "Greeting", "", "ignored"]], shared);
            var row = RoleIntentHumanData.ReadWorkbook(stream).Single();
            Check(row.Role == "BenhNhan" && row.Label == "Greeting" && row.Text == Marker && row.Source == RoleIntentHumanData.Source);
        }
    }

    private static void SkipsExamplesAndBlankRows()
    {
        using var stream = Workbook([["", "", "", "", "", "not blank but ignored"], ["2", "bad", Marker, "bad", "VÍ DỤ", ""],
            ["3", "Bác sĩ", Marker, "DoctorQueue", "", ""]]);
        Check(RoleIntentHumanData.ReadWorkbook(stream).Single().Role == "BacSi");
    }

    private static void RejectsWrongHeaders()
    {
        using var stream = Workbook([], header: "Câu");
        var error = Reject(() => RoleIntentHumanData.ReadWorkbook(stream));
        Check(error.Contains("Câu train headers", StringComparison.Ordinal) && error.Contains("STT", StringComparison.Ordinal) && !error.Contains(Marker, StringComparison.Ordinal));
    }

    private static void RejectsWrongSheet()
    {
        using var stream = Workbook([], sheet: "Other");
        Check(Reject(() => RoleIntentHumanData.ReadWorkbook(stream)).Contains("Other", StringComparison.Ordinal));
    }

    private static void RejectsDuplicateStt()
    {
        using var stream = Workbook([["1", "Bệnh nhân", Marker, "Greeting", "", ""], ["1", "Bác sĩ", Marker + "2", "Greeting", "", ""]]);
        var error = Reject(() => RoleIntentHumanData.ReadWorkbook(stream));
        Check(error.Contains("Excel row 3", StringComparison.Ordinal) && !error.Contains(Marker, StringComparison.Ordinal));
    }

    private static void RejectsMissingAndMultilineCells()
    {
        foreach (var text in new[] { "", Marker + "\nsecond line", Marker + "\ttab" })
        {
            using var stream = Workbook([["1", "Bệnh nhân", text, "Greeting", "", ""]]);
            Check(!Reject(() => RoleIntentHumanData.ReadWorkbook(stream)).Contains(Marker, StringComparison.Ordinal));
        }
    }

    private static string Tsv(string role = "BenhNhan", string label = "Greeting", string? text = null) =>
        RoleIntentHumanData.Header + $"\n{role}\t{text ?? Marker}\t{label}\t{RoleIntentHumanData.Source}\n";

    private static void ReadsTsv()
    {
        var row = RoleIntentHumanData.ParseTsv(Tsv()).Single();
        Check(row.Role == "BenhNhan" && row.Label == "Greeting" && row.Text == Marker);
        RoleIntentHumanData.Validate([row], Data, 1);
    }

    private static void RejectsMalformedTsv()
    {
        foreach (var invalid in new[] { "bad\n", Tsv().Replace(RoleIntentHumanData.Source, "bad", StringComparison.Ordinal), Tsv() + "\n", Tsv().Replace("\tGreeting", "\t\tGreeting", StringComparison.Ordinal) })
            Check(!Reject(() => RoleIntentHumanData.ParseTsv(invalid)).Contains(Marker, StringComparison.Ordinal));
    }

    private static void ValidatesRolesAndCount()
    {
        Reject(() => RoleIntentHumanData.Validate(RoleIntentHumanData.ParseTsv(Tsv()), Data));
        foreach (var (role, label) in new[] { ("BenhNhan", "DoctorQueue"), ("bad", "Greeting"), ("BenhNhan", "bad") })
        {
            var error = Reject(() => RoleIntentHumanData.Validate(RoleIntentHumanData.ParseTsv(Tsv(role, label)), Data, 1));
            Check(error.Contains("Row 2", StringComparison.Ordinal) && !error.Contains(Marker, StringComparison.Ordinal));
        }
    }

    private static void RejectsNormalizedDuplicates()
    {
        var row = RoleIntentHumanData.ParseTsv(Tsv()).Single();
        Reject(() => RoleIntentHumanData.Validate([row, row with { Text = Marker.ToUpperInvariant() + "!" }], Data, 2));
        foreach (var file in new[] { RoleIntentDatasetGenerator.TrainFile, RoleIntentDatasetGenerator.ValidationFile })
        {
            var known = RoleIntentModelPipeline.Read<RoleIntentRecord[]>(Path.Combine(Data, file))[0];
            var error = Reject(() => RoleIntentHumanData.Validate([row with { Text = known.Text }], Data, 1));
            Check(error.Contains("duplicate of", StringComparison.Ordinal) && !error.Contains(known.Text, StringComparison.Ordinal));
        }
        foreach (var file in new[] { RoleIntentDatasetGenerator.SeedsFile, RoleIntentModelPipeline.EvalFile })
        {
            var known = File.ReadLines(Path.Combine(Data, file)).Skip(1).First().Split('\t')[1];
            Check(Reject(() => RoleIntentHumanData.Validate([row with { Text = known }], Data, 1)).Contains(file, StringComparison.Ordinal));
        }
    }

    private static void ReadsManifestAndDistribution()
    {
        var rows = RoleIntentHumanData.Read(Data);
        Check(rows.Length == 310 && rows.Count(x => x.Role == "BenhNhan") == 88 && rows.Select(x => x.Label).Distinct().Count() == 24);
        var manifest = RoleIntentModelPipeline.Read<RoleIntentHumanManifest>(Path.Combine(Data, RoleIntentHumanData.ManifestFile));
        Check(manifest.CountsByLabel.Values.Sum() == 310 && manifest.CountsByRole.Values.Sum() == 310 && manifest.Provenance == RoleIntentHumanData.Provenance);
        var all = RoleIntentHumanData.ReadAll(Data);
        Check(all.Length == 360 && all.Select(x => x.Id).Distinct().Count() == 360 && all.Count(x => x.Role == "BenhNhan") == 116);
        var supplement = RoleIntentModelPipeline.Read<RoleIntentHumanManifest>(Path.Combine(Data, RoleIntentHumanData.SupplementManifestFile));
        Check(supplement.RowCount == 50 && supplement.Provenance == RoleIntentHumanData.SupplementProvenance && supplement.CountsByLabel.Values.Sum() == 50);
        Temporary(directory =>
        {
            foreach (var file in Directory.EnumerateFiles(Data, "role_intent_*")) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            File.AppendAllText(Path.Combine(directory, RoleIntentHumanData.DataFile), "changed");
            Reject(() => RoleIntentHumanData.Read(directory));
        });
    }

    private static void LoadsSavedV3AndRejectsCorruption()
    {
        var model = RoleIntentModelV3Pipeline.Load(Models, out var metadata);
        Check(metadata.ModelVersion == RoleIntentModelV3Pipeline.Version && metadata.Train.Filtered.Samples == 1226 && metadata.Validation.Filtered.Samples == 283);
        Check(metadata.ConfigurationTrials.Length == 1 && metadata.ConfigurationTrials[0].Reproducibility.Passed);
        var validation = RoleIntentModelPipeline.Read<RoleIntentRecord[]>(Path.Combine(Data, RoleIntentDatasetGenerator.ValidationFile));
        var measured = RoleIntentModelPipeline.Measure(validation, model.Predict(validation, metadata.ThresholdPolicy.Threshold).Filtered, model.ScoreLabels);
        Check(Math.Abs(measured.MacroF1 - metadata.Validation.Filtered.MacroF1) < 1e-12);
        Temporary(directory =>
        {
            File.Copy(Path.Combine(Models, RoleIntentModelV3Pipeline.MetadataFile), Path.Combine(directory, RoleIntentModelV3Pipeline.MetadataFile));
            File.WriteAllText(Path.Combine(directory, RoleIntentModelV3Pipeline.ModelFile), "corrupt");
            Reject(() => RoleIntentModelV3Pipeline.Load(directory, out _));
        });
    }

    private static void RuntimeMatchesV3AndSupportsV2()
    {
        using var v3 = RoleIntentV3Evaluation.Model(RoleIntentV3Evaluation.Root(), "v3");
        using var v2 = RoleIntentV3Evaluation.Model(RoleIntentV3Evaluation.Root(), "v2");
        Check(v3.IsAvailable && v2.IsAvailable && v3.Threshold == .45 && v2.Threshold == .75);
        Check(new RoleIntentModelOptions().Directory.EndsWith("role-intent-v3", StringComparison.Ordinal));
        var offline = RoleIntentModelV3Pipeline.Load(Models, out var metadata);
        foreach (var row in RoleIntentHumanData.ReadAll(Data))
        {
            var role = Enum.Parse<AiActorRole>(RoleIntentDatasetGenerator.RoleCodes[row.Role]);
            var expected = offline.Predict([row], metadata.ThresholdPolicy.Threshold).Filtered.Single();
            var actual = v3.Predict(row.Text, role)!;
            Check(actual.Label == expected.TopLabel && Math.Abs(actual.Confidence - expected.Confidence) <= 1e-6);
            Check(actual.Probabilities.Keys.All(label => v3.IsLabelAllowed(label, role)));
        }
        Check(v3.Predict(Marker, (AiActorRole)999) is null);
        Temporary(directory =>
        {
            File.Copy(Path.Combine(Models, RoleIntentModelV3Pipeline.MetadataFile), Path.Combine(directory, RoleIntentModelV3Pipeline.MetadataFile));
            File.WriteAllText(Path.Combine(directory, RoleIntentModelV3Pipeline.ModelFile), "corrupt");
            using var bad = new RoleIntentModel(Options.Create(new RoleIntentModelOptions { Directory = directory }), NullLogger<RoleIntentModel>.Instance);
            Check(!bad.IsAvailable && bad.Predict(Marker, AiActorRole.Patient) is null);
        });
    }

    private static void SelectionUsesValidationOnly()
    {
        var selection = RoleIntentModelPipeline.Read<RoleIntentV3Selection>(Path.Combine(Models, RoleIntentModelV3Pipeline.SelectionFile));
        RoleIntentModelV3Pipeline.Load(Models, out var metadata);
        Check(selection.Rule == RoleIntentModelV3Pipeline.SelectionRule && selection.SelectedV3 ==
            (selection.V3ValidationMacroF1 >= selection.V2ValidationMacroF1 && selection.V3AcceptedAccuracy >= .9));
        Check(metadata.Configuration == new RoleIntentV2Configuration("lbfgs_l2_plain_500_zeroL1", "lbfgs", true, false, 500, 0, .01f));
        var model = RoleIntentModelV3Pipeline.Load(Models, out _);
        var rows = RoleIntentModelPipeline.Read<RoleIntentRecord[]>(Path.Combine(Data, RoleIntentDatasetGenerator.ValidationFile));
        var policy = RoleIntentModelV2Pipeline.SelectThreshold(rows, model.Predict(rows, 0).Filtered);
        Check(policy.Threshold == metadata.ThresholdPolicy.Threshold && policy.Trials.Single(x => x.Threshold == policy.Threshold).AcceptedAccuracy >= .9);
    }

    private static void ReportIsPrivateAndHasWilson()
    {
        var selection = RoleIntentModelPipeline.Read<RoleIntentV3Selection>(Path.Combine(Models, RoleIntentModelV3Pipeline.SelectionFile));
        var row = new HiddenRow(1, AiActorRole.Patient, "Greeting", Marker);
        ScoredRow[] scored = [new(1, row.Role, row.Label, "Greeting", true, "Greeting", "rule", false, "Greeting")];
        var report = RoleIntentV3Evaluation.Report(1, new Selection([row], [], []), scored, scored, scored, selection, "synthetic-sha", .75, .5);
        Check(!report.Contains(Marker, StringComparison.Ordinal) && report.Contains(RoleIntentV3Evaluation.Heading, StringComparison.Ordinal) && report.Contains("Wilson 95%", StringComparison.Ordinal));
        var (low, high) = Wilson(1, 1);
        Check(low > .20 && low < .21 && Math.Abs(high - 1) < 1e-12);
    }

    private static void PreservesOldHiddenSelfTests() => Check(HiddenEvalRunnerTests.Run() == 18);

    private static void SupplementBatchesStayDisjoint()
    {
        var row = RoleIntentHumanData.ParseTsv(Tsv()).Single();
        Reject(() => RoleIntentHumanData.CheckBatches([row], [row with { Text = Marker.ToUpperInvariant() + "!" }]));
        RoleIntentHumanData.CheckBatches([row], [row with { Text = "separate synthetic training sample" }]);
        var rows = RoleIntentHumanData.ReadAll(Data);
        Check(rows.Select(x => RoleIntentDatasetGenerator.Normalize(x.Text)).Distinct(StringComparer.Ordinal).Count() == 360);
    }

    private static void ScannerDetectsLeaksUsingSyntheticQuestions()
    {
        var rows = Enumerable.Range(1, 5).Select(i => new HiddenRow(i, AiActorRole.Patient, "Greeting", "synthetic-privacy-" + Guid.NewGuid().ToString("N"))).ToArray();
        Check(RoleIntentV3Evaluation.ScanQuestions(RoleIntentV3Evaluation.Root(), rows));
        Temporary(directory =>
        {
            Check(RoleIntentV3Evaluation.ScanQuestions(directory, rows));
            File.WriteAllText(Path.Combine(directory, ".synthetic-leak.txt"), rows[0].Text);
            Check(!RoleIntentV3Evaluation.ScanQuestions(directory, rows));
        });
    }

    private static void ScannerUsesUtf8WhenConsoleUsesWindows1258()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var previous = Console.InputEncoding;
        try
        {
            Console.InputEncoding = Encoding.GetEncoding(1258);
            Temporary(directory =>
            {
                var row = new HiddenRow(1, AiActorRole.Patient, "Greeting", "Câu tổng hợp kiểm tra tiếng Việt riêng tư " + Guid.NewGuid().ToString("N"));
                File.WriteAllText(Path.Combine(directory, "unrelated.txt"), "Dữ liệu tổng hợp không trùng.", new UTF8Encoding(false));
                var legacy = new ProcessStartInfo("rg")
                {
                    WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
                };
                foreach (var arg in new[] { "--fixed-strings", "--files-with-matches", "--file", "-", "." }) legacy.ArgumentList.Add(arg);
                using var process = Process.Start(legacy) ?? throw new InvalidOperationException("Synthetic scanner unavailable.");
                Check(process.StandardInput.Encoding.CodePage == 1258);
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.StandardInput.WriteLine(row.Text);
                process.StandardInput.Close();
                process.WaitForExit();
                Check(process.ExitCode == 2 && output.GetAwaiter().GetResult().Length == 0 && error.GetAwaiter().GetResult().Contains("UTF-8", StringComparison.OrdinalIgnoreCase));
                Check(RoleIntentV3Evaluation.ScanQuestions(directory, [row]));
                File.WriteAllText(Path.Combine(directory, ".synthetic-leak.txt"), row.Text, new UTF8Encoding(false));
                Check(!RoleIntentV3Evaluation.ScanQuestions(directory, [row]));
            });
        }
        finally { Console.InputEncoding = previous; }
    }

    private static MemoryStream Workbook(string[][] rows, bool shared = false, string sheet = "Câu train", string header = "Câu train")
    {
        var stream = new MemoryStream();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var strings = new List<string>();
        var all = new[] { new[] { "STT", "Vai trò", header, "Nhãn đúng", "Ghi chú", "Kiểm tra" } }.Concat(rows).ToArray();
        var xmlRows = all.Select((values, i) => new XElement(ns + "row", new XAttribute("r", i + 1), values.Select((value, j) =>
        {
            strings.Add(value);
            return new XElement(ns + "c", new XAttribute("r", ((char)('A' + j)).ToString() + (i + 1)), new XAttribute("t", shared ? "s" : "inlineStr"),
                shared ? new XElement(ns + "v", strings.Count - 1) : new XElement(ns + "is", new XElement(ns + "t", value)));
        }))).ToArray();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string path, XElement xml) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(xml.ToString()); }
            Add("xl/workbook.xml", new XElement(ns + "workbook", new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", sheet), new XAttribute(r + "id", "rId1")))));
            Add("xl/_rels/workbook.xml.rels", new XElement("Relationships", new XElement("Relationship", new XAttribute("Id", "rId1"), new XAttribute("Target", "worksheets/sheet1.xml"))));
            Add("xl/worksheets/sheet1.xml", new XElement(ns + "worksheet", new XElement(ns + "sheetData", xmlRows)));
            if (shared) Add("xl/sharedStrings.xml", new XElement(ns + "sst", strings.Select(x => new XElement(ns + "si", new XElement(ns + "t", x)))));
        }
        stream.Position = 0;
        return stream;
    }

    private static string Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException exception) { return exception.Message; }
        throw new InvalidOperationException("Expected invalid data rejection.");
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Check failed."); }
    private static void Temporary(Action<string> action)
    {
        var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var directory = Path.Combine(root, "clinic-v3-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { if (!Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe temporary path."); Directory.Delete(directory, true); }
    }
}
