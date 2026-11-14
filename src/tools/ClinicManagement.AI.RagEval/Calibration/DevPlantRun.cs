using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record DevVariant(DevAnswer Answer, string Edited, string Kind, string Position, string? OriginalSentence = null);

public sealed class DevPlantSelection
{
    private readonly HashSet<(string Code, string Side)> _selected = [];
    private readonly List<DevVariant> _variants = [];
    public IReadOnlyList<DevVariant> Variants => _variants;

    public bool Contains(DevAnswer answer) => _selected.Contains((answer.Code, answer.Side));

    public bool TryAdd(DevAnswer answer, DevEdit edit, string kind)
    {
        if (Contains(answer)) return false;
        var edited = edit.Apply(answer.Text);
        if (edited == answer.Text) return false;
        _selected.Add((answer.Code, answer.Side));
        _variants.Add(new(answer, edited, kind, DevPlantRun.Positions[DevEdits.Position(edit.Offset, answer.Text.Length)]));
        return true;
    }
}

internal sealed record DevPlantResult(DevAnswer[] Originals, DevVariant[] Variants, DevControl[] Controls,
    int Eligible, IReadOnlyDictionary<string, EvaluationQuestion> Questions)
{
    public IEnumerable<DevVariant> Plants => Variants.Where(v => v.Kind is not (DevPlantRun.UnsafeKind or DevPlantRun.ParaphraseKind));
}

internal sealed class DevPlantRun
{
    public static IReadOnlyList<string> Positions { get; } = ["Đầu", "Giữa", "Cuối"];
    internal const string NumberKind = "Sai số liệu";
    internal const string NegationKind = "Phủ định / đảo nghĩa";
    internal const string ConditionKind = "Đổi đối tượng / điều kiện";
    internal const string AddedTrueKind = "Ngoài nguồn / dự kiến đúng y khoa";
    internal const string AddedFalseKind = "Ngoài nguồn / dự kiến sai y khoa";
    internal const string UnsafeKind = "Không an toàn";
    internal const string ParaphraseKind = "Diễn đạt lại đúng nguồn";
    internal static IReadOnlyList<(string Kind, int Target)> Targets { get; } =
    [
        (NumberKind, 25), (NegationKind, 15), (ConditionKind, 20), (AddedTrueKind, 20), (AddedFalseKind, 20),
        (UnsafeKind, 25), (ParaphraseKind, 60)
    ];

    private readonly IReadOnlyDictionary<string, EvaluationQuestion> _questions;
    private readonly IReadOnlyDictionary<DevAnswer, ReplayPassage[]> _passages;
    private readonly DevAnswer[] _answers;
    private readonly DevAnswer[] _shuffled;
    private readonly DevAnswer[] _eligible;
    private readonly DevPlantModel _model;
    private readonly DevPlantSelection _selection = new();

    internal DevPlantRun(EvaluationQuestion[] questions, DevAnswer[] answers, DevPlantModel model,
        IReadOnlyDictionary<string, string> corpus, int seed)
    {
        _questions = questions.ToDictionary(q => q.Stt!, StringComparer.Ordinal);
        _answers = answers.OrderBy(a => a.Code, StringComparer.Ordinal).ThenBy(a => a.Side, StringComparer.Ordinal).ToArray();
        _model = model;
        if (answers.Length != questions.Length * 2 || answers.Select(a => (a.Code, a.Side)).Distinct().Count() != answers.Length ||
            answers.Any(a => !_questions.ContainsKey(a.Code) || a.Side is not ("A" or "B") || a.Mode is not ("LLM" or "Trích đoạn")) ||
            answers.GroupBy(a => a.Code).Any(group => group.Select(a => a.Mode).Distinct().Count() != 2))
            throw new InvalidDataException("Dev answers do not match the dev CSV.");

        _passages = _answers.ToDictionary(a => a, a => RecordedPassages(a, corpus));
        _shuffled = (DevAnswer[])_answers.Clone();
        new Random(seed).Shuffle(_shuffled);
        _eligible = _shuffled.Where(a => a.HasContent).ToArray();
    }

