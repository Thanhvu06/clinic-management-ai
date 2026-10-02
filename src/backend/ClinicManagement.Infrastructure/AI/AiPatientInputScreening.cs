using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>Shared legacy patient PII and supplemental prompt-injection screening.</summary>
public static class AiPatientInputScreening
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Phone = new(@"(?:\+84|0)[35789]\d{8}", RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex Email = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex Identity = new(@"\b\d{9}\b|\b\d{12}\b", RegexOptions.Compiled, MatchTimeout);

    public static bool ContainsPii(string text)
    {
        try
        {
            return Phone.IsMatch(text) || Email.IsMatch(text) || Identity.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed without retaining or logging the submitted text.
            return true;
        }
    }
    public static bool IsPromptInjection(string text) => new[]
    {
        "bỏ qua quy tắc", "ignore previous", "bỏ qua hướng dẫn", "đóng vai bác sĩ",
        "hãy chẩn đoán", "xuất toàn bộ dữ liệu", "system prompt", "developer mode"
    }.Any(keyword => text.ToLowerInvariant().Contains(keyword));
}
