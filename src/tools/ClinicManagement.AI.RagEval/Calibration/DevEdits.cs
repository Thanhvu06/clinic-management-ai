using System.Text.RegularExpressions;

namespace ClinicManagement.AI.RagEval;

public sealed record DevEdit(int Offset, int Length, string Replacement)
{
    public string Apply(string original) =>
        string.Concat(original.AsSpan(0, Offset), Replacement, original.AsSpan(Offset + Length));
}

public static partial class DevEdits
{
    [GeneratedRegex(@"(?<![\p{L}0-9])(?:115|0[0-9]{8,10})(?![0-9])|(?<![\p{L}0-9.,])[0-9]+(?:[.,][0-9]+)*(?=\s*(?:[–-]\s*[0-9]+(?:[.,][0-9]+)*\s*)?(?:giờ|tiếng|phút|ngày|tuần|tháng|năm|lần|lít|tuổi|đồng|VNĐ|VND|viên|liều|mg|mcg|ml|g|đ)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    [GeneratedRegex(@"\b(?:không\s+(?:được|nên|cần|thể|bắt buộc|tự|chờ|thay thế|gửi|chia sẻ|dùng|cho phép|có)|(?:được phép|có thể|bắt buộc|nên|cần|hãy|phải))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Polarity();

    [GeneratedRegex(@"[.!?;](?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundary();

    public static DevEdit[] Numbers(string text) => Number().Matches(text).Select(match =>
    {
        var value = match.Value;
        var last = value[^1];
        var newDigit = last == '9' ? '8' : (char)(last + 1);
        return new DevEdit(match.Index + value.Length - 1, 1, newDigit.ToString());
    }).ToArray();

    public static DevEdit[] Negations(string text) => Polarity().Matches(text).Where(match =>
        !Regex.IsMatch(text[..match.Index], @"\bkhông(?:\s+\p{L}+){0,2}\s+$", RegexOptions.IgnoreCase))
        .Select(match =>
        {
            if (match.Value.StartsWith("không ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = match.Value[6..].TrimStart();
                return new DevEdit(match.Index, match.Length, WithInitialCase(rest, match.Value));
            }
            var replacement = match.Value.Equals("hãy", StringComparison.OrdinalIgnoreCase)
                ? "đừng" : "không " + match.Value.ToLowerInvariant();
            return new DevEdit(match.Index, match.Length, WithInitialCase(replacement, match.Value));
        }).ToArray();

    public static DevEdit Insert(string text, string sentence, int position)
    {
        if (position == 0) return new(0, 0, sentence + " ");
        if (position == 2)
        {
            var separator = text[^1] is '.' or '!' or '?' ? " " : ". ";
            return new(text.Length, 0, separator + sentence);
        }
        var boundary = MiddleBoundary(text);
        return boundary == 0 ? Insert(text, sentence, 2) : new(boundary, 0, " " + sentence);
    }

    public static bool HasMiddleBoundary(string text) => MiddleBoundary(text) > 0;

    private static int MiddleBoundary(string text) =>
        SentenceBoundary().Matches(text).Select(m => m.Index + 1)
            .Where(offset => Position(offset, text.Length) == 1)
            .OrderBy(offset => Math.Abs(offset - text.Length / 2)).FirstOrDefault();

    public static int Position(int offset, int length) => Math.Min(2, offset * 3 / Math.Max(length, 1));

    public static DevEdit? ReplaceUnique(string original, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from == to) return null;
        var index = original.IndexOf(from, StringComparison.Ordinal);
        if (index < 0 || original.LastIndexOf(from, StringComparison.Ordinal) != index) return null;
        if (index > 0 && char.IsLetterOrDigit(original[index - 1]) && char.IsLetterOrDigit(from[0]) ||
            index + from.Length < original.Length && char.IsLetterOrDigit(original[index + from.Length]) && char.IsLetterOrDigit(from[^1]))
            return null;
        return new(index, from.Length, to);
    }

    public static bool IsSingleSentence(string text) =>
        !string.IsNullOrWhiteSpace(text) && !text.Contains('\n') && Regex.Matches(text, @"[.!?](?=\s|$)").Count == 1 &&
        text[^1] is '.' or '!' or '?';

    private static string WithInitialCase(string replacement, string original) => char.IsUpper(original[0])
        ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
}
