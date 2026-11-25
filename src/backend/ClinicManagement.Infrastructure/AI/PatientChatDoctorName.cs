using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI;

internal static class PatientChatDoctorName
{
    private static readonly Regex DoctorMention = new(
        @"\b(?:bác sĩ|bac si|bác sỹ|bac sy|bs\.?)\s+(?<name>[\p{L}\p{M}\p{N}]+(?:[ \t]+[\p{L}\p{M}\p{N}]+)*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex InformalMention = new(
        @"\b(?:tìm|xem\s+lịch)\s+bác\s+(?<name>[\p{L}\p{M} ]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NameSuffix = new(
        @"\s+(?:vào|từ|lúc|ngày|khoa|chuyên khoa|sáng|chiều|tối|ở|của|trong|có|khám|giúp|giup|ơi|nhé|với)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CommonPhrase = new(
        @"^(?:o|cua|trong|tai|nao|ai|gi|gioi|khac|phu hop|chuyen khoa|khoa|kham|cho|giup|oi|hoi|tu van|nhe)\b|\b(?:nao|ai|giup|cho)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex CapitalizedName = new(
        @"^\p{Lu}[\p{L}\p{M}]*(?:[ \t]+(?:\p{Lu}[\p{L}\p{M}]*|\d+))*$",
        RegexOptions.CultureInvariant);

    internal static string? ExtractCandidate(string message)
    {
        var match = DoctorMention.Match(message);
        if (!match.Success) match = InformalMention.Match(message);
        if (!match.Success) return null;

        var name = AiBookingWizardService.StripScheduleQuestion(match.Groups["name"].Value.Trim());
        var suffix = NameSuffix.Match(name);
        return suffix.Success ? name[..suffix.Index] : name;
    }

    internal static bool IsCandidate(string? name) =>
        name is { Length: >= 2 } && !CommonPhrase.IsMatch(AiTextNormalizer.NormalizeForComparison(name));

    internal static bool LooksLikeName(string? name) => IsCandidate(name) && CapitalizedName.IsMatch(name!);
}
