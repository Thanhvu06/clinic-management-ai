using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class ClinicAiContextTests : IntegrationTestBase
{
    public ClinicAiContextTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetClinicContextJsonAsync_ReturnsValidJsonWithRealData()
    {
        using var scope = Factory.Services.CreateScope();
        var contextService = scope.ServiceProvider.GetRequiredService<IClinicAiContextService>();

        var json = await contextService.GetClinicContextJsonAsync();

        Assert.NotNull(json);
        
        // Assert it doesn't contain the hardcoded strings
        Assert.DoesNotContain("123 Nguyễn Văn Cừ", json);
        Assert.DoesNotContain("1900 1234", json);
        
        // Assert it contains expected structure from the seeder (e.g., DemoSeed has doctors)
        // Since we are using DevelopmentDataSeeder, it likely has some real-like data
        Assert.Contains("Specialties", json);
        Assert.Contains("Doctors", json);
    }
}
