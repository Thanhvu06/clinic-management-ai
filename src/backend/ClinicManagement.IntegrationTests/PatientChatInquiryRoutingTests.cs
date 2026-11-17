using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Knowledge;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Knowledge;
using ClinicManagement.Infrastructure.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class PatientChatInquiryRoutingTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("Nên đặt lịch trước bao nhiêu ngày?")]
    [InlineData("Phải đặt lịch trước mấy ngày?")]
    [InlineData("Có cần đặt lịch trước không?")]
    [InlineData("Đặt trước bao lâu?")]
    [InlineData("Được đặt xa nhất bao lâu?")]
    public async Task Booking_lead_time_questions_use_knowledge(string message)
    {
        var classification = Classify(message);
        Assert.Equal(AiChatIntentTypes.UnclearOrOutOfScope, classification.Intent);
        Assert.True(KnowledgeQuestionPolicy.IsCandidate(message, classification.Intent, false, classification));
        Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, classification.Intent, true, classification));

        using var scope = Factory.Services.CreateScope();
        var answerer = new Mock<IClinicKnowledgeAnswerer>();
        answerer.Setup(a => a.AnswerAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeAnswer("Bạn có thể xem các khung giờ còn trống trên website hoặc hỏi lễ tân.",
                [new("appointments", "Đặt lịch", "Đặt lịch trên website")], false, false));
        var service = ActivatorUtilities.CreateInstance<AiSpecialtyService>(scope.ServiceProvider, answerer.Object);

        var response = await service.ChatAsync(new AiChatRequestDto { Message = message });

        Assert.Equal("KnowledgeExtractive", response.DialogueOutcome);
        Assert.Equal("appointments", Assert.Single(response.Sources).DocId);
        Assert.Empty(response.SpecialtySuggestions);
        Assert.Empty(response.Actions);
    }

    [Theory]
    [InlineData("Tôi muốn đặt lịch khám")]
    [InlineData("Đặt lịch khoa Nhi ngày mai")]
    public void Booking_commands_keep_the_booking_intent(string message)
    {
        var classification = Classify(message);

        Assert.Equal(AiChatIntentTypes.StartBooking, classification.Intent);
        Assert.False(KnowledgeQuestionPolicy.IsCandidate(message, classification.Intent, false, classification));
    }

    [Theory]
    [InlineData("Gói khám tổng quát có mấy gói, nên chọn gói nào?")]
    [InlineData("Phòng khám có những gói khám sức khỏe nào?")]
    [InlineData("goi kham tong quat gia bao nhieu")]
    [InlineData("Có những gói sức khỏe nào?")]
    [InlineData("phong kham co goi xet nghiem nao")]
    public async Task Package_inquiries_return_the_active_catalog(string message)
    {
        Assert.Equal(AiChatIntentTypes.PricingInquiry, Classify(message).Intent);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var standard = await db.HealthPackages.SingleAsync(p => p.Code == "PKG-TEST");
        var active = new HealthPackage
        {
            Code = "PKG-CHAT-ACTIVE", Name = "Gói xét nghiệm danh mục test",
            Description = "Mô tả xét nghiệm test", Price = 325000m, IsActive = true
        };
        var inactive = new HealthPackage
        {
            Code = "PKG-CHAT-INACTIVE", Name = "Gói đã ngừng bán test", IsActive = false
        };
        db.HealthPackages.AddRange(active, inactive);
        await db.SaveChangesAsync();

        try
        {
            var response = await SendChatAsync(message);

            Assert.Equal(AiChatIntentTypes.PricingInquiry, response.PrimaryIntent);
            Assert.Equal("PricingInquiryResolved", response.DialogueOutcome);
            Assert.Empty(response.SpecialtySuggestions);
            Assert.Empty(response.Sources);
            foreach (var package in new[] { standard, active })
            {
                Assert.Contains(package.Name, response.Message);
                Assert.Contains(package.Description, response.Message);
                Assert.Contains(BusinessDisplay.Money(package.Price), response.Message);
            }
            Assert.DoesNotContain(inactive.Name, response.Message);
            Assert.Contains("liên hệ lễ tân hoặc bác sĩ để được tư vấn chọn gói", response.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            db.HealthPackages.RemoveRange(active, inactive);
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("Tôi bị sốt, mệt mỏi thì nên khám khoa nào?")]
    [InlineData("Tôi bị sốt, mệt mỏi và muốn hỏi gói khám, nên khám khoa nào?")]
    public async Task Reported_symptoms_keep_specialty_recommendation(string message)
    {
        Assert.Equal(AiChatIntentTypes.SpecialtyRecommendation, Classify(message).Intent);

        var response = await SendChatAsync(message);

        Assert.NotEmpty(response.SpecialtySuggestions);
        Assert.Empty(response.Sources);
        Assert.NotEqual("PricingInquiryResolved", response.DialogueOutcome);
    }

    private static IntentClassificationResult Classify(string message) =>
        new VietnameseIntentClassifier(IntentClassificationMode.Off).Classify(message);

    private async Task<AiChatResponseDto> SendChatAsync(string message)
    {
        using var patient = await CreateAuthenticatedClientAsync("pat1@test.com");
        using var response = await patient.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto { Message = message });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
    }
}
