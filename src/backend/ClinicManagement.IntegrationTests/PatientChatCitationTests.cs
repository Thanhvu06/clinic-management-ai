using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientChatCitationTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static readonly KnowledgeSource ChangeAppointment =
        new("appointments", "Đặt, đổi, hủy lịch", "Gửi yêu cầu đổi hoặc hủy");
    private static readonly KnowledgeSource Prescriptions =
        new("prescriptions", "Đơn thuốc và nhận thuốc", "Hạn lĩnh và trao đổi với dược sĩ");

    [Fact]
    public async Task Rescheduling_follow_up_cites_the_appointment_passage_without_the_pharmacy_passage()
    {
        const string followUp = "Nếu tôi bấm đổi không được thì nên liên hệ với ai?";
        const string answer = "Bạn hãy gửi yêu cầu đổi hoặc hủy trong Lịch hẹn của tôi. Lễ tân sẽ xử lý yêu cầu đổi lịch.";
        using var http = new HttpClient(new GeneratedReply(answer));
        var retriever = new Mock<IClinicSemanticKnowledgeRetriever>();
        var candidates = new KnowledgeRetrieval([
            Hit(ChangeAppointment, "Trong Lịch hẹn của tôi, gửi yêu cầu đổi hoặc hủy. Lễ tân xử lý yêu cầu đổi lịch."),
            Hit(Prescriptions, "Nếu đơn hết hạn, liên hệ bác sĩ hoặc dược sĩ để được hướng dẫn lĩnh thuốc.", .76)
        ]);
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string question, CancellationToken _) => question == followUp ? candidates : new KnowledgeRetrieval([]));
        retriever.Setup(r => r.RetrieveSemanticAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeRetrieval([]));
        var answerer = CreateAnswerer(retriever.Object, http);

        using var application = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClinicKnowledgeAnswerer>();
            services.AddSingleton<IClinicKnowledgeAnswerer>(answerer);
        }));
        using var authenticated = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var patient = application.CreateClient();
        patient.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var history = new List<ChatMessageDto>();
        string? sessionId = null;

        foreach (var question in new[]
        {
            "Lịch hẹn sắp tới của tôi trùng với ca làm mất rồi.",
            "Tôi muốn dời lịch hẹn đó sang buổi tối, phải vào mục nào?"
        })
        {
            var turn = await SendAsync(patient, question, history, sessionId);
            sessionId = turn.SessionId ?? sessionId;
            history.Add(new ChatMessageDto { Role = "user", Content = question });
            history.Add(new ChatMessageDto { Role = "model", Content = turn.Message[..Math.Min(500, turn.Message.Length)] });
        }
        var response = await SendAsync(patient, followUp, history, sessionId);

        Assert.Equal("KnowledgeAnswer", response.DialogueOutcome);
        Assert.Equal(answer, response.Message);
        Assert.Equal(ChangeAppointment, Assert.Single(response.Sources));
        Assert.Contains(response.ToolResults, result => result.ResultType == "knowledge_sources");
        retriever.Verify(r => r.RetrieveAsync(followUp, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Tôi ghé PK tối T2 lúc 19h thì còn tiếp nhận không?", "database-clinic-locations", "Giờ hoạt động hiện hành",
        "")]
    [InlineData("Trước buổi khám đầu tiên, tôi nên đem giấy tờ nhận diện nào?", "prepare-visit", "Lần đầu và tái khám",
        "Bạn cần mang giấy tờ tùy thân có ảnh và hồ sơ khám cũ.")]
    [InlineData("Cho mình đọc quy trình đặt lịch qua website trước khi thao tác.", "appointments", "Đặt lịch trên website",
        "Đăng nhập tài khoản bệnh nhân rồi chọn chuyên khoa, bác sĩ và khung giờ. Nhập lý do khám và xác nhận đặt lịch.")]
    [InlineData("Kết quả xét nghiệm chỉ hiện sau khi bác sĩ đã duyệt phải không?", "diagnostic-results", "Khi nào có thể xem kết quả",
        "Kết quả xét nghiệm hiển thị sau khi bác sĩ duyệt công bố.")]
    public async Task Clinic_information_retains_the_source_that_supports_the_answer(
        string question, string docId, string heading, string answer)
    {
        using var http = new HttpClient(new GeneratedReply(answer));
        var source = new KnowledgeSource(docId, docId, heading);
        var retriever = new Mock<IClinicKnowledgeRetriever>();
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeRetrieval([
                Hit(source, answer),
                Hit(Prescriptions, "Đơn hết hạn thì trao đổi với dược sĩ về quy trình lĩnh thuốc.", .76)
            ]));
        using var scope = Factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(
            scope.ServiceProvider, CreateAnswerer(retriever.Object, http));

        var response = await service.ChatAsync(new AiChatRequestDto { Message = question });

        Assert.Equal("Giờ hoạt động hiện hành" == heading ? "FacilityInquiryResolved" : "KnowledgeAnswer",
            response.DialogueOutcome);
        var citation = Assert.Single(response.Sources);
        Assert.Equal(docId, citation.DocId);
        Assert.Equal(heading, citation.Heading);
    }

    [Fact]
    public async Task Separate_answer_sentences_can_cite_two_prompt_passages()
    {
        var identification = new KnowledgeSource("bring-documents", "Giấy tờ", "Giấy tờ nhận diện");
        var preparation = new KnowledgeSource("prepare-visit", "Chuẩn bị trước khám", "Đến sớm");
        const string identityText = "Bạn hãy mang căn cước công dân hoặc giấy tờ tùy thân có ảnh.";
        const string preparationText = "Bạn nên đến sớm 15 phút để làm thủ tục tiếp nhận tại quầy.";
        using var http = new HttpClient(new GeneratedReply($"{identityText} {preparationText}"));
        var retriever = new Mock<IClinicKnowledgeRetriever>();
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeRetrieval([Hit(identification, identityText), Hit(preparation, preparationText, .72)]));

        var response = await CreateAnswerer(retriever.Object, http)
            .AnswerAsync("Tôi cần chuẩn bị giấy tờ nhận diện và đến trước giờ hẹn bao lâu?");

        Assert.NotNull(response);
        Assert.Equal(new[] { identification, preparation }, response.Sources);
    }

    private static KnowledgeHit Hit(KnowledgeSource source, string text, double score = .8) =>
        new(new KnowledgeChunk($"{source.DocId}:{source.Heading}", source, text), score);

    private static ClinicKnowledgeAnswerer CreateAnswerer(IClinicKnowledgeRetriever retriever, HttpClient http) =>
        new(retriever, new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true })));

    private static async Task<AiChatResponseDto> SendAsync(HttpClient client, string question, List<ChatMessageDto> history, string? sessionId)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = question, Context = history, SessionId = sessionId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }

    private sealed class GeneratedReply(string answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = new { content = answer } })
            });
    }
}
