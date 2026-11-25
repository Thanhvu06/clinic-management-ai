using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientEmergencyRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string BookingMessage = "đặt lịch khám thần kinh sáng mai";
    private const string WizardUrl = "/api/v1/ai/booking-wizard";

    [Theory]
    [InlineData("Cậu em vừa bị méo miệng khi đang ăn và nói không tròn tiếng.")]
    [InlineData("Bà vừa thấy đau thắt ngực khi ngồi xem ti vi.")]
    [InlineData("Cháu tôi đột nhiên co giật ngay lúc đang chơi ở sân.")]
    [InlineData("Bạn tôi có dấu hiệu ngộ độc sau khi uống một chất lạ.")]
    [InlineData("Chú tôi khó thở ngay cả khi đang ngồi yên.")]
    [InlineData("Em tôi bị vết rách ở bắp chân và đang chảy máu nhiều.")]
    public async Task EmergencyPrecedesChatBookingStartAndOpenWizard(string symptom)
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var sessionId = NewSession();
        var chat = await PostAsync<AiChatResponseDto>(client, "/api/v1/ai/chat", new { message = symptom, sessionId });
        Assert.Equal("EMERGENCY", chat.Urgency);
        Assert.Null(chat.BookingDraft);
        AssertEmergencyAction(chat.Actions);

        var mixedMessage = $"{symptom} Có nên {BookingMessage} không?";
        var mixed = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId, step = "start", typedMessage = mixedMessage, hint = "thần kinh" });
        AssertEmergencyStep(mixed);

        var open = await OpenWizardAsync(client, sessionId);
        var interrupted = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId, step = "text", text = symptom, optionToken = open.TextToken });
        AssertEmergencyStep(interrupted);

        var resumed = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId, step = "pick", optionToken = open.Options[0].Token });
        Assert.Equal("day", resumed.Step);
    }

    [Fact]
    public async Task LongComposerMessageIsScreenedBeforeWizardOptionValidation()
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var sessionId = NewSession();
        var open = await OpenWizardAsync(client, sessionId);
        var typedMessage = string.Concat(Enumerable.Repeat("Em đang cố mô tả rõ chuyện vừa xảy ra. ", 7)) + "Chị tôi khó thở.";
        var response = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId, step = "text", typedMessage, optionToken = open.TextToken });
        AssertEmergencyStep(response);
    }

    [Fact]
    public async Task TypedEmergencyPrecedesPersonalDataReadEvenWithSuggestionCode()
    {
        using var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await PostAsync<JsonElement>(client, "/api/v1/ai/copilot/chat", new
        {
            message = "Xem lịch hẹn của tôi, anh tôi vừa bị co giật ngay trong bữa cơm.",
            suggestionCode = "patient.my_appointments",
            isTypedInput = true,
            sessionId = NewSession()
        });
        Assert.Equal(AiChatIntentTypes.EmergencyEscalation, response.GetProperty("intent").GetString());
        Assert.Equal("SafetyBlocked", response.GetProperty("providerState").GetString());
        Assert.Contains("115", response.GetProperty("message").GetString());
        Assert.Empty(response.GetProperty("cards").EnumerateArray());
        Assert.Empty(response.GetProperty("executedToolNames").EnumerateArray());
        Assert.False(response.GetProperty("providerWasCalled").GetBoolean());
    }

    private static async Task<AiBookingWizardResponseDto> OpenWizardAsync(HttpClient client, string sessionId)
    {
        var response = await PostAsync<AiBookingWizardResponseDto>(client, WizardUrl,
            new { sessionId, step = "start", typedMessage = BookingMessage, hint = "thần kinh" });
        Assert.Equal("doctor", response.Step);
        Assert.Equal("Thần kinh", response.Summary?.SpecialtyName);
        Assert.NotNull(response.TextToken);
        Assert.NotEmpty(response.Options);
        return response;
    }

    private static void AssertEmergencyStep(AiBookingWizardResponseDto response)
    {
        Assert.Equal("EMERGENCY", response.ErrorCode);
        Assert.Equal("SafetyBlocked", response.AssistantMode);
        Assert.Contains("115", response.Message);
        Assert.Empty(response.Options);
        Assert.Null(response.ReviewAction);
        Assert.False(response.ProviderWasCalled);
        AssertEmergencyAction(response.Actions);
    }

    private static void AssertEmergencyAction(IReadOnlyList<AiActionDto> actions)
    {
        var action = Assert.Single(actions);
        Assert.Equal(AiActionTypes.CallEmergency, action.Type);
        Assert.Equal("tel:115", action.Payload.TargetUrl);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static string NewSession() => $"sess_{Guid.NewGuid():N}";
}
