using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Diagnostics;

internal static class DiagnosticAuditDescriptions
{
    internal const string ResultsVerifiedAction = "DiagnosticResultsVerified";

    private static readonly IReadOnlyDictionary<string, string> RoleLabels = new Dictionary<string, string>
    {
        [RoleNames.Doctor] = "Bác sĩ",
        [RoleNames.LabSupervisor] = "Trưởng phòng xét nghiệm",
        [RoleNames.DiagnosticTechnician] = "Kỹ thuật viên"
    };

    // VerifiedByRole is read back from this text, so the role label must stay in the sentence.
    private static readonly Regex ResultsVerifiedPattern = new(@"^Duyệt kết quả phiếu #\S+ \((?<label>[^)]+)\)\.", RegexOptions.CultureInvariant);

    internal static string Verification(DiagnosticOrder order, string action, string role, string? note) => action == ResultsVerifiedAction
        ? $"Duyệt kết quả phiếu #{order.OrderCode} ({RoleLabels[role]}).{NoteSuffix(note)}"
        : $"{RoleLabels[role]}: Phiếu #{order.OrderCode}.{NoteSuffix(note)}";

    internal static string? VerificationRole(string? description)
    {
        var verified = description == null ? null : ResultsVerifiedPattern.Match(description);
        if (verified?.Success == true)
            return RoleLabels.FirstOrDefault(pair => pair.Value == verified.Groups["label"].Value).Key;
        return RoleLabels.Keys.FirstOrDefault(role =>
            description?.StartsWith($"{RoleLabels[role]}:", StringComparison.Ordinal) == true ||
            description?.StartsWith($"VerifiedByRole={role};", StringComparison.Ordinal) == true);
    }

    internal static string Specimen(DiagnosticOrder order, DiagnosticOrderItem item, string action, string? note)
    {
        var verb = action switch { "SpecimenCollected" => "Lấy", "SpecimenReceived" => "Nhận", _ => "Từ chối" };
        var recollection = item.RecollectCount > 0 ? $" (lần lấy lại {item.RecollectCount})" : string.Empty;
        return $"{verb} mẫu {item.SpecimenBarcode} của phiếu #{order.OrderCode}{recollection}.{NoteSuffix(note)}";
    }

    private static string NoteSuffix(string? note) => string.IsNullOrWhiteSpace(note) ? string.Empty : $" {note.Trim()}";
}
