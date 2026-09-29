using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>
/// Keeps medication prescribing outside the chat assistant without treating a
/// normal medication question as prompt injection.
/// </summary>
public static class AiMedicalScopeGuard
{
    private static readonly Regex PrescriptionRequest = new(
        @"(?:\bke\s+(?:thuoc|don)\b|\bbac\s+si\b.{0,80}\bke\s+thuoc\b|\b(?:lieu|uong)\b.{0,40}\bthuoc\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool IsPrescriptionRequest(string? message)
    {
        var normalized = AiTextNormalizer.NormalizeForComparison(message);
        return PrescriptionRequest.IsMatch(normalized);
    }
}
