using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgeQuestionPolicy
{
    private static readonly (string PublicQuestion, string Pattern)[] Topics =
    [
        ("Chuẩn bị trước khi khám", "chuan bi|truoc khi kham|di kham|tai kham|quan ao|trang phuc|danh sach thuoc|di ung|buoi kham"),
        ("Chuẩn bị xét nghiệm, nhịn ăn, uống nước theo hướng dẫn", "nhin an|uong nuoc|lay mau|xet nghiem|sieu am|chup phim|chup x quang|cong huong tu|chan doan hinh anh"),
        ("Giấy tờ cần mang khi khám", "giay to|can cuoc|giay khai sinh|ho so cu|mang theo|cccd"),
        ("Quy trình một lượt khám, tiếp nhận, khám, cận lâm sàng, thanh toán, nhận thuốc", "quy trinh|thu tuc|cac buoc|tiep nhan|lay so|xep hang|buoc tiep theo|thu tu|kham xong|luot kham"),
        ("Hướng dẫn đặt lịch, gửi yêu cầu đổi hủy lịch và vắng mặt", "dat lich|doi lich|huy lich|vang mat|tre hen|den tre|bo hen|khong den|lo hen"),
        ("Nhận kết quả cận lâm sàng sau khi bác sĩ duyệt", "ket qua|bac si duyet|cho duyet|bao lau|cong bo"),
        ("Đơn thuốc và nhận thuốc tại nhà thuốc", "don thuoc|toa thuoc|nhan thuoc|linh thuoc|nha thuoc|mua thuoc"),
        ("Thanh toán tại quầy, hóa đơn, không thanh toán trực tuyến", "thanh toan|tra tien|hoa don|bien lai|online|truc tuyen|bao hiem|noi tru|vien phi|khoan thu|the ngan hang"),
        ("Tài khoản, đăng ký, đăng nhập website và quên mật khẩu", "tai khoan|dang nhap|dang ky|mat khau|website|dang xuat"),
        ("Bảo mật dữ liệu cá nhân và quyền riêng tư", "bao mat|du lieu|rieng tu|thong tin ca nhan|chia se|nguoi khac|lich su chat"),
        ("Hướng dẫn khi nào cần đi cấp cứu", "cap cuu|khan cap|dot quy|co giat|bat tinh|chay mau|dau nguc|kho tho"),
        ("Trẻ em, người cao tuổi và phụ nữ mang thai đi khám", "tre em|tre nho|nguoi cao tuoi|nguoi gia|mang thai|co thai|thai phu|nguoi di kem")
    ];

    public static string? PublicQuestion(string question)
    {
        var text = KnowledgeText.Normalize(question);
        var topics = Topics.Where(topic => Regex.IsMatch(text, @"\b(?:" + topic.Pattern + @")\b",
            RegexOptions.CultureInvariant)).Select(topic => topic.PublicQuestion).ToArray();
        // Only server-owned phrases cross the model boundary; free-form names and clinical details never do.
        return topics.Length == 0 ? null : "Thông tin phòng khám về: " + string.Join("; ", topics) + ".";
    }

    public static bool IsCandidate(string message, string intent, bool explicitIntent)
    {
        if (explicitIntent || intent is AiChatIntentTypes.PricingInquiry or AiChatIntentTypes.FacilityInquiry
            or AiChatIntentTypes.CancelDraft or AiChatIntentTypes.ConfirmBooking or AiChatIntentTypes.ReviewDraft)
            return false;
        var text = KnowledgeText.Normalize(message);
        if (Regex.IsMatch(text, @"\b(?:gia kham|gia bao nhieu|chi phi|phi kham|gio lam|mo cua|dia chi|so dien thoai)\b")) return false;
        var symptomText = Regex.Replace(KnowledgeText.Normalize(message, false), @"\b(?:lần đầu|lan dau)\b", "");
        if (Regex.IsMatch(symptomText,
            @"\b(?:đau|dau|sốt|sot|ho|ngứa|ngua|nôn|non|chóng mặt|chong mat|tê tay|te tay|mệt mỏi|met moi|khó thở|kho tho)\b")) return false;
        if (PublicQuestion(message) is null) return false;
        return Regex.IsMatch(text,
            @"\b(?:chuan bi|co can|can mang|giay to|quy trinh|thu tuc|huong dan|cach|the nao|bao lau|khi nao|duoc khong|khong a|la gi|tai sao)\b");
    }
}