    public static async Task RunAsync(string directory, string? outputName, string? compareName, TextWriter output)
    {
        var path = WorkbookPath(directory, outputName ?? "dev-kiem-plant.xlsx");
        StatisticsReport.EnsureNew(path);
        var comparePath = compareName is null ? null : WorkbookPath(directory, compareName);
        if (comparePath is not null && !File.Exists(comparePath))
            throw new FileNotFoundException("The comparison workbook is missing.");

        var check = await DevBlindProvenance.LoadDevCsvAsync(Path.Combine(directory, "dev-kiem.csv"));
        var answers = await LoadAnswersAsync(directory, "dev-kiem");
        var corpus = new MarkdownKnowledgeCatalog().Chunks.ToDictionary(c => c.Id, c => c.Text, StringComparer.Ordinal);
        using var model = new DevPlantModel();
        var digest = await model.GetDigestAsync();
        var run = new DevPlantRun(check, answers, model, corpus, DevPlantModel.Seed);
        var result = await run.GenerateAsync(output);
        if (digest != await model.GetDigestAsync())
            throw new InvalidOperationException("The Ollama model digest changed during dev-plant.");

        DevPlantWorkbook.Save(path, result, digest);
        await ReportIdenticalAsync("dev-kiem", check, answers, output);
        if (File.Exists(Path.Combine(directory, "dev-chinh", DevBlindProvenance.FileName)))
        {
            var main = await DevBlindProvenance.LoadDevCsvAsync(Path.Combine(directory, "dev-chinh.csv"));
            await ReportIdenticalAsync("dev-chinh", main, await LoadAnswersAsync(directory, "dev-chinh"), output);
        }
        if (comparePath is not null) await DevPlantWorkbook.CompareAsync(comparePath, path, output);
        await output.WriteLineAsync(path);
    }

    internal async Task<DevPlantResult> GenerateAsync(TextWriter output)
    {
        AddScriptEdits(NumberKind, 25, DevEdits.Numbers);
        await AddConditionsAsync(20);
        AddScriptEdits(NegationKind, 15, DevEdits.Negations);
        await AddOutsideSourceAsync(20);

        foreach (var (kind, target) in Targets.Take(5))
            await output.WriteLineAsync($"{kind}: {_selection.Variants.Count(v => v.Kind == kind)}/{target}");

        var controls = _answers.SelectMany(a => DevPlantSentences.Find(a, _passages[a])).ToArray();
        var unsafeVariants = await AddUnsafeAsync(25);
        await output.WriteLineAsync($"{UnsafeKind}: {unsafeVariants.Count}/25");
        var paraphrases = await AddParaphrasesAsync(controls, 60);
        await output.WriteLineAsync($"{ParaphraseKind}: {paraphrases.Count}/60");
        var variants = _selection.Variants.Concat(unsafeVariants).Concat(paraphrases).ToArray();
        await output.WriteLineAsync("Loại | Số mẫu | Mục tiêu | Thiếu | LLM | Trích đoạn | Đầu | Giữa | Cuối | Cả câu");
        foreach (var (kind, target) in Targets)
            await ReportVariantsAsync(kind, target, variants.Where(v => v.Kind == kind), output);
        await ReportVariantsAsync("Tổng cài bịa", 100, _selection.Variants, output);
        await output.WriteLineAsync($"Câu gốc: {_answers.Length}; đủ điều kiện cài bịa: {_eligible.Length}; TuDong: {controls.Length}; thiếu so với 200: {Math.Max(0, 200 - controls.Length)}");
        foreach (var group in DevQuestions.Groups)
            await output.WriteLineAsync($"{UnsafeKind} | {group}: {unsafeVariants.Count(v => _questions[v.Answer.Code].Category == group)}");
        return new(_answers, variants, controls, _eligible.Length, _questions);
    }

    private static ReplayPassage[] RecordedPassages(DevAnswer answer, IReadOnlyDictionary<string, string> corpus)
    {
        if (answer.Passages is not null) return answer.Passages;
        // Older exports identified the verbatim chunk and the cited heading, but did not save all retrieved passages.
        var ids = answer.ExactSourceId is { } exact ? new[] { exact } :
            answer.IsExtractive ? [] : answer.SourceIds;
        return ids.Select(id => new ReplayPassage(id, corpus[id], 0)).ToArray();
    }

    private void AddScriptEdits(string kind, int quota, Func<string, DevEdit[]> findEdits)
    {
        var candidates = _eligible.SelectMany(answer => findEdits(answer.Text).Select(edit => (Answer: answer, Edit: edit))).ToArray();
        for (var i = 0; i < quota; i++)
        {
            var best = candidates.Where(c => !_selection.Contains(c.Answer))
                .OrderBy(c => Priority(c.Answer, DevEdits.Position(c.Edit.Offset, c.Answer.Text.Length), kind))
                .FirstOrDefault();
            if (best.Answer is null) break;
            _selection.TryAdd(best.Answer, best.Edit, kind);
        }
    }

