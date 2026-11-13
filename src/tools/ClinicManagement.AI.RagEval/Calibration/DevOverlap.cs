using System.Globalization;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public sealed record DevOverlapResult(EvaluationQuestion[] Retained, int Removed, int[] RemovedByGroup, int[] Histogram);

public static class DevOverlap
{
    public static bool IsOverlap(double cosine, double jaccard) => cosine >= 0.90 || jaccard >= 0.60;

    internal static string[] ReadHiddenTexts(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Câu hỏi");
        var last = sheet.LastRowUsed()?.RowNumber() ?? 3;
        var texts = new List<string>();
        for (var row = 4; row <= last; row++)
        {
            var code = sheet.Cell(row, 1).GetString().Trim();
            if (code.Equals("VD", StringComparison.OrdinalIgnoreCase) || code.Equals("VÍ DỤ", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = sheet.Cell(row, 3).GetString().Trim();
            if (text.Length > 0) texts.Add(text);
            else if (code.Length > 0) throw new InvalidDataException("Missing question text.");
        }
        if (texts.Count == 0) throw new InvalidDataException("No hidden questions found.");
        return texts.ToArray();
    }

    public static DevOverlapResult Compare(IReadOnlyList<EvaluationQuestion> dev, IReadOnlyList<string> hidden,
        IReadOnlyList<float[]> devVectors, IReadOnlyList<float[]> hiddenVectors)
    {
        if (hidden.Count == 0 || dev.Count != devVectors.Count || hidden.Count != hiddenVectors.Count)
            throw new InvalidDataException("Embedding count does not match question count.");
        var dimension = hiddenVectors[0].Length;
        if (dimension == 0 || devVectors.Concat(hiddenVectors).Any(vector =>
                vector.Length != dimension || vector.Any(value => !float.IsFinite(value)) || vector.All(value => value == 0)))
            throw new InvalidDataException("Invalid embedding vectors.");
        var hiddenWords = hidden.Select(Words).ToArray();
        var retained = new List<EvaluationQuestion>();
        var histogram = new int[40];
        var removed = new int[DevQuestions.Groups.Count];
        var groupIndices = DevQuestions.Groups.Select((group, index) => (group, index))
            .ToDictionary(pair => pair.group, pair => pair.index, StringComparer.Ordinal);
        for (var i = 0; i < dev.Count; i++)
        {
            if (dev[i].Category is not { } group || !groupIndices.TryGetValue(group, out var groupIndex))
                throw new InvalidDataException("Unknown dev group.");
            var words = Words(dev[i].Text);
            var maxCosine = -1d;
            var overlaps = false;
            for (var j = 0; j < hidden.Count; j++)
            {
                var cosine = Math.Clamp(Bm25KnowledgeIndex.Cosine(devVectors[i], hiddenVectors[j]), -1, 1);
                maxCosine = Math.Max(maxCosine, cosine);
                var intersection = words.Count(hiddenWords[j].Contains);
                var union = words.Count + hiddenWords[j].Count - intersection;
                overlaps |= IsOverlap(cosine, union == 0 ? 0 : (double)intersection / union);
            }
            histogram[Math.Min(39, (int)Math.Floor(maxCosine * 20 + 20))]++;
            if (overlaps) removed[groupIndex]++;
            else retained.Add(dev[i]);
        }
        return new(retained.ToArray(), removed.Sum(), removed, histogram);
    }

    public static async Task WriteSummaryAsync(DevOverlapResult result, TextWriter output)
    {
        await output.WriteLineAsync($"Dev bị loại: {result.Removed}");
        for (var i = 0; i < result.RemovedByGroup.Length; i++)
            await output.WriteLineAsync($"{DevQuestions.Groups[i]}: {result.RemovedByGroup[i]}");
        await output.WriteLineAsync("Histogram cosine lớn nhất (mỗi khoảng 0,05):");
        for (var i = 0; i < result.Histogram.Length; i++)
        {
            var bracket = i == result.Histogram.Length - 1 ? ']' : ')';
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"[{(i - 20) / 20d:F2}, {(i - 19) / 20d:F2}{bracket} {result.Histogram[i]}"));
        }
    }

    private static HashSet<string> Words(string text) =>
        KnowledgeText.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}
