using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientChatDoctorAndMedicineTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("Gặp bác sĩ ở từng khoa hiện thu ra sao?")]
    [InlineData("Mức phí khi gặp BS của khoa là bao nhiêu?")]
    [InlineData("Cho mình hỏi phí gặp bác sĩ trong phòng khám theo chuyên khoa.")]
    [InlineData("Gặp bác sĩ Đinh Thị Yến thì giá khám bao nhiêu?")]
    public async Task Consultation_fees_take_priority_over_doctor_lookup(string question)
    {
        var response = await ChatAsync(question);

        Assert.Equal("PricingInquiryResolved", response.DialogueOutcome);
        Assert.Contains("Bảng giá chi phí khám", response.Message);
        Assert.Contains("Da liễu", response.Message);
        Assert.DoesNotContain(response.Actions, action => action.Type == AiActionTypes.SelectDoctor);
    }

    [Theory]
    [InlineData("Cho tôi xem bác sĩ ở từng khu khám.", "ở từng")]
    [InlineData("Tôi muốn tìm bác sĩ Của Khoa.", "Của Khoa")]
    [InlineData("Cho xem bác sĩ trong phòng khám.", "trong phòng khám")]
    [InlineData("Cho xem bác sĩ nào trực hôm nay?", "nào")]
    [InlineData("Tôi muốn tìm BS giỏi tại đây.", "giỏi")]
    public async Task Ordinary_phrases_are_never_reported_as_missing_doctors(string question, string providerName)
    {
        Assert.Null(new VietnameseIntentClassifier(IntentClassificationMode.Off).Classify(question).ExtractedDoctorName);
        ConfigureProvider(new AiChatProviderResult
        {
            IsSuccess = true, Status = "Success", Reply = "Bạn có thể xem danh sách bác sĩ.",
            PrimaryIntent = AiChatIntentTypes.DoctorSearch, ExtractedDoctorName = providerName
        });

        var response = await ChatAsync(question);

        Assert.DoesNotContain("không tìm thấy bác sĩ nào có tên", response.Message);
        Assert.DoesNotContain(response.Actions, action => action.Type == AiActionTypes.SelectDoctor);
    }

    [Theory]
    [InlineData("Tìm bác sĩ Nguyễn Minh Khải giúp tôi.", "Nguyễn Minh Khải")]
    [InlineData("Cho tôi tìm BS Trần Thu Hà có khám không?", "Trần Thu Hà")]
    [InlineData("Tìm bác sĩ đinh thị yến giúp tôi.", "Đinh Thị Yến")]
    public async Task Seeded_doctors_are_resolved_from_the_database(string question, string fullName)
    {
        using var scope = Factory.Services.CreateScope();
        await DevelopmentDataSeeder.SeedMasterDataAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctorId = await (from doctor in db.Doctors
                              join user in db.Users on doctor.UserId equals user.Id
                              where user.FullName == fullName
                              select doctor.Id).SingleAsync();
        ConfigureProvider(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });

        var response = await ChatAsync(question);

        var action = Assert.Single(response.Actions, action => action.Type == AiActionTypes.SelectDoctor);
        Assert.Equal(doctorId, action.Payload.DoctorId);
        Assert.Contains(fullName, action.Label);
    }

    [Theory]
    [InlineData("Da tôi nổi mề đay, nhờ chọn một thứ thuốc dùng tối nay.", "Da liễu")]
    [InlineData("Tay bị nổi mẩn, hãy chọn giúp một loại dược phẩm.", "Da liễu")]
    [InlineData("Mình bị ợ chua, bạn gợi ý thuốc giúp mình nhé.", "Tiêu hóa")]
    [InlineData("Tôi bị đầy bụng, tối nay uống gì cho đỡ?", "Tiêu hóa")]
    [InlineData("Da nổi ban, tôi nên mua gì uống bây giờ?", "Da liễu")]
    public async Task Medicine_picking_is_refused_before_specialty_guidance(string question, string specialty)
    {
        var response = await ChatAsync(question);

        Assert.Equal("OutOfScopeMedicalRequest", response.DialogueOutcome);
        Assert.StartsWith("Tôi không thể gợi ý hay chọn thuốc cho bạn", response.Message);
        Assert.Contains("liên hệ bác sĩ hoặc dược sĩ", response.Message.Split('\n')[0]);
        Assert.Contains($"\n\nDựa trên từ khóa triệu chứng bạn mô tả, bạn có thể khám: {specialty}", response.Message);
        Assert.Contains(response.SpecialtySuggestions, suggestion => suggestion.SpecialtyName == specialty);
        Assert.False(response.ProviderWasCalled);
        Assert.Equal(response.Message, response.Reply);
    }

    [Theory]
    [InlineData("Da nổi mẩn quanh cổ thì nên khám chuyên khoa nào?", "Da liễu")]
    [InlineData("Tôi bị nổi mẩn, nhờ gợi ý chuyên khoa để kiểm tra dị ứng thuốc.", "Da liễu")]
    public async Task Specialty_only_questions_do_not_get_a_medicine_refusal(string question, string specialty)
    {
        ConfigureProvider(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });

        var response = await ChatAsync(question);

        Assert.NotEqual("OutOfScopeMedicalRequest", response.DialogueOutcome);
        Assert.DoesNotContain("không thể gợi ý hay chọn thuốc", response.Message);
        Assert.Contains(response.SpecialtySuggestions, suggestion => suggestion.SpecialtyName == specialty);
    }

    private void ConfigureProvider(AiChatProviderResult result) =>
        Factory.MockAiProvider.Setup(provider => provider.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private async Task<AiChatResponseDto> ChatAsync(string question)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = question });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }
}
