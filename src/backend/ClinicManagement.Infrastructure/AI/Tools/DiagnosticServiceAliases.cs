using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Conversation;

namespace ClinicManagement.Infrastructure.AI.Tools;

internal static class DiagnosticServiceAliases
{
    private static readonly (string[] Names, string[] Codes)[] Aliases =
    [
        (["cong thuc mau", "xet nghiem mau tong quat"], ["LAB-CBC"]),
        (["duong huyet"], ["LAB-GLU"]),
        (["chuc nang gan", "men gan"], ["LAB-LFT"]),
        (["chuc nang than"], ["LAB-RFT"]),
        (["dien tim"], ["ECG", "IMG-ECG"]),
        (["x quang phoi"], ["IMG-CXR"]),
        (["sieu am bung"], ["US-ABD"])
    ];

    internal static HashSet<string> Resolve(string query)
    {
        var text = Regex.Replace(AiTextNormalizer.NormalizeForComparison(query), @"[^\p{L}\p{N}]+", " ");
        return Aliases.Where(a => a.Names.Any(name => Regex.IsMatch(text, @"\b" + Regex.Escape(name) + @"\b")))
            .SelectMany(a => a.Codes).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
