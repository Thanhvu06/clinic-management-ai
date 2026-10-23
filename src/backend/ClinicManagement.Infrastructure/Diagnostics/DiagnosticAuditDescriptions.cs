using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Diagnostics;

internal static class DiagnosticAuditDescriptions
{
    private static readonly IReadOnlyDictionary<string, string> RoleLabels = new Dictionary<string, string>
    {
        [RoleNames.Doctor] = "Bác sĩ",
        [RoleNames.LabSupervisor] = "Trưởng phòng xét nghiệm",
        [RoleNames.DiagnosticTechnician] = "Kỹ thuật viên"
    };

    internal static string Verification(DiagnosticOrder order, string role, string? note) =>
        $"{RoleLabels[role]}: Phiếu #{order.OrderCode}.{(string.IsNullOrWhiteSpace(note) ? string.Empty : $" {note.Trim()}")}";

    internal static string? VerificationRole(string? description) => RoleLabels.Keys.FirstOrDefault(role =>
        description?.StartsWith($"{RoleLabels[role]}:", StringComparison.Ordinal) == true ||
        description?.StartsWith($"VerifiedByRole={role};", StringComparison.Ordinal) == true);

    internal static string Specimen(DiagnosticOrder order, DiagnosticOrderItem item, string action, string? note)
    {
        var verb = action switch { "SpecimenCollected" => "Lấy", "SpecimenReceived" => "Nhận", _ => "Từ chối" };
        var recollection = item.RecollectCount > 0 ? $" (lần lấy lại {item.RecollectCount})" : string.Empty;
        return $"{verb} mẫu {item.SpecimenBarcode} của phiếu #{order.OrderCode}{recollection}.{(string.IsNullOrWhiteSpace(note) ? string.Empty : $" {note.Trim()}")}";
    }
}
