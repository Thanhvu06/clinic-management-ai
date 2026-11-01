using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.IntegrationTests;

public class SymptomSpecialtyRulesDiacriticsTests
{
    private static AppDbContext Database()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        for (var index = 1; index <= 11; index++)
            db.Specialties.Add(new Specialty { Id = index, SpecialtyCode = $"SP{index:D2}", Name = $"Khoa {index}", IsActive = true, AiEnabled = true });
        db.SaveChanges();
        return db;
    }

    [Theory]
    [InlineData("đặt lịch khám hộ mẹ tôi")]
    [InlineData("tôi bị ngã ngựa")]
    [InlineData("tôi sợ bỏ sót bệnh")]
    public async Task AccentedLookalikes_DoNotMatchAnySpecialty(string message)
    {
        using var db = Database();
        var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto(), message);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.True(SymptomSpecialtyRules.RequiresClarification(true, message));
    }

    [Theory]
    [InlineData("tôi bị ho khan", "SP01")]
    [InlineData("toi bi ho khan", "SP01")]
    [InlineData("nổi mẩn ngứa", "SP04")]
    [InlineData("noi man ngua", "SP04")]
    public async Task MatchesWithAndWithoutDiacritics(string message, string code)
    {
        using var db = Database();
        var response = await SymptomSpecialtyRules.ApplyAsync(db, new AiChatResponseDto(), message);
        Assert.Equal(code, Assert.Single(response.SpecialtySuggestions).SpecialtyCode);
    }
}
