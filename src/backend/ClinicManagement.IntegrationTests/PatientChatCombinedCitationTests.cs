using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientChatCombinedCitationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Result_timing_cites_both_release_rule_and_turnaround()
    {
        var released = PublicPassage("diagnostic-results", "Khi nào có thể xem kết quả");
        var turnaround = PublicPassage("diagnostic-results", "Thời gian trả và trao đổi kết quả");
        const string answer =
            "Cần chờ bác sĩ duyệt công bố, còn thời gian có kết quả phụ thuộc loại xét nghiệm, chẩn đoán hình ảnh; chat không cam kết thời hạn chung, nếu lỗi tải dữ liệu hãy bấm Thử lại, liên hệ cơ sở khi chưa tải được tệp và trao đổi ý nghĩa kết quả với bác sĩ.";
        using var http = new HttpClient(new FixedReply(answer));
        var answerer = Answerer(http, _ => true, released, turnaround);

        using var app = UseAnswerer(answerer);
        using var patient = await PatientAsync(app.CreateClient());
        var response = await AskAsync(patient, "Khám xong thì chờ mấy ngày mới có kết quả?", [], null);

        Assert.Equal("KnowledgeAnswer", response.DialogueOutcome);
        Assert.Equal(answer, response.Message);
        Assert.Equal(new[] { released.Source, turnaround.Source }, response.Sources);
    }

    [Fact]
    public async Task Rescheduling_follow_up_does_not_restore_the_pharmacy_citation()
    {
        var scheduling = PublicPassage("appointments", "Gửi yêu cầu đổi hoặc hủy");
        var pharmacy = PublicPassage("prescriptions", "Hạn lĩnh và trao đổi với dược sĩ");
        const string followUp = "Nếu không dời được trên mạng, tôi phải liên hệ ai?";
        using var http = new HttpClient(new FixedReply(
            "Nếu không dời được lịch trên mạng, bạn hãy gửi yêu cầu đổi hoặc hủy trong Lịch hẹn của tôi; yêu cầu sẽ chờ lễ tân xử lý."));
        using var app = UseAnswerer(Answerer(http, question => question == followUp, scheduling, pharmacy));
        using var patient = await PatientAsync(app.CreateClient());
        var context = new List<ChatMessageDto>();
        string? sessionId = null;

        foreach (var question in new[]
        {
            "Buổi hẹn sắp tới của tôi trùng với lịch công tác mất rồi.",
            "Tôi muốn chuyển lịch hẹn đó sang ngày kế tiếp, nên mở mục nào?"
        })
        {
            var turn = await AskAsync(patient, question, context, sessionId);
            sessionId = turn.SessionId;
            context.Add(new ChatMessageDto { Role = "user", Content = question });
            context.Add(new ChatMessageDto { Role = "model", Content = turn.Message[..Math.Min(500, turn.Message.Length)] });
        }

        var response = await AskAsync(patient, followUp, context, sessionId);

        Assert.Equal("KnowledgeAnswer", response.DialogueOutcome);
        Assert.Equal(scheduling.Source, Assert.Single(response.Sources));
    }

    [Theory]
    [InlineData(
        "Khi khám lần đầu tôi phải chuẩn bị giấy tờ gì và xác nhận lịch theo cách nào?",
        "Mang căn cước công dân",
        "đăng nhập tài khoản bệnh nhân, mở danh sách chuyên khoa, chọn bác sĩ, khung giờ còn trống, điền lý do và kiểm tra thông tin trước khi bấm xác nhận, lưu mã đặt lịch, xem thông báo thành công, kiểm tra email và ghi lại số điện thoại hỗ trợ nếu cần",
        " rồi ")]
    [InlineData(
        "Cho tôi hỏi thủ tục nhận phiếu siêu âm và xin đổi lịch trên web.",
        "Nhận phiếu siêu âm giấy",
        "mở lịch hẹn, chọn ngày giờ mới còn trống, ghi rõ lý do thay đổi, kiểm tra thông tin và gửi yêu cầu để lễ tân xử lý, theo dõi trạng thái chờ xác nhận, đọc thông báo cập nhật và kiểm tra lịch mới trước khi tới cơ sở",
        "; để đổi lịch thì ")]
    public async Task One_sentence_can_cite_a_short_clause_alongside_a_longer_clause(
        string question, string firstClause, string secondClause, string connector)
    {
        const string accessInformation = "Mỗi địa điểm bố trí sơ đồ chỉ dẫn và biển tên khu vực riêng. Khi tới nơi, " +
            "người bệnh nhìn bảng hướng dẫn tại sảnh. Trường hợp cần hỗ trợ di chuyển, báo nhân viên trực ở cửa. " +
            "Tòa nhà bố trí lối đi riêng và thang máy cho người sử dụng xe lăn. " +
            "Khu tiếp nhận có ghế ngồi, bảng điện tử, cửa kính, thang bộ và lối hướng ra đường. " +
            "Nhân viên trực sảnh hướng dẫn vị trí phòng vệ sinh, chỗ đỗ xe, trạm xe buýt và lối sang tòa nhà phụ.";
        var first = new KnowledgeChunk("first", new KnowledgeSource("first", "Thông tin", "Hướng dẫn thứ nhất"),
            firstClause + ". " + accessInformation);
        var second = new KnowledgeChunk("second", new KnowledgeSource("second", "Thông tin", "Hướng dẫn thứ hai"), secondClause);
        var answer = firstClause + connector + secondClause + ".";
        using var http = new HttpClient(new FixedReply(answer));

        var response = await Answerer(http, _ => true, first, second).AnswerAsync(question);

        Assert.NotNull(response);
        Assert.Equal(new[] { first.Source, second.Source }, response.Sources);
    }

    private static KnowledgeChunk PublicPassage(string document, string heading) =>
        new MarkdownKnowledgeCatalog().Chunks.Single(chunk => chunk.Source.DocId == document && chunk.Source.Heading == heading);

    private static ClinicKnowledgeAnswerer Answerer(
        HttpClient http, Func<string, bool> hasPassages, KnowledgeChunk first, KnowledgeChunk second)
    {
        var retriever = new Mock<IClinicKnowledgeRetriever>();
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string question, CancellationToken _) => hasPassages(question)
                ? new KnowledgeRetrieval([new(first, .8), new(second, .75)])
                : new KnowledgeRetrieval([]));
        return new(retriever.Object, new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true })),
            filterUserCitations: true);
    }

    private WebApplicationFactory<Program> UseAnswerer(IClinicKnowledgeAnswerer answerer) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClinicKnowledgeAnswerer>();
            services.AddSingleton(answerer);
        }));

    private async Task<HttpClient> PatientAsync(HttpClient patient)
    {
        using var authenticated = await CreateAuthenticatedClientAsync("pat1@test.com");
        patient.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        return patient;
    }

    private static async Task<AiChatResponseDto> AskAsync(
        HttpClient patient, string question, List<ChatMessageDto> context, string? sessionId)
    {
        using var reply = await patient.PostAsJsonAsync("/api/v1/ai/chat",
            new AiChatRequestDto { Message = question, Context = context, SessionId = sessionId });
        reply.EnsureSuccessStatusCode();
        return (await reply.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }

    private sealed class FixedReply(string answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = new { content = answer } })
            });
    }
}
