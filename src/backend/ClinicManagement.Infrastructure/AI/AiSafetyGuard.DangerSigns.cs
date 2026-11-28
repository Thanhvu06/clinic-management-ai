namespace ClinicManagement.Infrastructure.AI;

public sealed partial class AiSafetyGuard
{
    private static readonly string[] DangerSigns =
    [
        "lơ mơ", "không tỉnh táo", "mất ý thức", "mất tri giác", "hôn mê", "bất động không đáp ứng",
        "không đánh thức được", "gọi không tỉnh", "gọi mãi không tỉnh", "gọi không dậy", "lay không tỉnh",
        "thở rít", "ngưng thở", "ngừng thở", "tắt thở", "thở ngáp", "thở hổn hển không nói được",
        "ho sặc", "sặc sụa", "hóc nghẹn", "mắc nghẹn không thở được", "môi tím", "tím môi", "tím đầu ngón",
        "giật liên hồi", "sùi bọt mép", "nôn máu", "ói máu", "nôn ra huyết", "ho ra máu nhiều",
        "đi ngoài ra máu", "đại tiện ra máu", "đi tiêu ra máu", "phân đen như hắc ín",
        "máu phun thành tia", "máu không ngừng chảy", "máu chảy không ngừng", "máu chảy không dứt",
        "đột ngột yếu liệt", "yếu liệt đột ngột", "liệt đột ngột", "đột ngột liệt", "đột nhiên liệt",
        "đột ngột nói đớ", "đột ngột nói ngọng", "đột nhiên nói khó"
    ];

    private static readonly string[] Swallowing = ["nuốt", "ăn", "uống", "ngậm"];
    private static readonly string[] FoodGrains = ["hạt cơm", "hạt muối", "hạt đường", "hạt nêm"];
    private static readonly string[] Choking = ["sặc", "nghẹn", "hóc"];
    private static readonly string[] Children = ["bé", "con", "cháu", "trẻ", "em bé", "đứa nhỏ", "em nhỏ"];
    private static readonly string[] ObjectRoutes = ["nuốt", "ngậm", "hít", "ăn", "hóc", "sặc"];
    private static readonly string[] ChildForeignBodies =
    [
        "đồng xu", "tiền xu", "cúc áo", "nút áo", "đồ chơi", "viên bi", "hạt cườm", "chi tiết nhựa",
        "mảnh nhựa", "hạt nhựa", "hạt", "hột", "miếng lego", "cục tẩy", "dị vật", "vật lạ", "ốc vít",
        "vòng cao su", "miếng cao su", "kẹp tóc"
    ];

    private static bool HasDangerSign(string text, TextToken[] tokens) =>
        FindReportedMatches(text, tokens, DangerSigns).Any() ||
        FindReportedMatches(text, tokens, Choking).Any(choking =>
            FindReportedMatches(text, tokens, Swallowing).Any(swallowing => AreNearby(text, tokens, choking, swallowing)));

    private static bool HasChildForeignBody(string text, TextToken[] tokens)
    {
        var children = FindMatches(tokens, Children).Where(child =>
            tokens[child.StartTokenIndex].Comparison != "con" ||
            tokens[child.StartTokenIndex].Value is "con" &&
            (child.EndTokenIndex + 1 == tokens.Length || tokens[child.EndTokenIndex + 1].Comparison is not ("cho" or "meo" or "vat")));
        var routes = FindReportedMatches(text, tokens, ObjectRoutes).Where(route =>
            children.Any(child => child.EndTokenIndex < route.StartTokenIndex && AreNearby(text, tokens, child, route)));
        var food = FindMatches(tokens, FoodGrains).Select(item => item.StartTokenIndex).ToHashSet();
        var objects = FindMatches(tokens, ChildForeignBodies).Where(body => !food.Contains(body.StartTokenIndex)).ToArray();
        return routes.Any(route => objects.Any(body => route.EndTokenIndex < body.StartTokenIndex &&
            AreNearby(text, tokens, route, body) &&
            (body.StartTokenIndex != body.EndTokenIndex || tokens[body.StartTokenIndex].Comparison is not ("hat" or "hot") ||
                tokens[route.StartTokenIndex].Comparison != "an")));
    }

    private static IEnumerable<PhraseMatch> FindReportedMatches(string text, TextToken[] tokens, string[] phrases) =>
        FindMatches(tokens, phrases).Where(match =>
            !IsNegated(text, tokens, match.StartTokenIndex) &&
            !IsNonIncident(text, tokens, match.StartTokenIndex) &&
            !IsHistoricalMention(text, tokens, match.StartTokenIndex) &&
            !HasClauseBoundaryBetween(text, tokens[match.StartTokenIndex].EndIndex, tokens[match.EndTokenIndex].StartIndex));
}
