using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ChatbotBacklogRegressionTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("Chuẩn bị thông tin dị ứng để nói với bác sĩ được ko?")]
    [InlineData("Xét nghiệm xong có quay lại gặp bác sĩ không?")]
    [InlineData("Kết quả xét nghiệm chỉ xem sau khi bác sĩ duyệt à?")]
    [InlineData("Tôi có nên nhập số điện thoại vào cuộc trò chuyện?")]
    [InlineData("Mô hình RAG có nhận lịch sử chat không?")]
    [InlineData("Người giám hộ trẻ cần trao đổi thông tin với bác sĩ trực tiếp không?")]
    [InlineData("Chờ duyệt hủy trên lịch hẹn nghĩa là gì?")]
    [InlineData("Đã có kết luận bác sĩ trên kết quả nghĩa là gì?")]
    [InlineData("Đăng nhập bằng email hoặc số điện thoại như thế nào?")]
    public void Public_workflow_questions_pass_the_knowledge_gate(string message)
    {
        var classification = new VietnameseIntentClassifier().Classify(message);

        Assert.True(KnowledgeQuestionPolicy.IsCandidate(message, classification.Intent, false, classification));
        Assert.True(KnowledgeQuestionPolicy.IsEarlyGuidance(message, classification));
    }

    [Theory]
    [InlineData("Chuẩn bị trước khi khám cần gì?")]
    [InlineData("Đi khám nên đến sớm bao nhiêu phút?")]
    public async Task Visit_preparation_questions_return_the_public_document(string message)
    {
        var response = await ChatAsync(message, useLlm: false);

        Assert.Equal("KnowledgeExtractive", response.DialogueOutcome);
        Assert.Equal("prepare-visit", Assert.Single(response.Sources).DocId);
    }

    [Theory]
    [InlineData("Tôi đau ngực, chuẩn bị trước khi khám cần gì?")]
    [InlineData("Cho tôi xem kết quả xét nghiệm của tôi sau khi bác sĩ duyệt.")]
    [InlineData("Số điện thoại của phòng khám là gì?")]
    [InlineData("Chuẩn bị trước khi khám nội tổng quát với bác sĩ Khải cần gì?")]
    public void Public_guidance_does_not_preempt_symptoms_personal_reads_or_selections(string message)
    {
        var classification = new VietnameseIntentClassifier().Classify(message);

        Assert.False(KnowledgeQuestionPolicy.IsEarlyGuidance(message, classification) &&
            KnowledgeQuestionPolicy.IsCandidate(message, classification.Intent, false, classification));
    }

    [Theory]
    [InlineData("Bà tôi nôn ra máu rất nhiều.")]
    [InlineData("Bạn tôi bị tai nạn xe, chảy máu ở đầu không cầm được.")]
    [InlineData("Con tôi uống nhầm chai thuốc tẩy.")]
    [InlineData("Tôi mang thai 8 tháng, đang ra máu nhiều và đau bụng dữ dội.")]
    [InlineData("Ông tôi lên cơn hen, xịt thuốc rồi vẫn thở không nổi, môi tím.")]
    [InlineData("Bé sơ sinh 20 ngày tuổi sốt 39 độ, bỏ bú, li bì.")]
    [InlineData("Tôi bị bỏng nước sôi cả cánh tay, da phồng rộp và tuột ra.")]
    [InlineData("bo toi dot ngot dau dau du doi, non voi, nhin mot thanh hai")]
    public async Task Emergency_red_flags_send_immediate_advice_in_both_modes(string message)
    {
        foreach (var useLlm in new[] { true, false })
            AssertEmergency(await ChatAsync(message, useLlm));
    }

    [Theory]
    [InlineData("Tôi không chảy máu ở đầu. Tôi cầm được đồ vật.")]
    [InlineData("Không bị nôn ra máu, tôi muốn hỏi thủ tục nhập viện.")]
    [InlineData("Tôi bị bỏng nhẹ ở đầu ngón tay.")]
    [InlineData("Tôi có thai, hiện không ra máu nhiều.")]
    [InlineData("Bé sơ sinh đang bú tốt, không sốt.")]
    public void Red_flag_rules_respect_negation_and_severity(string message) =>
        Assert.False(new AiSafetyGuard().Inspect(message).IsEmergency);

    [Fact]
    public async Task Emergency_scheduling_question_takes_the_safety_route_in_both_modes()
    {
        foreach (var useLlm in new[] { true, false })
        {
            var response = await ChatAsync("Tình huống y tế khẩn cấp thì có nên đổi lịch trước không?", useLlm);

            AssertEmergency(response);
            Assert.Contains("không chờ", response.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Extractive_medication_question_does_not_return_unrelated_preparation_text()
    {
        var response = await ChatAsync("Mang thai 3 tháng đầu có uống thuốc cảm được không?", useLlm: false);

        Assert.Equal("OutOfScopeMedicalRequest", response.DialogueOutcome);
        Assert.Empty(response.Sources);
        Assert.Contains("liên hệ", response.Message);
    }

    [Fact]
    public async Task Treatment_diet_questions_do_not_route_to_test_preparation()
    {
        foreach (var useLlm in new[] { true, false })
        {
            var response = await ChatAsync("Mỡ máu cao nên ăn uống thế nào?", useLlm);

            Assert.Empty(response.Sources);
            Assert.DoesNotContain("Knowledge", response.DialogueOutcome ?? string.Empty);
        }
    }

    [Theory]
    [InlineData("Bé 2 tuổi sốt 38,5 độ thì cho uống paracetamol liều bao nhiêu?", "paracetamol")]
    [InlineData("Viêm họng có cần uống kháng sinh không?", "kháng sinh")]
    [InlineData("Mất ngủ kéo dài nên uống thuốc gì?", "mất ngủ")]
    [InlineData("Uống kháng sinh mấy ngày thì ngưng được?", "kháng sinh")]
    public async Task Prescription_refusal_directs_the_patient_to_a_clinician_in_both_modes(string message, string omitted)
    {
        foreach (var useLlm in new[] { true, false })
        {
            var response = await ChatAsync(message, useLlm);

            Assert.Equal("OutOfScopeMedicalRequest", response.DialogueOutcome);
            Assert.Contains("không thể kê đơn", response.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("liên hệ bác sĩ", response.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(response.Message, response.Reply);
            Assert.DoesNotContain(omitted, response.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"\b\d+\s*(?:mg|ml|viên)\b", response.Message);
        }
    }

    private async Task<AiChatResponseDto> ChatAsync(string message, bool useLlm)
    {
        using var scope = Factory.Services.CreateScope();
        using var http = new HttpClient(new FakeOllama());
        var client = new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = useLlm }));
        var retriever = scope.ServiceProvider.GetRequiredService<IClinicKnowledgeRetriever>();
        var answerer = new ClinicKnowledgeAnswerer(retriever, client);
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer);
        return await service.ChatAsync(new AiChatRequestDto { Message = message });
    }

    private static void AssertEmergency(AiChatResponseDto response)
    {
        Assert.Equal("SafetyEmergency", response.DialogueOutcome);
        Assert.Contains("115", string.Join(' ', response.Message.Split('.').Take(2)));
        Assert.DoesNotContain("không sao", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("theo dõi ở nhà", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("tel:115", Assert.Single(response.Actions).Payload.TargetUrl);
        Assert.False(response.ProviderWasCalled);
    }

    private sealed class FakeOllama : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { done = true, message = new { content = "Hãy hỏi nhân viên tại quầy." } })
            });
    }
}
