using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.AI.Training;

public static class HiddenEvalRunner
{
    internal const string Provenance = "Viết tay bởi Nguyễn Thanh Vũ, 2026-10-18, không có AI hỗ trợ; nằm ngoài repo";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlyDictionary<string, AiActorRole> Roles = new Dictionary<string, AiActorRole>(StringComparer.Ordinal)
    {
        ["Bệnh nhân"] = AiActorRole.Patient, ["Lễ tân"] = AiActorRole.Receptionist,
        ["Bác sĩ"] = AiActorRole.Doctor, ["Kỹ thuật viên"] = AiActorRole.DiagnosticTechnician,
        ["Dược sĩ"] = AiActorRole.Pharmacist, ["Quản trị"] = AiActorRole.Admin
    };

    public static int Run(string[] args) => Execute(args, Console.Out, Console.Error);

    internal static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                output.WriteLine($"Hidden eval self-tests: {HiddenEvalRunnerTests.Run()} passed.");
                return 0;
            }
            string? file = null;
            var update = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--file" && file is null && i + 1 < args.Length) file = args[++i];
                else if (args[i] == "--update-manifest" && !update) update = true;
                else throw new HiddenEvalException("Tham số không hợp lệ.");
            }
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                throw new HiddenEvalException("Không tìm thấy file đề; dừng.");
            var root = RepositoryRoot();
            var fullPath = Path.GetFullPath(file);
            var resolvedPath = new FileInfo(fullPath).ResolveLinkTarget(true)?.FullName ?? fullPath;
            if (IsWithin(fullPath, root) || IsWithin(resolvedPath, root))
                throw new HiddenEvalException("File đề phải nằm ngoài repo.");
            var project = Path.Combine(root, "src", "tools", "ClinicManagement.AI.Training");
            var reportPath = Path.Combine(root, "docs", "ai", "HIDDEN_EVAL.md");
            var previousReport = File.ReadAllText(reportPath);
            EnsureSecondRunAbsent(previousReport);
            var data = Path.Combine(project, "data");
            var manifestPath = Path.Combine(data, "hidden_eval_manifest.json");
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            CheckHash(manifestPath, sha, update);
            stream.Position = 0;
            var catalog = ReadCatalog(Path.Combine(data, "role_intent_labels_v1.json"));
            var rows = ReadWorkbook(stream, catalog);
            if (rows.Count == 0) throw new HiddenEvalException("Bộ đề không có dòng hợp lệ; dừng.");
            var selection = ExcludeDuplicates(rows, ReadKnownTexts(data));
            if (selection.Included.Count == 0) throw new HiddenEvalException("N = 0 sau loại trùng; dừng.");

            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "backend", "ClinicManagement.Api", "appsettings.json")));
            var routerSettings = settings.RootElement.GetProperty("RoleIntentModel").Deserialize<RoleIntentModelOptions>(JsonOptions)!;
            var modelOptions = new RoleIntentModelOptions
            {
                Directory = Path.Combine(project, "models", "role-intent-v2"),
                LabelsPath = Path.Combine(data, "role_intent_labels_v1.json")
            };
            using var model = new RoleIntentModel(Options.Create(modelOptions), NullLogger<RoleIntentModel>.Instance);
            if (!model.IsAvailable) throw new HiddenEvalException("Model v2 không khả dụng; dừng.");
            if (routerSettings.Threshold.HasValue && routerSettings.Threshold.Value != model.Threshold)
                throw new HiddenEvalException("Ngưỡng production khác metadata; dừng để tránh chấm sai cấu hình.");
            var router = new HybridIntentRouter(new AiDeterministicPlanner(), model,
                NullLogger<HybridIntentRouter>.Instance, Options.Create(routerSettings));
            var pipeline = new AiConversationPipeline(new VietnameseIntentClassifier(), new AiSafetyGuard());
            LockManifest(manifestPath, sha, rows, update);
            var scored = Score(selection.Included, model, router, pipeline);
            var report = BuildReport(rows.Count, selection, scored, catalog.Keys, model.Threshold,
                routerSettings.OverrideEnabled, routerSettings.OverrideThreshold);
            File.WriteAllText(reportPath, AppendSecondRun(previousReport, report), new UTF8Encoding(false));
            output.Write(report);
            return 0;
        }
        catch (HiddenEvalException exception) { error.WriteLine(exception.Message); return 1; }
        // XML, model and IO exceptions can contain cell values; only the failure type may leave this boundary.
        catch (Exception exception) { error.WriteLine($"Hidden eval dừng ({exception.GetType().Name})."); return 1; }
    }

    internal static Dictionary<string, HashSet<string>> ReadCatalog(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.EnumerateArray().ToDictionary(
            x => x.GetProperty("label").GetString()!,
            x => x.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    internal static List<HiddenRow> ReadWorkbook(Stream stream, IReadOnlyDictionary<string, HashSet<string>> catalog)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var workbook = LoadXml(archive, "xl/workbook.xml");
        var sheet = workbook.Descendants(Spreadsheet + "sheet").SingleOrDefault(x => (string?)x.Attribute("name") == "Câu hỏi")
            ?? throw new HiddenEvalException("Không tìm thấy sheet Câu hỏi.");
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var id = (string?)sheet.Attribute(relationships + "id");
        var relation = LoadXml(archive, "xl/_rels/workbook.xml.rels").Root!.Elements()
            .Single(x => (string?)x.Attribute("Id") == id);
        if ((string?)relation.Attribute("TargetMode") == "External") throw new HiddenEvalException("Sheet external không được hỗ trợ.");
        var target = (string?)relation.Attribute("Target") ?? throw new HiddenEvalException("Sheet thiếu target.");
        var sheetPath = new Uri(new Uri("https://xlsx.local/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
        var shared = archive.GetEntry("xl/sharedStrings.xml") is null ? [] : LoadXml(archive, "xl/sharedStrings.xml")
            .Descendants(Spreadsheet + "si").Select(RichText).ToArray();
        var headers = new[] { "STT", "Vai trò", "Câu hỏi", "Nhãn đúng", "Ghi chú" };
        Dictionary<string, string>? columns = null;
        var rows = new List<HiddenRow>();
        var ids = new HashSet<int>();
        var errors = new List<string>();
        var physicalRow = 0;
        foreach (var row in LoadXml(archive, sheetPath).Descendants(Spreadsheet + "row"))
        {
            physicalRow++;
            var cells = row.Elements(Spreadsheet + "c").ToDictionary(
                c => Regex.Replace((string?)c.Attribute("r") ?? "", "[0-9]", ""),
                c => CellValue(c, shared).Trim(), StringComparer.Ordinal);
            if (columns is null)
            {
                if (headers.All(h => cells.Values.Contains(h, StringComparer.Ordinal)))
                    columns = headers.ToDictionary(h => h, h => cells.Single(c => c.Value == h).Key, StringComparer.Ordinal);
                else if (headers.Take(4).Select((h, index) => cells.GetValueOrDefault(((char)('A' + index)).ToString(), "") == h).All(x => x)
                    && string.IsNullOrWhiteSpace(cells.GetValueOrDefault("E", "")))
                    columns = headers.Select((h, index) => (Header: h, Column: ((char)('A' + index)).ToString()))
                        .ToDictionary(x => x.Header, x => x.Column, StringComparer.Ordinal);
                continue;
            }
            string Cell(string header) => cells.GetValueOrDefault(columns[header], "");
            if (headers.Skip(1).All(h => string.IsNullOrWhiteSpace(Cell(h))) ||
                Cell("Ghi chú").Equals("VÍ DỤ", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(Cell("STT"), NumberStyles.None, CultureInfo.InvariantCulture, out var stt) || stt <= 0)
            {
                errors.Add($"STT không hợp lệ tại dòng {physicalRow}.");
                continue;
            }
            if (!ids.Add(stt)) { errors.Add($"STT {stt}: lặp STT."); continue; }
            if (!Roles.TryGetValue(Cell("Vai trò"), out var role)) { errors.Add($"STT {stt}: vai trò không hợp lệ."); continue; }
            var label = Cell("Nhãn đúng");
            if (!catalog.TryGetValue(label, out var allowed) || !allowed.Contains(role.ToString()))
            {
                errors.Add($"STT {stt}: nhãn không hợp lệ với vai trò.");
                continue;
            }
            if (Normalize(Cell("Câu hỏi")).Length == 0) { errors.Add($"STT {stt}: thiếu câu hỏi."); continue; }
            rows.Add(new HiddenRow(stt, role, label, Cell("Câu hỏi")));
        }
        if (columns is null) throw new HiddenEvalException("Thiếu cột bắt buộc.");
        if (errors.Count > 0) throw new HiddenEvalException(string.Join(Environment.NewLine, errors));
        return rows;
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        using var stream = (archive.GetEntry(path) ?? throw new HiddenEvalException("XLSX thiếu XML bắt buộc.")).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string RichText(XElement element) => string.Concat(element.Descendants(Spreadsheet + "t")
        .Where(t => !t.Ancestors(Spreadsheet + "rPh").Any()).Select(t => t.Value));

    private static string CellValue(XElement cell, string[] shared)
    {
        var type = (string?)cell.Attribute("t");
        var value = (string?)cell.Element(Spreadsheet + "v") ?? "";
        if (type == "s") return shared[int.Parse(value, CultureInfo.InvariantCulture)];
        return type == "inlineStr" ? RichText(cell) : value;
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var plain = string.Concat(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        return Regex.Replace(plain.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim().TrimEnd('?', '.', '!', ' ');
    }

    internal static HashSet<string> ReadKnownTexts(string directory)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == "hidden_eval_manifest.json") continue;
            if (Path.GetExtension(file).Equals(".tsv", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadLines(file).Skip(1))
                    foreach (var value in line.Split('\t')) Add(value);
            }
            else if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                Visit(json.RootElement);
            }
        }
        return known;

        void Add(string value) { var normalized = Normalize(value); if (normalized.Length > 0) known.Add(normalized); }
        void Visit(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.String) Add(item.GetString()!);
            else if (item.ValueKind == JsonValueKind.Object) foreach (var property in item.EnumerateObject()) Visit(property.Value);
            else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) Visit(child);
        }
    }

    internal static Selection ExcludeDuplicates(IEnumerable<HiddenRow> rows, HashSet<string> known)
    {
        var included = new List<HiddenRow>();
        var dataDuplicates = new List<int>();
        var internalDuplicates = new List<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var normalized = Normalize(row.Text);
            if (known.Contains(normalized)) dataDuplicates.Add(row.Stt);
            else if (!seen.Add(normalized)) internalDuplicates.Add(row.Stt);
            else included.Add(row);
        }
        return new(included, dataDuplicates, internalDuplicates);
    }

    internal static void CheckHash(string path, string sha, bool update)
    {
        if (!File.Exists(path) || update) return;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new HiddenEvalException("Manifest không hợp lệ.");
        if (!string.Equals(manifest.Sha256, sha, StringComparison.Ordinal))
            throw new HiddenEvalException("SHA-256 khác manifest đã khóa; dừng. Chỉ dùng --update-manifest khi được yêu cầu khóa lại.");
    }

    internal static void LockManifest(string path, string sha, IReadOnlyList<HiddenRow> rows, bool update)
    {
        CheckHash(path, sha, update);
        var manifest = new Manifest(sha, rows.Count,
            rows.GroupBy(r => r.Role.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            rows.GroupBy(r => r.Label).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Provenance);
        if (File.Exists(path) && !update)
        {
            var previous = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions)!;
            if (previous.RowCount != manifest.RowCount || previous.Provenance != Provenance ||
                !SameCounts(previous.CountsByRole, manifest.CountsByRole) || !SameCounts(previous.CountsByLabel, manifest.CountsByLabel))
                throw new HiddenEvalException("Số liệu manifest không khớp; dừng.");
            return;
        }
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static bool SameCounts(Dictionary<string, int> left, Dictionary<string, int> right) =>
        left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var value) && x.Value == value);

    internal static List<ScoredRow> Score(IReadOnlyList<HiddenRow> rows, IRoleIntentModel model,
        HybridIntentRouter router, AiConversationPipeline pipeline)
    {
        var result = new List<ScoredRow>();
        foreach (var row in rows)
        {
            var prediction = model.Predict(row.Text, row.Role);
            if (prediction is null) throw new HiddenEvalException($"STT {row.Stt}: model không trả dự đoán; dừng.");
            router.Reset();
            var analysis = pipeline.Analyze(row.Text);
            var decision = router.Plan(new AiCopilotPlanningContext { Role = row.Role, NormalizedMessage = analysis.NormalizedText, Analysis = analysis });
            var route = router.LastRoute ?? throw new HiddenEvalException($"STT {row.Stt}: thiếu HybridIntentRoute; dừng.");
            if (route.Source is not ("rule" or "model" or "fallback" or "override"))
                throw new HiddenEvalException($"STT {row.Stt}: Source không hợp lệ; dừng.");
            result.Add(new(row.Stt, row.Role, row.Label, SafeLabel(prediction.Label), prediction.Confidence >= model.Threshold,
                SafeLabel(route.Label), route.Source, AsksForClarification(decision), FinalDecisionLabel(decision, row.Role)));
        }
        return result;
    }

    private static string SafeLabel(string label) => Regex.IsMatch(label, @"\A[A-Za-z][A-Za-z0-9_]*\z")
        ? label : throw new HiddenEvalException("Dự đoán không phải mã nhãn; dừng.");

    internal static bool AsksForClarification(AiPlannerDecision decision) => decision.Clarification is not null ||
        MissingContext(decision.ErrorCode) || MissingContext(decision.SubIntent);

    private static bool MissingContext(string? code) => code is AiPlannerErrorCodes.ResourceContextRequired or
        "MissingAssignedCase" or "MissingPrescriptionForPayment" or "MissingAppointmentCode";

    internal static string FinalDecisionLabel(AiPlannerDecision decision, AiActorRole role)
    {
        if (decision.RequiresProvider) return "ProviderRequired";
        if (decision.ToolCalls.Count == 1)
        {
            var tool = decision.ToolCalls[0].Name;
            if (tool == "clinic.search_knowledge") return "ClinicKnowledge";
            if (tool == "reception.lookup_appointment") return "LookupAppointment";
            foreach (var (label, code) in HybridIntentRouter.SuggestionCodes)
                if (AiSuggestionCatalog.Find(code, role)?.ToolName == tool) return label;
            return "UnmappedDecision";
        }
        if (decision.ToolCalls.Count > 1) return "UnmappedDecision";
        return decision.SubIntent switch
        {
            "Greeting" => "Greeting", "RoleHelp" => "Help", "BookingWizard" => "StartBooking",
            _ => SafeLabel(decision.Intent)
        };
    }

    private const string SecondRunHeading = "## Lần chấm 2 (sau sửa router)";
    private static void EnsureSecondRunAbsent(string previous)
    {
        if (previous.Contains(SecondRunHeading, StringComparison.Ordinal))
            throw new HiddenEvalException("Đã có lần chấm 2; dừng để tránh chấm lại.");
    }

    internal static string AppendSecondRun(string previous, string report)
    {
        EnsureSecondRunAbsent(previous);
        return previous.TrimEnd() + Environment.NewLine + Environment.NewLine + report;
    }

    internal static string BuildReport(int rowCount, Selection selection, IReadOnlyList<ScoredRow> scored,
        IEnumerable<string> catalog, double threshold, bool overrideEnabled, double? overrideThreshold)
    {
        var report = new StringBuilder();
        report.AppendLine(SecondRunHeading);
        report.AppendLine();
        report.AppendLine(Provenance);
        report.AppendLine();
        report.AppendLine($"Số dòng hợp lệ trước loại trùng: {rowCount}. N: {scored.Count}. Số câu bị loại: {selection.DataDuplicates.Count + selection.InternalDuplicates.Count}.");
        report.AppendLine($"STT trùng data: {Ids(selection.DataDuplicates)}. STT trùng nội bộ: {Ids(selection.InternalDuplicates)}.");
        report.AppendLine("Manifest đếm toàn bộ dòng hợp lệ trước loại trùng; dòng trống (kể cả chỉ điền sẵn STT) và VÍ DỤ không được tính.");
        report.AppendLine("Nếu bốn header STT/Vai trò/Câu hỏi/Nhãn đúng nằm đúng A–D và header E trống, E vẫn được đọc làm Ghi chú; cột bổ sung được bỏ qua.");
        report.AppendLine("So khớp trùng trên các ô TSV và giá trị chuỗi JSON trong data (đệ quy), không phân biệt vai trò; giữ lần đầu nếu trùng nội bộ.");
        report.AppendLine();
        report.AppendLine($"T1: RoleIntentModel v2 thật, lọc nhãn theo vai trò, ngưỡng metadata = {Number(threshold)}; accepted khi confidence >= ngưỡng.");
        report.AppendLine($"T2: HybridIntentRouter.Plan, AiConversationPipeline mặc định (Shadow), role của từng dòng; resource rỗng, không memory. OverrideEnabled = {overrideEnabled}; OverrideThreshold = {(overrideThreshold.HasValue ? Number(overrideThreshold.Value) : "N/A")} từ appsettings.json.");
        report.AppendLine("T2 dùng quyết định Plan trả về: tool thực tế được map về nhãn theo vai trò; Greeting/RoleHelp/BookingWizard dùng nhãn của handler. RequiresProvider được ghi ProviderRequired, tool không map được ghi UnmappedDecision. LastRoute.Label chỉ là telemetry, không dùng thay cho quyết định cuối.");
        report.AppendLine("Source=fallback luôn sai ở cả strict và lenient, kể cả telemetry label trùng gold. Hỏi lại khi có Clarification hoặc ErrorCode/SubIntent thuộc RESOURCE_CONTEXT_REQUIRED, MissingAssignedCase, MissingPrescriptionForPayment, MissingAppointmentCode.");
        report.AppendLine("T2 strict: chỉ đúng khi không fallback, không hỏi lại, và nhãn quyết định cuối trùng gold. T2 lenient: đúng theo strict hoặc hỏi lại (vẫn loại fallback). Lenient là mức chấp nhận được theo quy ước, không chứng minh hiểu đúng intent. Không gọi provider hoặc thực thi tool.");
        report.AppendLine("Macro-F1 bình quân các nhãn xuất hiện trong gold hoặc dự đoán của tầng/phạm vi tương ứng; chia cho 0 cho P/R/F1 được tính là 0.");
        report.AppendLine();
        report.AppendLine("### Tổng và theo vai trò");
        report.AppendLine();
        report.AppendLine("| Phạm vi | N | T1 đúng | T1 accuracy | T1 macro-F1 | Coverage | Accepted accuracy | Không chắc | T1 Wilson 95% | Hỏi lại | T2 strict đúng | T2 strict accuracy | T2 strict macro-F1 | T2 strict Wilson 95% | T2 lenient đúng | T2 lenient accuracy | T2 lenient Wilson 95% |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---|---:|---:|---|");
        Summary("Tổng", scored);
        foreach (var role in Roles.Values) Summary(role.ToString(), scored.Where(x => x.Role == role).ToArray());
        report.AppendLine();
        report.AppendLine("### Source T2");
        report.AppendLine();
        report.AppendLine("Source override được giữ riêng theo production (thuộc nhánh dùng model).");
        report.AppendLine();
        report.AppendLine("| Phạm vi | rule | model | fallback | override |");
        report.AppendLine("|---|---:|---:|---:|---:|");
        Sources("Tổng", scored);
        foreach (var role in Roles.Values) Sources(role.ToString(), scored.Where(x => x.Role == role).ToArray());
        report.AppendLine();
        report.AppendLine("### P/R/F1 theo nhãn (T2 strict)");
        report.AppendLine();
        report.AppendLine("| Nhãn | Support | T1 P | T1 R | T1 F1 | T2 P | T2 R | T2 F1 |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var label in catalog.Concat(scored.Select(x => x.T1)).Concat(scored.Select(x => x.StrictPrediction)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var t1 = PerLabel(scored, label, false);
            var t2 = PerLabel(scored, label, true);
            report.AppendLine($"| {label} | {scored.Count(x => x.Gold == label)} | {Percent(t1.P)} | {Percent(t1.R)} | {Percent(t1.F1)} | {Percent(t2.P)} | {Percent(t2.R)} | {Percent(t2.F1)} |");
        }
        foreach (var router in new[] { false, true })
        {
            report.AppendLine();
            report.AppendLine($"### Top 10 cặp nhầm {(router ? "T2 strict" : "T1")}");
            report.AppendLine();
            report.AppendLine("| Nhãn đúng | Nhãn dự đoán | Số lượng |");
            report.AppendLine("|---|---|---:|");
            foreach (var pair in scored.Where(x => x.Gold != Predicted(x, router))
                .GroupBy(x => (x.Gold, Prediction: Predicted(x, router)))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Gold, StringComparer.Ordinal).ThenBy(g => g.Key.Prediction, StringComparer.Ordinal).Take(10))
                report.AppendLine($"| {pair.Key.Gold} | {pair.Key.Prediction} | {pair.Count()} |");
        }
        report.AppendLine();
        report.AppendLine("### Các STT dự đoán sai (T1 hoặc T2 strict)");
        report.AppendLine();
        report.AppendLine("| STT | Vai trò | Nhãn đúng | T1 dự đoán | T2 strict dự đoán | Hỏi lại | T2 lenient đúng |");
        report.AppendLine("|---:|---|---|---|---|---:|---:|");
        foreach (var row in scored.Where(x => x.Gold != x.T1 || !x.StrictCorrect).OrderBy(x => x.Stt))
            report.AppendLine($"| {row.Stt} | {row.Role} | {row.Gold} | {row.T1} | {row.StrictPrediction} | {(row.AsksAgain ? 1 : 0)} | {(row.LenientCorrect ? 1 : 0)} |");
        report.AppendLine();
        report.AppendLine("### Giới hạn");
        report.AppendLine();
        report.AppendLine("Bộ đề do 1 người viết, khoảng 4–6 câu/nhãn nên khoảng tin cậy rộng; không đại diện đầy đủ người dùng hay phân bố production. Wilson 95% là khoảng cho accuracy nhị thức, giả định các câu độc lập; câu cùng người viết có thể tương quan.");
        report.AppendLine("Context tối thiểu không có ca bệnh/đơn thuốc được xác minh; router có thể yêu cầu làm rõ. Strict và lenient lần này khác định nghĩa T2 của lần 1 nên không so trực tiếp thành mức cải thiện router. Không chỉnh gì thêm sau khi xem kết quả của lần chấm này.");
        report.AppendLine();
        report.AppendLine("Kiểm thử runner (XLSX tự tạo, không đọc đề thật): `dotnet test src/tools/ClinicManagement.AI.Training/HiddenEvalRunnerTests.proj --configuration Release`. MSBuild gọi self-test theo pattern Phase4 của tool, không thêm NuGet package.");
        return report.ToString();

        void Summary(string name, IReadOnlyList<ScoredRow> rows)
        {
            var n = rows.Count;
            var t1Correct = rows.Count(x => x.T1 == x.Gold);
            var t2Correct = rows.Count(x => x.StrictCorrect);
            var lenientCorrect = rows.Count(x => x.LenientCorrect);
            var accepted = rows.Where(x => x.Accepted).ToArray();
            report.AppendLine($"| {name} | {n} | {t1Correct} | {Rate(t1Correct, n)} | {Percent(MacroF1(rows, false))} | {Rate(accepted.Length, n)} | {Rate(accepted.Count(x => x.T1 == x.Gold), accepted.Length)} | {Rate(n - accepted.Length, n)} | {Interval(t1Correct, n)} | {rows.Count(x => x.AsksAgain)} | {t2Correct} | {Rate(t2Correct, n)} | {Percent(MacroF1(rows, true))} | {Interval(t2Correct, n)} | {lenientCorrect} | {Rate(lenientCorrect, n)} | {Interval(lenientCorrect, n)} |");
        }
        void Sources(string name, IReadOnlyList<ScoredRow> rows)
        {
            string Source(string source) => $"{rows.Count(x => x.Source == source)} ({Rate(rows.Count(x => x.Source == source), rows.Count)})";
            report.AppendLine($"| {name} | {Source("rule")} | {Source("model")} | {Source("fallback")} | {Source("override")} |");
        }
    }

    internal static (double P, double R, double F1) PerLabel(IReadOnlyList<ScoredRow> rows, string label, bool router)
    {
        var tp = rows.Count(x => x.Gold == label && Predicted(x, router) == label);
        var actual = rows.Count(x => x.Gold == label);
        var predicted = rows.Count(x => Predicted(x, router) == label);
        var p = predicted == 0 ? 0 : (double)tp / predicted;
        var r = actual == 0 ? 0 : (double)tp / actual;
        return (p, r, p + r == 0 ? 0 : 2 * p * r / (p + r));
    }

    internal static double MacroF1(IReadOnlyList<ScoredRow> rows, bool router) => rows.Count == 0 ? double.NaN :
        rows.Select(x => x.Gold).Concat(rows.Select(x => Predicted(x, router))).Distinct(StringComparer.Ordinal)
            .Average(label => PerLabel(rows, label, router).F1);

    private static string Predicted(ScoredRow row, bool router) => router ? row.StrictPrediction : row.T1;

    internal static (double Low, double High) Wilson(int correct, int n)
    {
        if (n == 0) return (double.NaN, double.NaN);
        const double z = 1.959963984540054;
        var p = (double)correct / n;
        var denominator = 1 + z * z / n;
        var center = (p + z * z / (2 * n)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denominator;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Percent(double value) => double.IsFinite(value) ? (100 * value).ToString("0.00", CultureInfo.InvariantCulture) + "%" : "N/A";
    private static string Rate(int count, int total) => total == 0 ? "N/A" : Percent((double)count / total);
    private static string Interval(int count, int total) { var (low, high) = Wilson(count, total); return total == 0 ? "N/A" : $"[{Percent(low)}, {Percent(high)}]"; }
    private static string Ids(IReadOnlyList<int> ids) => ids.Count == 0 ? "0" : string.Join(", ", ids.Order());
    private static bool IsWithin(string path, string root) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "src", "backend", "ClinicManagement.sln"))) return current.FullName;
        throw new HiddenEvalException("Không xác định được repo.");
    }

    internal sealed class HiddenRow(int stt, AiActorRole role, string label, string text)
    {
        public int Stt { get; } = stt;
        public AiActorRole Role { get; } = role;
        public string Label { get; } = label;
        [System.Text.Json.Serialization.JsonIgnore] public string Text { get; } = text;
    }

    internal sealed record Selection(List<HiddenRow> Included, List<int> DataDuplicates, List<int> InternalDuplicates);
    internal sealed record ScoredRow(int Stt, AiActorRole Role, string Gold, string T1, bool Accepted, string T2, string Source,
        bool AsksAgain = false, string? DecisionLabel = null)
    {
        public string StrictPrediction => Source == "fallback" ? "Fallback" : AsksAgain ? "Clarification" : DecisionLabel ?? T2;
        public bool StrictCorrect => Source != "fallback" && !AsksAgain && StrictPrediction == Gold;
        public bool LenientCorrect => Source != "fallback" && (AsksAgain || StrictCorrect);
    }
    internal sealed record Manifest(string Sha256, int RowCount, Dictionary<string, int> CountsByRole, Dictionary<string, int> CountsByLabel, string LockedDate, string Provenance);
    internal sealed class HiddenEvalException(string message) : Exception(message);
}