    private async Task AddConditionsAsync(int quota)
    {
        var pending = _eligible.Where(a => !_selection.Contains(a)).ToList();
        var selected = 0;
        for (var attempt = 0; attempt < 2 && selected < quota; attempt++)
        {
            var remaining = new List<DevAnswer>();
            while (pending.Count > 0 && selected < quota)
            {
                var position = PreferredPosition(ConditionKind);
                var answer = pending.MinBy(a => (DevEdits.Negations(a.Text).Length > 0,
                    Priority(a, position, ConditionKind)))!;
                pending.Remove(answer);
                var prompt = attempt == 0 ? DevPlantModel.Condition : DevPlantModel.ConditionRetry;
                var reply = await AskAsync(prompt, answer, answer.Text, Positions[position]);
                var edit = DevPlantValidation.Condition(answer.Text, reply);
                if (edit is null && attempt == 0)
                    edit = DevPlantScenarios.ConditionEdits(answer.Text)
                        .MinBy(candidate => Priority(answer, DevEdits.Position(candidate.Offset, answer.Text.Length), ConditionKind));
                if (edit is not null && _selection.TryAdd(answer, edit, ConditionKind)) selected++;
                else remaining.Add(answer);
            }
            pending = remaining;
        }
    }

    private async Task AddOutsideSourceAsync(int quota)
    {
        var requests = new[] { (Kind: AddedTrueKind, Prompt: DevPlantModel.AddedTrue), (Kind: AddedFalseKind, Prompt: DevPlantModel.AddedFalse) };
        var totals = new int[requests.Length];
        var rejected = new HashSet<(DevAnswer Answer, int Request)>();
        for (var round = 0; round < 2 && totals.Min() < quota; round++)
        {
            rejected.Clear();
            while (true)
            {
                var available = Enumerable.Range(0, requests.Length)
                    .Where(index => totals[index] < quota && _eligible.Any(a =>
                        !_selection.Contains(a) && !rejected.Contains((a, index)))).ToArray();
                if (available.Length == 0) break;

                var index = available.MinBy(i => totals[i]);
                var (kind, prompt) = requests[index];
                var (answer, position) = _eligible.Where(a => !_selection.Contains(a) && !rejected.Contains((a, index)))
                    .SelectMany(a => Enumerable.Range(0, Positions.Count).Where(p => p != 1 || DevEdits.HasMiddleBoundary(a.Text))
                        .Select(p => (Answer: a, Position: p)))
                    .MinBy(pair => Priority(pair.Answer, pair.Position, kind));
                var sources = Sources(answer);
                var context = round == 0 ? Positions[position] : $"{Positions[position]}\n{DevPlantModel.AddedRetry}";
                var reply = await AskAsync(prompt, answer, answer.Text, context);
                var sentence = DevPlantValidation.NewSentence(answer.Text, sources, reply?.Text);
                if (sentence is null || index == 1)
                    sentence = DevPlantScenarios.OutsideSentences(_questions[answer.Code].Text + " " + answer.Text, index == 0)
                        .Select(text => DevPlantValidation.NewSentence(answer.Text, sources, text))
                        .FirstOrDefault(text => text is not null) ?? sentence;
                if (sentence is not null && _selection.TryAdd(answer, DevEdits.Insert(answer.Text, sentence, position), kind))
                    totals[index]++;
                else
                    rejected.Add((answer, index));
            }
        }
    }

    private async Task<List<DevVariant>> AddUnsafeAsync(int quota)
    {
        var result = new List<DevVariant>();
        var unique = new HashSet<(string Code, string Text)>();
        var candidates = _shuffled.Where(a => !string.IsNullOrWhiteSpace(a.Text))
            .Where(a => _questions[a.Code].Category == DevQuestions.Emergency ||
                _questions[a.Code].Category == DevQuestions.InScope && a.HasContent)
            .OrderByDescending(a => _questions[a.Code].Category == DevQuestions.Emergency);
        foreach (var answer in candidates)
        {
            var emergency = _questions[answer.Code].Category == DevQuestions.Emergency;
            var reply = await AskAsync(DevPlantModel.Unsafe, answer, answer.Text, emergency ? "Khẩn cấp" : "Trong phạm vi");
            var edits = DevPlantValidation.Unsafe(answer.Text, Sources(answer), reply, emergency);
            foreach (var edit in edits)
            {
                var text = edit.Apply(answer.Text).Trim();
                if (text == answer.Text || !unique.Add((answer.Code, KnowledgeText.Normalize(text, false)))) continue;
                result.Add(new(answer, text, UnsafeKind, Positions[DevEdits.Position(edit.Offset, answer.Text.Length)]));
                break;
            }
            if (result.Count == quota) break;
        }
        return result;
    }

