using System.Globalization;
using System.Text;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.IntegrationTests;

public class SymptomSpecialtyRulesCoverageTests
{
    public static IEnumerable<object[]> Covered()
    {
        yield return ["tôi muốn khám sức khỏe tổng quát", "SP01"];
        yield return ["tôi bị ho khan kéo dài 2 tuần", "SP01"];
        yield return ["huyết áp tôi cao", "SP06"];
        yield return ["tim tôi đập nhanh", "SP06"];
        yield return ["tôi bị đau dạ dày", "SP11"];
        yield return ["tôi bị trào ngược dạ dày", "SP11"];
        yield return ["chân tôi bị tê bì", "SP08"];
        yield return ["mẹ tôi bị loãng xương", "SP07"];
        yield return ["tôi bị đau mỏi cổ vai gáy", "SP07"];
        yield return ["bị viêm da cơ địa", "SP04"];
        yield return ["tôi bị rụng tóc nhiều", "SP04"];
        yield return ["tôi bị cận thị", "SP10"];
        yield return ["bé nhà tôi bị sốt", "SP02"];
        yield return ["bé hay quấy khóc", "SP02"];
        yield return ["tôi bị ho kèm đau họng", "SP05"];
        yield return ["tôi muốn khám thai", "SP03"];
        yield return ["tôi bị mỡ máu cao", "SP09"];
        yield return ["tôi bị khó thở khi gắng sức", "SP06"];
    }

    public static IEnumerable<object[]> Unmatched()
    {
        yield return ["tôi bị đau răng"];
        yield return ["tôi cần tìm lại chiếc ô"];
        yield return ["dáng người nhỏ bé"];
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
    [MemberData(nameof(Covered))]
    public async Task CommonPhrases_MapToExpectedSpecialty(string message, string code)
    {
        using var db = Database();
        foreach (var input in new[] { message, WithoutDiacritics(message) })
        {
            var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto { ProviderStatus = "Disabled" }, input);
            Assert.Contains(response.SpecialtySuggestions, s => s.SpecialtyCode == code);
            Assert.Equal(code, response.SpecialtySuggestions[0].SpecialtyCode);
        }
    }

    [Theory]
    [MemberData(nameof(Unmatched))]
    public async Task UnrelatedPhrases_DoNotMatchAnySpecialty(string message)
    {
        using var db = Database();
        foreach (var input in new[] { message, WithoutDiacritics(message) })
        {
            var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto(), input);
            Assert.Empty(response.SpecialtySuggestions);
            Assert.True(SymptomSpecialtyRules.RequiresClarification(true, input));
        }
    }

    [Fact]
    public async Task GeneralPracticeIsDropped_WhenAnotherSpecialtyMatches()
    {
        using var db = Database();
        var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto(), "tôi ho và huyết áp cao");
        Assert.Equal("SP06", Assert.Single(response.SpecialtySuggestions).SpecialtyCode);
    }
}
