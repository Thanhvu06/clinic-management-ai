using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class NumberedKnowledgeAnswerRegressionTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("\n", 3)]
    [InlineData(" ", 3)]
    [InlineData("\n", 6)]
    [InlineData(" ", 6)]
    public async Task Numbered_procedure_survives_generation_postprocessing_and_output_guard(string separator, int count)
    {
        var steps = new[]
        {
            "Đăng nhập bằng tài khoản bệnh nhân đã có.",
            "Mở danh sách lịch hẹn và chọn đặt lịch mới.",
            "Chọn chuyên khoa cần thăm khám.",
            "Chọn bác sĩ cùng ngày và khung giờ còn trống.",
            "Nhập lý do khám rồi kiểm tra nội dung xác nhận.",
            "Bấm xác nhận đặt lịch và chờ thông báo thành công."
        };
        var generated = "Các bước thực hiện:" + separator + string.Join(separator,
            steps.Take(count).Select((step, index) => $"{index + 1}. {step}"));
        var response = await ProcessAsync(generated);

        Assert.Equal("KnowledgeAnswer", response.DialogueOutcome);
        Assert.Equal(generated, response.Message);
        Assert.Contains(steps[count - 1], response.Message);
        Assert.DoesNotMatch(@"(?:^|\s)\d+[.)]\s*$", response.Message);
        Assert.Single(response.Sources);
        Assert.Equal("knowledge_sources", Assert.Single(response.ToolResults).ResultType);
    }

    [Fact]
    public async Task Restored_final_list_item_still_passes_through_medical_output_safety()
    {
        var response = await ProcessAsync(
            "1. Đăng nhập. 2. Mở lịch hẹn. 3. Chọn khoa. 4. Chọn giờ. 5. Kiểm tra. 6. Uống amoxicillin 500mg mỗi ngày.");

        Assert.Equal("MEDICAL_PRESCRIPTION_OUT_OF_SCOPE", response.DialogueOutcome);
        Assert.Equal("SafetyBlocked", response.ProviderState);
        Assert.Empty(response.Sources);
        Assert.DoesNotContain("amoxicillin", response.Message);
    }

    [Fact]
    public async Task Ordinary_prose_keeps_the_existing_five_sentence_limit()
    {
        var response = await ProcessAsync("Mở trang chủ. Đăng nhập. Mở lịch hẹn. Chọn khoa. Chọn giờ. Kiểm tra thông tin.");

        Assert.Equal("Mở trang chủ. Đăng nhập. Mở lịch hẹn. Chọn khoa. Chọn giờ.", response.Message);
    }

    private async Task<AiChatResponseDto> ProcessAsync(string generated)
    {
        var retriever = new Mock<IClinicKnowledgeRetriever>();
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeRetrieval([
                new KnowledgeHit(new KnowledgeChunk("booking-procedure", new("booking-procedure", "Hướng dẫn thao tác", "Đặt lịch"),
                    "Các bước đặt lịch công khai trên website."), 1)
            ]));
        using var http = new HttpClient(new GeneratedAnswerHandler(generated));
        var answerer = new ClinicKnowledgeAnswerer(retriever.Object,
            new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true })));
        using var scope = Factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer);

        return await service.ChatAsync(new AiChatRequestDto
        {
            Message = "Xin hướng dẫn từng bước đặt lịch khám trên website để tôi đọc trước."
        });
    }

    private sealed class GeneratedAnswerHandler(string generated) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = generated } }),
                    Encoding.UTF8, "application/json")
            });
    }
}