    private async Task<List<DevVariant>> AddParaphrasesAsync(DevControl[] controls, int quota)
    {
        var result = new List<DevVariant>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var prior = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var candidates = controls.DistinctBy(c => KnowledgeText.Normalize(c.Text, false)).ToArray();
        var pending = candidates.ToList();
        for (var round = 0; round < 2 && result.Count < quota; round++)
        {
            var remaining = new List<DevControl>();
            foreach (var control in pending)
            {
                var prompt = round == 0 ? DevPlantModel.Paraphrase : DevPlantModel.ParaphraseRetry;
                var reply = await AskAsync(prompt, control.Answer, control.Text, "");
                var edited = DevPlantValidation.Paraphrase(control.Text, reply?.Text);
                if (!Add(control, edited)) remaining.Add(control);

                if (result.Count == quota) break;
            }
            pending = remaining;
        }

        foreach (var control in candidates)
        {
            if (result.Count == quota) break;
            if (!prior.TryGetValue(control.Text, out var previous)) continue;
            var reply = await AskAsync(DevPlantModel.ParaphraseAlternative, control.Answer, control.Text, string.Join("\n", previous));
            Add(control, DevPlantValidation.Paraphrase(control.Text, reply?.Text));
        }
        return result;

        bool Add(DevControl control, string? edited)
        {
            if (edited is null || !seen.Add(KnowledgeText.Normalize(edited, false))) return false;
            if (!prior.TryGetValue(control.Text, out var previous)) prior[control.Text] = previous = [];
            previous.Add(edited);
            result.Add(new(control.Answer, edited, ParaphraseKind, $"Câu {control.Sentence}", control.Text));
            return true;
        }
    }

    private static Task ReportVariantsAsync(string kind, int target, IEnumerable<DevVariant> variants, TextWriter output)
    {
        var rows = variants.ToArray();
        var distribution = new[] { rows.Count(v => v.Answer.Mode == "LLM"), rows.Count(v => v.Answer.Mode == "Trích đoạn") }
            .Concat(Positions.Select(position => rows.Count(v => v.Position == position)))
            .Append(rows.Count(v => v.OriginalSentence is not null));
        return output.WriteLineAsync($"{kind} | {rows.Length} | {target} | {target - rows.Length} | {string.Join(" | ", distribution)}");
    }

    private int PreferredPosition(string kind) => Enumerable.Range(0, Positions.Count)
        .MinBy(p => _selection.Variants.Count(v => v.Position == Positions[p]) +
            _selection.Variants.Count(v => v.Kind == kind && v.Position == Positions[p]));

    private int Priority(DevAnswer answer, int position, string kind)
    {
        var current = _selection.Variants;
        return 3 * current.Count(v => v.Answer.Mode == answer.Mode) +
            3 * current.Count(v => v.Position == Positions[position]) +
            current.Count(v => v.Kind == kind && v.Answer.Mode == answer.Mode) +
            current.Count(v => v.Kind == kind && v.Position == Positions[position]);
    }

    private string Sources(DevAnswer answer) => ReplayPassage.Format(_passages[answer]);

    private Task<DevModelReply?> AskAsync(string prompt, DevAnswer answer, string text, string context) =>
        _model.GenerateAsync(prompt, _questions[answer.Code], text, Sources(answer), context);

    private static string WorkbookPath(string directory, string name)
    {
        if (name != Path.GetFileName(name) || !name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a workbook filename in --out-dir.");
        return Path.Combine(directory, name);
    }

    private static async Task<DevAnswer[]> LoadAnswersAsync(string directory, string set) =>
        JsonSerializer.Deserialize<DevAnswer[]>(await File.ReadAllTextAsync(Path.Combine(directory, set, DevBlindProvenance.FileName)),
            StatisticsReport.JsonOptions) ?? throw new InvalidDataException($"Missing answers for {set}.");

    private static async Task ReportIdenticalAsync(string set, EvaluationQuestion[] questions, DevAnswer[] answers, TextWriter output)
    {
        var identical = answers.GroupBy(a => a.Code).Where(g => g.Count() == 2 && g.First().Text == g.Last().Text)
            .ToDictionary(g => g.Key, g => g.All(a => !a.HasContent), StringComparer.Ordinal);
        foreach (var group in DevQuestions.Groups)
        {
            var codes = questions.Where(q => q.Category == group).Select(q => q.Stt!).ToArray();
            await output.WriteLineAsync($"{set} | {group} | A=B: {codes.Count(identical.ContainsKey)}/{codes.Length}" +
                $" | theo chính sách: {codes.Count(code => identical.GetValueOrDefault(code))}");
        }
    }
}
