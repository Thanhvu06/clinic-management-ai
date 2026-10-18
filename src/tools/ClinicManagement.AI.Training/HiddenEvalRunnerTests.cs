using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using static ClinicManagement.AI.Training.HiddenEvalRunner;

namespace ClinicManagement.AI.Training;

public static class HiddenEvalRunnerTests
{
    private const string Secret = "synthetic-question-privacy-marker-7d1f";
    private static readonly string[] RoleNames = ["Bệnh nhân", "Lễ tân", "Bác sĩ", "Kỹ thuật viên", "Dược sĩ", "Quản trị"];
    private static readonly AiActorRole[] RoleValues = [AiActorRole.Patient, AiActorRole.Receptionist, AiActorRole.Doctor,
        AiActorRole.DiagnosticTechnician, AiActorRole.Pharmacist, AiActorRole.Admin];
    private static readonly Dictionary<string, HashSet<string>> Catalog = new(StringComparer.Ordinal)
    {
        ["Greeting"] = RoleValues.Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal),
        ["DoctorQueue"] = ["Doctor"], ["MyBills"] = ["Patient"]
    };

    public static int Run()
    {
        Action[] tests = [ReadsSharedStringsAndAllRoles, ReadsInlineStringsAndReorderedColumns, SkipsBlankAndExampleRows, ReadsNotesWithBlankHeader,
            RejectsInvalidLabelsAndRoles, RejectsDuplicateIdsAndMissingText, RejectsMissingSheetAndColumns,
            NormalizesVietnamese, ExcludesAllDataAndInternalDuplicates, LocksAndUpdatesManifest,
            ReportContainsNoQuestionText, MetricsAndWilsonAreCorrect, ScoresBothLayersAtThreshold, ErrorsContainNoCellOrArgumentText,
            FallbackTelemetryNeverScoresCorrect, ClarificationSeparatesStrictAndLenient,
            FinalDecisionUsesHandlerInsteadOfTelemetry, AppendingReportPreservesFirstRun];
        for (var i = 0; i < tests.Length; i++)
        {
            try { tests[i](); }
            catch (Exception exception) { throw new HiddenEvalException($"Self-test {i + 1} thất bại ({exception.GetType().Name})."); }
        }
        return tests.Length;
    }

    private static void ReadsSharedStringsAndAllRoles()
    {
        var input = RoleNames.Select((role, i) => new[] { (i + 1).ToString(), role, Secret + i, "Greeting", "" }).ToArray();
        using var workbook = Workbook(input);
        var rows = ReadWorkbook(workbook, Catalog);
        Check(rows.Count == 6);
        for (var i = 0; i < rows.Count; i++)
        {
            Check(rows[i].Stt == i + 1 && rows[i].Role == RoleValues[i]);
            Check(rows[i].Text == Secret + i && rows[i].Label == "Greeting");
        }
    }

    private static void FallbackTelemetryNeverScoresCorrect()
    {
        var fallback = new ScoredRow(1, AiActorRole.Patient, "MyBills", "MyBills", true, "MyBills", "fallback", true, "MyBills");
        Check(fallback.StrictPrediction == "Fallback" && !fallback.StrictCorrect && !fallback.LenientCorrect);
        Near(PerLabel([fallback], "MyBills", true).F1, 0);
    }

    private static void ClarificationSeparatesStrictAndLenient()
    {
        foreach (var decision in new[]
        {
            new AiPlannerDecision { Clarification = Secret },
            new AiPlannerDecision { ErrorCode = AiPlannerErrorCodes.ResourceContextRequired },
            new AiPlannerDecision { ErrorCode = "MissingAssignedCase" },
            new AiPlannerDecision { SubIntent = "MissingPrescriptionForPayment" },
            new AiPlannerDecision { SubIntent = "MissingAppointmentCode" }
        })
        {
            Check(AsksForClarification(decision));
            var row = new ScoredRow(1, AiActorRole.Doctor, "PatientSummary", "PatientSummary", true,
                "PatientSummary", "model", AsksForClarification(decision), "PatientSummary");
            Check(!row.StrictCorrect && row.LenientCorrect && row.StrictPrediction == "Clarification");
            var selected = new Selection([new(1, AiActorRole.Doctor, "PatientSummary", Secret)], [], []);
            var report = BuildReport(1, selected, [row], ["PatientSummary"], .75, true, .8);
            Check(!report.Contains(Secret, StringComparison.Ordinal));
            Check(report.Contains("T2 strict", StringComparison.Ordinal) && report.Contains("T2 lenient", StringComparison.Ordinal));
        }
        Check(!AsksForClarification(new AiPlannerDecision { ErrorCode = "FORBIDDEN" }));
    }

    private static void FinalDecisionUsesHandlerInsteadOfTelemetry()
    {
        var planner = new AiDeterministicPlanner();
        var decision = planner.Plan(new AiCopilotPlanningContext { Role = AiActorRole.Patient, NormalizedMessage = "synthetic hoa don" });
        var label = FinalDecisionLabel(decision, AiActorRole.Patient);
        Check(label == "MyBills");
        var row = new ScoredRow(1, AiActorRole.Patient, "MyBills", "MyBills", true, "MyVisits", "rule", false, label);
        Check(row.StrictCorrect && row.LenientCorrect);
        Check(FinalDecisionLabel(new AiPlannerDecision { RequiresProvider = true, Intent = "MyBills" }, AiActorRole.Patient) == "ProviderRequired");
    }

    private static void AppendingReportPreservesFirstRun()
    {
        const string first = "# Evaluation\n\n## Lần chấm 1\n\nFirst metrics\n\n## Router v2 — tập phát triển\n\nDevelopment metrics";
        const string second = "## Lần chấm 2 (sau sửa router)\n\nSecond metrics";
        var document = AppendSecondRun(first, second);
        Check(document.StartsWith(first, StringComparison.Ordinal) && document.EndsWith(second, StringComparison.Ordinal));
        Fails(() => AppendSecondRun(document, second));
    }

    private static void ReadsInlineStringsAndReorderedColumns()
    {
        using var workbook = Workbook([["31", "Bác sĩ", Secret, "DoctorQueue", ""]], inline: true, reordered: true);
        var row = ReadWorkbook(workbook, Catalog).Single();
        Check(row.Stt == 31 && row.Role == AiActorRole.Doctor && row.Label == "DoctorQueue" && row.Text == Secret);
    }

    private static void SkipsBlankAndExampleRows()
    {
        using var workbook = Workbook([["", "", "", "", ""], ["2", Secret, Secret, Secret, "VÍ DỤ"],
            ["3", "Bệnh nhân", Secret, "MyBills", ""], ["4", "", "", "", ""]]);
        Check(ReadWorkbook(workbook, Catalog).Single().Stt == 3);
    }

    private static void ReadsNotesWithBlankHeader()
    {
        using var workbook = Workbook([["1", "Bệnh nhân", Secret, "Greeting", "VÍ DỤ"],
            ["2", "Bác sĩ", Secret + "2", "DoctorQueue", ""]], blankNotesHeader: true);
        Check(ReadWorkbook(workbook, Catalog).Single().Stt == 2);
    }

    private static void RejectsInvalidLabelsAndRoles()
    {
        foreach (var row in new[]
        {
            new[] { "4", "Bệnh nhân", Secret, "DoctorQueue", "" },
            new[] { "4", "Bệnh nhân", Secret, Secret, "" },
            new[] { "4", Secret, Secret, "Greeting", "" }
        })
        {
            using var workbook = Workbook([row]);
            var message = Fails(() => ReadWorkbook(workbook, Catalog));
            Check(message.Contains("STT 4", StringComparison.Ordinal) && !message.Contains(Secret, StringComparison.Ordinal));
        }
    }

    private static void RejectsDuplicateIdsAndMissingText()
    {
        using var duplicate = Workbook([["5", "Bệnh nhân", Secret, "Greeting", ""], ["5", "Bệnh nhân", Secret, "Greeting", ""]]);
        Check(Fails(() => ReadWorkbook(duplicate, Catalog)).Contains("STT 5", StringComparison.Ordinal));
        using var empty = Workbook([["6", "Bệnh nhân", " ", "Greeting", ""]]);
        Check(Fails(() => ReadWorkbook(empty, Catalog)).Contains("STT 6", StringComparison.Ordinal));
        using var invalidId = Workbook([[Secret, "Bệnh nhân", Secret, "Greeting", ""]]);
        Check(!Fails(() => ReadWorkbook(invalidId, Catalog)).Contains(Secret, StringComparison.Ordinal));
    }

    private static void RejectsMissingSheetAndColumns()
    {
        using var sheet = Workbook([], sheetName: "Other");
        Fails(() => ReadWorkbook(sheet, Catalog));
        using var columns = Workbook([], omitHeader: true);
        Fails(() => ReadWorkbook(columns, Catalog));
    }

    private static void NormalizesVietnamese()
    {
        Check(Normalize("  ĐẶT\tLỊCH   khám?!..  ") == "dat lich kham");
        Check(Normalize("đặt lịch khám".Normalize(NormalizationForm.FormD)) == "dat lich kham");
        Check(Normalize("Câu? hỏi!") == "cau? hoi");
        Check(Normalize("Câu hỏi ? . ! ") == "cau hoi");
    }

    private static void ExcludesAllDataAndInternalDuplicates() => InTemporaryDirectory(directory =>
    {
        File.WriteAllText(Path.Combine(directory, "seed.tsv"), "vai_tro\tcau\tnhan\nChung\tĐẶT LỊCH khám.\tGreeting\n");
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "nested", "cases.json"), "{\"cases\":[{\"inputVi\":\"XEM HỒ SƠ!\"}]}");
        var rows = new List<HiddenRow>
        {
            new(11, AiActorRole.Patient, "Greeting", "dat lich   kham?"),
            new(12, AiActorRole.Doctor, "Greeting", "xem ho so."),
            new(13, AiActorRole.Patient, "Greeting", Secret),
            new(14, AiActorRole.Doctor, "Greeting", Secret.ToUpperInvariant() + "!")
        };
        var selection = ExcludeDuplicates(rows, ReadKnownTexts(directory));
        Check(selection.Included.Single().Stt == 13);
        Check(selection.DataDuplicates.SequenceEqual(new[] { 11, 12 }));
        Check(selection.InternalDuplicates.SequenceEqual(new[] { 14 }));
    });

    private static void LocksAndUpdatesManifest() => InTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "hidden_eval_manifest.json");
        var rows = new List<HiddenRow> { new(1, AiActorRole.Patient, "Greeting", Secret), new(2, AiActorRole.Doctor, "DoctorQueue", Secret + "2") };
        LockManifest(path, new string('a', 64), rows, false);
        var locked = File.ReadAllText(path);
        Check(!locked.Contains(Secret, StringComparison.Ordinal));
        using var manifest = JsonDocument.Parse(locked);
        Check(manifest.RootElement.GetProperty("rowCount").GetInt32() == 2);
        Check(manifest.RootElement.GetProperty("countsByRole").GetProperty("Doctor").GetInt32() == 1);
        Check(manifest.RootElement.GetProperty("countsByLabel").GetProperty("Greeting").GetInt32() == 1);
        LockManifest(path, new string('a', 64), rows, false);
        Check(File.ReadAllText(path) == locked);
        Fails(() => CheckHash(path, new string('b', 64), false));
        Fails(() => LockManifest(path, new string('b', 64), rows, false));
        Check(File.ReadAllText(path) == locked);
        LockManifest(path, new string('b', 64), rows, true);
        CheckHash(path, new string('b', 64), false);
        Fails(() => LockManifest(path, new string('b', 64), rows.Take(1).ToArray(), false));
    });

    private static void ReportContainsNoQuestionText()
    {
        var selected = new Selection([new(42, AiActorRole.Patient, "MyBills", Secret),
            new(43, AiActorRole.Doctor, "DoctorQueue", Secret + "2")], [3], [4]);
        ScoredRow[] scored = [new(42, AiActorRole.Patient, "MyBills", "Greeting", false, "MyBills", "model"),
            new(43, AiActorRole.Doctor, "DoctorQueue", "DoctorQueue", true, "DoctorQueue", "rule")];
        var report = BuildReport(4, selected, scored, Catalog.Keys, .75, true, .8);
        using var output = new StringWriter();
        output.Write(report);
        Check(!output.ToString().Contains(Secret, StringComparison.Ordinal));
        Check(report.Contains("| 42 | Patient | MyBills | Greeting | MyBills |", StringComparison.Ordinal));
        Check(report.Contains("| Patient | 0 (0.00%) | 1 (100.00%) | 0 (0.00%) | 0 (0.00%) |", StringComparison.Ordinal));
        Check(report.Contains("| Doctor | 1 (100.00%) | 0 (0.00%) | 0 (0.00%) | 0 (0.00%) |", StringComparison.Ordinal));
        Check(report.Contains("N/A", StringComparison.Ordinal));
        Check(!JsonSerializer.Serialize(selected).Contains(Secret, StringComparison.Ordinal));
    }

    private static void MetricsAndWilsonAreCorrect()
    {
        ScoredRow[] rows = [new(1, AiActorRole.Patient, "Greeting", "Greeting", true, "Greeting", "rule"),
            new(2, AiActorRole.Patient, "MyBills", "Greeting", false, "MyBills", "model")];
        var metric = PerLabel(rows, "Greeting", false);
        Near(metric.P, .5); Near(metric.R, 1); Near(metric.F1, 2.0 / 3);
        Near(MacroF1(rows, false), 1.0 / 3); Near(MacroF1(rows, true), 1);
        var (low, high) = Wilson(50, 100);
        Near(low, .4038315303659956); Near(high, .5961684696340044);
        Near(Wilson(0, 10).Low, 0); Near(Wilson(10, 10).High, 1);
        Check(double.IsNaN(Wilson(0, 0).Low));
    }

    private static void ScoresBothLayersAtThreshold()
    {
        var model = new SyntheticModel();
        var router = new HybridIntentRouter(new AiDeterministicPlanner(), model, NullLogger<HybridIntentRouter>.Instance);
        var pipeline = new AiConversationPipeline(new VietnameseIntentClassifier(IntentClassificationMode.Off), new AiSafetyGuard());
        HiddenRow[] rows = [new(1, AiActorRole.Patient, "MyBills", Secret), new(2, AiActorRole.Patient, "MyBills", Secret + "2")];
        var scored = Score(rows, model, router, pipeline);
        Check(scored[0].Accepted && !scored[1].Accepted);
        Check(scored[0].T1 == "MyBills" && scored[0].T2 == "MyBills" && scored[0].Source == "model");
        Check(scored[1].T1 == "MyBills" && scored[1].T2 == "MyBills" && scored[1].Source == "fallback");
        Check(model.Calls.All(x => x == AiActorRole.Patient));
    }

    private sealed class SyntheticModel : IRoleIntentModel
    {
        public bool IsAvailable => true;
        public double Threshold => .75;
        public List<AiActorRole> Calls { get; } = [];
        public bool IsLabelAllowed(string label, AiActorRole role) => label == "MyBills" && role == AiActorRole.Patient;
        public RoleIntentModelPrediction Predict(string text, AiActorRole role)
        {
            Calls.Add(role);
            return new("MyBills", text.EndsWith('2') ? .749 : .75, new Dictionary<string, double> { ["MyBills"] = 1 });
        }
    }

    private static void ErrorsContainNoCellOrArgumentText() => InTemporaryDirectory(directory =>
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Check(Execute(["--unknown", Secret], output, error) == 1);
        Check(!output.ToString().Contains(Secret, StringComparison.Ordinal) && !error.ToString().Contains(Secret, StringComparison.Ordinal));
        var path = Path.Combine(directory, "synthetic.xlsx");
        File.WriteAllText(path, Secret);
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        Check(Execute(["--file", path], output, error) == 1);
        Check(!output.ToString().Contains(Secret, StringComparison.Ordinal) && !error.ToString().Contains(Secret, StringComparison.Ordinal));
    });

    private static MemoryStream Workbook(string[][] rows, bool inline = false, bool reordered = false,
        string sheetName = "Câu hỏi", bool omitHeader = false, bool blankNotesHeader = false)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace package = "http://schemas.openxmlformats.org/package/2006/relationships";
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write("xl/workbook.xml", new XElement(ns + "workbook", new XElement(ns + "sheets",
                new XElement(ns + "sheet", new XAttribute("name", sheetName), new XAttribute("sheetId", 7), new XAttribute(rel + "id", "rId7")))));
            Write("xl/_rels/workbook.xml.rels", new XElement(package + "Relationships", new XElement(package + "Relationship",
                new XAttribute("Id", "rId7"), new XAttribute("Target", "/xl/worksheets/hidden.xml"), new XAttribute("Type", rel.NamespaceName + "/worksheet"))));
            string[] headers = ["STT", "Vai trò", "Câu hỏi", "Nhãn đúng", "Ghi chú"];
            if (blankNotesHeader) headers[4] = "";
            var allRows = new List<string[]> { new[] { "Synthetic title", "", "", "", "" } };
            if (!omitHeader) allRows.Add(headers);
            allRows.AddRange(rows);
            int[] order = reordered ? [4, 2, 0, 3, 1] : [0, 1, 2, 3, 4];
            var shared = new List<string>();
            var sheetData = new XElement(ns + "sheetData");
            for (var r = 0; r < allRows.Count; r++)
            {
                var xmlRow = new XElement(ns + "row", new XAttribute("r", r + 1));
                for (var c = 0; c < order.Length; c++)
                {
                    var value = allRows[r][order[c]];
                    if (value.Length == 0) continue;
                    var cell = new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + c)}{r + 1}"));
                    if (order[c] == 0 && r > 1 && int.TryParse(value, out _)) cell.Add(new XElement(ns + "v", value));
                    else if (inline) cell.Add(new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)));
                    else { cell.Add(new XAttribute("t", "s"), new XElement(ns + "v", shared.Count)); shared.Add(value); }
                    xmlRow.Add(cell);
                }
                sheetData.Add(xmlRow);
            }
            Write("xl/worksheets/hidden.xml", new XElement(ns + "worksheet", sheetData));
            if (!inline) Write("xl/sharedStrings.xml", new XElement(ns + "sst", shared.Select(value => new XElement(ns + "si",
                new XElement(ns + "r", new XElement(ns + "t", value[..(value.Length / 2)])),
                new XElement(ns + "r", new XElement(ns + "t", value[(value.Length / 2)..])),
                new XElement(ns + "rPh", new XElement(ns + "t", "phonetic-must-be-ignored"))))));

            void Write(string name, XElement xml)
            {
                using var entry = zip.CreateEntry(name).Open();
                new XDocument(xml).Save(entry);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void InTemporaryDirectory(Action<string> action)
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.GetFullPath(Path.Combine(root, "clinic-hidden-eval-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            if (!directory.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe temporary directory.");
            Directory.Delete(directory, true);
        }
    }

    private static string Fails(Action action)
    {
        try { action(); }
        catch (HiddenEvalException exception) { return exception.Message; }
        throw new InvalidOperationException("Expected validation failure.");
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Synthetic assertion failed."); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < 1e-10);
}
