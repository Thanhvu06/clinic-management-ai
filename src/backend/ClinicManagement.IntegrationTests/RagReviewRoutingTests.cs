using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class RagReviewRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("đặt lịch khám như thế nào")]
    [InlineData("hướng dẫn đổi lịch hẹn")]
    [InlineData("cách hủy lịch hẹn như thế nào")]
    public async Task General_action_guidance_reaches_the_RAG_answerer_through_the_real_chat_flow(string message)
    {
        using var scope = Factory.Services.CreateScope();
        var answerer = new Mock<IClinicKnowledgeAnswerer>();
        answerer.Setup(a => a.AnswerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeAnswer("Hướng dẫn chung từ tài liệu.", [new("appointments", "Lịch hẹn", "Hướng dẫn")], false, false));
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer.Object);
        var response = await service.ChatAsync(new AiChatRequestDto { Message = message });
        Assert.Equal("KnowledgeExtractive", response.DialogueOutcome);
        answerer.Verify(a => a.AnswerAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("hủy lịch hẹn của tôi")]
    [InlineData("đổi lịch hẹn ngày mai")]
    [InlineData("xem kết quả xét nghiệm của tôi")]
    [InlineData("đơn thuốc của tôi")]
    [InlineData("cách xem kết quả xét nghiệm của tôi")]
    public async Task Personal_reads_and_changes_keep_the_existing_handler_priority(string message)
    {
        using var scope = Factory.Services.CreateScope();
        var answerer = new Mock<IClinicKnowledgeAnswerer>(MockBehavior.Strict);
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer.Object);
        var response = await service.ChatAsync(new AiChatRequestDto { Message = message });
        Assert.DoesNotContain("Knowledge", response.DialogueOutcome ?? "");
        Assert.Empty(response.Sources);
        answerer.Verify(a => a.AnswerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
