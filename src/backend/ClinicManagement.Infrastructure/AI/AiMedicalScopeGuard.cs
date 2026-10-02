using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Keeps patient requests to prescribe, dose, start, stop or change medication
/// outside the chat assistant without blocking staff planner/action workflows or
/// ordinary read questions about an already issued prescription.
/// </summary>
public static class AiMedicalScopeGuard
{
    // Output is not a user question: numeric dosing and authored prescriptions
    // must be rejected even without request words such as "bao nhiêu".
    private static readonly Regex ProviderDoseOrPrescriptionAdvice = new(
        @"\b\d+(?:[.,]\d+)?\s*(?:mg|mcg|ml|viên|vien)\b|\b(?:toi|minh)\s+ke\s+(?:don|thuoc)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProviderMedicationChangeAdvice = new(
        @"\b(?:hãy|nên)\s+(?:ngừng|ngưng|dừng|đổi|bỏ)\s+thuốc\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsUnsafeProviderAdvice(AiActorRole role, string? text) =>
        role == AiActorRole.Patient && !string.IsNullOrWhiteSpace(text) &&
        (IsPrescriptionRequest(role, text) ||
         ProviderDoseOrPrescriptionAdvice.IsMatch(AiTextNormalizer.NormalizeForComparison(text)) ||
         ProviderMedicationChangeAdvice.IsMatch(AiTextNormalizer.Normalize(text)));

    private static readonly Regex PreservedPrescriptionRead = new(
        @"\bđã\s+kê\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FoldedPrescriptionRead = new(
        @"\b(?:bac\s+si\s+da\s+ke|thuoc\s+(?:bac\s+si\s+)?da\s+ke|(?:toa|don)\s+thuoc\s+cua\s+toi|thuoc\s+cua\s+toi)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PrescriptionRequest = new(
        @"\bke\s+(?:thuoc|don)(?:\s+thuoc)?\s+(?:cho|giup)\b|\bcho\s+(?:toi|minh)\s+(?:toa|don)\b.{0,40}\b(?:tri|chua)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MedicationChange = new(
        @"\b(?:ngừng|ngưng|đổi|bỏ|tăng|giảm|dừng)\s+(?:(?:liều|lượng)\s+)?thuốc\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RequestContext = new(
        @"\b(?:có nên|được không|tôi muốn|cho tôi|giúp tôi|hãy|nên)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnaccentedChange = new(
        @"\b(?:(?:ngung|dung|doi|bo) thuoc|(?:tang|giam) lieu)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnaccentedDoseRequest = new(
        @"\blieu\s+(?:dung|luong)\b|\buong\b.{0,50}\bbao\s+nhieu\s+(?:vien|mg)\b|\bbao\s+nhieu\s+(?:vien|mg)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AccentedDoseRequest = new(
        @"\bliều\b.{0,60}(?:bao\s+nhiêu|viên|mg|thuốc|paracetamol)|\buống\b.{0,60}\b(?:bao\s+nhiêu|mấy)\s+viên\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PrescriptionAddition = new(
        @"\bke\s+them\b|\bke\s+(?:thuoc|don)\s+moi\b|\bthem\s+(?:thuoc|don\s+thuoc)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DosageSuggestion = new(
        @"\b(?:co\s+nen|nen)\b.{0,40}\b(?:bao\s+nhieu|may|lieu|tang|giam|gap\s+doi)\b|\b(?:bao\s+nhieu|may|lieu|tang|giam|gap\s+doi)\b.{0,40}\b(?:co\s+nen|nen)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsPrescriptionRequest(string? message) =>
        IsPrescriptionRequest(AiActorRole.Patient, message);

    public static bool IsPrescriptionRequest(AiActorRole role, string? message)
    {
        if (role != AiActorRole.Patient || string.IsNullOrWhiteSpace(message))
            return false;

        var preserved = AiTextNormalizer.Normalize(message).ToLowerInvariant();
        var folded = AiTextNormalizer.NormalizeForComparison(preserved);

        // "liều" is intentionally checked with accents preserved so it cannot
        // swallow the ordinary conjunction/question word "liệu". In fully
        // unaccented input only explicit dose forms such as "lieu dung" or
        // "bao nhieu vien" are accepted.
        if ((MedicationChange.IsMatch(preserved) && RequestContext.IsMatch(preserved)) ||
            (preserved == folded && UnaccentedChange.IsMatch(preserved)) ||
            PrescriptionAddition.IsMatch(folded))
            return true;

        foreach (var clause in SplitClauses(message))
        {
            var clausePreserved = clause.Preserved;
            var clauseFolded = clause.Folded;
            if (DosageSuggestion.IsMatch(clauseFolded))
                return true;

            var isReadClause = PreservedPrescriptionRead.IsMatch(clausePreserved) ||
                               FoldedPrescriptionRead.IsMatch(clauseFolded);
            var isSoftRequest = AccentedDoseRequest.IsMatch(clausePreserved) ||
                                PrescriptionRequest.IsMatch(clauseFolded) ||
                                UnaccentedDoseRequest.IsMatch(clauseFolded);
            if (isSoftRequest && !isReadClause)
                return true;
        }
        return false;
    }

    private static IEnumerable<(string Preserved, string Folded)> SplitClauses(string message)
    {
        foreach (var rawPart in Regex.Split(message, @"[.!?;\r\n]+"))
        {
            var preserved = AiTextNormalizer.Normalize(rawPart).ToLowerInvariant();
            if (preserved.Length == 0) continue;
            var folded = AiTextNormalizer.NormalizeForComparison(preserved);
            var start = 0;
            foreach (Match boundary in Regex.Matches(folded, @"\b(?:bay\s+gio|gio|nhung|sau\s+do)\b", RegexOptions.CultureInvariant))
            {
                if (boundary.Value == "gio" && Regex.IsMatch(folded[..boundary.Index], @"\bmay\s*$", RegexOptions.CultureInvariant))
                    continue;

                if (boundary.Index > start)
                    yield return (preserved[start..boundary.Index].Trim(), folded[start..boundary.Index].Trim());
                start = boundary.Index + boundary.Length;
            }

            if (start < preserved.Length)
                yield return (preserved[start..].Trim(), folded[start..].Trim());
        }
    }
}
