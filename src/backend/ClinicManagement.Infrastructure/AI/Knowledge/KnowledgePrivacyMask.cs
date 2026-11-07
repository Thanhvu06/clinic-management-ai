using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgePrivacyMask
{
    private const string Hidden = "[ĐÃ CHE]";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Email = Pattern(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}");
    private static readonly Regex LongNumber = Pattern(@"(?<!\d)(?:\d[ .-]?){8,}\d(?!\d)");
    private static readonly Regex Phone = Pattern(@"(?<!\d)(?:\+?84|0)(?:[\s().-]*\d){8,10}(?!\d)");
    private static readonly Regex RecordCode = Pattern(@"\b(?:APT|APPT|PAT|MRN|HS|BN|LH|PK|RX)[-_]?[A-Z0-9]+(?:[-_][A-Z0-9]+)*\b");
    private static readonly Regex Age = Pattern(@"\b\d{1,3}\s*(?:tuổi|tuoi)\b");
    private const string QuestionWord = @"(?:có|co|cần|can|muốn|muon|xin|hỏi|hoi|đi|di|nhịn|nhin|chuẩn|chuan|thì|thi|và|va|là|la)";
    private static readonly Regex ContextCode = Pattern(
        @"(\b(?:mã|ma)\s+(?:(?:lịch|lich)\s+(?:hẹn|hen)|(?:hồ|ho)\s+(?:sơ|so)|(?:bệnh|benh)\s+(?:nhân|nhan))\s*(?:(?:là|la)\s+|[:#]\s*)?)" +
        @"(?!" + QuestionWord + @"\b)[\p{L}\p{N}]+(?:[-_./][\p{L}\p{N}]+)*");
    private static readonly Regex Name = Pattern(
        @"(\b(?:(?:tôi|toi)\s+(?:tên|ten)|(?:tên|ten)\s+(?:tôi|toi)|con\s+(?:tôi|toi)\s+(?:tên|ten))(?:\s+(?:là|la))?\s+)" +
        @"(?!" + QuestionWord + @"\b)[\p{L}\p{M}]+(?:[ \t]+(?!" + QuestionWord + @"\b)[\p{L}\p{M}]+){0,7}");

    public static string Mask(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return string.Empty;
        try
        {
            var masked = Email.Replace(question, Hidden);
            masked = LongNumber.Replace(masked, Hidden);
            masked = Phone.Replace(masked, Hidden);
            masked = RecordCode.Replace(masked, Hidden);
            masked = Age.Replace(masked, Hidden);
            masked = ContextCode.Replace(masked, "$1" + Hidden);
            return Name.Replace(masked, "$1" + Hidden);
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }

    private static Regex Pattern(string pattern) => new(pattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);
}
