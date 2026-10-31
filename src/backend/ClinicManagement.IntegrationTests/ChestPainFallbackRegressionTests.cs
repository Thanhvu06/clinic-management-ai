using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Infrastructure.AI;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public class ChestPainFallbackRegressionTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("tức ngực")]
    [InlineData("đau tức ngực")]
    [InlineData("nặng ngực")]
    [InlineData("đè ép ngực")]
    [InlineData("đau ngực trái")]
    [InlineData("khó thở khi nằm")]
    [InlineData("đau tức vùng ngực")]
    [InlineData("dau tuc vung nguc")]
    [InlineData("dau nhói vùng nguc")]
    [InlineData("dau nguc trai")]
    [InlineData("de ep nguc")]
    public void ChestVariants_AreEmergencyRedFlags(string message) => Assert.True(new AiSafetyGuard().Inspect(message).IsEmergency);

    [Theory]
    [InlineData("không đau tức ngực")]
    [InlineData("không bị đau tức vùng ngực")]
    [InlineData("khong dau tuc vung nguc")]
    [InlineData("đau khớp gối, tôi muốn khám ngực")]
    [InlineData("đau đầu và vùng ngực không có vấn đề")]
    [InlineData("không nặng ngực")]
    public void NegatedOrUnrelatedSymptoms_DoNotCreateChestRedFlags(string message) => Assert.False(new AiSafetyGuard().Inspect(message).IsEmergency);

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Timeout")]
    public async Task EmergencyWinsOverSymptomSuggestions_WithoutCallingProvider(string status)
    {
        await AuthenticateAsync("pat1@test.com");
        SetProvider(status);
        Factory.MockAiProvider.Invocations.Clear();
        var response = await ChatAsync("đau tức vùng ngực, hồi hộp và nổi mẩn ngứa ở tay");
        Assert.Equal("EMERGENCY", response.Urgency);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Equal(AiActionTypes.CallEmergency, Assert.Single(response.Actions).Type);
        Assert.Equal("tel:115", response.Actions[0].Payload.TargetUrl);
        Factory.MockAiProvider.Verify(provider => provider.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(SymptomSpecialtyRulesTests.Examples), MemberType = typeof(SymptomSpecialtyRulesTests))]
    public async Task DisabledProvider_UsesRulesThroughChatEndpoint(string message, string code)
    {
        await AuthenticateAsync("pat1@test.com");
        SetProvider("Disabled");
        var response = await ChatAsync(message);
        Assert.True(response.SpecialtySuggestions.Count > 0, $"{response.DialogueOutcome}: {response.Message}; provider={response.ProviderStatus}");
        Assert.Equal(code, Assert.Single(response.SpecialtySuggestions).SpecialtyCode);
        Assert.StartsWith("Dựa trên từ khóa triệu chứng bạn mô tả, bạn có thể khám:", response.Message);
        Assert.Contains("theo quy tắc", response.Message);
        Assert.DoesNotContain("bảo trì", response.Message);
        Assert.True(response.FallbackActive);
    }

    [Theory]
    [InlineData("Timeout", "Dịch vụ AI phản hồi quá lâu")]
    [InlineData("RateLimited", "Dịch vụ AI đang nhận quá nhiều yêu cầu")]
    [InlineData("AuthFailure", "Dịch vụ AI đang gặp sự cố cấu hình xác thực")]
    [InlineData("NetworkError", "Không thể hoàn tất yêu cầu AI do lỗi mạng tạm thời")]
    public async Task ProviderFailure_PreservesMessageAndStillSuggestsSpecialty(string status, string prefix)
    {
        await AuthenticateAsync("pat1@test.com");
        SetProvider(status);
        var response = await ChatAsync("nổi mẩn ngứa ở tay");
        Assert.StartsWith(prefix, response.Message);
        Assert.Equal("SP04", Assert.Single(response.SpecialtySuggestions).SpecialtyCode);
        Assert.Equal(status, response.ProviderStatus);
    }

    [Fact]
    public async Task DisabledWithoutMatch_UsesNeutralMessageAndManualActions()
    {
        await AuthenticateAsync("pat1@test.com");
        SetProvider("Disabled");
        var response = await ChatAsync("Tôi muốn đặt lịch nhưng chưa rõ chuyên khoa");
        Assert.True(response.Actions.Count > 0, $"{response.DialogueOutcome}: {response.Message}; provider={response.ProviderStatus}");
        Assert.Empty(response.SpecialtySuggestions);
        Assert.DoesNotContain("bảo trì", response.Message);
        Assert.Contains(response.Actions, action => action.Type == AiActionTypes.ManualSpecialtySelection);
        Assert.Contains(response.Actions, action => action.Type == AiActionTypes.ViewDoctors);
    }

    private void SetProvider(string status) => Factory.MockAiProvider.Setup(provider => provider.ChatWithAiAsync(It.IsAny<string>(),
        It.IsAny<List<ChatMessageDto>>(), It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AiChatProviderResult { IsSuccess = false, Status = status });

    private async Task<AiChatResponseDto> ChatAsync(string message)
    {
        var result = await Client.PostAsJsonAsync("/api/v1/ai/chat", new { message });
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<ClinicManagement.Application.Common.Models.ApiResponse<AiChatResponseDto>>())!.Data!;
    }
}
