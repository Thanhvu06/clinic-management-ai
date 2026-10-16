using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicManagement.Application.Prescriptions;

public static class IngredientNormalizer
{
    public static string NormalizeKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                result.Append(c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c));
        return Regex.Replace(result.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }

    public static List<string> SplitIngredients(string? activeIngredient) =>
        Regex.Split(NormalizeKey(activeIngredient), @"[,+/]|\s+va\s+")
            .Select(NormalizeKey).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    public static bool MatchesKey(string ingredient, string key) => key.Length > 0 &&
        (ingredient == key || ingredient.StartsWith(key + " ", StringComparison.Ordinal));

    public static bool ContainsKeyAsWord(string? text, string? key)
    {
        var normalizedText = NormalizeKey(text);
        var normalizedKey = NormalizeKey(key);
        return normalizedKey.Length > 0 &&
            (" " + normalizedText + " ").Contains(" " + normalizedKey + " ", StringComparison.Ordinal);
    }
}
