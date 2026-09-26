namespace ClinicManagement.Application.AI.Tools;

/// <summary>
/// Server-owned read-only tools for the authenticated workspaces. These tools
/// intentionally have no write equivalent in Phase 2; operational writes still
/// go through the existing domain endpoints and explicit human confirmation.
/// </summary>
public static class AiRoleToolCatalog
{
    public static IReadOnlyList<AiToolDefinition> Definitions { get; } = new[]
    {
        RoleTool("reception.get_today_appointments", "Xem lịch hẹn trong ngày của cơ sở được phân quyền", AiActorRole.Receptionist, AiActorCapability.ReadReceptionWorkspace),
        RoleTool("reception.get_queue", "Xem hàng đợi tiếp nhận của cơ sở được phân quyền", AiActorRole.Receptionist, AiActorCapability.ReadReceptionWorkspace),
        RoleTool("reception.lookup_appointment", "Tra cứu lịch hẹn bằng mã đối soát", AiActorRole.Receptionist, AiActorCapability.ReadReceptionWorkspace),
        RoleTool("doctor.get_my_queue", "Xem hàng đợi bệnh nhân được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace),
        RoleTool("doctor.get_patient_summary", "Xem tóm tắt bệnh nhân trong encounter được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace),
        RoleTool("doctor.get_diagnostic_orders", "Xem chỉ định cận lâm sàng của ca được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace),
        RoleTool("technician.get_worklist", "Xem danh sách chỉ định cận lâm sàng được phân công", AiActorRole.DiagnosticTechnician, AiActorCapability.ReadDiagnosticWorkspace),
        RoleTool("pharmacist.get_prescription_queue", "Xem đơn thuốc đủ điều kiện xử lý", AiActorRole.Pharmacist, AiActorCapability.ReadPharmacyWorkspace),
        RoleTool("pharmacist.get_inventory_status", "Xem trạng thái tồn kho tổng hợp", AiActorRole.Pharmacist, AiActorCapability.ReadPharmacyWorkspace),
        RoleTool("admin.get_dashboard_metrics", "Xem chỉ số tổng hợp không chứa dữ liệu lâm sàng", AiActorRole.Admin, AiActorCapability.ReadAdminMetrics),
        RoleTool("admin.get_ai_health", "Xem chỉ số hoạt động AI đã được khử định danh", AiActorRole.Admin, AiActorCapability.ReadAdminMetrics)
    };

    private static AiToolDefinition RoleTool(string name, string description, AiActorRole role, AiActorCapability capability) => new()
    {
        Name = name,
        Version = "1.0",
        Description = description,
        AccessMode = AiToolAccessMode.RoleRestricted,
        RiskLevel = AiToolRiskLevel.Low,
        Confirmation = AiToolConfirmationRequirement.None,
        AllowedRoles = new HashSet<AiActorRole> { role },
        Capabilities = new HashSet<AiActorCapability> { capability },
        DataSources = new[] { new AiToolDataSource("ClinicCare domain database", "database") }
    };
}
