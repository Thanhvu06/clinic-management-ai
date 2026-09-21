using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.AI.Training;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Infrastructure.AI;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiDraftVersionContractTests : IntegrationTestBase
{
    public AiDraftVersionContractTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99)]
    public void AiActionValidator_Rejects_NonPositive_DraftVersion(int invalidVersion)
    {
        var action = new AiActionDto
        {
            Id = "act-test",
            Type = AiActionTypes.SelectDoctor,
            Label = "Chọn bác sĩ",
            Style = "secondary",
            DraftVersion = invalidVersion,
            Payload = new AiActionPayloadDto
            {
                SpecialtyId = 1,
                DoctorId = 10,
                DraftVersion = invalidVersion
            }
        };

        var isValid = AiActionValidator.Validate(action, out var error);
        Assert.False(isValid);
        Assert.Contains("Action DraftVersion must be a positive integer", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void AiActionValidator_Accepts_Positive_DraftVersion(int validVersion)
    {
        var action = new AiActionDto
        {
            Id = "act-test",
            Type = AiActionTypes.SelectDoctor,
            Label = "Chọn bác sĩ",
            Style = "secondary",
            DraftVersion = validVersion,
            Payload = new AiActionPayloadDto
            {
                SpecialtyId = 1,
                DoctorId = 10,
                DraftVersion = validVersion
            }
        };

        var isValid = AiActionValidator.Validate(action, out var error);
        Assert.True(isValid, error);
    }

    [Fact]
    public void AiActionTypes_IsBookingAction_Correctly_Identifies_Booking_And_NonBooking_Actions()
    {
        // Booking actions
        Assert.True(AiActionTypes.IsBookingAction(AiActionTypes.SelectDoctor));
        Assert.True(AiActionTypes.IsBookingAction(AiActionTypes.SelectSlot));
        Assert.True(AiActionTypes.IsBookingAction(AiActionTypes.ReviewBooking));
        Assert.True(AiActionTypes.IsBookingAction(AiActionTypes.ConfirmBooking));
        Assert.True(AiActionTypes.IsBookingAction(AiActionTypes.ChangePreferredDate));

        // Non-booking actions
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ContactReception));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ViewBills));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ViewMyAppointments));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ViewDiagnosticResults));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ViewPrescriptions));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.CallEmergency));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ViewSpecialty));
        Assert.False(AiActionTypes.IsBookingAction(AiActionTypes.ViewDoctors));
        Assert.False(AiActionTypes.IsBookingAction(null));
        Assert.False(AiActionTypes.IsBookingAction(string.Empty));
    }

    [Fact]
    public async Task Given_Request_Without_DraftVersion_When_BookingFlowTriggered_Then_Yields_Version_1_Across_Draft_And_Actions()
    {
        await AuthenticateAsync("pat1@test.com");

        Factory.MockAiProvider
            .Setup(x => x.ChatWithAiAsync(
                It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                Reply = "Tôi gợi ý bạn khám bác sĩ tại chuyên khoa Tim mạch.",
                SuggestedSpecialtyCodes = new List<string> { "SP06" },
                ExtractedSpecialtyCode = "SP06",
                ExtractedDoctorName = "Nguyễn Minh Khải",
                Urgency = "ROUTINE"
            });

        var request = new AiChatRequestDto
        {
            Message = "Tôi muốn đặt lịch khám bác sĩ Nguyễn Minh Khải",
            DraftVersion = null // First message in conversation
        };

        var response = await Client.PostAsJsonAsync("/api/v1/ai/chat", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        // Verify bookingDraft has version 1
        if (data.TryGetProperty("bookingDraft", out var draftElem) && draftElem.ValueKind != JsonValueKind.Null)
        {
            Assert.True(draftElem.TryGetProperty("version", out var verElem));
            Assert.Equal(1, verElem.GetInt32());
        }

        // Verify actions: no booking action should have draftVersion = null
        if (data.TryGetProperty("actions", out var actionsElem) && actionsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var act in actionsElem.EnumerateArray())
            {
                var type = act.GetProperty("type").GetString();
                if (AiActionTypes.IsBookingAction(type))
                {
                    Assert.True(act.TryGetProperty("draftVersion", out var actVerElem), $"Action {type} missing draftVersion property");
                    Assert.False(actVerElem.ValueKind == JsonValueKind.Null, $"Action {type} has null draftVersion");
                    Assert.Equal(1, actVerElem.GetInt32());

                    // Payload must also have draftVersion = 1
                    var payload = act.GetProperty("payload");
                    Assert.True(payload.TryGetProperty("draftVersion", out var payloadVerElem), $"Action {type} payload missing draftVersion");
                    Assert.Equal(1, payloadVerElem.GetInt32());
                }
            }
        }
    }

    [Fact]
    public async Task Given_Request_With_DraftVersion_V_When_BookingFlowTriggered_Then_Yields_Version_V_Plus_1()
    {
        await AuthenticateAsync("pat1@test.com");

        Factory.MockAiProvider
            .Setup(x => x.ChatWithAiAsync(
                It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                Reply = "Tôi đã cập nhật thông tin khung giờ khám mới nhất cho bạn.",
                SuggestedSpecialtyCodes = new List<string> { "SP06" },
                ExtractedSpecialtyCode = "SP06",
                ExtractedDoctorName = "Nguyễn Minh Khải",
                Urgency = "ROUTINE"
            });

        const int initialVersion = 2;
        var request = new AiChatRequestDto
        {
            Message = "Tôi muốn đổi khung giờ",
            DraftVersion = initialVersion
        };

        var response = await Client.PostAsJsonAsync("/api/v1/ai/chat", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        const int expectedNextVersion = initialVersion + 1; // 3

        if (data.TryGetProperty("bookingDraft", out var draftElem) && draftElem.ValueKind != JsonValueKind.Null)
        {
            Assert.True(draftElem.TryGetProperty("version", out var verElem));
            Assert.Equal(expectedNextVersion, verElem.GetInt32());
        }

        if (data.TryGetProperty("actions", out var actionsElem) && actionsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var act in actionsElem.EnumerateArray())
            {
                var type = act.GetProperty("type").GetString();
                if (AiActionTypes.IsBookingAction(type))
                {
                    Assert.True(act.TryGetProperty("draftVersion", out var actVerElem));
                    Assert.Equal(expectedNextVersion, actVerElem.GetInt32());

                    var payload = act.GetProperty("payload");
                    Assert.True(payload.TryGetProperty("draftVersion", out var payloadVerElem));
                    Assert.Equal(expectedNextVersion, payloadVerElem.GetInt32());
                }
            }
        }
    }

    [Fact]
    public async Task Given_Provider_Failure_Degraded_Mode_Synchronizes_DraftVersion_Correctly()
    {
        await AuthenticateAsync("pat1@test.com");

        // Force provider failure
        Factory.MockAiProvider
            .Setup(x => x.ChatWithAiAsync(
                It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                IsSuccess = false,
                Status = "Failure",
                Reply = string.Empty
            });

        var request = new AiChatRequestDto
        {
            Message = "Tôi muốn đặt lịch khám bác sĩ Nguyễn Minh Khải",
            DraftVersion = 1
        };

        var response = await Client.PostAsJsonAsync("/api/v1/ai/chat", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("Degraded", data.GetProperty("assistantStatus").GetString());

        // BookingDraft must have version >= 1
        if (data.TryGetProperty("bookingDraft", out var draftElem) && draftElem.ValueKind != JsonValueKind.Null)
        {
            Assert.True(draftElem.TryGetProperty("version", out var verElem));
            var draftVersion = verElem.GetInt32();
            Assert.True(draftVersion >= 1);

            // Any booking action must match the draft version, none can have null
            if (data.TryGetProperty("actions", out var actionsElem) && actionsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var act in actionsElem.EnumerateArray())
                {
                    var type = act.GetProperty("type").GetString();
                    if (AiActionTypes.IsBookingAction(type))
                    {
                        Assert.True(act.TryGetProperty("draftVersion", out var actVerElem));
                        Assert.False(actVerElem.ValueKind == JsonValueKind.Null);
                        Assert.Equal(draftVersion, actVerElem.GetInt32());
                    }
                }
            }
        }
    }
}
