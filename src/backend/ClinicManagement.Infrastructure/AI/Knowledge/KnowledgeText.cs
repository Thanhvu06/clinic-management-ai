using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgeText
{
    private static readonly HashSet<string> StopWords = new(
        "toi minh em ban anh chi cho la va cua co khong ko k can gi khi nhu nao the voi duoc mot nhung de thi ma a ak nhe nho hoi xin ve o di den muon lam sao bao nhieu da chua nay kham phong hay tang giam nhat gio hom ngay nam thang roi luon rat qua biet vay ha oi luc hien phut".Split(' '));
    private static readonly HashSet<string> AccentedStopWords = new(
        "tôi mình em bạn anh chị cho là và của có không ko k cần gì khi như nào thế với được một những để thì mà à ạ ak nhé nhỏ hỏi xin về ở đi đến muốn làm sao bao nhiêu đã chưa này khám phòng hay tăng giảm nhất giờ hôm nay ngày năm tháng rồi luôn rất quá qua biết vậy hả ơi lúc hiện phút".Split(' '));
    private static readonly Regex MaskedSegment = new(
        @"(?:(?:\b(?:tôi|toi|con|em|mình|minh|tên|ten|là|la|email|số|so|cccd|cmnd|hồ|ho|sơ|mã|ma|lịch|lich|hẹn|hen|bệnh|benh|nhân|nhan)\b|\s|:|#)+)?\[ĐÃ CHE\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Normalize(string value, bool fold = true)
    {
        value = value.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        if (fold)
            value = string.Concat(value.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
                .Normalize(NormalizationForm.FormC).Replace('đ', 'd');
        return Regex.Replace(value, @"[^\p{L}\p{N}]+", " ").Trim();
    }

    public static string[] ContentTerms(string value)
    {
        var clean = StripMasked(value);
        var accented = Normalize(clean, false).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return accented.Where(w => !IsStopWord(w) && !char.IsDigit(w[0])).Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string[] Terms(string value)
    {
        var clean = StripMasked(value);
        var accented = Normalize(clean, false).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = accented.Where(w => !IsStopWord(w)).ToArray();
        var foldedContent = content.Select(w => Normalize(w)).ToArray();
        return foldedContent
            .Concat(content.Where(w => w != Normalize(w)))
            .Concat(foldedContent.Zip(foldedContent.Skip(1), (a, b) => a + "_" + b))
            .ToArray();
    }

    private static bool IsStopWord(string word) =>
        word == Normalize(word) ? StopWords.Contains(word) : AccentedStopWords.Contains(word);

    private static string StripMasked(string value) =>
        value.Contains("[ĐÃ CHE]", StringComparison.Ordinal) ? MaskedSegment.Replace(value, " ") : value;
}
