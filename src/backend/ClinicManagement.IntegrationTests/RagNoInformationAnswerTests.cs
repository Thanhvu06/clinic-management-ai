using System.Net.Http.Json;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

public sealed class RagNoInformationAnswerTests
{
    private const string WeatherNoInformation = "Thông tin về thời tiết không có trong tài liệu được cung cấp.";

    [Theory]
    [InlineData(WeatherNoInformation)]
    [InlineData("Nội dung này không được đề cập trong tài liệu.")]
    [InlineData("Tài liệu được cung cấp không nêu về vé máy bay.")]
    [InlineData("Câu hỏi nằm ngoài phạm vi của tài liệu.")]
    [InlineData("Chưa có thông tin về vấn đề này.")]
    [InlineData("Thong tin ve thoi tiet khong co trong tai lieu duoc cung cap.")]
    [InlineData("Thông tin này không nằm trong tài liệu công khai.")]
    [InlineData("Mục này không nêu trong tài liệu.")]
    [InlineData("Nội dung này không được nhắc đến trong tài liệu.")]
    [InlineData("Tài liệu được cung cấp không có mục về vé máy bay.")]
    [InlineData("Tài liệu công khai chưa đề cập đến việc này.")]
    [InlineData("Tài liệu không nhắc đến giờ bay.")]
    [InlineData("Không có dữ liệu về việc này.")]
    public void Recognizes_no_information_answers(string reply) =>
        Assert.True(ClinicKnowledgeAnswerer.IsNoInformationAnswer(reply));

    [Theory]
    [InlineData("Bạn không cần nhịn ăn trước khi siêu âm tuyến giáp.")]
    [InlineData("Không có phí đổi lịch nếu báo trước.")]
    [InlineData("Theo tài liệu được cung cấp, không có phí đổi lịch nếu báo trước.")]
    [InlineData("Đến trễ dưới 30 phút vẫn được tiếp nhận bình thường.")]
    public void Retains_substantive_answers(string reply) =>
        Assert.False(ClinicKnowledgeAnswerer.IsNoInformationAnswer(reply));

    [Fact]
    public async Task No_information_reply_does_not_return_unrelated_sources()
    {
        var source = new KnowledgeSource("diagnostic-results", "Nhận kết quả cận lâm sàng", "Nhận kết quả");
        var answer = await AnswerAsync("Hôm nay thời tiết thế nào?", WeatherNoInformation, source);

        Assert.Null(answer);
    }

    [Fact]
    public async Task Supported_reply_preserves_the_appointment_source()
    {
        const string reply = "Đến trễ dưới 30 phút vẫn được tiếp nhận bình thường.";
        var source = new KnowledgeSource("appointments", "Đặt, đổi, hủy lịch và vắng mặt", "Đến trễ và vắng mặt");
        var answer = await AnswerAsync("Nếu tôi đến trễ 20 phút thì sao?", reply, source);

        Assert.NotNull(answer);
        Assert.Equal(reply, answer.Text);
        Assert.True(answer.Generated);
        Assert.Equal(source, Assert.Single(answer.Sources));
    }

    private static async Task<KnowledgeAnswer?> AnswerAsync(string question, string reply, KnowledgeSource source)
    {
        var retriever = new Mock<IClinicKnowledgeRetriever>();
        retriever.Setup(r => r.RetrieveAsync(question, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeRetrieval([new KnowledgeHit(new KnowledgeChunk("test:0", source, "Trích đoạn công khai."), 0.9)]));
        using var http = new HttpClient(new ReplyHandler(reply));
        var ollama = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true }));

        return await new ClinicKnowledgeAnswerer(retriever.Object, ollama).AnswerAsync(question);
    }

    private sealed class ReplyHandler(string reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage { Content = JsonContent.Create(new { message = new { content = reply } }) });
    }
}
