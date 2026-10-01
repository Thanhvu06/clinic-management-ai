using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Suggestions;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI.Planning;

/// <summary>
/// Routes only explicit, high-confidence commands. Ambiguous text deliberately
/// returns RequiresProvider instead of falling through to a role default tool.
/// </summary>
public sealed class AiDeterministicPlanner : IAiDeterministicPlanner
{
    private static readonly Regex Greeting = new(@"^(?:xin\s+)?(?:chao|hello|hi|cam on|cảm ơn)(?:\s+(?:ban|bạn|ai|tro ly|trợ lý))?[!.?]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Help = new(@"\b(?:giup|huong dan|lam duoc gi|co the lam gi)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Single-tool read routes shared by the free-text rules and suggestion
    // buttons, so both paths report the same intent, sub-intent and route.
    private static readonly IReadOnlyDictionary<string, (string Intent, string SubIntent, string Route)> SingleReadRoutes =
        new Dictionary<string, (string Intent, string SubIntent, string Route)>(StringComparer.OrdinalIgnoreCase)
        {
            ["patient.get_my_diagnostic_results"] = (AiChatIntentTypes.DiagnosticLookup, "MyDiagnosticResults", "/patient/diagnostic-results"),
            ["patient.get_my_prescriptions"] = (AiChatIntentTypes.PrescriptionLookup, "MyPrescriptions", "/patient/prescriptions"),
            ["patient.get_my_bills"] = (AiChatIntentTypes.ViewAppointments, "MyBills", "/patient/invoices"),
            ["patient.get_my_visits"] = (AiChatIntentTypes.ViewAppointments, "MyVisits", "/patient/appointments"),
            ["patient.get_my_appointments"] = (AiChatIntentTypes.ViewAppointments, "MyAppointments", "/patient/appointments"),
            ["doctor.get_my_queue"] = (AiChatIntentTypes.QueueLookup, "DoctorQueue", "/doctor/queue"),
            ["doctor.get_patient_summary"] = (AiChatIntentTypes.PatientSummary, "AssignedPatientSummary", "/doctor/appointments"),
            ["doctor.get_diagnostic_orders"] = (AiChatIntentTypes.DiagnosticLookup, "AssignedDiagnosticOrders", "/doctor/appointments"),
            ["doctor.get_prescription_status"] = (AiChatIntentTypes.PrescriptionLookup, "AssignedPrescriptionStatus", "/doctor/appointments"),
            ["reception.get_today_appointments"] = (AiChatIntentTypes.ViewAppointments, "TodayAppointments", "/reception/appointments"),
            ["reception.get_queue"] = (AiChatIntentTypes.QueueLookup, "ReceptionQueue", "/reception"),
            ["technician.get_worklist"] = (AiChatIntentTypes.DiagnosticLookup, "TechnicianWorklist", "/diagnostics"),
            ["pharmacist.get_prescription_queue"] = (AiChatIntentTypes.PrescriptionLookup, "PrescriptionQueue", "/pharmacy/prescriptions"),
            ["pharmacist.get_inventory_status"] = (AiChatIntentTypes.PharmacyInventory, "InventoryStatus", "/pharmacy/inventory"),
            ["pharmacist.get_prescription_payment_status"] = (AiChatIntentTypes.PrescriptionLookup, "PrescriptionPaymentStatus", "/pharmacy/prescriptions"),
            ["admin.get_dashboard_metrics"] = (AiChatIntentTypes.AdminMetrics, "DashboardMetrics", "/admin"),
            ["admin.get_ai_health"] = (AiChatIntentTypes.AdminMetrics, "AiHealth", "/admin/audit-logs")
        };

    public AiPlannerDecision PlanSuggestion(AiSuggestionDefinition? suggestion, AiResolvedResourceContext resource)
    {
        if (suggestion is null) return SuggestionRejected();
        if (suggestion.ActionKind == AiSuggestionActionKind.Wizard)
            return Local(AiChatIntentTypes.StartBooking, "BookingWizard", "Chọn Đặt lịch khám để bắt đầu chọn chuyên khoa và lịch trống.", 1m);
        if (!SingleReadRoutes.TryGetValue(suggestion.ToolName, out var route))
            return SuggestionRejected();

        if (suggestion.ResourceKind == AiSuggestionResourceKind.None)
            return ReadTool(suggestion.ToolName, suggestion.ToolName.StartsWith("patient.", StringComparison.OrdinalIgnoreCase) ? new { page = 1, pageSize = 20 } : new { });

        if (suggestion.ResourceKind == AiSuggestionResourceKind.Prescription)
            return resource.PrescriptionId.HasValue
                // Resource arguments are attached by the existing binding pipeline.
                ? ReadTool(suggestion.ToolName, new { })
                : new AiPlannerDecision
                {
                    PlannerMode = AiPlannerModes.Deterministic,
                    Intent = route.Intent,
                    SubIntent = "MissingPrescriptionForPayment",
                    ErrorCode = AiPlannerErrorCodes.ResourceContextRequired,
                    Confidence = 1m,
                    Clarification = "Hãy mở đúng đơn thuốc cần kiểm tra để đối chiếu thanh toán.",
                    Message = "Hãy mở đúng đơn thuốc cần kiểm tra để đối chiếu thanh toán.",
                    NavigationRoute = route.Route
                };

        if (!resource.AppointmentId.HasValue && !resource.VisitId.HasValue)
            return new AiPlannerDecision
            {
                PlannerMode = AiPlannerModes.Deterministic,
                Intent = route.Intent,
                SubIntent = "MissingAssignedCase",
                ErrorCode = AiPlannerErrorCodes.ResourceContextRequired,
                Confidence = 1m,
                Clarification = "Hãy mở một ca khám được phân công hoặc chọn một lượt trong hàng đợi của bạn để tôi đọc đúng bệnh nhân.",
                Message = "Hãy mở một ca khám được phân công hoặc chọn một lượt trong hàng đợi của bạn để tôi đọc đúng bệnh nhân.",
                NavigationRoute = route.Route
            };

        return ReadTool(suggestion.ToolName, resource.VisitId.HasValue
            ? new { visitId = resource.VisitId.Value }
            : new { appointmentId = resource.AppointmentId!.Value });
    }

    private static AiPlannerDecision SuggestionRejected() => new()
    {
        PlannerMode = AiPlannerModes.Deterministic,
        Intent = AiChatIntentTypes.ClarificationRequired,
        SubIntent = AiPlannerErrorCodes.ToolNotAllowed,
        ErrorCode = AiPlannerErrorCodes.ToolNotAllowed,
        Confidence = 1m,
        Clarification = "Gợi ý này không có sẵn cho tài khoản của bạn. Hãy chọn một gợi ý khác hoặc mô tả rõ dữ liệu cần tra cứu.",
        Message = "Gợi ý này không có sẵn cho tài khoản của bạn. Hãy chọn một gợi ý khác hoặc mô tả rõ dữ liệu cần tra cứu."
    };

    private static AiPlannerDecision ReadTool(string toolName, object arguments)
    {
        var route = SingleReadRoutes[toolName];
        return Tool(route.Intent, route.SubIntent, toolName, arguments, route.Route);
    }

    public AiPlannerDecision Plan(AiCopilotPlanningContext context)
    {
        var text = AiTextNormalizer.NormalizeForComparison(context.NormalizedMessage);
        if (string.IsNullOrWhiteSpace(text))
            return Clarify("Bạn muốn tôi hỗ trợ tra cứu nội dung nào trong workspace hiện tại?", "EmptyInput");

        var knowledgeRequest = Regex.IsMatch(text, @"\b(?:bac si|bsi|doctor|gio lam viec|ngay nghi|chu nhat|gia|chi phi|dich vu|co so|phong kham|danh sach|danh muc|bang gia|liet ke|huong dan|truoc kham|chuan bi xet nghiem|chuyen khoa|tom tat|tom luoc|tong hop)\b", RegexOptions.CultureInvariant);

        if (Greeting.IsMatch(text) || context.Analysis.Intent.Intent == AiChatIntentTypes.Greeting)
        {
            return Local(AiChatIntentTypes.Greeting, "Greeting", "Xin chào. Tôi có thể hỗ trợ tra cứu dữ liệu trong phạm vi công việc và quyền hiện tại của bạn.", 1m);
        }

        if (Help.IsMatch(text) && !knowledgeRequest)
        {
            return Local(AiChatIntentTypes.Help, "RoleHelp", "Bạn có thể dùng các gợi ý bên dưới hoặc mô tả rõ dữ liệu cần tra cứu. Tôi sẽ không thực hiện thao tác ghi trong cuộc hội thoại này.", .99m);
        }

        var containsReadRequest = Regex.IsMatch(text,
            @"\b(?:xem|tra cuu|tim|kiem tra|tom tat|tom luoc|tong hop|danh sach|hang doi|lich|thong tin|doc)\b",
            RegexOptions.CultureInvariant);
        var containsWriteRequest = HasAffirmativeWriteRequest(text);
        if (containsReadRequest && containsWriteRequest)
        {
            return Clarify(
                "Tôi không thể trộn tra cứu với thao tác ghi trong cùng một kế hoạch. Hãy chọn một việc và xác nhận thao tác ghi qua màn hình hành động.",
                "MixedReadWritePlan");
        }

        var roleDecision = context.Role switch
        {
            AiActorRole.Receptionist => PlanReception(text),
            AiActorRole.Doctor => PlanDoctor(text, context.Resource),
            AiActorRole.DiagnosticTechnician => PlanTechnician(text),
            AiActorRole.Pharmacist => PlanPharmacist(text, context.Resource),
            AiActorRole.Admin => PlanAdmin(text),
            AiActorRole.Patient => PlanPatient(text),
            _ => ProviderRequired(context.Analysis.Intent.Intent)
        };

        if (knowledgeRequest && roleDecision.ToolCalls.Count == 0 && roleDecision.Clarification is null && IsAmbiguousCatalogRequest(text))
            return Clarify("Bạn muốn xem danh sách hay tra cứu tên chuyên khoa, bác sĩ, dịch vụ hoặc cơ sở nào?", "AmbiguousCatalogQuery", AiChatIntentTypes.FacilityInquiry, "/locations");

        if (knowledgeRequest && roleDecision.ToolCalls.Count == 0 && roleDecision.Clarification is null)
            return Tool(AiChatIntentTypes.FacilityInquiry, "ClinicKnowledge", "clinic.search_knowledge", BuildKnowledgeArguments(text, context.NormalizedMessage), "/locations");

        return roleDecision;
    }

    private static AiPlannerDecision PlanReception(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:chuan bi check[- ]?in|check[- ]?in lich hen|tiep nhan lich hen)\b", RegexOptions.CultureInvariant))
            return Clarify("Check-in phải đi qua action gateway và bước xác nhận rõ ràng; tôi không tự thực hiện thao tác ghi từ cuộc hội thoại.", "WriteRequiresExplicitActionConfirmation");

        if (Regex.IsMatch(text, @"\b(?:tra cuu|tim|kiem tra)\b.*\b(?:ma )?(?:lich hen|cuoc hen|lh)\b", RegexOptions.CultureInvariant))
        {
            var code = Regex.Match(text, @"\b[a-z]{2,}-?\d{3,}\b", RegexOptions.CultureInvariant).Value;
            if (string.IsNullOrWhiteSpace(code))
                return Clarify("Vui lòng cung cấp mã lịch hẹn cần tra cứu.", "MissingAppointmentCode", AiChatIntentTypes.ViewAppointments);
            return Tool(AiChatIntentTypes.ViewAppointments, "LookupAppointment", "reception.lookup_appointment", new { appointmentCode = code.ToUpperInvariant() }, "/reception/appointments");
        }

        if (Regex.IsMatch(text, @"\b(?:lich hen|cuoc hen|lh)\b.*\b(?:hom nay|trong ngay|sang nay)\b|\b(?:hom nay|trong ngay|sang nay)\b.*\b(?:lich hen|cuoc hen|lh)\b|\b(?:danh sach|so luong)\b.*\b(?:nguoi|cuoc hen|lich)\b.*\b(?:dat|hen)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.ViewAppointments, "TodayAppointments", "reception.get_today_appointments", new { }, "/reception/appointments");

        if (Regex.IsMatch(text, @"\b(?:hang doi|xep hang|cho tiep nhan|quay tiep nhan|dang cho|queue)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.QueueLookup, "ReceptionQueue", "reception.get_queue", new { }, "/reception");

        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanDoctor(string text, AiResolvedResourceContext resource)
    {
        if (Regex.IsMatch(text, @"\b(?:chuan bi (?:phieu|ban nhap|don)|ke (?:don|thuoc)|phat hanh|tao (?:phieu|don))\b", RegexOptions.CultureInvariant))
            return Clarify("Thao tác ghi của bác sĩ phải đi qua action gateway và bước xác nhận rõ ràng; cuộc hội thoại không tự tạo hoặc phát hành dữ liệu.", "WriteRequiresExplicitActionConfirmation");

        var asksSummary = (Regex.IsMatch(text, @"\b(?:tom tat|tom luoc|tong hop|mo)\b.*\b(?:benh nhan|ca kham|ca|ho so|hien tai|luot)\b", RegexOptions.CultureInvariant) &&
            Regex.IsMatch(text, @"\b(?:tom tat|tom luoc|tong hop|ho so|ca kham|benh nhan)\b", RegexOptions.CultureInvariant)) ||
            (Regex.IsMatch(text, @"\b(?:trieu chung|dau hieu sinh ton|sinh hieu|chief complaint)\b", RegexOptions.CultureInvariant) &&
             Regex.IsMatch(text, @"\b(?:benh nhan|ca|luot|walk in|walk-in)\b", RegexOptions.CultureInvariant));
        var asksOrders = Regex.IsMatch(text, @"\b(?:chi dinh|can lam sang|xet nghiem|chan doan hinh anh)\b", RegexOptions.CultureInvariant);
        var asksPrescription = Regex.IsMatch(text, @"\b(?:don thuoc|toa thuoc|trang thai .*thuoc|thuoc cua ca)\b", RegexOptions.CultureInvariant);

        var hasCaseContext = resource.AppointmentId.HasValue || resource.VisitId.HasValue;
        if ((asksSummary || asksOrders || asksPrescription) && !hasCaseContext)
            return Clarify("Hãy mở một ca khám được phân công hoặc chọn một lượt trong hàng đợi của bạn để tôi đọc đúng bệnh nhân.", "MissingAssignedCase", AiChatIntentTypes.PatientSummary, "/doctor/appointments");

        if (asksSummary || asksOrders || asksPrescription)
        {
            var calls = new List<AiPlannerToolCall>();
            if (asksSummary)
                calls.Add(resource.VisitId.HasValue
                    ? Call("doctor.get_patient_summary", new { visitId = resource.VisitId.Value })
                    : Call("doctor.get_patient_summary", new { appointmentId = resource.AppointmentId!.Value }));
            if (asksOrders)
                calls.Add(resource.VisitId.HasValue
                    ? Call("doctor.get_diagnostic_orders", new { visitId = resource.VisitId.Value })
                    : Call("doctor.get_diagnostic_orders", new { appointmentId = resource.AppointmentId!.Value }));
            if (asksPrescription)
                calls.Add(resource.VisitId.HasValue
                    ? Call("doctor.get_prescription_status", new { visitId = resource.VisitId.Value })
                    : Call("doctor.get_prescription_status", new { appointmentId = resource.AppointmentId!.Value }));
            return new AiPlannerDecision
            {
                PlannerMode = AiPlannerModes.Deterministic,
                Intent = asksSummary ? AiChatIntentTypes.PatientSummary : asksPrescription ? AiChatIntentTypes.PrescriptionLookup : AiChatIntentTypes.DiagnosticLookup,
                SubIntent = asksSummary && asksOrders ? "PatientSummaryAndDiagnosticOrders" : asksSummary && asksPrescription ? "PatientSummaryAndPrescription" : asksSummary ? "AssignedPatientSummary" : asksPrescription ? "AssignedPrescriptionStatus" : "AssignedDiagnosticOrders",
                Confidence = .99m,
                NavigationRoute = "/doctor/appointments",
                ToolCalls = calls
            };
        }

        if (Regex.IsMatch(text, @"\b(?:hang doi|benh nhan tiep theo|ca tiep theo|danh sach cho|queue cua toi|ca benh .*\b(?:cho|dang cho)\b|danh sach .*\b(?:benh nhan|nguoi benh)\b.*\b(?:hom nay|tiep theo)\b|(?:benh nhan|nguoi benh)\b.*\bdanh sach\b.*\b(?:hom nay|tiep theo)\b)", RegexOptions.CultureInvariant))
            return ReadTool("doctor.get_my_queue", new { });

        if (Regex.IsMatch(text, @"\b(?:benh nhan|ca kham|luot kham|trieu chung|sinh hieu|chan doan|chi tiet ho so|don thuoc|chi dinh|ket qua)\b", RegexOptions.CultureInvariant))
            return Clarify("Hãy mở hoặc chọn một ca khám được phân công từ hàng đợi của bạn; tôi không gửi hồ sơ lâm sàng sang nhà cung cấp AI để đoán tài nguyên.", "MissingAssignedCase", AiChatIntentTypes.PatientSummary, "/doctor/appointments");

        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanTechnician(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:chuan bi tiep nhan phieu|tiep nhan phieu|nhan phieu|ghi ket qua|hoan tat order)\b", RegexOptions.CultureInvariant))
            return Clarify("Thao tác kỹ thuật phải đi qua action gateway và bước xác nhận rõ ràng; cuộc hội thoại không tự ghi kết quả.", "WriteRequiresExplicitActionConfirmation");

        if (Regex.IsMatch(text, @"\b(?:worklist|wl|danh sach chi dinh|chi dinh dang cho|cong viec dang cho|qua han|phieu xet nghiem|phieu nay\b.*\b(?:muc|chua|hoan thanh)\b|chi dinh .*\b(?:doi|cho xu ly|chua xu ly)\b)", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.DiagnosticLookup, "TechnicianWorklist", "technician.get_worklist", new { }, "/diagnostics");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanPharmacist(string text, AiResolvedResourceContext resource)
    {
        if (Regex.IsMatch(text, @"\b(?:chuan bi giu cho|giu cho thuoc|cap phat don thuoc)\b", RegexOptions.CultureInvariant))
            return Clarify("Giữ chỗ hoặc cấp phát thuốc phải đi qua action gateway và bước xác nhận rõ ràng.", "WriteRequiresExplicitActionConfirmation");

        var asksPrescriptionPayment = Regex.IsMatch(text, @"\b(?:thanh toan|da thanh toan|tra du|du tien|con thieu|thieu tien|payment|paid)\b", RegexOptions.CultureInvariant) &&
                                      Regex.IsMatch(text, @"\b(?:don|toa|thuoc)\b", RegexOptions.CultureInvariant);
        if (asksPrescriptionPayment)
        {
            if (!resource.PrescriptionId.HasValue)
                return Clarify("Hãy mở đúng đơn thuốc cần kiểm tra để tôi đối chiếu thanh toán theo từng dòng thuốc, không suy diễn từ hóa đơn khám.", "MissingPrescriptionForPayment", AiChatIntentTypes.PrescriptionLookup, "/pharmacy/prescriptions");

            return Tool(AiChatIntentTypes.PrescriptionLookup, "PrescriptionPaymentStatus", "pharmacist.get_prescription_payment_status", new { prescriptionId = resource.PrescriptionId.Value }, "/pharmacy/prescriptions");
        }

        if (Regex.IsMatch(text, @"\b(?:ton kho|kho thuoc|sap het|thieu thuoc|so luong .*thuoc.*kho|thuoc.*trong kho|kho .*thuoc|thuoc .*duoi nguong)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.PharmacyInventory, "InventoryStatus", "pharmacist.get_inventory_status", new { }, "/pharmacy/inventory");
        if (Regex.IsMatch(text, @"\b(?:don thuoc|hang doi cap thuoc|cho cap|cho phat|don .*\b(?:xep hang|dang cho)\b|xep hang .*\bdon\b)", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.PrescriptionLookup, "PrescriptionQueue", "pharmacist.get_prescription_queue", new { }, "/pharmacy/prescriptions");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanAdmin(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:tinh trang ai|suc khoe ai|he thong ai .*khoe|ai .*khoe|dich vu ai|provider|tool execution|audit ai)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.AdminMetrics, "AiHealth", "admin.get_ai_health", new { }, "/admin/audit-logs");
        if (Regex.IsMatch(text, @"\b(?:thong ke|dashboard|chi so|bao cao tong hop)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.AdminMetrics, "DashboardMetrics", "admin.get_dashboard_metrics", new { }, "/admin");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanPatient(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:ket qua|ket qua xet nghiem|xet nghiem cua toi|chi dinh cua toi|ket qua can lam sang|kq xet nghiem)\b", RegexOptions.CultureInvariant))
            return ReadTool("patient.get_my_diagnostic_results", new { page = 1, pageSize = 20 });
        if (Regex.IsMatch(text, @"\b(?:don thuoc|thuoc cua toi|thuoc dang|ke don|bac si ke thuoc|phat thuoc|toa thuoc)\b", RegexOptions.CultureInvariant))
            return ReadTool("patient.get_my_prescriptions", new { page = 1, pageSize = 20 });
        if (Regex.IsMatch(text, @"\b(?:hoa don|thanh toan|vien phi|chi phi da|chi phi kham|bien lai)\b", RegexOptions.CultureInvariant))
            return ReadTool("patient.get_my_bills", new { page = 1, pageSize = 20 });
        if (Regex.IsMatch(text, @"\b(?:luot kham|lan kham|lich su kham|tom tat ca kham|chan doan cua toi|trieu chung cua toi)\b", RegexOptions.CultureInvariant))
            return ReadTool("patient.get_my_visits", new { page = 1, pageSize = 20 });
        if (Regex.IsMatch(text, @"\b(?:lich(?:\s+(?:hen|cu|cuoc hen|da dat|truoc|sap toi|cua toi|kham cua toi))|cuoc hen|hen sap toi)\b", RegexOptions.CultureInvariant))
            return ReadTool("patient.get_my_appointments", new { page = 1, pageSize = 20 });
        if (Regex.IsMatch(text, @"\b(?:benh an|ho so|luot kham|chan doan|trieu chung|ket qua|don thuoc|hoa don)\b", RegexOptions.CultureInvariant))
            return Clarify("Bạn hãy nêu rõ muốn xem lịch hẹn, lượt khám, kết quả, đơn thuốc hay hóa đơn của chính tài khoản này.", "AmbiguousPatientRead", AiChatIntentTypes.ClarificationRequired, "/patient");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision Tool(string intent, string subIntent, string name, object arguments, string route) => new()
    {
        PlannerMode = AiPlannerModes.Deterministic,
        Intent = intent,
        SubIntent = subIntent,
        Confidence = .99m,
        NavigationRoute = route,
        ToolCalls = new[] { Call(name, arguments) }
    };

    private static string InferKnowledgeEntity(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:gia|phi|chi phi|bao nhieu tien|vien phi)\b", RegexOptions.CultureInvariant))
            return "price";
        if (Regex.IsMatch(text, @"\b(?:bac si|bsi|doctor|nguoi nao kham)\b", RegexOptions.CultureInvariant))
            return "doctor";
        if (Regex.IsMatch(text, @"\b(?:chuyen khoa|khoa)\b", RegexOptions.CultureInvariant))
            return "specialty";
        if (Regex.IsMatch(text, @"\b(?:dich vu|xet nghiem|sieu am|noi soi|chup)\b", RegexOptions.CultureInvariant))
            return "diagnostic_service";
        if (Regex.IsMatch(text, @"\b(?:co so|phong kham|dia chi|gio lam viec|ngay nghi|chu nhat|mo cua)\b", RegexOptions.CultureInvariant))
            return "facility";
        return "all";
    }

    private static object BuildKnowledgeArguments(string text, string displayText)
    {
        var entity = InferKnowledgeEntity(text);
        if (!string.Equals(entity, "doctor", StringComparison.OrdinalIgnoreCase))
            return new { entity, query = displayText, limit = 10 };

        var specialty = Regex.Match(text, @"\b(?:kham|chuyen khoa)\s+(?<value>.+?)(?=\s+\b(?:o|tai)\s+co so\b|\s*$)", RegexOptions.CultureInvariant).Groups["value"].Value.Trim().TrimEnd('?', '.', '!');
        var facility = Regex.Match(text, @"\b(?:o|tai)\s+co so\s+(?<value>.+)$", RegexOptions.CultureInvariant).Groups["value"].Value.Trim().TrimEnd('?', '.', '!');
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity"] = entity,
            // Keep the full user query as the primary match so a request for
            // a named/inactive doctor cannot broaden into another doctor that
            // merely shares the specialty filter.
            ["query"] = displayText,
            ["limit"] = 10
        };
        if (!string.IsNullOrWhiteSpace(specialty)) arguments["specialtyQuery"] = specialty;
        if (!string.IsNullOrWhiteSpace(facility)) arguments["facilityQuery"] = facility;
        return arguments;
    }

    private static bool IsAmbiguousCatalogRequest(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:danh sach|danh muc|liet ke|tat ca|bang gia|muc gia|cac\s+(?:co so|bac si|dich vu|chuyen khoa))\b", RegexOptions.CultureInvariant))
            return false;

        return Regex.IsMatch(text, @"^(?:cho toi|hay|xin|muon biet|tra cuu|xem|tim|co the cho toi)?\s*(?:co\s+)?(?:bac si|bsi|dich vu|chuyen khoa|co so|phong kham|gia|thong tin)(?:\s+(?:nao|gi|the nao|khong))?[?.!]*$", RegexOptions.CultureInvariant);
    }

    private static AiPlannerToolCall Call(string name, object arguments) => new()
    {
        Name = name,
        Version = "1.0",
        Arguments = JsonSerializer.SerializeToElement(arguments)
    };

    private static AiPlannerDecision Local(string intent, string subIntent, string message, decimal confidence) => new()
    {
        PlannerMode = AiPlannerModes.Deterministic,
        Intent = intent,
        SubIntent = subIntent,
        Confidence = confidence,
        Message = message
    };

    private static AiPlannerDecision Clarify(string question, string subIntent, string intent = AiChatIntentTypes.ClarificationRequired, string? route = null) => new()
    {
        PlannerMode = AiPlannerModes.Deterministic,
        Intent = intent,
        SubIntent = subIntent,
        Confidence = 1m,
        Clarification = question,
        Message = question,
        NavigationRoute = route
    };

    private static AiPlannerDecision ProviderRequired(string intent) => new()
    {
        PlannerMode = AiPlannerModes.LocalClassifier,
        Intent = intent,
        Confidence = 0m,
        RequiresProvider = true
    };

    private static bool HasAffirmativeWriteRequest(string text)
    {
        var writePattern = new Regex(@"\b(?:tao|(?<!da\s)dat\s+(?:lich|hen|cho)|check in|ghi|luu|hoan tat|cap phat|giu cho|ke (?:don|thuoc)|chuan bi (?:phieu|ban nhap|don)|thuc hien|lam luon|xuat|phat hanh)\b", RegexOptions.CultureInvariant);
        foreach (Match match in writePattern.Matches(text))
        {
            var prefix = text[..match.Index].TrimEnd();
            if (Regex.IsMatch(prefix, @"(?:\bkhong\s+phai|\bkhong)\s*$", RegexOptions.CultureInvariant))
                continue;
            return true;
        }

        return false;
    }
}
