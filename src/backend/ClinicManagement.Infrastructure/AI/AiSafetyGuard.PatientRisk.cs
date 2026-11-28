namespace ClinicManagement.Infrastructure.AI;

public sealed partial class AiSafetyGuard
{
    private static readonly string[] SelfHarmPhrases =
    [
        "không muốn sống", "không còn muốn sống", "chẳng muốn sống", "muốn kết thúc cuộc đời",
        "muốn kết liễu", "tự tử", "tự sát", "tự vẫn", "tự làm hại", "tự hại bản thân",
        "làm hại chính mình", "làm hại bản thân"
    ];

    private static readonly string[] ImmediateSelfHarmPhrases =
    [
        "muốn tự tử", "muốn tự sát", "đang tự tử", "vừa tự tử", "sắp tự tử", "sẽ tự tử", "đang tự sát", "vừa tự sát", "sắp tự sát",
        "có ý định tự làm hại bản thân", "đang tự làm hại", "vừa tự làm hại", "sắp tự làm hại", "đã tự làm hại"
    ];

    private static readonly string[] DistressSymptoms =
    [
        "đau", "mệt", "ngứa", "nhức", "sợ", "rát", "buồn nôn", "chóng mặt", "khó chịu", "nóng", "lạnh",
        "đói", "khát", "buồn ngủ", "mắc cười", "buồn cười", "chán ăn", "oi bức", "nực", "tê", "ê ẩm", "buốt"
    ];
    private static readonly string[] EmotionalDistress = ["buồn", "chán đời", "cô đơn", "tuyệt vọng", "bế tắc", "tủi thân", "trống rỗng"];
    private static readonly string[] DistressIdioms = ["muốn chết", "chết mất", "chết đi được"];
    private static readonly string[] ExplicitDeathIntent = ["muốn chết cho xong", "muốn chết thật", "muốn chết để", "thật sự muốn chết"];

    internal static string PatientSelfHarmMessage(bool immediate) =>
        "Mình rất tiếc vì bạn đang trải qua chuyện khó khăn như vậy. Hãy liên hệ ngay với người thân hoặc người bạn tin cậy và nhờ họ ở bên bạn. " +
        (immediate ? "Hãy gọi 115 ngay để được giúp đỡ khẩn cấp."
                   : "Nếu bạn đang gặp nguy hiểm tức thời hoặc đã làm hại bản thân, hãy gọi 115 ngay.");

    private static bool MatchesSafetyPhrase(string text, TextToken[] tokens, string[] phrases) =>
        phrases.SelectMany(phrase => FindPhraseMatches(tokens, phrase)).Any(match =>
            !IsNegated(text, tokens, match.StartTokenIndex) &&
            !HasClauseBoundaryBetween(text, tokens[match.StartTokenIndex].EndIndex, tokens[match.EndTokenIndex].StartIndex));

    private static bool HasSelfHarmIntent(string text, TextToken[] tokens) =>
        MatchesSafetyPhrase(text, tokens, SelfHarmPhrases) ||
        MatchesSafetyPhrase(text, tokens, ExplicitDeathIntent) ||
        FindPhraseMatches(tokens, "muốn chết").Any(match =>
            !IsNegated(text, tokens, match.StartTokenIndex) &&
            !HasClauseBoundaryBetween(text, tokens[match.StartTokenIndex].EndIndex, tokens[match.EndTokenIndex].StartIndex) &&
            !IsDistressIdiom(text, tokens, match));

    internal static bool HasDistressIdiom(string text)
    {
        var normalized = NormalizePreservingDiacritics(text);
        var tokens = Tokenize(normalized);
        return FindDistressIdioms(normalized, tokens).Any();
    }

    internal static string RemoveDistressIdioms(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormC);
        var tokens = Tokenize(normalized);
        foreach (var match in FindDistressIdioms(normalized, tokens).OrderByDescending(match => match.StartTokenIndex))
        {
            var start = tokens[match.StartTokenIndex - 1].EndIndex;
            var end = tokens[match.EndTokenIndex].EndIndex;
            normalized = normalized.Remove(start, end - start);
        }
        return normalized;
    }

    private static IEnumerable<PhraseMatch> FindDistressIdioms(string text, TextToken[] tokens) =>
        FindMatches(tokens, DistressIdioms).Where(match => IsDistressIdiom(text, tokens, match));

    private static bool IsDistressIdiom(string text, TextToken[] tokens, PhraseMatch idiom) =>
        DistressSymptoms.SelectMany(symptom => FindPhraseMatches(tokens, symptom)).Any(symptom =>
            idiom.StartTokenIndex > symptom.EndTokenIndex && idiom.StartTokenIndex - symptom.EndTokenIndex <= 6 &&
            !IsNegated(text, tokens, symptom.StartTokenIndex) &&
            !HasClauseBoundaryBetween(text, tokens[symptom.StartTokenIndex].EndIndex, tokens[idiom.EndTokenIndex].StartIndex) &&
            !FindMatches(tokens, EmotionalDistress).Any(emotion =>
                emotion.StartTokenIndex < idiom.StartTokenIndex && emotion.EndTokenIndex >= idiom.StartTokenIndex - 6 &&
                (emotion.StartTokenIndex < symptom.StartTokenIndex || emotion.EndTokenIndex > symptom.EndTokenIndex) &&
                !HasClauseBoundaryBetween(text, tokens[emotion.EndTokenIndex].EndIndex, tokens[idiom.StartTokenIndex].StartIndex)) &&
            !tokens[(symptom.EndTokenIndex + 1)..idiom.StartTokenIndex].Any(token =>
                token.Comparison is "toi" or "minh" or "em" or "chau" or "nhung" or "nen" or "va" or "that" or "thuc" or "su"));
}
