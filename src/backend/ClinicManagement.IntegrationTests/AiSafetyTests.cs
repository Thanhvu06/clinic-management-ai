using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.DTOs;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiSafetyTests : IntegrationTestBase
{
    public AiSafetyTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_ValidSymptom_When_ProviderReturnsValidCode_Then_Success()
    {
        // Mock provider
        Factory.MockAiProvider
            .Setup(x => x.GetSuggestionsFromAiAsync(It.IsAny<string>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AiProviderSuggestionResult>
            {
                new AiProviderSuggestionResult { SpecialtyCode = "SP-01", Reason = "Mock reason" }
            });

        var request = new AiSuggestionRequestDto { SymptomDescription = "Tôi bị đau đầu rất nhiều." };
        var response = await Client.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("SUCCESS", json);
        Assert.Contains("SP-01", json);
        Assert.Contains("Gợi ý chỉ mang tính tham khảo", json);
    }

    [Fact]
    public async Task Given_ValidSymptom_When_ProviderReturnsInvalidCode_Then_ManualSelection()
    {
        Factory.MockAiProvider
            .Setup(x => x.GetSuggestionsFromAiAsync(It.IsAny<string>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AiProviderSuggestionResult>
            {
                new AiProviderSuggestionResult { SpecialtyCode = "SP-99-INVALID", Reason = "Mock reason" }
            });

        var request = new AiSuggestionRequestDto { SymptomDescription = "Tôi bị đau đầu rất nhiều." };
        var response = await Client.PostAsJsonAsync("/api/v1/ai/specialty-suggestions", request);
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANUAL_SELECTION_REQUIRED", json);
    }
}
