using System.Text.Json;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;

namespace ClinicManagement.AI.RagEval;

public sealed record DevVariant(DevAnswer Answer, string Edited, string Kind, string Position);

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

internal sealed class DevPlantRun
{
    public static IReadOnlyList<string> Positions { get; } = ["Đầu", "Giữa", "Cuối"];
    private const string NumberKind = "Sai số liệu";
    private const string NegationKind = "Phủ định / đảo nghĩa";
    private const string ConditionKind = "Đổi đối tượng / điều kiện";
    private const string AddedTrueKind = "Ngoài nguồn / dự kiến đúng y khoa";
    private const string AddedFalseKind = "Ngoài nguồn / dự kiến sai y khoa";
    private const string UnsafeKind = "Không an toàn";
    private const string ParaphraseKind = "Diễn đạt lại đúng nguồn";
    private static readonly (string Kind, int Target)[] Targets =
    [
        (NumberKind, 25), (NegationKind, 15), (ConditionKind, 20), (AddedTrueKind, 20), (AddedFalseKind, 20),
        (UnsafeKind, 25), (ParaphraseKind, 60)
    ];

    private readonly IReadOnlyDictionary<string, EvaluationQuestion> _questions;
    private readonly IReadOnlyDictionary<string, KnowledgeChunk> _chunks;
    private readonly DevAnswer[] _answers;
    private readonly DevPlantModel _model;
    private readonly Random _random = new(DevQuestions.Seed);
    private readonly DevPlantSelection _selection = new();

    private DevPlantRun(EvaluationQuestion[] questions, DevAnswer[] answers, DevPlantModel model)
    {
        _questions = questions.ToDictionary(q => q.Stt!, StringComparer.Ordinal);
        _chunks = new MarkdownKnowledgeCatalog().Chunks.ToDictionary(chunk => chunk.Id, StringComparer.Ordinal);
        _answers = answers;
        _model = model;
        if (answers.Length != questions.Length * 2 || answers.Select(a => (a.Code, a.Side)).Distinct().Count() != answers.Length ||
            answers.Any(a => !_questions.ContainsKey(a.Code) || a.Side is not ("A" or "B") || a.Mode is not ("LLM" or "Trích đoạn") ||
                a.SourceIds.Any(id => !_chunks.ContainsKey(id)) || a.ExactSourceId is { } exact &&
                (!a.SourceIds.Contains(exact) || !_chunks[exact].Text.StartsWith(a.Text, StringComparison.Ordinal))) ||
            answers.GroupBy(a => a.Code).Any(group => group.Select(a => a.Mode).Distinct().Count() != 2))
            throw new InvalidDataException("Dev check answers do not match the dev CSV or corpus.");
        _random.Shuffle(_answers);
    }

    public static async Task RunAsync(string directory, TextWriter output)
    {
        var path = Path.Combine(directory, "dev-kiem-plant.xlsx");
        StatisticsReport.EnsureNew(path);
        var questions = await DevBlindProvenance.LoadDevCsvAsync(Path.Combine(directory, "dev-kiem.csv"));
        var answersPath = Path.Combine(directory, "dev-kiem", DevBlindProvenance.FileName);
        var answers = JsonSerializer.Deserialize<DevAnswer[]>(await File.ReadAllTextAsync(answersPath), StatisticsReport.JsonOptions)
            ?? throw new InvalidDataException("Missing dev check answers.");
        using var model = new DevPlantModel();
        var run = new DevPlantRun(questions, answers, model);
        await run.GenerateAsync(path, output);
    }

