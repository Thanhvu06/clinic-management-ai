using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiChatUiBatchTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Legacy_patient_provider_prompt_declares_query_instead_of_leaving_arguments_to_guesswork()
    {
        var handler = new AiRolePlannerContractTests.CapturingHandler(body =>
            AiRolePlannerContractTests.Envelope(AiRolePlannerContractTests.PlannerJson("DoctorSearch",
                AiRolePlannerContractTests.ToolCall("clinic.search_specialties",
                    body.Contains("query", StringComparison.Ordinal) ? "{\"query\":\"Tai mũi họng\"}" : "{\"keyword\":\"Tai mũi họng\"}"))));
        var provider = AiRolePlannerContractTests.CreateProvider(handler);

        var result = await provider.ChatWithAiAsync("Tìm lịch khám khoa Tai mũi họng", new(), new(), "{}");

        Assert.True(result.IsSuccess);
        var call = Assert.Single(result.ToolCalls);
        Assert.True(call.Arguments.TryGetProperty("query", out _), Assert.Single(handler.Bodies));
        Assert.False(call.Arguments.TryGetProperty("keyword", out _));
        Assert.Contains("fromDate", Assert.Single(handler.Bodies));
        Assert.Contains("toDate", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task Legacy_patient_chat_executes_the_fake_provider_query_through_the_real_tool_handler()
    {
        var handler = new AiRolePlannerContractTests.CapturingHandler(body =>
            AiRolePlannerContractTests.Envelope(AiRolePlannerContractTests.PlannerJson("DoctorSearch",
                AiRolePlannerContractTests.ToolCall("clinic.search_specialties",
                    body.Contains("query", StringComparison.Ordinal) ? "{\"query\":\"Tai mũi họng\"}" : "{\"keyword\":\"Tai mũi họng\"}"))));
        using var configured = Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IAiSpecialtySuggestionProvider>(AiRolePlannerContractTests.CreateProvider(handler))));
        var client = configured.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = "pat1@test.com", password = "Pass@123" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new { message = "Tìm lịch khám khoa Tai mũi họng" });
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("ProviderAssisted", data.GetProperty("executionMode").GetString());
        var result = Assert.Single(data.GetProperty("toolResults").EnumerateArray());
        Assert.Equal("clinic.search_specialties", result.GetProperty("toolName").GetString());
        Assert.Equal("completed", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData("lịch hẹn của tôi", "patient.get_my_appointments")]
    [InlineData("lượt khám của tôi", "patient.get_my_visits")]
    [InlineData("đơn thuốc của tôi", "patient.get_my_prescriptions")]
    [InlineData("kết quả xét nghiệm của tôi", "patient.get_my_diagnostic_results")]
    [InlineData("hóa đơn của tôi", "patient.get_my_bills")]
    [InlineData("xin chào", null)]
    [InlineData("bạn làm được gì", null)]
    public async Task Disabled_provider_patient_chat_uses_existing_local_read_and_help_rules(string message, string? tool)
    {
        Factory.MockAiProvider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var sessionId = "batch_" + Guid.NewGuid().ToString("N");
        var legacy = await client.PostAsJsonAsync("/api/v1/ai/chat", new { message, sessionId });
        legacy.EnsureSuccessStatusCode();
        output.WriteLine("LEGACY " + message + " " + await legacy.Content.ReadAsStringAsync());
        if (message == "xin chào")
            Assert.StartsWith("Xin chào", (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("message").GetString());
        var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new { message, sessionId, conversationId = sessionId, currentRoute = "/patient" });
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        output.WriteLine(JsonSerializer.Serialize(new { message, response = data }));

        if (tool is not null)
        {
            Assert.Equal(tool, Assert.Single(data.GetProperty("executedToolNames").EnumerateArray()).GetString());
        }
        else if (message == "xin chào") Assert.StartsWith("Xin chào", data.GetProperty("message").GetString());
        else Assert.Equal("Help", data.GetProperty("intent").GetString());
    }

    [Theory]
    [InlineData("UNKNOWN_TOOL_ARGUMENT")]
    [InlineData("FORBIDDEN_TOOL_ARGUMENT")]
    [InlineData("MISSING_TOOL_ARGUMENT")]
    [InlineData("INVALID_DATE")]
    public void Argument_error_cards_hide_details_without_changing_the_executor_error(string code)
    {
        var result = AiToolExecutionResult.Failed(code, "Tham số 'keyword' không được phép.");
        var response = new AiGroundedResponseComposer().Compose(new AiPlannerDecision(), new[] { result });
        const string expected = "ClinicCare chưa xử lý được yêu cầu này. Bạn thử diễn đạt lại hoặc chọn một gợi ý bên dưới.";
        Assert.Equal(expected, response.Message);
        Assert.Equal(expected, Assert.Single(response.Cards).Description);
        Assert.Equal(code, result.Error?.Code);
        Assert.Contains("keyword", result.Error?.Message);
    }

    [Theory]
    [InlineData("xin chào", "Greeting")]
    [InlineData("tôi muốn đặt lịch", "StartBooking")]
    [InlineData("đặt lịch giúp tôi", "StartBooking")]
    public async Task Greeting_is_short_and_booking_navigation_survives_unverified_provider_reply(string message, string intent)
    {
        Factory.MockAiProvider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { IsSuccess = true, Status = "Success", PrimaryIntent = intent,
                Reply = "Xin chào, xem https://unverified.test để đặt lịch.", IsClear = true });
        var client = await CreateAuthenticatedClientAsync("pat1@test.com");
        var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new { message });
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        if (intent == "Greeting") Assert.StartsWith("Xin chào", data.GetProperty("message").GetString());
        else Assert.Contains(data.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("label").GetString() == "Mở trang Đặt lịch khám");
        Assert.DoesNotContain("unverified.test", data.GetProperty("message").GetString());
    }
}
