using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Application.AI.Suggestions;

public enum AiSuggestionActionKind { ReadTool, Wizard }
public enum AiSuggestionResourceKind { None, DoctorCase, Prescription }

public sealed record AiSuggestionDefinition(
    string Code,
    string Label,
    AiActorRole Role,
    string ToolName,
    AiSuggestionResourceKind ResourceKind,
    AiSuggestionActionKind ActionKind = AiSuggestionActionKind.ReadTool,
    string? WizardStep = null,
    string? Group = null);

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
        new AiSuggestionDefinition("patient.start_booking", "Đặt lịch khám", AiActorRole.Patient, "", AiSuggestionResourceKind.None, AiSuggestionActionKind.Wizard, "start", "Đặt lịch"),
        new AiSuggestionDefinition("doctor.my_queue", "Hôm nay tôi khám ai?", AiActorRole.Doctor, "doctor.get_my_queue", AiSuggestionResourceKind.None, Group: "Việc hôm nay"),
        new AiSuggestionDefinition("doctor.patient_summary", "Tóm tắt bệnh nhân đang mở", AiActorRole.Doctor, "doctor.get_patient_summary", AiSuggestionResourceKind.DoctorCase, Group: "Theo ca đang mở"),
        new AiSuggestionDefinition("doctor.diagnostic_orders", "Chỉ định cận lâm sàng của ca này", AiActorRole.Doctor, "doctor.get_diagnostic_orders", AiSuggestionResourceKind.DoctorCase, Group: "Theo ca đang mở"),
        new AiSuggestionDefinition("doctor.prescription_status", "Trạng thái đơn thuốc của ca này", AiActorRole.Doctor, "doctor.get_prescription_status", AiSuggestionResourceKind.DoctorCase, Group: "Theo ca đang mở"),
        new AiSuggestionDefinition("patient.my_appointments", "Lịch hẹn của tôi", AiActorRole.Patient, "patient.get_my_appointments", AiSuggestionResourceKind.None, Group: "Dữ liệu của tôi"),
        new AiSuggestionDefinition("patient.my_visits", "Lượt khám của tôi", AiActorRole.Patient, "patient.get_my_visits", AiSuggestionResourceKind.None, Group: "Dữ liệu của tôi"),
        new AiSuggestionDefinition("patient.my_diagnostic_results", "Kết quả cận lâm sàng của tôi", AiActorRole.Patient, "patient.get_my_diagnostic_results", AiSuggestionResourceKind.None, Group: "Dữ liệu của tôi"),
        new AiSuggestionDefinition("patient.my_prescriptions", "Đơn thuốc của tôi", AiActorRole.Patient, "patient.get_my_prescriptions", AiSuggestionResourceKind.None, Group: "Dữ liệu của tôi"),
        new AiSuggestionDefinition("patient.my_bills", "Hóa đơn của tôi", AiActorRole.Patient, "patient.get_my_bills", AiSuggestionResourceKind.None, Group: "Dữ liệu của tôi"),
        new AiSuggestionDefinition("receptionist.today_appointments", "Lịch hẹn hôm nay", AiActorRole.Receptionist, "reception.get_today_appointments", AiSuggestionResourceKind.None, Group: "Việc hôm nay"),
        new AiSuggestionDefinition("receptionist.queue", "Hàng đợi tiếp nhận", AiActorRole.Receptionist, "reception.get_queue", AiSuggestionResourceKind.None, Group: "Việc hôm nay"),
        new AiSuggestionDefinition("technician.worklist", "Chỉ định cần thực hiện", AiActorRole.DiagnosticTechnician, "technician.get_worklist", AiSuggestionResourceKind.None, Group: "Việc hôm nay"),
        new AiSuggestionDefinition("pharmacist.prescription_queue", "Đơn thuốc chờ xử lý", AiActorRole.Pharmacist, "pharmacist.get_prescription_queue", AiSuggestionResourceKind.None, Group: "Việc hôm nay"),
        new AiSuggestionDefinition("pharmacist.inventory", "Tồn kho thuốc", AiActorRole.Pharmacist, "pharmacist.get_inventory_status", AiSuggestionResourceKind.None, Group: "Tổng quan"),
        new AiSuggestionDefinition("pharmacist.prescription_payment", "Thanh toán của đơn đang mở", AiActorRole.Pharmacist, "pharmacist.get_prescription_payment_status", AiSuggestionResourceKind.Prescription, Group: "Theo đơn đang mở"),
        new AiSuggestionDefinition("admin.dashboard_metrics", "Chỉ số hôm nay", AiActorRole.Admin, "admin.get_dashboard_metrics", AiSuggestionResourceKind.None, Group: "Tổng quan"),
        new AiSuggestionDefinition("admin.ai_health", "Hoạt động của trợ lý AI", AiActorRole.Admin, "admin.get_ai_health", AiSuggestionResourceKind.None, Group: "Tổng quan")
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
        IReadOnlyCollection<string>? excludedToolNames = null,
        bool hasPrescriptionResource = false) =>
        Definitions
            .Where(x => x.Role == role && (x.ResourceKind switch
            {
                AiSuggestionResourceKind.None => true,
                AiSuggestionResourceKind.DoctorCase => hasCaseResource,
                AiSuggestionResourceKind.Prescription => hasPrescriptionResource,
                _ => false
            }))
            .Where(x => excludedToolNames is null || !excludedToolNames.Contains(x.ToolName, StringComparer.OrdinalIgnoreCase))
            .Take(MaxSuggestions)
            .Select(x => new AiSuggestionItemDto { Code = x.Code, Label = x.Label, Group = x.Group })
            .ToArray();
}
