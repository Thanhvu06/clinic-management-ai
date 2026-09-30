using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Application.AI.Suggestions;

public enum AiSuggestionActionKind { ReadTool, Wizard }

public sealed record AiSuggestionDefinition(
    string Code,
    string Label,
    AiActorRole Role,
    string ToolName,
    bool RequiresResource,
    AiSuggestionActionKind ActionKind = AiSuggestionActionKind.ReadTool,
    string? WizardStep = null);

/// <summary>
/// Server-owned suggestion buttons. The browser only ever sends a code; the
/// server maps it to an existing read tool through the normal authorization
/// pipeline, or to the patient wizard start step without a tool invocation.
/// Codes and labels are stable wire values — never derived from user text.
/// </summary>
public static class AiSuggestionCatalog
{
    public const int MaxSuggestions = 6;
    public const int MaxCodeLength = 64;
    public static readonly Regex CodePattern = new(@"^[a-z]+(\.[a-z_]+)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<AiSuggestionDefinition> Definitions { get; } = new[]
    {
        new AiSuggestionDefinition("patient.start_booking", "Đặt lịch khám", AiActorRole.Patient, "", false, AiSuggestionActionKind.Wizard, "start"),
        new AiSuggestionDefinition("doctor.my_queue", "Hôm nay tôi khám ai?", AiActorRole.Doctor, "doctor.get_my_queue", false),
        new AiSuggestionDefinition("doctor.patient_summary", "Tóm tắt bệnh nhân đang mở", AiActorRole.Doctor, "doctor.get_patient_summary", true),
        new AiSuggestionDefinition("doctor.diagnostic_orders", "Chỉ định cận lâm sàng của ca này", AiActorRole.Doctor, "doctor.get_diagnostic_orders", true),
        new AiSuggestionDefinition("doctor.prescription_status", "Trạng thái đơn thuốc của ca này", AiActorRole.Doctor, "doctor.get_prescription_status", true),
        new AiSuggestionDefinition("patient.my_appointments", "Lịch hẹn của tôi", AiActorRole.Patient, "patient.get_my_appointments", false),
        new AiSuggestionDefinition("patient.my_visits", "Lượt khám của tôi", AiActorRole.Patient, "patient.get_my_visits", false),
        new AiSuggestionDefinition("patient.my_diagnostic_results", "Kết quả cận lâm sàng của tôi", AiActorRole.Patient, "patient.get_my_diagnostic_results", false),
        new AiSuggestionDefinition("patient.my_prescriptions", "Đơn thuốc của tôi", AiActorRole.Patient, "patient.get_my_prescriptions", false),
        new AiSuggestionDefinition("patient.my_bills", "Hóa đơn của tôi", AiActorRole.Patient, "patient.get_my_bills", false)
    };

    /// <summary>Finds a code only when it belongs to the caller's role.</summary>
    public static AiSuggestionDefinition? Find(string? code, AiActorRole role)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim();
        return Definitions.FirstOrDefault(x => x.Role == role && string.Equals(x.Code, normalized, StringComparison.Ordinal));
    }

    public static IReadOnlyList<AiSuggestionItemDto> ForRole(
        AiActorRole role,
        bool hasCaseResource,
        IReadOnlyCollection<string>? excludedToolNames = null) =>
        Definitions
            .Where(x => x.Role == role && (!x.RequiresResource || hasCaseResource))
            .Where(x => excludedToolNames is null || !excludedToolNames.Contains(x.ToolName, StringComparer.OrdinalIgnoreCase))
            .Take(MaxSuggestions)
            .Select(x => new AiSuggestionItemDto { Code = x.Code, Label = x.Label })
            .ToArray();
}
