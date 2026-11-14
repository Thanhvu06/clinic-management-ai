using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

public enum ReplayComparison
{
    PassageMatches,
    DefaultRefusalMatches,
    DifferentPassage,
    MissingAdditionalPassages,
    TruncatedPassage,
    UnexpectedRefusal,
    EmptyRetrievalWithOtherAnswer,
    MissingAnswerOrKey,
    MissingReplayRow
}

public sealed record ReplayVerificationResult(int Rows, int Matched, int Mismatched, int Indeterminate,
    IReadOnlyDictionary<ReplayComparison, int> ByType)
{
    public bool IsComplete => Rows == 150 && Matched == Rows;
}

public static partial class ReplayVerification
{
    private static readonly HashSet<string> DefaultRefusals = new(StringComparer.Ordinal)
    {
        "ClinicCare chỉ hỗ trợ thông tin sức khỏe, lịch khám và hoạt động của phòng khám trong phạm vi quyền của bạn.",
        "Tôi không thể kê đơn hoặc hướng dẫn liều dùng qua cuộc trò chuyện. Bạn có thể đặt lịch để được bác sĩ thăm khám, hoặc xem toa thuốc đã được cơ sở xác nhận trong tài khoản của mình.",
        "Tôi là Trợ lý ClinicCare AI hỗ trợ điều hướng chuyên khoa, lịch khám và thông tin phòng khám. Tôi tuân thủ nghiêm ngặt các quy tắc an toàn y khoa và không thực hiện các yêu cầu nằm ngoài phạm vi hỗ trợ."
    };

    public static ReplayComparison Compare(string? excerpt, IReadOnlyList<ReplayPassage> passages)
    {
        if (string.IsNullOrWhiteSpace(excerpt)) return ReplayComparison.MissingAnswerOrKey;
        var text = Normalize(excerpt);
        var refused = DefaultRefusals.Contains(text);
        if (passages.Count == 0)
            return refused ? ReplayComparison.DefaultRefusalMatches : ReplayComparison.EmptyRetrievalWithOtherAnswer;
        if (refused) return ReplayComparison.UnexpectedRefusal;

        // The historical extractive answer uses the first ranked hit, with a five-sentence cap.
        var normalized = passages.Select(passage => Normalize(passage.Text)).ToArray();
        var top = normalized[0];
        if (text.Contains(top, StringComparison.Ordinal))
            return normalized.All(passage => text.Contains(passage, StringComparison.Ordinal))
                ? ReplayComparison.PassageMatches : ReplayComparison.MissingAdditionalPassages;
        return top.StartsWith(text, StringComparison.Ordinal) && SentenceBreak().Split(top).Length > 5 &&
            text == string.Join(' ', SentenceBreak().Split(top).Take(5))
            ? ReplayComparison.TruncatedPassage : ReplayComparison.DifferentPassage;
    }

    public static ReplayVerificationResult Verify(IXLWorkbook master, IReadOnlyDictionary<string, ReplayPassage[]> entries)
    {
        var grades = master.Worksheet("Phiếu chấm");
        var keys = master.Worksheet("Khóa");
        var keyRows = BlindAnswerWorkbook.SttRows(keys);
        var gradeRows = BlindAnswerWorkbook.SttRows(grades);
        var counts = new Dictionary<ReplayComparison, int>();
        foreach (var code in gradeRows.Keys.Union(entries.Keys, StringComparer.Ordinal).Union(keyRows.Keys, StringComparer.Ordinal))
        {
            var comparison = CompareRow(code);
            counts[comparison] = counts.GetValueOrDefault(comparison) + 1;
        }
        var total = counts.Values.Sum();
        var matched = counts.GetValueOrDefault(ReplayComparison.PassageMatches) + counts.GetValueOrDefault(ReplayComparison.DefaultRefusalMatches);
        var indeterminate = counts.GetValueOrDefault(ReplayComparison.MissingAnswerOrKey) + counts.GetValueOrDefault(ReplayComparison.MissingReplayRow);
        return new(total, matched, total - matched - indeterminate, indeterminate, counts);

        ReplayComparison CompareRow(string code)
        {
            if (!entries.TryGetValue(code, out var passages)) return ReplayComparison.MissingReplayRow;
            if (!keyRows.TryGetValue(code, out var keyRow) || !gradeRows.TryGetValue(code, out var gradeRow))
                return ReplayComparison.MissingAnswerOrKey;
            var extractive = keys.Cell(keyRow, 2).GetString().Trim().Normalize(NormalizationForm.FormC) switch
            {
                "LLM" => 5,
                "Trích đoạn" => 4,
                _ => 0
            };
            return extractive == 0 ? ReplayComparison.MissingAnswerOrKey : Compare(grades.Cell(gradeRow, extractive).GetString(), passages);
        }
    }

    private static string Normalize(string value) => Whitespace().Replace(value.Normalize(NormalizationForm.FormC).Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();
}
