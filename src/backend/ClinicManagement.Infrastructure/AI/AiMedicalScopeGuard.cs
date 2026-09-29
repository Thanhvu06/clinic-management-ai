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
    private static readonly Regex ExistingPrescriptionRead = new(
        @"\b(?:bac\s+si\s+ke\s+thuoc|thuoc\s+(?:bac\s+si\s+)?da\s+ke|(?:toa|don)\s+thuoc\s+cua\s+toi|thuoc\s+cua\s+toi)\b",
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
        if (AccentedDoseRequest.IsMatch(preserved) || PrescriptionRequest.IsMatch(folded) ||
            UnaccentedDoseRequest.IsMatch(folded) ||
            (MedicationChange.IsMatch(preserved) && RequestContext.IsMatch(preserved)) ||
            (preserved == folded && UnaccentedChange.IsMatch(preserved)))
            return true;

        // Read exemptions never bypass an explicit request to change medication.
        if (ExistingPrescriptionRead.IsMatch(folded))
            return false;
        return false;
    }
}
