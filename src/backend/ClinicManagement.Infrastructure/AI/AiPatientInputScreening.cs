using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI;

/// <summary>Shared legacy patient PII and supplemental prompt-injection screening.</summary>
public static class AiPatientInputScreening
{
    public static bool ContainsPii(string text) =>
        Regex.IsMatch(text, @"(?:\+84|0)[35789]\d{8}") ||
        Regex.IsMatch(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}") ||
        Regex.IsMatch(text, @"\b\d{9}\b|\b\d{12}\b");
    public static bool IsPromptInjection(string text) => new[]
    {
        "bỏ qua quy tắc", "ignore previous", "bỏ qua hướng dẫn", "đóng vai bác sĩ",
        "hãy chẩn đoán", "xuất toàn bộ dữ liệu", "system prompt", "developer mode"
    }.Any(keyword => text.ToLowerInvariant().Contains(keyword));
}
