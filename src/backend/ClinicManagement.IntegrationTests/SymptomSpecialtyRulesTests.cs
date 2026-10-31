using System.Globalization;
using System.Text;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.IntegrationTests;

public class SymptomSpecialtyRulesTests
{
    public static IEnumerable<object[]> Examples()
    {
        yield return ["hồi hộp, đánh trống ngực khi leo cầu thang", "SP06"];
        yield return ["nổi mẩn ngứa ở tay", "SP04"];
        yield return ["đau khớp gối", "SP07"];
        yield return ["ù tai, nghe kém", "SP05"];
        yield return ["mờ mắt", "SP10"];
        yield return ["ợ chua, đầy bụng", "SP11"];
        yield return ["khát nhiều, tiểu nhiều", "SP09"];
        yield return ["đau đầu, chóng mặt", "SP08"];
        yield return ["con tôi 3 tuổi bị sốt", "SP02"];
        yield return ["trễ kinh", "SP03"];
        yield return ["mệt mỏi, sốt nhẹ", "SP01"];
    }

    private static AppDbContext Database()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        for (var index = 1; index <= 11; index++)
            db.Specialties.Add(new Specialty { Id = index, SpecialtyCode = $"SP{index:D2}", Name = $"Khoa {index}", IsActive = true, AiEnabled = true });
        db.SaveChanges();
        return db;
    }

    private static string WithoutDiacritics(string value) => new string(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray())
        .Normalize(NormalizationForm.FormC).Replace('đ', 'd');

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task AllElevenSpecialties_AreMatchedWithAndWithoutDiacritics(string message, string code)
    {
        using var db = Database();
        foreach (var input in new[] { message, WithoutDiacritics(message) })
        {
            var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto { ProviderStatus = "Disabled" }, input);
            var suggestion = Assert.Single(response.SpecialtySuggestions);
            Assert.Equal(code, suggestion.SpecialtyCode);
            Assert.Contains("theo quy tắc", suggestion.Reason);
            Assert.Contains("từ khóa khớp:", suggestion.Reason);
            Assert.Contains("không phải chẩn đoán", response.Message);
            var action = Assert.Single(response.Actions);
            Assert.Equal(AiActionTypes.StartBooking, action.Type);
            Assert.Equal(suggestion.SpecialtyId, action.Payload.SpecialtyId);
            Assert.Equal($"/patient/book?specialtyId={suggestion.SpecialtyId}", action.Payload.TargetUrl);
        }
    }

    [Fact]
    public async Task UnmatchedMessage_PreservesManualActionsAndMessage()
    {
        using var db = Database();
        var response = new AiChatResponseDto { ProviderStatus = "Disabled", Message = "Chọn chuyên khoa thủ công",
            Actions = [new AiActionDto { Type = AiActionTypes.ManualSpecialtySelection }, new AiActionDto { Type = AiActionTypes.ViewDoctors }] };
        await SymptomSpecialtyRules.ApplyAsync(db, response, "Tôi cần tìm lại chiếc ô");
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Equal("Chọn chuyên khoa thủ công", response.Message);
        Assert.Equal(2, response.Actions.Count);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InactiveOrAiDisabledSpecialty_IsNeverSuggested(bool active, bool enabled)
    {
        using var db = Database();
        var specialty = await db.Specialties.SingleAsync(s => s.SpecialtyCode == "SP06");
        specialty.IsActive = active;
        specialty.AiEnabled = enabled;
        await db.SaveChangesAsync();
        var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto(), "hồi hộp, đánh trống ngực khi leo cầu thang");
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Empty(response.Actions);
    }

    [Fact]
    public async Task MultipleMatches_AreLimitedToTwoDistinctSpecialties()
    {
        using var db = Database();
        var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto(), "mờ mắt, đau khớp gối, nổi mẩn ngứa và mệt mỏi");
        Assert.Equal(2, response.SpecialtySuggestions.Count);
        Assert.Equal(2, response.SpecialtySuggestions.Select(s => s.SpecialtyId).Distinct().Count());
        Assert.DoesNotContain(response.SpecialtySuggestions, s => s.SpecialtyCode == "SP01");
        Assert.Equal(new[] { 1, 2 }, response.SpecialtySuggestions.Select(s => s.Rank));
    }
}
