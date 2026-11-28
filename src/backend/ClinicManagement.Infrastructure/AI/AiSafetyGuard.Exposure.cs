using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.AI;

public sealed partial class AiSafetyGuard
{
    private static readonly string[] Ingestion = ["uống", "nuốt", "ăn"];
    private static readonly string[] Inhalation = ["hít", "hít thở", "ngửi"];
    private static readonly string[] Contact = ["dính", "bắn", "văng", "đổ", "tiêm"];
    private static readonly string[] DoseRoutes = ["uống", "nuốt", "dùng", "tiêm"];
    private static readonly string[] DoseAlerts = ["quá liều thuốc", "quá liều", "cả vỉ", "cả lọ", "cả chai", "cả hộp", "nhầm thuốc", "nhầm liều"];
    private static readonly string[] ContactSites = ["mắt", "da", "tay", "mặt"];
    private static readonly string[] Chemicals =
    [
        "hóa chất", "nước tẩy", "thuốc tẩy", "chất tẩy", "nước javel", "nước rửa bồn cầu", "chất thông cống",
        "nước thông cống", "chất ăn mòn", "axit", "a xít", "xút", "thuốc trừ sâu", "thuốc sâu", "thuốc diệt cỏ",
        "thuốc diệt chuột", "thuốc chuột", "bả chuột", "xăng", "dầu hỏa", "dầu diesel", "dầu máy",
        "thuốc diệt côn trùng", "dung môi", "thuốc nhuộm", "cồn công nghiệp", "dầu nhớt", "nước rửa kính"
    ];
    private static readonly string[] ForeignBodies =
    [
        "pin", "nam châm", "vật sắc nhọn", "dị vật sắc nhọn", "kim tiêm", "đinh", "lưỡi dao", "mảnh kính",
        "mảnh thủy tinh", "nấm lạ", "nấm dại", "kim khâu", "lưỡi lam", "miếng kim loại sắc"
    ];
    private static readonly string[] Fumes = ["khí gas", "khí ga", "khí co", "khí clo", "khói", "khí độc", "carbon monoxide", "hơi xăng", "hơi dung môi"];
    private static readonly Regex NonIncidentPrefix = new(
        @"\b(?:phong tranh|tranh|neu|gia su|co nen|nen|cach|nguy co khi|tac hai khi)(?:\s+(?:lo|vo tinh|bi|phai|khong))?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RoutineDose = new(
        @"\b(?:moi ngay|moi tuan|trong \d+ ngay|theo don|chia (?:lam )?\d+ lan|lieu khuyen cao)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex AcuteDose = new(
        @"\b(?:vua|lo|nham|qua lieu|mot luc|cung luc|mot lan|lien tiep|qua nhieu)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex LargeCount = new(
        @"\b(?:[5-9]|\d{2,}|nam|sau|bay|tam|chin|muoi|chuc|nhieu) (?:vien|ong|goi|lieu)\b",
        RegexOptions.CultureInvariant);

    private static bool HasHazardousExposure(string text, TextToken[] tokens)
    {
        var dewormers = FindMatches(tokens, ["thuốc tẩy giun", "thuốc tẩy sán"]).Select(match => match.StartTokenIndex).ToHashSet();
        var chemicals = FindMatches(tokens, Chemicals).Where(match => !dewormers.Contains(match.StartTokenIndex)).ToArray();
        return HasExposureAfter(text, tokens, Ingestion, chemicals.Concat(FindMatches(tokens, ForeignBodies))) ||
            HasExposureAfter(text, tokens, Inhalation, chemicals.Concat(FindMatches(tokens, Fumes))) ||
            FindMatches(tokens, Contact).Any(route => !IsNegated(text, tokens, route.StartTokenIndex) &&
                !IsNonIncident(text, tokens, route.StartTokenIndex) && chemicals.Any(hazard =>
                {
                    if (!AreNearby(text, tokens, route, hazard)) return false;
                    return tokens[route.StartTokenIndex].Comparison == "tiem" || FindMatches(tokens, ContactSites)
                        .Any(site => AreNearby(text, tokens, route, site) && AreNearby(text, tokens, hazard, site));
                }));
    }

    private static bool HasExposureAfter(string text, TextToken[] tokens, string[] routes, IEnumerable<PhraseMatch> hazards) =>
        hazards.Any(hazard => FindMatches(tokens, routes).Any(route => route.EndTokenIndex < hazard.StartTokenIndex &&
            !IsNegated(text, tokens, route.StartTokenIndex) && !IsNonIncident(text, tokens, route.StartTokenIndex) &&
            AreNearby(text, tokens, route, hazard)));

    private static List<PhraseMatch> FindMedicationMisuseMatches(string text, TextToken[] tokens)
    {
        var alerts = FindMatches(tokens, DoseAlerts).ToArray();
        var matches = new List<PhraseMatch>();
        foreach (var route in FindMatches(tokens, DoseRoutes))
        {
            foreach (var alert in alerts.Where(alert => alert.StartTokenIndex > route.EndTokenIndex && AreNearby(text, tokens, route, alert)))
                matches.Add(new PhraseMatch(route.StartTokenIndex, alert.EndTokenIndex));

            var end = route.EndTokenIndex;
            while (end + 1 < tokens.Length && end - route.EndTokenIndex < 12 &&
                !HasClauseBoundaryBetween(text, tokens[end].EndIndex, tokens[end + 1].StartIndex))
                end++;
            var clause = string.Join(' ', tokens[route.StartTokenIndex..(end + 1)].Select(token => token.Comparison));
            if (!IsNonIncident(text, tokens, route.StartTokenIndex) && LargeCount.IsMatch(clause) &&
                (!RoutineDose.IsMatch(clause) || AcuteDose.IsMatch(clause)))
                matches.Add(new PhraseMatch(route.StartTokenIndex, end));
        }
        return matches;
    }

    private static IEnumerable<PhraseMatch> FindMatches(TextToken[] tokens, string[] phrases) =>
        phrases.SelectMany(phrase => FindPhraseMatches(tokens, phrase));

    private static bool AreNearby(string text, TextToken[] tokens, PhraseMatch first, PhraseMatch second) =>
        Math.Abs(first.StartTokenIndex - second.StartTokenIndex) <= 9 &&
        !HasClauseBoundaryBetween(text, tokens[Math.Min(first.StartTokenIndex, second.StartTokenIndex)].EndIndex,
            tokens[Math.Max(first.EndTokenIndex, second.EndTokenIndex)].StartIndex);

    private static bool IsNonIncident(string text, TextToken[] tokens, int route) =>
        NonIncidentPrefix.IsMatch(StripDiacritics(text[..tokens[route].StartIndex].TrimEnd()));
}
