using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit.Abstractions;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class AiCancelKeywordTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    : IntegrationTestBase(factory)
{
    [Theory]
    [InlineData("tôi bị huyết áp cao")]
    [InlineData("đặt lịch với bác sĩ Huy")]
    [InlineData("tôi ở huyện Bến Cát")]
    [InlineData("tôi muốn tìm bác sĩ Huyền")]
    [InlineData("huy")]
    [InlineData("xhủy")]
    [InlineData("hủyx")]
    [InlineData("xhuỷ")]
    [InlineData("huỷx")]
    [InlineData("huy lichx")]
    [InlineData("xhuy lich")]
    public async Task NonCancelWords_UseNormalSessionFlow_WithoutCancelIntent(string message)
    {
        var response = await ChatWithoutSessionAsync(message);
        Assert.NotEqual(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
        Assert.NotEqual("DraftCancelled", response.DialogueOutcome);
        Assert.NotEqual("NothingToCancel", response.DialogueOutcome);
        using var scope = Factory.Services.CreateScope();
        var session = Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiSessions.ToListAsync());
        if (response.SessionId != null)
            Assert.Equal(response.SessionId, session.SessionId);
        Assert.Equal(Patient1Id, session.UserId);
        Assert.True(session.IsActive);
    }

    [Theory]
    [InlineData("hủy lịch", true)]
    [InlineData("huỷ lịch hẹn", true)]
    [InlineData("tôi muốn hủy", true)]
    [InlineData("HỦY!", true)]
    [InlineData("(huỷ)", true)]
    [InlineData("huy lich", false)]
    [InlineData("HUY LICH", false)]
    [InlineData("huy hen", false)]
    [InlineData("huy dat", false)]
    [InlineData("huy   lich", false)]
    public async Task CancelKeywords_PreserveStatelessGuard_WithoutCreatingSession(string message, bool classifiedAsCancel)
    {
        var response = await ChatWithoutSessionAsync(message);
        Assert.Null(response.SessionId);
        using var scope = Factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiSessions.ToListAsync());
        if (classifiedAsCancel)
        {
            Assert.Equal(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
            Assert.Equal("NothingToCancel", response.DialogueOutcome);
            Assert.Equal("NotCalled", response.ProviderStatus);
            Assert.Empty(Factory.MockAiProvider.Invocations);
        }
        else
        {
            // Accentless phrases retain the existing classifier result; this fix
            // changes only the early cancellation/session guard, not AI routing.
            Assert.Equal(AiChatIntentTypes.UnclearOrOutOfScope, response.PrimaryIntent);
            Assert.Equal("UnclearInput", response.DialogueOutcome);
            Assert.Equal("NotCalled", response.ProviderStatus);
            Assert.Empty(Factory.MockAiProvider.Invocations);
        }
    }

    [Fact]
    public async Task ExplicitCancelIntent_PreservesStatelessCancellation_WithoutKeyword()
    {
        var response = await ChatWithoutSessionAsync("xin chào", AiChatIntentTypes.CancelDraft);
        Assert.Equal(AiChatIntentTypes.CancelDraft, response.PrimaryIntent);
        Assert.Equal("NothingToCancel", response.DialogueOutcome);
        Assert.Null(response.SessionId);
        Assert.Empty(Factory.MockAiProvider.Invocations);
        using var scope = Factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiSessions.ToListAsync());
    }

    private async Task<AiChatResponseDto> ChatWithoutSessionAsync(string message, string? intent = null)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AiBookingConfirmations.ExecuteDeleteAsync();
            await db.AiSelectionSnapshots.ExecuteDeleteAsync();
            await db.AiCancelledDraftScopes.ExecuteDeleteAsync();
            await db.AiSessions.ExecuteDeleteAsync();
            await db.AiAuditLogs.ExecuteDeleteAsync();
            await db.Appointments.ExecuteUpdateAsync(update => update.SetProperty(x => x.Status,
                ClinicManagement.Domain.Enums.AppointmentStatus.Cancelled));
        }
        Factory.MockAiProvider.Reset();
        Factory.MockAiProvider.Setup(x => x.ChatWithAiAsync(It.IsAny<string>(), It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult { IsSuccess = false, Status = "Disabled" });
        await AuthenticateAsync("pat1@test.com");
        var result = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = message, Intent = intent
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var response = (await result.Content.ReadFromJsonAsync<ApiResponse<AiChatResponseDto>>())!.Data!;
        using var afterScope = Factory.Services.CreateScope();
        var count = await afterScope.ServiceProvider.GetRequiredService<AppDbContext>().AiSessions.CountAsync();
        output.WriteLine($"Input: {message}; intent: {response.PrimaryIntent ?? "null"}; outcome: {response.DialogueOutcome ?? "null"}; sessionCreated: {count > 0}; provider: {response.ProviderStatus}");
        return response;
    }
}
