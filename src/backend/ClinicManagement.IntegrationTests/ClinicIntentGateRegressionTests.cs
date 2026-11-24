using System.Net;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ClinicIntentGateRegressionTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("PK có BS khám cho tôi bị ho vào T4 lúc 20h ko?")]
    [InlineData("Tôi muốn đặt lịch khám lần đầu.")]
    [InlineData("huy lich giup minh ngay")]
    [InlineData("huy hen giup minh ngay")]
    [InlineData("Kết quả mỡ máu cao thì chế độ ăn uống cần thay đổi ra sao?")]
    public async Task Abbreviations_do_not_turn_symptom_or_booking_requests_into_public_information(string question)
    {
        var (response, retriever, handler) = await ProcessAsync(new AiChatRequestDto { Message = question });

        Assert.NotEqual("FacilityInquiryResolved", response.DialogueOutcome);
        Assert.Empty(response.Sources);
        retriever.VerifyNoOtherCalls();
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_partially_selected_booking_date_is_not_a_fresh_semantic_question()
    {
        var (response, retriever, handler) = await ProcessAsync(new AiChatRequestDto
        {
            Message = "PK có mở vào T4 lúc 20h ko?", PendingSlotDate = "2026-12-01"
        });

        Assert.Empty(response.Sources);
        retriever.Verify(r => r.RetrieveSemanticAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("PK có mở vào T7 lúc 19h ko?")]
    [InlineData("Tôi muốn đọc HD ĐLK trước khi tạo lịch mới.")]
    [InlineData("BG khám CK được xem ở trang nào của PK?")]
    [InlineData("Lần đầu gặp BS cần đem GT tùy thân gì?")]
    [InlineData("CN tôi ghé PK buổi tối có tiếp đón k?")]
    [InlineData("toi can mang giay to nao cho buoi kham dau tien?")]
    [InlineData("co can den som de lam thu tuc tiep nhan khong?")]
    [InlineData("dat lich kham tren website gom nhung buoc nao?")]
    [InlineData("Trước khi rời nhà để đi khám, tôi phải chuẩn bị giấy tờ gì?")]
    [InlineData("Tôi cần bỏ các hồ sơ cũ nào vào túi khi tới buổi khám đầu?")]
    [InlineData("Có phải ngồi nhà bấm thanh toán hay trả tiền lúc tới quầy?")]
    [InlineData("Muốn nhờ người nhà đi kèm buổi khám thì cần giấy tờ gì?")]
    [InlineData("Tôi quên mật khẩu tài khoản khám bệnh, phải làm sao?")]
    [InlineData("Đã làm xét nghiệm xong thì xem kết quả ở mục nào trên website?")]
    [InlineData("Cho hỏi khi đã lấy mẫu rồi thì làm thế nào biết bác sĩ đã công bố kết quả?")]
    public async Task Fresh_clinic_questions_pass_the_intent_gate_and_keep_sources(string question)
    {
        var (response, retriever, _) = await ProcessAsync(new AiChatRequestDto { Message = question });

        Assert.NotEqual("UnclearInput", response.DialogueOutcome);
        Assert.NotEmpty(response.Sources);
        Assert.Contains(response.ToolResults, result => result.ResultType == "knowledge_sources");
        if (response.DialogueOutcome == "KnowledgeAnswer")
            retriever.Verify(r => r.RetrieveSemanticAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Chỉ cách nướng khoai lang bằng nồi chiên cho gia đình.")]
    [InlineData("Tính nghiệm phương trình 7x trừ 11 bằng 31.")]
    [InlineData("Viết một đoạn mã Python để sắp xếp danh sách số.")]
    [InlineData("Bạn hãy soạn lời bài hát mừng sinh nhật cho bạn tôi.")]
    [InlineData("Dự đoán kết quả trận bóng đá tối thứ sáu.")]
    [InlineData("Gợi ý chiến lược đầu tư cổ phiếu tuần tới.")]
    public async Task Far_out_of_scope_questions_do_not_reach_retrieval_or_generation(string question)
    {
        var (response, retriever, handler) = await ProcessAsync(new AiChatRequestDto { Message = question });

        Assert.Equal("UnclearInput", response.DialogueOutcome);
        Assert.Empty(response.Sources);
        retriever.VerifyNoOtherCalls();
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("Người nhà tôi vừa nuốt nhầm dung dịch chống côn trùng, xin giúp ngay.", true)]
    [InlineData("Đồng nghiệp tôi bị bóp nghẹt ngực và khó thở ngay lúc này.", true)]
    [InlineData("Tôi cần liều thuốc loratadine mỗi ngày là bao nhiêu viên?", false)]
    [InlineData("Cần uống mấy viên amoxicillin mỗi lần?", false)]
    public async Task Safety_and_dosage_still_precede_the_recovered_knowledge_gate(string question, bool emergency)
    {
        var (response, retriever, handler) = await ProcessAsync(new AiChatRequestDto { Message = question });

        Assert.Equal(emergency ? "SafetyEmergency" : "OutOfScopeMedicalRequest", response.DialogueOutcome);
        Assert.Equal(emergency ? "EMERGENCY" : "ROUTINE", response.Urgency);
        Assert.Empty(response.Sources);
        retriever.VerifyNoOtherCalls();
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Mild_food_poisoning_recommends_gastroenterology_without_emergency_or_RAG()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Specialties.AnyAsync(s => s.SpecialtyCode == "SP11"))
            {
                db.Specialties.Add(new Specialty { Name = "Tiêu hóa", SpecialtyCode = "SP11", IsActive = true, AiEnabled = true });
                await db.SaveChangesAsync();
            }
        }
        Factory.MockAiProvider.Setup(provider => provider.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });
        var (response, retriever, _) = await ProcessAsync(new AiChatRequestDto
        {
            Message = "Tôi chỉ bị ngộ độc thực phẩm nhẹ và muốn tìm chuyên khoa phù hợp để khám."
        });

        Assert.NotEqual("EMERGENCY", response.Urgency);
        Assert.Contains(response.SpecialtySuggestions, specialty => specialty.SpecialtyCode == "SP11");
        Assert.DoesNotContain(response.Actions, action => action.Type == AiActionTypes.CallEmergency);
        retriever.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Semantic_recovery_does_not_replace_an_active_booking_draft()
    {
        var (response, retriever, handler) = await ProcessAsync(new AiChatRequestDto
        {
            Message = "Tôi cần bỏ những thứ gì vào túi trước buổi khám?",
            PendingSpecialtyId = SpecialtyEntityId,
            Reason = "Tái khám theo hẹn",
            DraftVersion = 7
        });

        Assert.Empty(response.Sources);
        retriever.Verify(r => r.RetrieveSemanticAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(SpecialtyEntityId, response.BookingDraft?.SpecialtyId);
        Assert.Equal(7, response.BookingDraft?.Version);
    }

    [Theory]
    [InlineData("BS ở CK Nhi nhận khám CN lúc 18h30 ko?", "bác sĩ ở chuyên khoa Nhi nhận khám chủ nhật lúc 18 giờ 30 phút không?")]
    [InlineData("HD ĐLK tại PK vào T2 lúc 8h k?", "hướng dẫn đặt lịch khám tại phòng khám vào thứ hai lúc 8 giờ không?")]
    [InlineData("GT của đồ thị là bao nhiêu?", "GT của đồ thị là bao nhiêu?")]
    [InlineData("Tra hồ sơ PK-123 và mã BS_456", "Tra hồ sơ PK-123 và mã BS_456")]
    public void Abbreviations_are_contextual_and_do_not_rewrite_record_codes(string raw, string expected) =>
        Assert.Equal(expected, ClinicQuestionNormalizer.Expand(raw));

    private async Task<(AiChatResponseDto Response, Mock<IClinicSemanticKnowledgeRetriever> Retriever, GeneratedAnswerHandler Handler)>
        ProcessAsync(AiChatRequestDto request)
    {
        var retriever = new Mock<IClinicSemanticKnowledgeRetriever>(MockBehavior.Strict);
        retriever.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeRetrieval([]));
        retriever.Setup(r => r.RetrieveSemanticAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeRetrieval([
                new KnowledgeHit(new KnowledgeChunk("clinic-public-guide", new("clinic-public-guide", "Hướng dẫn công khai", "Chuẩn bị và thao tác"),
                    "Chuẩn bị giấy tờ và xem hướng dẫn trên website trước khi khám."), 0.72)
            ], true, true));
        var handler = new GeneratedAnswerHandler();
        using var http = new HttpClient(handler);
        var answerer = new ClinicKnowledgeAnswerer(retriever.Object,
            new OllamaKnowledgeClient(http, Options.Create(new OllamaOptions { IsEnabled = true })));
        using var scope = Factory.Services.CreateScope();
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer);
        return (await service.ChatAsync(request), retriever, handler);
    }

    private sealed class GeneratedAnswerHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = "Bạn có thể xem hướng dẫn công khai và chuẩn bị giấy tờ trước khi khám." } }),
                    Encoding.UTF8, "application/json")
            });
        }
    }
}
