using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class OllamaPatientAddressTests
{
    [Theory]
    [InlineData("Mai tới PK khám lần đầu thì chuẩn bị GT nào?", "Mai tới phòng khám")]
    [InlineData("Sáng mai khám ở PK, tôi cần chuẩn bị GT nào?", "Sáng mai khám")]
    [InlineData("Chiều mai đi khám, cần đem theo GT gì?", "Chiều mai đi khám")]
    public async Task Knowledge_prompt_guides_temporal_reading_and_patient_address(string question, string expandedStart)
    {
        using var handler = new PromptRecorder();
        using var http = new HttpClient(handler);
        var client = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true }));

        await client.ChatAsync(ClinicQuestionNormalizer.Expand(question), "Mang giấy tờ tùy thân khi đi khám.", default);

        Assert.Contains("luôn xưng hô bằng «bạn»", handler.SystemPrompt);
        Assert.Contains("«mai», «sáng mai» và «chiều mai» là thời gian", handler.SystemPrompt);
        Assert.StartsWith("Câu hỏi đã làm sạch: " + expandedStart, handler.UserPrompt);
        Assert.Contains("giấy tờ", handler.UserPrompt.Split('\n')[0]);
    }

    private sealed class PromptRecorder : HttpMessageHandler
    {
        internal string SystemPrompt { get; private set; } = "";
        internal string UserPrompt { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var messages = body.RootElement.GetProperty("messages");
            SystemPrompt = messages[0].GetProperty("content").GetString()!;
            UserPrompt = messages[1].GetProperty("content").GetString()!;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = "Bạn cần mang giấy tờ." } }) };
        }
    }
}
