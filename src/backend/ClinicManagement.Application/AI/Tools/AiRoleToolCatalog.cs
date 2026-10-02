namespace ClinicManagement.Application.AI.Tools;

/// <summary>
/// Server-owned read-only tools for the authenticated workspaces. These tools
/// intentionally have no write equivalent in Phase 2; operational writes still
/// go through the existing domain endpoints and explicit human confirmation.
/// </summary>
public static class AiRoleToolCatalog
{
    private static readonly IReadOnlyList<AiToolArgumentDefinition> DoctorResourceSchema = new[]
    {
        Arg("appointmentId", AiToolArgumentType.Integer, serverBound: true),
        Arg("visitId", AiToolArgumentType.Integer, serverBound: true)
    };

    private static readonly AiToolResourceBinding DoctorResourceBinding = new()
    {
        ServerBoundArgumentNames = new[] { "visitId", "appointmentId" },
        RequiresCurrentResource = true
    };

    public static IReadOnlyList<AiToolDefinition> Definitions { get; } = new[]
    {
        RoleTool("reception.get_today_appointments", "Xem lịch hẹn trong ngày của cơ sở được phân quyền", AiActorRole.Receptionist, AiActorCapability.ReadReceptionWorkspace),
        RoleTool("reception.get_queue", "Xem hàng đợi tiếp nhận của cơ sở được phân quyền", AiActorRole.Receptionist, AiActorCapability.ReadReceptionWorkspace),
        RoleToolWithSchema("reception.lookup_appointment", "Tra cứu lịch hẹn bằng mã đối soát", AiActorRole.Receptionist, AiActorCapability.ReadReceptionWorkspace,
            new[] { Arg("appointmentCode", AiToolArgumentType.String, required: true) }),
        RoleTool("doctor.get_my_queue", "Xem hàng đợi bệnh nhân được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace),
        RoleToolWithSchema("doctor.get_patient_summary", "Xem tóm tắt bệnh nhân trong encounter được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace,
            DoctorResourceSchema, DoctorResourceBinding),
        RoleToolWithSchema("doctor.get_diagnostic_orders", "Xem chỉ định cận lâm sàng của ca được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace,
            DoctorResourceSchema, DoctorResourceBinding),
        RoleToolWithSchema("doctor.get_prescription_status", "Xem trạng thái đơn thuốc của ca được phân công", AiActorRole.Doctor, AiActorCapability.ReadDoctorWorkspace,
            DoctorResourceSchema, DoctorResourceBinding),
        RoleTool("technician.get_worklist", "Xem danh sách chỉ định cận lâm sàng được phân công", AiActorRole.DiagnosticTechnician, AiActorCapability.ReadDiagnosticWorkspace),
        RoleTool("pharmacist.get_prescription_queue", "Xem đơn thuốc đủ điều kiện xử lý", AiActorRole.Pharmacist, AiActorCapability.ReadPharmacyWorkspace),
        RoleToolWithSchema("pharmacist.get_prescription_payment_status", "Đối chiếu thanh toán theo từng dòng của đơn thuốc đang mở", AiActorRole.Pharmacist, AiActorCapability.ReadPharmacyWorkspace,
            new[] { Arg("prescriptionId", AiToolArgumentType.Integer, serverBound: true) },
            new AiToolResourceBinding { ServerBoundArgumentNames = new[] { "prescriptionId" }, RequiresCurrentResource = true }),
        RoleTool("pharmacist.get_inventory_status", "Xem tồn kho toàn hệ thống (mô hình hiện tại chưa phân tách theo cơ sở; vẫn yêu cầu phân công Pharmacist hợp lệ)", AiActorRole.Pharmacist, AiActorCapability.ReadPharmacyWorkspace),
        RoleTool("admin.get_dashboard_metrics", "Xem chỉ số tổng hợp không chứa dữ liệu lâm sàng", AiActorRole.Admin, AiActorCapability.ReadAdminMetrics),
        RoleTool("admin.get_ai_health", "Xem chỉ số hoạt động AI đã được khử định danh", AiActorRole.Admin, AiActorCapability.ReadAdminMetrics),
        RoleToolWithSchema("clinic.search_knowledge", "Tra cứu kiến thức phòng khám đã được phê duyệt và có nguồn", AiActorRole.Patient, AiActorCapability.ReadClinicCatalog,
            new[]
            {
                Arg("entity", AiToolArgumentType.String), Arg("query", AiToolArgumentType.String, required: true),
                Arg("specialtyQuery", AiToolArgumentType.String), Arg("facilityQuery", AiToolArgumentType.String),
                Arg("limit", AiToolArgumentType.Integer)
            },
            AiToolResourceBinding.None,
            AiActorRole.Receptionist, AiActorRole.Doctor, AiActorRole.DiagnosticTechnician, AiActorRole.Pharmacist, AiActorRole.Admin)
    };

    private static AiToolDefinition RoleTool(string name, string description, AiActorRole role, AiActorCapability capability, params AiActorRole[] additionalRoles) => new()
    {
        Name = name,
        Version = "1.0",
        Description = description,
        AccessMode = AiToolAccessMode.RoleRestricted,
        RiskLevel = AiToolRiskLevel.Low,
        Confirmation = AiToolConfirmationRequirement.None,
        AllowedRoles = new HashSet<AiActorRole>(new[] { role }.Concat(additionalRoles)),
        Capabilities = new HashSet<AiActorCapability> { capability },
        DataSources = new[] { new AiToolDataSource("ClinicCare domain database", "database") }
    };

    private static AiToolDefinition RoleToolWithSchema(
        string name,
        string description,
        AiActorRole role,
        AiActorCapability capability,
        IReadOnlyList<AiToolArgumentDefinition> schema,
        AiToolResourceBinding? binding = null,
        params AiActorRole[] additionalRoles) => new()
    {
        Name = name,
        Version = "1.0",
        Description = description,
        AccessMode = AiToolAccessMode.RoleRestricted,
        RiskLevel = AiToolRiskLevel.Low,
        Confirmation = AiToolConfirmationRequirement.None,
        AllowedRoles = new HashSet<AiActorRole>(new[] { role }.Concat(additionalRoles)),
        Capabilities = new HashSet<AiActorCapability> { capability },
        DataSources = new[] { new AiToolDataSource("ClinicCare domain database", "database") },
        ArgumentSchema = schema,
        ResourceBinding = binding ?? AiToolResourceBinding.None
    };

    private static AiToolArgumentDefinition Arg(string name, AiToolArgumentType type, bool required = false, bool serverBound = false) =>
        new(name, type, required, serverBound);

}
