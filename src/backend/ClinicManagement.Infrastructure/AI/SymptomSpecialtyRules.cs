using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.AI;

public static class SymptomSpecialtyRules
{
    private sealed record KeywordPattern(string Label, Regex Pattern)
    {
        public static KeywordPattern Create(string keyword)
        {
            var anchored = keyword.StartsWith('^');
            var parts = keyword.TrimStart('^').Split(" ... ").Select(Normalize);
            var body = string.Join("(?: [^ ]+){0,2} ", parts.Select(Regex.Escape));
            var pattern = (anchored ? "^" : "(?<![^ ])") + body + "(?![^ ])";
            return new KeywordPattern(keyword.TrimStart('^'), new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled));
        }
    }

    private sealed record Rule(string Code, params string[] Keywords)
    {
        public KeywordPattern[] Patterns { get; } = Keywords.Select(KeywordPattern.Create).ToArray();
    }

    private static readonly Rule[] Rules =
    [
        new("SP02", "con tôi", "con em", "con bé", "em bé", "cháu bé", "bé nhà tôi", "^bé", "trẻ em", "trẻ nhỏ", "trẻ sơ sinh", "sơ sinh"),
        new("SP03", "trễ kinh", "chậm kinh", "rối loạn kinh", "đau bụng kinh", "mang thai", "thai kỳ", "khám thai", "khí hư", "ra huyết", "có thai"),
        new("SP04", "nổi mẩn", "mẩn ngứa", "ngứa", "nổi ban", "phát ban", "mụn", "chàm", "viêm da", "rụng tóc", "nấm da", "mề đay"),
        new("SP05", "ù tai", "nghe kém", "đau tai", "nghẹt mũi", "viêm xoang", "đau họng", "viêm amidan", "chảy máu cam", "ho ... đau họng"),
        new("SP06", "hồi hộp", "đánh trống ngực", "tim đập nhanh", "tim ... đập nhanh", "loạn nhịp", "huyết áp", "tăng huyết áp", "khó thở khi gắng sức"),
        new("SP07", "đau khớp", "khớp gối", "đau lưng", "đau vai", "sưng khớp", "cứng khớp", "loãng xương", "cổ vai gáy", "thoát vị", "gout"),
        new("SP08", "đau đầu", "chóng mặt", "tê tay", "run tay", "đau nửa đầu", "tê bì", "mất ngủ", "động kinh"),
        new("SP09", "khát nhiều", "tiểu nhiều", "đường huyết", "đái tháo đường", "bướu cổ", "tuyến giáp", "tiểu đường", "mỡ máu", "cường giáp"),
        new("SP10", "mờ mắt", "đau mắt", "đỏ mắt", "nhìn mờ", "ngứa mắt", "cận thị", "viễn thị", "loạn thị", "khô mắt"),
        new("SP11", "ợ chua", "đầy bụng", "đau bụng", "ợ nóng", "tiêu chảy", "táo bón", "dạ dày", "trào ngược", "đi ngoài"),
        new("SP01", "mệt mỏi", "sốt nhẹ", "sốt", "khám tổng quát", "mệt kéo dài", "ho", "cảm cúm", "khám sức khỏe", "sức khỏe tổng quát", "khám sức khỏe ... tổng quát")
    ];

    public static bool RequiresClarification(bool unclear, string message)
    {
        var clauses = Clauses(message);
        return unclear && !Rules.Any(rule => MatchedKeywords(rule, clauses).Length > 0);
    }

    private static string[] Clauses(string message) =>
        Regex.Split(message, @"[,.;:!?()\r\n]+").Select(Normalize).Where(clause => clause.Length > 0).ToArray();

    private static string[] MatchedKeywords(Rule rule, string[] clauses) =>
        rule.Patterns.Where(keyword => clauses.Any(clause => keyword.Pattern.IsMatch(clause))).Select(keyword => keyword.Label).ToArray();

    public static async Task<AiChatResponseDto> ApplyAsync(AppDbContext db, AiChatResponseDto response, string message, CancellationToken ct = default)
    {
        var clauses = Clauses(message);
        var matches = Rules.Select((rule, index) => new
        {
            rule.Code, Index = index, Keywords = MatchedKeywords(rule, clauses)
        }).Where(match => match.Keywords.Length > 0).ToList();
        var codes = matches.Select(match => match.Code).ToList();
        var specialties = await db.Specialties.AsNoTracking()
            .Where(s => s.IsActive && s.AiEnabled && codes.Contains(s.SpecialtyCode)).ToListAsync(ct);
        var eligible = matches.Where(match => specialties.Any(s => s.SpecialtyCode == match.Code)).ToList();
        if (eligible.Any(match => match.Code != "SP01")) eligible.RemoveAll(match => match.Code == "SP01");
        var suggestions = eligible.OrderByDescending(match => match.Code == "SP02")
            .ThenByDescending(match => match.Keywords.Max(keyword => keyword.Length)).ThenBy(match => match.Index)
            .Take(2).Select((match, index) =>
            {
                var specialty = specialties.Single(s => s.SpecialtyCode == match.Code);
                return new AiSpecialtySuggestionDto
                {
                    SpecialtyId = specialty.Id, SpecialtyCode = specialty.SpecialtyCode, SpecialtyName = specialty.Name,
                    Rank = index + 1, Reason = $"Gợi ý theo quy tắc; từ khóa khớp: {string.Join(", ", match.Keywords)}."
                };
            }).ToList();
        if (suggestions.Count == 0) return response;

        response.SpecialtySuggestions = suggestions;
        response.ManualSelectionRequired = false;
        var guidance = $"Dựa trên từ khóa triệu chứng bạn mô tả, bạn có thể khám: {string.Join(", ", suggestions.Select(s => s.SpecialtyName))}. Đây là gợi ý tham khảo, không phải chẩn đoán. Gợi ý này được tạo theo quy tắc từ khóa.";
        response.Message = response.ProviderStatus == "Disabled" ? guidance : response.Message + "\n\n" + guidance;
        response.Reply = response.Message;
        response.Actions.InsertRange(0, suggestions.Select(suggestion => new AiActionDto
        {
            Id = $"act-rule-book-{suggestion.SpecialtyCode}", Type = AiActionTypes.StartBooking,
            Label = $"Đặt lịch {suggestion.SpecialtyName}", Description = suggestion.Reason,
            RequiresAuthentication = false, RequiresConfirmation = false, Style = "primary",
            Payload = new AiActionPayloadDto
            {
                SpecialtyId = suggestion.SpecialtyId, SpecialtyCode = suggestion.SpecialtyCode, SpecialtyName = suggestion.SpecialtyName,
                TargetUrl = $"{SafeRoutes.BookAppointment}?specialtyId={suggestion.SpecialtyId}"
            }
        }));
        return response;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character == 'đ' ? 'd' : character);
        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"[^a-z0-9]+", " ").Trim();
    }
}
