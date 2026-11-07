using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public static class KnowledgeText
{
    private static readonly HashSet<string> StopWords = new(
        "toi minh em ban anh chi cho la va cua co khong can gi khi nhu nao the voi duoc mot nhung de thi ma a ak nhe nhe nho hoi xin ve o di den muon lam sao bao nhieu xin da chua nay kham phong".Split(' '));

    public static string Normalize(string value, bool fold = true)
    {
        value = value.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        if (fold)
            value = string.Concat(value.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
                .Normalize(NormalizationForm.FormC).Replace('đ', 'd');
        return Regex.Replace(value, @"[^\p{L}\p{N}]+", " ").Trim();
    }

    public static string[] Terms(string value)
    {
        var folded = Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var accented = Normalize(value, false).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return folded.Where(w => !StopWords.Contains(w))
            .Concat(accented.Where(w => w != Normalize(w) && !StopWords.Contains(Normalize(w))))
            .Concat(folded.Zip(folded.Skip(1), (a, b) => a + "_" + b)
                .Where(w => w.Split('_').All(part => !StopWords.Contains(part)))).ToArray();
    }
}
