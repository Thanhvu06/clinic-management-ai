using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
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

    public AiPlannerDecision Plan(AiCopilotPlanningContext context)
    {
        var text = AiTextNormalizer.NormalizeForComparison(context.NormalizedMessage);
        if (string.IsNullOrWhiteSpace(text))
            return Clarify("Bạn muốn tôi hỗ trợ tra cứu nội dung nào trong workspace hiện tại?", "EmptyInput");

        var knowledgeRequest = Regex.IsMatch(text, @"\b(?:gio lam viec|ngay nghi|chu nhat|gia|chi phi|dich vu|co so|phong kham|huong dan|truoc kham|chuan bi xet nghiem|chuyen khoa|tom tat|tom luoc|tong hop)\b", RegexOptions.CultureInvariant);

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
        var containsWriteRequest = Regex.IsMatch(text,
            @"\b(?:tao|(?<!da\s)dat\s+(?:lich|hen|cho)|check in|ghi|luu|hoan tat|cap phat|giu cho|ke (?:don|thuoc)|chuan bi (?:phieu|ban nhap|don)|thuc hien|lam luon|xuat|phat hanh)\b",
            RegexOptions.CultureInvariant);
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
            AiActorRole.Pharmacist => PlanPharmacist(text),
            AiActorRole.Admin => PlanAdmin(text),
            _ => ProviderRequired(context.Analysis.Intent.Intent)
        };

        if (knowledgeRequest && roleDecision.ToolCalls.Count == 0 && roleDecision.Clarification is null)
            return Tool(AiChatIntentTypes.FacilityInquiry, "ClinicKnowledge", "clinic.search_knowledge", new { query = context.NormalizedMessage }, "/locations");

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

        var asksSummary = Regex.IsMatch(text, @"\b(?:tom tat|tom luoc|tong hop|mo)\b.*\b(?:benh nhan|ca kham|ca|ho so|hien tai|luot)\b", RegexOptions.CultureInvariant) &&
            Regex.IsMatch(text, @"\b(?:tom tat|tom luoc|tong hop|ho so|ca kham|benh nhan)\b", RegexOptions.CultureInvariant);
        var asksOrders = Regex.IsMatch(text, @"\b(?:chi dinh|can lam sang|xet nghiem|chan doan hinh anh)\b", RegexOptions.CultureInvariant);

        if (asksSummary && !resource.AppointmentId.HasValue)
            return Clarify("Hãy mở một lịch hẹn được phân công hoặc cung cấp appointment context hợp lệ để tôi tóm tắt đúng bệnh nhân.", "MissingAssignedAppointment", AiChatIntentTypes.PatientSummary, "/doctor/appointments");

        if (asksSummary || asksOrders)
        {
            var calls = new List<AiPlannerToolCall>();
            if (asksSummary)
                calls.Add(Call("doctor.get_patient_summary", new { appointmentId = resource.AppointmentId!.Value }));
            if (asksOrders)
                calls.Add(Call("doctor.get_diagnostic_orders", new { }));
            return new AiPlannerDecision
            {
                PlannerMode = AiPlannerModes.Deterministic,
                Intent = asksSummary ? AiChatIntentTypes.PatientSummary : AiChatIntentTypes.DiagnosticLookup,
                SubIntent = asksSummary && asksOrders ? "PatientSummaryAndDiagnosticOrders" : asksSummary ? "AssignedPatientSummary" : "MyDiagnosticOrders",
                Confidence = .99m,
                NavigationRoute = "/doctor/appointments",
                ToolCalls = calls
            };
        }

        if (Regex.IsMatch(text, @"\b(?:hang doi|benh nhan tiep theo|danh sach cho|queue cua toi|danh sach .*\b(?:benh nhan|nguoi benh)\b.*\b(?:hom nay|tiep theo)\b|(?:benh nhan|nguoi benh)\b.*\bdanh sach\b.*\b(?:hom nay|tiep theo)\b)", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.QueueLookup, "DoctorQueue", "doctor.get_my_queue", new { }, "/doctor/queue");

        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanTechnician(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:chuan bi tiep nhan phieu|tiep nhan phieu|nhan phieu|ghi ket qua|hoan tat order)\b", RegexOptions.CultureInvariant))
            return Clarify("Thao tác kỹ thuật phải đi qua action gateway và bước xác nhận rõ ràng; cuộc hội thoại không tự ghi kết quả.", "WriteRequiresExplicitActionConfirmation");

        if (Regex.IsMatch(text, @"\b(?:worklist|wl|danh sach chi dinh|chi dinh dang cho|cong viec dang cho|qua han|phieu xet nghiem|chi dinh .*\b(?:doi|cho xu ly|chua xu ly)\b)", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.DiagnosticLookup, "TechnicianWorklist", "technician.get_worklist", new { }, "/diagnostics");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanPharmacist(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:chuan bi giu cho|giu cho thuoc|cap phat don thuoc)\b", RegexOptions.CultureInvariant))
            return Clarify("Giữ chỗ hoặc cấp phát thuốc phải đi qua action gateway và bước xác nhận rõ ràng.", "WriteRequiresExplicitActionConfirmation");

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

    private static AiPlannerDecision Tool(string intent, string subIntent, string name, object arguments, string route) => new()
    {
        PlannerMode = AiPlannerModes.Deterministic,
        Intent = intent,
        SubIntent = subIntent,
        Confidence = .99m,
        NavigationRoute = route,
        ToolCalls = new[] { Call(name, arguments) }
    };

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
}
