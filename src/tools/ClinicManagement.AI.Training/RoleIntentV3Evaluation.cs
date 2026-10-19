using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ClinicManagement.AI.Training.HiddenEvalRunner;

namespace ClinicManagement.AI.Training;

public static class RoleIntentV3Evaluation
{
    internal const string Heading = "## Lần chấm 3 (model v3, dữ liệu người viết)";

    internal static string Root()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "backend", "ClinicManagement.sln"))) return directory.FullName;
        throw new InvalidDataException("Repository root unavailable.");
    }

    private static string Project(string root) => Path.Combine(root, "src", "tools", "ClinicManagement.AI.Training");
    internal static RoleIntentModel Model(string root, string version) => new(Options.Create(new RoleIntentModelOptions
    {
        Directory = Path.Combine(Project(root), "models", "role-intent-" + version),
        LabelsPath = Path.Combine(Project(root), "data", RoleIntentDatasetGenerator.LabelsFile)
    }), NullLogger<RoleIntentModel>.Instance);

    private static RoleIntentModelOptions Settings(string root)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "backend", "ClinicManagement.Api", "appsettings.json")));
        return json.RootElement.GetProperty("RoleIntentModel").Deserialize<RoleIntentModelOptions>(RoleIntentModelPipeline.JsonOptions)!;
    }

    private static List<ScoredRow> Router(IReadOnlyList<HiddenRow> rows, RoleIntentModel model, RoleIntentModelOptions settings)
    {
        var router = new HybridIntentRouter(new AiDeterministicPlanner(), model, NullLogger<HybridIntentRouter>.Instance, Options.Create(settings));
        return Score(rows, model, router, new AiConversationPipeline(new VietnameseIntentClassifier(), new AiSafetyGuard()));
    }

    public static void ValidateRouter(string version, string reportPath)
    {
        if (version is not ("v2" or "v3")) throw new InvalidDataException("Router validation version must be v2 or v3.");
        var root = Root();
        var data = Path.Combine(Project(root), "data");
        var rows = RoleIntentModelPipeline.Read<RoleIntentRecord[]>(Path.Combine(data, RoleIntentDatasetGenerator.ValidationFile));
        var cases = new List<HiddenRow>();
        foreach (var row in rows)
            foreach (var role in row.Role == "Chung" ? RoleIntentDatasetGenerator.RoleCodes.Values : [row.Role])
                cases.Add(new(cases.Count + 1, Enum.Parse<AiActorRole>(role), row.Label, row.Text));
        if (rows.Length != 283 || cases.Count != 498) throw new InvalidDataException("Router validation count mismatch.");
        using var model = Model(root, version);
        if (!model.IsAvailable) throw new InvalidDataException("Router model unavailable.");
        var settings = Settings(root);
        if (settings.Threshold != model.Threshold) throw new InvalidDataException("Production threshold differs from validation model.");
        var scored = Router(cases, model, settings);
        RoleIntentModelV3Pipeline.Write(reportPath, new
        {
            version, samples = scored.Count, model.Threshold, settings.OverrideEnabled, settings.OverrideThreshold,
            t1Correct = scored.Count(x => x.T1 == x.Gold), t1Accuracy = (double)scored.Count(x => x.T1 == x.Gold) / scored.Count,
            strictCorrect = scored.Count(x => x.StrictCorrect), strictAccuracy = (double)scored.Count(x => x.StrictCorrect) / scored.Count,
            lenientCorrect = scored.Count(x => x.LenientCorrect), lenientAccuracy = (double)scored.Count(x => x.LenientCorrect) / scored.Count,
            strictMacroF1 = MacroF1(scored, true), clarification = scored.Count(x => x.AsksAgain),
            sources = scored.GroupBy(x => x.Source).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count())
        });
        Console.WriteLine(File.ReadAllText(reportPath));
    }

    public static int RunHidden(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] != "--version" || args[1] != "v3" || args[2] != "--file")
                throw new HiddenEvalException("Expected --version v3 --file <external.xlsx>.");
            var root = Root();
            var project = Project(root);
            var data = Path.Combine(project, "data");
            var modelDirectory = Path.Combine(project, "models", "role-intent-v3");
            var selection = RoleIntentModelPipeline.Read<RoleIntentV3Selection>(Path.Combine(modelDirectory, RoleIntentModelV3Pipeline.SelectionFile));
            RoleIntentModelV3Pipeline.Load(modelDirectory, out var v3meta);
            RoleIntentModelV2Pipeline.Load(Path.Combine(project, "models", "role-intent-v2"), out var v2meta);
            if (selection.Rule != RoleIntentModelV3Pipeline.SelectionRule || selection.V3ModelSha256 != v3meta.ModelSha256 || selection.V2ModelSha256 != v2meta.ModelSha256 ||
                selection.SelectedV3 != (v3meta.Validation.Filtered.MacroF1 >= v2meta.Validation.Filtered.MacroF1 && v3meta.Validation.Filtered.AcceptedAccuracy >= .9))
                throw new HiddenEvalException("Selection is not frozen correctly.");
            foreach (var (file, sha) in v3meta.DataSha256)
                if (RoleIntentDatasetGenerator.FileSha256(Path.Combine(data, file)) != sha) throw new HiddenEvalException("Frozen input changed.");
            if (!File.Exists(Path.Combine(modelDirectory, RoleIntentModelV3Pipeline.ReportFile))) throw new HiddenEvalException("Complete non-hidden evaluation first.");
            var reportPath = Path.Combine(root, "docs", "ai", "HIDDEN_EVAL.md");
            var previous = File.ReadAllText(reportPath);
            if (previous.Contains(Heading, StringComparison.Ordinal)) throw new HiddenEvalException("Third evaluation already exists; do not rerun.");
            var path = Path.GetFullPath(args[3]);
            var resolved = new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path;
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new HiddenEvalException("Workbook must be outside the repository.");
            using var v2 = Model(root, "v2");
            using var v3 = Model(root, "v3");
            if (!v2.IsAvailable || !v3.IsAvailable) throw new HiddenEvalException("Both frozen models must load.");
            var runtime = selection.SelectedV3 ? v3 : v2;
            var settings = Settings(root);
            if (settings.Threshold != runtime.Threshold) throw new HiddenEvalException("Runtime threshold mismatch.");
            var known = ReadKnownTexts(data);
            var catalog = ReadCatalog(Path.Combine(data, RoleIntentDatasetGenerator.LabelsFile));
            var attempt = Path.Combine(Path.GetTempPath(), "clinic-role-intent-v3-" + v3meta.ModelSha256 + ".attempt");
            using var once = new FileStream(attempt, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            CheckHash(Path.Combine(data, "hidden_eval_manifest.json"), sha256, false);
            stream.Position = 0;
            var rows = ReadWorkbook(stream, catalog);
            var manifest = RoleIntentModelPipeline.Read<Manifest>(Path.Combine(data, "hidden_eval_manifest.json"));
            if (rows.Count != manifest.RowCount || rows.GroupBy(x => x.Role.ToString()).Any(g => manifest.CountsByRole.GetValueOrDefault(g.Key) != g.Count()) ||
                rows.GroupBy(x => x.Label).Any(g => manifest.CountsByLabel.GetValueOrDefault(g.Key) != g.Count())) throw new HiddenEvalException("Hidden manifest counts differ.");
            var included = ExcludeDuplicates(rows, known);
            if (included.Included.Count == 0) throw new HiddenEvalException("No hidden rows after deduplication.");
            var routed = Router(included.Included, runtime, settings);
            var t1v2 = selection.SelectedV3 ? T1(included.Included, v2) : routed;
            var t1v3 = selection.SelectedV3 ? routed : T1(included.Included, v3);
            var scan = ScanQuestions(root, included.Included);
            if (!scan) throw new HiddenEvalException("Question leakage scan failed; no question text emitted.");
            stream.Position = 0;
            if (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != sha256) throw new HiddenEvalException("Workbook changed during evaluation.");
            var report = Report(rows.Count, included, t1v2, t1v3, routed, selection, sha256, v2.Threshold, v3.Threshold);
            File.AppendAllText(reportPath, "\n\n" + report.Replace("\r\n", "\n"), new UTF8Encoding(false));
            Console.Write(report);
            return 0;
        }
        catch (HiddenEvalException exception) { Console.Error.WriteLine(exception.Message); return 1; }
        catch (Exception exception) { Console.Error.WriteLine($"Third hidden evaluation stopped ({exception.GetType().Name}); do not rerun."); return 1; }
    }

    private static List<ScoredRow> T1(IEnumerable<HiddenRow> rows, IRoleIntentModel model) => rows.Select(row =>
    {
        var p = model.Predict(row.Text, row.Role) ?? throw new HiddenEvalException("Frozen T1 prediction unavailable.");
        return new ScoredRow(row.Stt, row.Role, row.Label, p.Label, p.Confidence >= model.Threshold, "", "", false);
    }).ToList();

    internal static bool ScanQuestions(string root, IReadOnlyList<HiddenRow> rows)
    {
        var selected = rows.ToArray();
        for (var i = 0; i < 5 && i < selected.Length; i++)
        {
            var j = RandomNumberGenerator.GetInt32(i, selected.Length);
            (selected[i], selected[j]) = (selected[j], selected[i]);
        }
        var start = new ProcessStartInfo("rg")
        {
            WorkingDirectory = root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "--fixed-strings", "--files-with-matches", "--hidden", "--no-ignore", "--glob", "!.git/**", "--glob", "!**/bin/**", "--glob", "!**/obj/**", "--glob", "!**/node_modules/**", "--file", "-", "." }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new HiddenEvalException("Leakage scanner unavailable.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        foreach (var row in selected.Take(5)) process.StandardInput.WriteLine(row.Text);
        process.StandardInput.Close();
        process.WaitForExit();
        return process.ExitCode == 1 && stdout.GetAwaiter().GetResult().Length == 0 && stderr.GetAwaiter().GetResult().Length == 0;
    }

    internal static string Report(int rowCount, Selection included, IReadOnlyList<ScoredRow> v2, IReadOnlyList<ScoredRow> v3,
        IReadOnlyList<ScoredRow> routed, RoleIntentV3Selection selection, string sha, double v2Threshold, double v3Threshold)
    {
        var text = new StringBuilder();
        text.AppendLine(Heading).AppendLine().AppendLine(Provenance).AppendLine();
        text.AppendLine($"Một lần chạy đọc XLSX, chấm T1 cả v2/v3 trên cùng tập và T2 với runtime đã chọn {selection.RuntimeVersion} trước khi mở đề. SHA-256: `{sha}`.");
        text.AppendLine($"Dòng hợp lệ: {rowCount}; sau loại trùng: {routed.Count}; trùng data: {included.DataDuplicates.Count}; trùng nội bộ: {included.InternalDuplicates.Count}. So trùng với toàn bộ data hiện có, gồm 360 câu người viết.");
        text.AppendLine($"T1 dùng RoleIntentModel thật, role mask và ngưỡng metadata: v2={v2Threshold.ToString(CultureInfo.InvariantCulture)}, v3={v3Threshold.ToString(CultureInfo.InvariantCulture)}. T2 dùng Plan quyết định cuối, context tối thiểu, không memory, không gọi provider/tool; override giữ 0.8.");
        text.AppendLine("Fallback luôn sai. Strict loại hỏi lại; lenient chấp nhận hỏi lại khi không fallback. Macro-F1 dùng hợp nhãn gold/dự đoán, P/R/F1 mẫu số 0 bằng 0. Wilson 95% cho accuracy nhị thức.").AppendLine();
        text.AppendLine("### T1 v2 và v3, tổng và theo vai trò").AppendLine();
        text.AppendLine("| Model | Vai trò | N | Đúng | Accuracy | Macro-F1 | Accepted | Coverage | Accepted accuracy | Không chắc | Wilson 95% |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var (name, scores) in new[] { ("v2", v2), ("v3", v3) })
            foreach (var (role, group) in Groups(scores))
            {
                var correct = group.Count(x => x.T1 == x.Gold);
                var accepted = group.Where(x => x.Accepted).ToArray();
                text.AppendLine($"| {name} | {role} | {group.Count} | {correct} | {Rate(correct, group.Count)} | {Percent(MacroF1(group, false))} | {accepted.Length} | {Rate(accepted.Length, group.Count)} | {Rate(accepted.Count(x => x.T1 == x.Gold), accepted.Length)} | {Rate(group.Count - accepted.Length, group.Count)} | {Interval(correct, group.Count)} |");
            }
        text.AppendLine().AppendLine("### T2 runtime đã chọn, tổng và theo vai trò").AppendLine();
        text.AppendLine("| Vai trò | N | Hỏi lại | Strict đúng | Strict accuracy | Strict macro-F1 | Strict Wilson 95% | Lenient đúng | Lenient accuracy | Lenient Wilson 95% | rule | model | fallback | override |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---|---:|---:|---|---:|---:|---:|---:|");
        foreach (var (role, group) in Groups(routed))
        {
            var strict = group.Count(x => x.StrictCorrect);
            var lenient = group.Count(x => x.LenientCorrect);
            text.AppendLine($"| {role} | {group.Count} | {group.Count(x => x.AsksAgain)} | {strict} | {Rate(strict, group.Count)} | {Percent(MacroF1(group, true))} | {Interval(strict, group.Count)} | {lenient} | {Rate(lenient, group.Count)} | {Interval(lenient, group.Count)} | {group.Count(x => x.Source == "rule")} | {group.Count(x => x.Source == "model")} | {group.Count(x => x.Source == "fallback")} | {group.Count(x => x.Source == "override")} |");
        }
        text.AppendLine().AppendLine("Chống lộ đề: 5 câu ngẫu nhiên truyền qua stdin trong bộ nhớ cho rg --fixed-strings; 0 file trùng. Không ghi mẫu ra đĩa. SHA-256 workbook vẫn khớp manifest sau chấm.");
        text.AppendLine("Giới hạn: tập của một người viết, ít mẫu/nhãn, các câu có thể tương quan nên Wilson chỉ mang tính mô tả. Lenient đo chấp nhận hỏi lại, không chứng minh đúng intent. Dữ liệu người viết đã được tác giả rà soát và viết lại hai câu trước nộp; không khẳng định blind hoàn toàn. Không sửa model, dữ liệu, ngưỡng hoặc code sau khi xem kết quả.");
        return text.ToString();
    }

    private static IEnumerable<(string Role, IReadOnlyList<ScoredRow> Rows)> Groups(IReadOnlyList<ScoredRow> rows)
    {
        yield return ("Tổng", rows);
        foreach (var role in RoleIntentDatasetGenerator.RoleCodes.Values.Select(Enum.Parse<AiActorRole>))
            yield return (role.ToString(), rows.Where(x => x.Role == role).ToArray());
    }
    private static string Percent(double value) => double.IsNaN(value) ? "N/A" : (value * 100).ToString("F2", CultureInfo.InvariantCulture) + "%";
    private static string Rate(int count, int n) => n == 0 ? "N/A" : Percent((double)count / n);
    private static string Interval(int count, int n) { var (low, high) = Wilson(count, n); return $"[{Percent(low)}, {Percent(high)}]"; }
}