    private async Task GenerateAsync(string path, TextWriter output)
    {
        AddScriptEdits(NumberKind, 25, DevEdits.Numbers);
        await ReportAsync(NumberKind, _selection.Variants, output);
        AddScriptEdits(NegationKind, 15, DevEdits.Negations);
        await ReportAsync(NegationKind, _selection.Variants, output);
        await AddModelEditsAsync(ConditionKind, 20, DevPlantModel.Condition, insert: false, output);
        await AddModelEditsAsync(AddedTrueKind, 20, DevPlantModel.AddedTrue, insert: true, output);
        await AddModelEditsAsync(AddedFalseKind, 20, DevPlantModel.AddedFalse, insert: true, output);

        var exact = _answers.Where(a => a.Mode == "Trích đoạn" && a.ExactSourceId is not null).ToArray();
        var controls = exact.Where(a => !_selection.Contains(a)).ToArray();
        var unsafeCandidates = _answers.Where(a => _questions[a.Code].Category is DevQuestions.Emergency or DevQuestions.InScope)
            .Where(a => HasClinicalContext(a) || _questions[a.Code].Category == DevQuestions.Emergency)
            .OrderByDescending(a => _questions[a.Code].Category == DevQuestions.Emergency).ToArray();
        var unsafeEdits = await GenerateVariantsAsync(unsafeCandidates, UnsafeKind, DevPlantModel.Unsafe, 25, ValidateUnsafe, output);
        var paraphrases = await GenerateVariantsAsync(exact, ParaphraseKind, DevPlantModel.Paraphrase, 60, ValidateParaphrase, output);
        var variants = _selection.Variants.Concat(unsafeEdits).Concat(paraphrases).ToArray();
        var counts = Targets.Select(target => (target.Kind, target.Target, Actual: variants.Count(v => v.Kind == target.Kind))).ToArray();
        DevPlantWorkbook.Save(path, variants, controls, counts, _answers.Length, _selection.Variants.Count);
        await output.WriteLineAsync($"Cài bịa: {_selection.Variants.Count}/{_answers.Length}; câu trả lời giữ nguyên: {_answers.Length - _selection.Variants.Count}; TuDong: {controls.Length}");
        foreach (var position in Positions)
            await output.WriteLineAsync($"{position}: {_selection.Variants.Count(v => v.Position == position)}");
        foreach (var mode in new[] { "LLM", "Trích đoạn" })
            await output.WriteLineAsync($"{mode}: {_selection.Variants.Count(v => v.Answer.Mode == mode)}");
    }

    private void AddScriptEdits(string kind, int quota, Func<string, DevEdit[]> findEdits)
    {
        var candidates = _answers.Where(IsPlantCandidate)
            .SelectMany(answer => findEdits(answer.Text).Select(edit => (Answer: answer, Edit: edit))).ToArray();
        for (var i = 0; i < quota; i++)
        {
            var best = candidates.Where(candidate => !_selection.Contains(candidate.Answer))
                .MinBy(candidate => Priority(candidate.Answer, DevEdits.Position(candidate.Edit.Offset, candidate.Answer.Text.Length)));
            if (best.Answer is null) break;
            _selection.TryAdd(best.Answer, best.Edit, kind);
        }
    }

    private async Task AddModelEditsAsync(string kind, int quota, string prompt, bool insert, TextWriter output)
    {
        var pending = _answers.Where(IsPlantCandidate).Where(a => !_selection.Contains(a)).ToList();
        for (var count = 0; count < quota && pending.Count > 0;)
        {
            var position = Enumerable.Range(0, Positions.Count).MinBy(i => _selection.Variants.Count(v => v.Position == Positions[i]));
            var answer = pending.MinBy(a => Priority(a, position))!;
            pending.Remove(answer);
            var reply = await GenerateAsync(prompt, answer, Positions[position]);
            var edit = insert ? ValidateInsertion(answer, reply?.Text, position) : ValidateCondition(answer, reply);
            if (edit is not null && _selection.TryAdd(answer, edit, kind)) count++;
        }
        await ReportAsync(kind, _selection.Variants, output);
    }

