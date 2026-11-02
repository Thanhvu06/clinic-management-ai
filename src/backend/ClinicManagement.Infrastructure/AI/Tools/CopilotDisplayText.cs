using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Tools;

internal static class CopilotDisplayText
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Pending"] = "Đang chờ", ["Confirmed"] = "Chờ tiếp nhận", ["CheckedIn"] = "Đã tiếp nhận",
        ["InConsultation"] = "Đang khám", ["Completed"] = "Đã hoàn tất", ["Cancelled"] = "Đã hủy", ["NoShow"] = "Vắng mặt",
        ["PendingReschedule"] = "Chờ duyệt đổi lịch", ["PendingCancellation"] = "Chờ duyệt hủy lịch",
        ["Reschedule"] = "Đổi lịch", ["Cancellation"] = "Hủy lịch", ["Approved"] = "Đã duyệt", ["Rejected"] = "Đã từ chối",
        ["Registered"] = "Đã đăng ký", ["WaitingForDoctor"] = "Chờ bác sĩ", ["WaitingDoctor"] = "Chờ bác sĩ",
        ["WaitingForDiagnostics"] = "Chờ CLS", ["InDiagnostics"] = "Chờ CLS", ["ResultsReady"] = "Có kết quả CLS",
        ["ConsultationCompleted"] = "Đã khám xong", ["Transferred"] = "Đã chuyển", ["InBilling"] = "Đang thanh toán", ["InPharmacy"] = "Đang tại quầy thuốc",
        ["Ordered"] = "Đã chỉ định", ["InProgress"] = "Đang thực hiện", ["Draft"] = "Bản nháp", ["Issued"] = "Đã phát hành",
        ["ReservedForPurchase"] = "Đã giữ thuốc", ["Dispensed"] = "Đã cấp phát", ["Expired"] = "Đã hết hạn",
        ["Unpaid"] = "Chưa thanh toán", ["Paid"] = "Đã thanh toán", ["PartiallyPaid"] = "Thanh toán một phần",
        ["Succeeded"] = "Thành công", ["Voided"] = "Đã hủy giao dịch",
        ["Laboratory"] = "Xét nghiệm", ["Ultrasound"] = "Siêu âm", ["Imaging"] = "Chẩn đoán hình ảnh", ["Other"] = "Khác"
    };

    private static readonly Regex Tokens = new(@"\b(?:" + string.Join("|", Labels.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")\b", RegexOptions.CultureInvariant);

    internal static string? Localize(string? text) => text is null ? null : Tokens.Replace(text.Replace(" (IsActive)", "", StringComparison.Ordinal), match => Labels[match.Value]);

    internal static string DoctorStatus(string status) => status == "Completed" ? "Đã khám xong" : Labels.GetValueOrDefault(status, "Không xác định");
}
