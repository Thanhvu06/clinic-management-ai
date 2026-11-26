using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

/// <summary>
/// Answers questions about cancelling ("hủy hẹn trước mấy tiếng", "hủy lịch có mất phí không") from the
/// appointments document. A command such as "hủy lịch hẹn ngày mai của tôi" has none of these cues and keeps
/// the cancellation flow. The document states no cancellation fee and no minimum notice, so neither is invented.
/// </summary>
public static class CancellationPolicyAnswer
{
    private const string DocumentTitle = "Đặt, đổi, hủy lịch và vắng mặt";
    public static readonly KnowledgeSource RequestSource = new("appointments", DocumentTitle, "Gửi yêu cầu đổi hoặc hủy");
    public static readonly KnowledgeSource DeadlineSource = new("appointments", DocumentTitle, "Hạn xử lý và hạn chế online");

    private static readonly Regex Cancel = new(@"\bhuy\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NotCancel = new(@"\b(?:khong (?:muon )?|dung )huy\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Timing = new(
        @"\b(?:truoc (?:may|bao nhieu|bn) (?:tieng|gio|ngay|phut)|truoc bao lau|truoc bao nhieu lau|sat gio|gan gio|muon nhat|cham nhat)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Fee = new(
        @"\b(?:phi|mat tien|ton tien|tra tien|bi phat|phat tien|tru tien|hoan tien|mat coc|tien coc)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Consequence = new(
        @"\b(?:co sao|khong sao|thi sao|bi gi|anh huong|quy dinh|chinh sach)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsQuestion(string message)
    {
        var plain = KnowledgeText.Normalize(message);
        return Cancel.IsMatch(plain) && !NotCancel.IsMatch(plain) &&
            (Timing.IsMatch(plain) || Fee.IsMatch(plain) || Consequence.IsMatch(plain));
    }

    public static string Compose(string message)
    {
        var plain = KnowledgeText.Normalize(message);
        var parts = new List<string>();
        if (Fee.IsMatch(plain))
            parts.Add("Tài liệu quy định của ClinicCare về đặt, đổi, hủy lịch không nêu khoản phí hay tiền phạt nào khi hủy lịch; nếu cần xác nhận về chi phí hoặc khoản đã thanh toán, vui lòng liên hệ lễ tân.");
        if (Timing.IsMatch(plain))
            parts.Add("Tài liệu không quy định phải hủy trước bao nhiêu giờ. Yêu cầu hủy còn chờ tới giờ bắt đầu lịch gốc sẽ hết hạn xử lý, vì vậy hãy gửi yêu cầu càng sớm càng tốt trước giờ khám.");
        parts.Add("Cách hủy: trong Lịch hẹn của tôi, chọn lịch còn cho phép thao tác, bấm Hủy lịch, nhập lý do rồi Xác nhận gửi yêu cầu; yêu cầu chờ lễ tân xử lý, chưa hủy ngay.");
        parts.Add("Hủy lịch khác với Vắng mặt: tạm khóa đặt lịch online chỉ tính các lịch bị ghi nhận Vắng mặt (không đến khám).");
        parts.Add("Cần hỗ trợ thêm, vui lòng liên hệ lễ tân.");
        return string.Join(" ", parts);
    }

    public static IReadOnlyList<KnowledgeSource> Sources => [RequestSource, DeadlineSource];
}