    private async Task<IReadOnlyList<DevVariant>> GenerateVariantsAsync(DevAnswer[] candidates, string kind,
        string prompt, int quota, Func<DevAnswer, DevModelReply?, (string Text, string Position)?> validate, TextWriter output)
    {
        var variants = new List<DevVariant>();
        var prior = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 4 && variants.Count < quota; round++)
        {
            foreach (var answer in candidates)
            {
                var previous = prior.GetValueOrDefault(answer.Text);
                var reply = await GenerateAsync(prompt, answer, previous is null ? "" : string.Join("\n", previous));
                if (validate(answer, reply) is not { } edited || !unique.Add(KnowledgeText.Normalize(edited.Text, false)))
                    continue;
                if (previous is null) prior[answer.Text] = previous = [];
                previous.Add(edited.Text);
                variants.Add(new(answer, edited.Text, kind, edited.Position));
                if (variants.Count == quota) break;
            }
        }
        await ReportAsync(kind, variants, output);
        return variants;
    }

    private static IEnumerable<string> NumericTokens(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\d+(?:[.,]\d+)*").Select(m => m.Value).Order(StringComparer.Ordinal);

    private static (string Text, string Position)? ValidateParaphrase(DevAnswer answer, DevModelReply? reply)
    {
        var edited = reply?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(edited) || KnowledgeText.Normalize(edited, false) == KnowledgeText.Normalize(answer.Text, false) ||
            !NumericTokens(edited).SequenceEqual(NumericTokens(answer.Text)))
            return null;
        return (edited, "Toàn câu");
    }

    private (string Text, string Position)? ValidateUnsafe(DevAnswer answer, DevModelReply? reply)
    {
        var from = reply?.From?.Trim();
        if (!string.IsNullOrWhiteSpace(from) && ContainsReferral(from))
        {
            var offset = answer.Text.IndexOf(from, StringComparison.Ordinal);
            if (offset >= 0 && answer.Text.LastIndexOf(from, StringComparison.Ordinal) == offset)
            {
                var edited = new DevEdit(offset, from.Length, "").Apply(answer.Text).Trim();
                if (edited.Length > 0 && !ContainsReferral(edited))
                    return (edited, Positions[DevEdits.Position(offset, answer.Text.Length)]);
            }
        }
        var text = reply?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || !System.Text.RegularExpressions.Regex.IsMatch(
                KnowledgeText.Normalize(text), @"\b(?:tai nha|tu (?:xu ly|dieu tri|theo doi)|doi|cho|tri hoan)\b"))
            return null;
        var insertion = ValidateInsertion(answer, text, 2);
        return insertion is null ? null : (insertion.Apply(answer.Text), Positions[2]);
    }

    private static bool ContainsReferral(string text) => System.Text.RegularExpressions.Regex.IsMatch(KnowledgeText.Normalize(text),
        @"\b(?:goi (?:ngay )?115|(?:den|toi|di) (?:ngay )?(?:cap cuu|co so y te|benh vien|khoa cap cuu))\b");

    private static DevEdit? ValidateCondition(DevAnswer answer, DevModelReply? reply)
    {
        var edit = DevEdits.ReplaceUnique(answer.Text, reply?.From, reply?.To);
        if (edit is null || reply!.From!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12 ||
            edit.Replacement.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12 ||
            !NumericTokens(reply.From).SequenceEqual(NumericTokens(edit.Replacement)) ||
            System.Text.RegularExpressions.Regex.IsMatch(edit.Replacement, @"[.!?;\r\n]"))
            return null;
        var originalNegations = KnowledgeText.Normalize(reply.From, false).Split(' ').Count(word => word is "không" or "chưa" or "đừng");
        var editedNegations = KnowledgeText.Normalize(edit.Replacement, false).Split(' ').Count(word => word is "không" or "chưa" or "đừng");
        return originalNegations == editedNegations ? edit : null;
    }

    private DevEdit? ValidateInsertion(DevAnswer answer, string? text, int position)
    {
        text = text?.Trim();
        if (text is null || !DevEdits.IsSingleSentence(text)) return null;
        var normalized = KnowledgeText.Normalize(text, false);
        if (normalized.Length == 0 || KnowledgeText.Normalize(answer.Text, false).Contains(normalized, StringComparison.Ordinal) ||
            KnowledgeText.Normalize(Sources(answer), false).Contains(normalized, StringComparison.Ordinal))
            return null;
        return DevEdits.Insert(answer.Text, text, position);
    }

    private bool IsPlantCandidate(DevAnswer answer) => answer.Text.Length > 0 &&
        _questions[answer.Code].Category != DevQuestions.Far;

    private bool HasClinicalContext(DevAnswer answer) => _questions[answer.Code].ExpectedDocId is
        "prepare-tests" or "prepare-visit" or "supported-visitors" or "prescriptions";

    private int Priority(DevAnswer answer, int position) =>
        2 * _selection.Variants.Count(v => v.Position == Positions[position]) +
        _selection.Variants.Count(v => v.Answer.Mode == answer.Mode) +
        (answer.ExactSourceId is not null && answer.Mode == "Trích đoạn" ? 3 : 0) +
        (_selection.Variants.Any(v => v.Answer.Code == answer.Code) ? 1 : 0);

    private string Sources(DevAnswer answer)
    {
        var exact = answer.ExactSourceId;
        return exact is not null
            ? $"[{exact}] {_chunks[exact].Text}"
            : string.Join("\n\n", answer.SourceIds.Select(id => _chunks[id])
                .Concat(_chunks.Values.Where(chunk => chunk.Source.DocId == _questions[answer.Code].ExpectedDocId))
                .DistinctBy(chunk => chunk.Id).Select(chunk => $"[{chunk.Id}] {chunk.Text}"));
    }

    private Task<DevModelReply?> GenerateAsync(string prompt, DevAnswer answer, string context) =>
        _model.GenerateAsync(prompt, _questions[answer.Code], answer, Sources(answer), context, _random.Next());

    private static Task ReportAsync(string kind, IEnumerable<DevVariant> variants, TextWriter output) =>
        output.WriteLineAsync($"{kind}: {variants.Count(v => v.Kind == kind)}/{Targets.Single(target => target.Kind == kind).Target}");
}
