using System.Text;
using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

/// <summary>
/// Recognises "thuốc paracetamol bao nhiu tiền" and extracts the medicine words to look up in the
/// public medicine price list. Dose questions ("uống bao nhiêu viên") carry no price cue and are left out.
/// </summary>
public static class MedicinePriceQuestion
{
    // Accented on purpose: unaccented "thuoc" is also "thuộc" and "hop" is also "hợp".
    private static readonly Regex MedicineContext = new(
        @"(?<![\p{L}])(?:thuốc|viên|hộp|vỉ|chai|lọ|tuýp|siro|si rô)(?![\p{L}\p{M}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AccentedPrice = new(@"(?<![\p{L}])(?<!đánh )(?:giá|bảng giá)(?![\p{L}\p{M}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlainPrice = new(
        @"\b(?:(?:mat|het|ton|tra|tinh) (?:bn|bao nhiu|bao nhieu|nhieu|nhiu)|(?:bn|bao nhiu|bao nhieu) (?:tien|k|nghin|ngan|dong)|" +
        @"tien (?:bn|bao nhiu|bao nhieu|nhieu|nhiu)|gia (?:sao|the nao|ra sao|bn|bao nhiu|bao nhieu|ban)|bang gia|nhieu tien|nhiu tien)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TrailingAmount = new(@"\b(?:bao nhieu|bao nhiu|bn)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DoseWord = new(@"\b(?:uong|dung|lieu|boi|xit|nho|ngam)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ConsultationWord = new(
        @"\b(?:kham|khoa|chuyen khoa|bac si|bs|dich vu|goi|xet nghiem|sieu am|noi soi|chup|vien phi)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AccentedFillers = new(
        ("cho tôi mình em anh chị bạn ad shop hỏi xin ơi với loại cái này đó kia thế nào sao vậy nhỉ nhé nha ạ à hả " +
         "có không ko được đc mua bán ở đây tại bên phòng khám là thì một mỗi rẻ đắt bao nhiêu nhiều nhiu bn tiền giá bảng hết mất tốn tính trả " +
         "k nghìn ngàn đồng vnd thuốc viên hộp vỉ chai lọ tuýp gói mg ml g của về hiện nay bây giờ").Split(' '), StringComparer.Ordinal);
    // Unaccented typing: every filler except those that are also medicine words ("mắt" in "thuốc nhỏ mắt").
    private static readonly HashSet<string> PlainFillers = new(
        AccentedFillers.Select(word => KnowledgeText.Normalize(word)).Where(word => word is not ("mat" or "nho")), StringComparer.Ordinal);

    public static bool HasMedicineContext(string message) => MedicineContext.IsMatch(Accented(message));

    internal static bool NamesMedicineAsSuch(string message) =>
        Regex.IsMatch(Accented(message), @"\b(?:thuốc|dược phẩm|siro|si rô)\b") ||
        Regex.IsMatch(message.TrimStart().ToLowerInvariant(), @"^(?:bang gia |gia )?thuoc\b");

    public static bool IsPriceQuestion(string message)
    {
        var plain = KnowledgeText.Normalize(message);
        if (AccentedPrice.IsMatch(Accented(message)) || PlainPrice.IsMatch(Regex.Replace(plain, @"\bdanh gia\b", " "))) return true;
        return TrailingAmount.IsMatch(plain) && !DoseWord.IsMatch(plain);
    }

    /// <summary>A price question that names no clinic service, so the words may be a medicine name.</summary>
    public static bool MayNameMedicine(string message) => !ConsultationWord.IsMatch(KnowledgeText.Normalize(message));

    /// <summary>The words left once price cues and fillers are removed, accented as typed.</summary>
    public static IReadOnlyList<string> Terms(string message)
    {
        var accented = KnowledgeText.Normalize(message, false).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return accented.Where(word => !AccentedFillers.Contains(word) && !PlainFillers.Contains(word) && !word.All(char.IsDigit)).ToList();
    }

    public static bool Matches(IReadOnlyList<string> terms, params string?[] fields)
    {
        if (terms.Count == 0) return false;
        var words = fields.Where(field => !string.IsNullOrWhiteSpace(field))
            .SelectMany(field => KnowledgeText.Normalize(field!).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        return terms.All(term => words.Contains(KnowledgeText.Normalize(term)));
    }

    private static string Accented(string message) => message.ToLowerInvariant().Normalize(NormalizationForm.FormC);
}
