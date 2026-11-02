using System.Text.Json;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.Infrastructure.AI.Planning;

internal static class AiCopilotAccuracyRouting
{
    private static readonly HashSet<string> SubIntents =
    [
        "PatientSearch", "NextPatient", "TechnicianCompletedToday",
        "PendingChangeRequests", "UnpaidInvoices", "ServiceGuidance", "MedicineStock", "MedicineBatches", "LowStock",
        "DoctorQueueCount", "DoctorScheduleNavigation", "DoctorLeaveNavigation", "RevenueReport", "ActiveDoctors",
        "PendingLeaveRequests", "AdminWriteNotSupported", "TechnicianPendingWorklist"
    ];

    public static bool IsAuthoritative(AiPlannerDecision decision) => SubIntents.Contains(decision.SubIntent ?? "");

    public static AiPlannerDecision? Plan(AiActorRole role, string text)
    {
        bool Has(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);
        if (role == AiActorRole.Receptionist)
        {
            var patientSearch = Regex.Match(text, @"\b(?:tim|tra cuu)\s+(?:benh nhan|nguoi benh|ho so)(?:\s+(?:theo|co))?\s+(?<query>.+)$");
            if (patientSearch.Success)
                return Read(AiChatIntentTypes.PatientSummary, "PatientSearch", "reception.search_patients", new { query = patientSearch.Groups["query"].Value }, "/reception");
            if (Has(@"\b(?:yeu cau|de nghi)\b") && Has(@"\b(?:doi|huy)\b.*\blich\b") && Has(@"\b(?:cho|pending|chua xu ly)\b"))
                return Read(AiChatIntentTypes.QueueLookup, "PendingChangeRequests", "reception.get_pending_change_requests", new { }, "/reception/change-requests");
            if (Has(@"\b(?:hoa don|bien lai)\b") && Has(@"\b(?:chua thanh toan|cho thanh toan|chua tra|con no|unpaid)\b"))
                return Read(AiChatIntentTypes.QueueLookup, "UnpaidInvoices", "reception.get_unpaid_invoices", new { }, "/reception/billing");
        }
        if (role == AiActorRole.DiagnosticTechnician)
        {
            if (Has(@"\b(?:phieu|chi dinh)\b") && Has(@"\b(?:hoan tat|hoan thanh|da xong)\b") && Has(@"\bhom nay\b"))
                return Read(AiChatIntentTypes.DiagnosticLookup, "TechnicianCompletedToday", "technician.get_completed_today", new { }, "/diagnostics");
            if (Has(@"\b(?:cach|huong dan|quy trinh|chuan bi|lay mau|benh pham)\b") &&
                (Has(@"\b(?:xet nghiem|lay mau|benh pham)\b") || Tools.DiagnosticServiceAliases.Resolve(text).Count > 0))
                return Read(AiChatIntentTypes.FacilityInquiry, "ServiceGuidance", "technician.get_service_guidance", new { query = text }, "/diagnostics");
            if (Has(@"\b(?:phieu|chi dinh)\b") && Has(@"\b(?:cho|chua|can lam|worklist)\b"))
                return Read(AiChatIntentTypes.DiagnosticLookup, "TechnicianPendingWorklist", "technician.get_worklist", new { }, "/diagnostics");
        }
        if (role == AiActorRole.Pharmacist)
        {
            if (Has(@"\b(?:lo|lo thuoc)\b") && Has(@"\b(?:het han|han dung)\b"))
                return Read(AiChatIntentTypes.PharmacyInventory, "MedicineBatches", "pharmacist.get_medicine_batches", new { status = Has(@"\bsap\b") ? "expiring" : "expired" }, "/pharmacy/batches");
            if (Has(@"\b(?:sap het hang|sap het thuoc|ton thap|duoi nguong|can nhap them)\b"))
                return Read(AiChatIntentTypes.PharmacyInventory, "LowStock", "pharmacist.get_low_stock", new { }, "/pharmacy/inventory");
            var stock = Regex.Match(text, @"^(?:(?:cho toi (?:hoi|xem)|kiem tra|ton kho)\s+)?(?:thuoc\s+)?(?<query>.+?)\s+(?:con|ton)\s+(?:lai\s+)?(?:bao nhieu|may)\b");
            if (stock.Success)
                return Read(AiChatIntentTypes.PharmacyInventory, "MedicineStock", "pharmacist.get_medicine_stock", new { query = stock.Groups["query"].Value }, "/pharmacy/inventory");
        }
        if (role == AiActorRole.Doctor)
        {
            if (Has(@"\b(?:benh nhan|nguoi benh|luot)\b") && Has(@"\b(?:tiep theo|ke tiep)\b"))
                return Read(AiChatIntentTypes.QueueLookup, "NextPatient", "doctor.get_next_patient", new { }, "/doctor/queue");
            if (Has(@"\b(?:xin nghi phep|dang ky nghi|don nghi phep)\b"))
                return Navigate(AiChatIntentTypes.Help, "DoctorLeaveNavigation", "Bạn có thể mở trang Xin nghỉ phép để chọn ngày và gửi yêu cầu. Tôi không tự tạo đơn qua trò chuyện.", "/doctor/leave-requests");
            if (Has(@"\b(?:lich lam viec|lich truc|ca truc)\b"))
                return Navigate(AiChatIntentTypes.Help, "DoctorScheduleNavigation", "Bạn có thể xem lịch làm việc và lịch trực của chính mình tại trang Lịch làm việc.", "/doctor/schedule");
            if (Has(@"\b(?:benh nhan|nguoi benh|luot)\b") &&
                (Has(@"\b(?:cho kham|cho den luot)\b") || (Has(@"\b(?:bao nhieu|so luong|dem)\b") && Has(@"\b(?:cho|dang cho|con cho)\b"))))
                return Read(AiChatIntentTypes.QueueLookup, "DoctorQueueCount", "doctor.get_my_queue", new { }, "/doctor/queue");
        }
        if (role == AiActorRole.Admin)
        {
            if (Has(@"\b(?:xoa|sua|cap nhat|chinh sua|vo hieu hoa)\b|\b(?:khoa|mo khoa)\s+(?:tai khoan|bac si|nhan vien|nguoi dung)\b") &&
                !Has(@"\bkhong\s+(?:xoa|sua|cap nhat|khoa)\b"))
                return Navigate(AiChatIntentTypes.ClarificationRequired, "AdminWriteNotSupported",
                    "Tôi không thực hiện xóa, khóa hoặc sửa dữ liệu qua trợ lý. Hãy mở trang quản lý để kiểm tra và thực hiện theo quyền của bạn.",
                    Has(@"\b(?:co so|phong ban)\b") ? "/admin/facilities" : "/admin/users", "WRITE_NOT_SUPPORTED");
            if (Has(@"\bdoanh thu\b"))
            {
                var days = Regex.Match(text, @"\b(\d+)\s+ngay\b");
                if (days.Success && (!int.TryParse(days.Groups[1].Value, out var count) || count is < 1 or > 366))
                    return Navigate(AiChatIntentTypes.ClarificationRequired, "RevenueReport", "Vui lòng chọn khoảng doanh thu từ 1 đến 366 ngày.", "/admin/billing");
                var period = days.Success ? $"last_{int.Parse(days.Groups[1].Value)}_days" : Has(@"\bhom qua\b") ? "yesterday" : Has(@"\bthang truoc\b") ? "last_month" : Has(@"\bthang nay\b") ? "this_month" : "today";
                return Read(AiChatIntentTypes.AdminMetrics, "RevenueReport", "admin.get_revenue_report", new { period }, "/admin/billing");
            }
            if (Has(@"\bbac si\b") && Has(@"\b(?:hoat dong|dang lam viec)\b") && Has(@"\b(?:bao nhieu|so luong|dem|danh sach)\b"))
                return Read(AiChatIntentTypes.AdminMetrics, "ActiveDoctors", "admin.get_active_doctors", new { }, "/admin/doctors");
            if (Has(@"\bnghi phep\b") && Has(@"\b(?:cho|pending|chua duyet)\b"))
                return Read(AiChatIntentTypes.AdminMetrics, "PendingLeaveRequests", "admin.get_pending_leave_requests", new { }, "/admin/leaves");
        }
        return null;
    }

    private static AiPlannerDecision Read(string intent, string subIntent, string tool, object arguments, string route) => new()
    {
        PlannerMode = AiPlannerModes.Deterministic, Intent = intent, SubIntent = subIntent, Confidence = 1m, NavigationRoute = route,
        ToolCalls = [new AiPlannerToolCall { Name = tool, Version = "1.0", Arguments = JsonSerializer.SerializeToElement(arguments) }]
    };

    private static AiPlannerDecision Navigate(string intent, string subIntent, string message, string route, string? error = null) => new()
    {
        PlannerMode = AiPlannerModes.Deterministic, Intent = intent, SubIntent = subIntent, Confidence = 1m,
        Message = message, NavigationRoute = route, ErrorCode = error
    };
}
