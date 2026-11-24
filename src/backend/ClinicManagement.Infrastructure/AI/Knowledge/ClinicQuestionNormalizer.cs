using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class ClinicQuestionNormalizer
{
    private static readonly Regex ClinicContext = new(
        @"\b(?:kham|phong kham|cliniccare|bac si|chuyen khoa|lich hen|xet nghiem|pk|bs|ck|dlk|giay to)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Abbreviation = new(
        @"(?<![\p{L}\p{N}_-])(?:pk|bs|ck|bg|hd|đlk|dlk|gt|ko|k|t[2-7]|cn)(?![\p{L}\p{N}_-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Dictionary<string, string> Expanded = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pk"] = "phòng khám", ["bs"] = "bác sĩ", ["ck"] = "chuyên khoa",
        ["bg"] = "bảng giá", ["hd"] = "hướng dẫn", ["đlk"] = "đặt lịch khám", ["dlk"] = "đặt lịch khám",
        ["gt"] = "giấy tờ", ["ko"] = "không", ["k"] = "không",
        ["t2"] = "thứ hai", ["t3"] = "thứ ba", ["t4"] = "thứ tư", ["t5"] = "thứ năm",
        ["t6"] = "thứ sáu", ["t7"] = "thứ bảy", ["cn"] = "chủ nhật"
    };

    public static string Expand(string? message)
    {
        var text = AiTextNormalizer.Normalize(message);
        if (!ClinicContext.IsMatch(KnowledgeText.Normalize(text))) return text;
        text = Abbreviation.Replace(text, match => Expanded[match.Value]);
        text = Regex.Replace(text, @"(?<![\p{L}\p{N}_-])(\d{1,2})h(\d{2})(?![\p{L}\p{N}_-])", "$1 giờ $2 phút", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"(?<![\p{L}\p{N}_-])(\d{1,2})h(?![\p{L}\p{N}_-])", "$1 giờ", RegexOptions.IgnoreCase);
    }
}
