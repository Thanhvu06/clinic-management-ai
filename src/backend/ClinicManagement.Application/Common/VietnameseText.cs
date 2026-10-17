using System.Globalization;
using System.Text;

namespace ClinicManagement.Application.Common;

public static class VietnameseText
{
    public static string ToSearchKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var result = new StringBuilder();
        foreach (var character in text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(character == 'đ' ? 'd' : character);
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
