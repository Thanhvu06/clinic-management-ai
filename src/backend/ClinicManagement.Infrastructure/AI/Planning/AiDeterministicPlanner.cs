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

        if (Greeting.IsMatch(text) || context.Analysis.Intent.Intent == AiChatIntentTypes.Greeting)
        {
            return Local(AiChatIntentTypes.Greeting, "Greeting", "Xin chào. Tôi có thể hỗ trợ tra cứu dữ liệu trong phạm vi công việc và quyền hiện tại của bạn.", 1m);
        }

        if (Help.IsMatch(text))
        {
            return Local(AiChatIntentTypes.Help, "RoleHelp", "Bạn có thể dùng các gợi ý bên dưới hoặc mô tả rõ dữ liệu cần tra cứu. Tôi sẽ không thực hiện thao tác ghi trong cuộc hội thoại này.", .99m);
        }

        if (Regex.IsMatch(text, @"\b(?:gio lam viec|ngay nghi|chu nhat|gia|chi phi|dich vu|co so|phong kham|huong dan|truoc kham|chuan bi xet nghiem|chuyen khoa)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.FacilityInquiry, "ClinicKnowledge", "clinic.search_knowledge", new { query = context.NormalizedMessage }, "/locations");

        return context.Role switch
        {
            AiActorRole.Receptionist => PlanReception(text),
            AiActorRole.Doctor => PlanDoctor(text, context.Resource),
            AiActorRole.DiagnosticTechnician => PlanTechnician(text),
            AiActorRole.Pharmacist => PlanPharmacist(text),
            AiActorRole.Admin => PlanAdmin(text),
            _ => ProviderRequired(context.Analysis.Intent.Intent)
        };
    }

    private static AiPlannerDecision PlanReception(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:tra cuu|tim|kiem tra)\b.*\b(?:ma )?(?:lich hen|cuoc hen)\b", RegexOptions.CultureInvariant))
        {
            var code = Regex.Match(text, @"\b[a-z]{2,}-?\d{3,}\b", RegexOptions.CultureInvariant).Value;
            if (string.IsNullOrWhiteSpace(code))
                return Clarify("Vui lòng cung cấp mã lịch hẹn cần tra cứu.", "MissingAppointmentCode", AiChatIntentTypes.ViewAppointments);
            return Tool(AiChatIntentTypes.ViewAppointments, "LookupAppointment", "reception.lookup_appointment", new { appointmentCode = code.ToUpperInvariant() }, "/reception/appointments");
        }

        if (Regex.IsMatch(text, @"\b(?:hang doi|cho tiep nhan|dang cho|queue)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.QueueLookup, "ReceptionQueue", "reception.get_queue", new { }, "/reception");

        if (Regex.IsMatch(text, @"\b(?:lich hen|cuoc hen)\b.*\b(?:hom nay|trong ngay)\b|\b(?:hom nay|trong ngay)\b.*\b(?:lich hen|cuoc hen)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.ViewAppointments, "TodayAppointments", "reception.get_today_appointments", new { }, "/reception/appointments");

        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanDoctor(string text, AiResolvedResourceContext resource)
    {
        var asksSummary = Regex.IsMatch(text, @"\b(?:tom tat|tong hop)\b.*\b(?:benh nhan|ca kham|ho so|hien tai)\b", RegexOptions.CultureInvariant);
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

        if (Regex.IsMatch(text, @"\b(?:hang doi|benh nhan tiep theo|danh sach cho|queue cua toi)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.QueueLookup, "DoctorQueue", "doctor.get_my_queue", new { }, "/doctor/queue");

        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanTechnician(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:worklist|danh sach chi dinh|chi dinh dang cho|cong viec dang cho|qua han)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.DiagnosticLookup, "TechnicianWorklist", "technician.get_worklist", new { }, "/diagnostics");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanPharmacist(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:ton kho|kho thuoc|sap het|thieu thuoc)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.PharmacyInventory, "InventoryStatus", "pharmacist.get_inventory_status", new { }, "/pharmacy/inventory");
        if (Regex.IsMatch(text, @"\b(?:don thuoc|hang doi cap thuoc|cho cap|cho phat)\b", RegexOptions.CultureInvariant))
            return Tool(AiChatIntentTypes.PrescriptionLookup, "PrescriptionQueue", "pharmacist.get_prescription_queue", new { }, "/pharmacy/prescriptions");
        return ProviderRequired(AiChatIntentTypes.UnclearOrOutOfScope);
    }

    private static AiPlannerDecision PlanAdmin(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:tinh trang ai|suc khoe ai|provider|tool execution|audit ai)\b", RegexOptions.CultureInvariant))
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
