namespace ClinicManagement.Application.AI.Tools;

/// <summary>
/// Prepare actions are available only through the direct human preparation
/// endpoint. They are intentionally absent from AiPlannerPolicy.
/// </summary>
public static class AiRoleActionCatalog
{
    private static readonly IReadOnlySet<AiActorRole> Reception = new HashSet<AiActorRole> { AiActorRole.Receptionist };
    private static readonly IReadOnlySet<AiActorRole> Doctor = new HashSet<AiActorRole> { AiActorRole.Doctor };
    private static readonly IReadOnlySet<AiActorRole> Technician = new HashSet<AiActorRole> { AiActorRole.DiagnosticTechnician };
    private static readonly IReadOnlySet<AiActorRole> Pharmacist = new HashSet<AiActorRole> { AiActorRole.Pharmacist };
    private static readonly IReadOnlySet<AiActorRole> AllOperational = new HashSet<AiActorRole>
    {
        AiActorRole.Receptionist, AiActorRole.Doctor, AiActorRole.DiagnosticTechnician, AiActorRole.Pharmacist
    };

    public static IReadOnlyList<AiToolDefinition> Definitions { get; } = new[]
    {
        Prepare("reception.prepare_check_in_appointment", "Chuẩn bị check-in lịch hẹn", Reception, AiActorCapability.PrepareReceptionAction),
        Prepare("reception.prepare_create_walk_in", "Chuẩn bị tiếp nhận bệnh nhân vãng lai từ hồ sơ đã có", Reception, AiActorCapability.PrepareReceptionAction),
        Prepare("doctor.prepare_diagnostic_order", "Chuẩn bị phiếu chỉ định cận lâm sàng", Doctor, AiActorCapability.PrepareDoctorAction),
        Prepare("doctor.prepare_prescription_draft", "Chuẩn bị bản nháp đơn thuốc; không phát hành", Doctor, AiActorCapability.PrepareDoctorAction),
        Prepare("technician.prepare_start_diagnostic_order", "Chuẩn bị tiếp nhận phiếu chỉ định", Technician, AiActorCapability.PrepareDiagnosticAction),
        Prepare("technician.prepare_record_diagnostic_result", "Chuẩn bị lưu kết quả kỹ thuật", Technician, AiActorCapability.PrepareDiagnosticAction),
        Prepare("technician.prepare_complete_diagnostic_order", "Chuẩn bị hoàn tất phiếu chỉ định", Technician, AiActorCapability.PrepareDiagnosticAction),
        Prepare("pharmacist.prepare_reserve_prescription", "Chuẩn bị giữ chỗ thuốc theo đơn hợp lệ", Pharmacist, AiActorCapability.PreparePharmacyAction),
        Prepare("pharmacist.prepare_dispense_prescription", "Chuẩn bị cấp phát đơn thuốc đã đủ điều kiện thanh toán", Pharmacist, AiActorCapability.PreparePharmacyAction),
        new AiToolDefinition
        {
            Name = "role.execute_confirmed_action",
            Version = "1.0",
            Description = "Thực hiện pending role action qua endpoint xác nhận trực tiếp",
            AccessMode = AiToolAccessMode.RoleRestricted,
            RiskLevel = AiToolRiskLevel.High,
            Confirmation = AiToolConfirmationRequirement.ExplicitUserConfirmation,
            AllowedRoles = AllOperational,
            Capabilities = new HashSet<AiActorCapability> { AiActorCapability.ExecuteConfirmedRoleAction },
            DataSources = new[] { new AiToolDataSource("pending_action_store", "database") }
        }
    };

    private static AiToolDefinition Prepare(string name, string description, IReadOnlySet<AiActorRole> roles, AiActorCapability capability) => new()
    {
        Name = name,
        Version = "1.0",
        Description = description,
        AccessMode = AiToolAccessMode.RoleRestricted,
        RiskLevel = AiToolRiskLevel.High,
        Confirmation = AiToolConfirmationRequirement.ExplicitUserConfirmation,
        AllowedRoles = roles,
        Capabilities = new HashSet<AiActorCapability> { capability },
        DataSources = new[] { new AiToolDataSource("ClinicCare domain service", "service") }
    };
}
