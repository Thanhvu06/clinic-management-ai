namespace ClinicManagement.Infrastructure.AI;

public sealed partial class AiSafetyGuard
{
    private static readonly string[] SelfHarmPhrases =
    [
        "không muốn sống", "không còn muốn sống", "chẳng muốn sống", "muốn chết", "muốn kết thúc cuộc đời",
        "muốn kết liễu", "tự tử", "tự sát", "tự vẫn", "tự làm hại", "tự hại bản thân",
        "làm hại chính mình", "làm hại bản thân"
    ];

    private static readonly string[] ImmediateSelfHarmPhrases =
    [
        "đang tự tử", "vừa tự tử", "sắp tự tử", "sẽ tự tử", "đang tự sát", "vừa tự sát", "sắp tự sát",
        "đang tự làm hại", "vừa tự làm hại", "sắp tự làm hại", "đã tự làm hại"
    ];

    private static readonly string[] ExposureVerbs = ["uống", "nuốt", "hít", "hít thở", "ăn"];
    private static readonly string[] Hazards =
    [
        "hóa chất", "nước tẩy", "thuốc tẩy", "chất tẩy", "nước javel", "nước rửa bồn cầu", "chất thông cống",
        "nước thông cống", "chất ăn mòn", "axit", "a xít", "xút", "thuốc trừ sâu", "thuốc sâu", "thuốc diệt cỏ",
        "thuốc diệt chuột", "thuốc chuột", "bả chuột", "xăng", "dầu hỏa", "dầu diesel", "dầu máy",
        "pin", "nam châm", "vật sắc nhọn", "dị vật sắc nhọn", "kim tiêm", "đinh", "lưỡi dao", "mảnh kính", "mảnh thủy tinh"
    ];

    internal static string PatientSelfHarmMessage(bool immediate) =>
        "Mình rất tiếc vì bạn đang trải qua chuyện khó khăn như vậy. Hãy liên hệ ngay với người thân hoặc người bạn tin cậy và nhờ họ ở bên bạn. " +
        (immediate ? "Hãy gọi 115 ngay để được giúp đỡ khẩn cấp."
                   : "Nếu bạn đang gặp nguy hiểm tức thời hoặc đã làm hại bản thân, hãy gọi 115 ngay.");

    private static bool MatchesSafetyPhrase(string text, TextToken[] tokens, string[] phrases) =>
        phrases.SelectMany(phrase => FindPhraseMatches(tokens, phrase)).Any(match =>
            !IsNegated(text, tokens, match.StartTokenIndex) &&
            !HasClauseBoundaryBetween(text, tokens[match.StartTokenIndex].EndIndex, tokens[match.EndTokenIndex].StartIndex));

    private static bool HasHazardousExposure(string text, TextToken[] tokens)
    {
        var verbs = ExposureVerbs.SelectMany(verb => FindPhraseMatches(tokens, verb))
            .Where(match => !IsNegated(text, tokens, match.StartTokenIndex)).ToArray();
        if (verbs.Length == 0) return false;

        var dewormingMedicine = new[] { "thuốc tẩy giun", "thuốc tẩy sán" }
            .SelectMany(phrase => FindPhraseMatches(tokens, phrase)).Select(match => match.StartTokenIndex).ToHashSet();
        return Hazards.SelectMany(hazard => FindPhraseMatches(tokens, hazard)).Any(hazard =>
            !dewormingMedicine.Contains(hazard.StartTokenIndex) &&
            verbs.Any(verb => hazard.StartTokenIndex > verb.EndTokenIndex &&
                hazard.StartTokenIndex - verb.EndTokenIndex <= 7 &&
                !HasClauseBoundaryBetween(text, tokens[verb.EndTokenIndex].EndIndex, tokens[hazard.EndTokenIndex].StartIndex)));
    }
}
