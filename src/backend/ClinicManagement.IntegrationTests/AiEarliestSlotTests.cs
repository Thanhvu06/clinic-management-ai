using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiEarliestSlotTests : IntegrationTestBase
{
    public AiEarliestSlotTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task FindEarliest_WithSelectedSpecialty_ReturnsCanonicalDbSlot_WithoutProviderOrMutation()
    {
        await AuthenticateAsync("pat1@test.com");
        ConfigureProviderToFailIfCalled();
        var date = GetFutureWorkingDate(40);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(13, 0), new TimeOnly(13, 30));

        int appointmentsBefore;
        int notificationsBefore;
        using (var beforeScope = Factory.Services.CreateScope())
        {
            var db = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            appointmentsBefore = await db.Appointments.CountAsync();
            notificationsBefore = await db.Notifications.CountAsync();
        }

        var response = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = CardiologySpecialtyId,
            PendingSlotDate = date.ToString("yyyy-MM-dd")
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Contains("Tim mạch", data.GetProperty("message").GetString());
        Assert.Contains("lấy trực tiếp từ hệ thống", data.GetProperty("message").GetString());

        var slotActions = GetActions(data, AiActionTypes.SelectSlot);
        var action = Assert.Single(slotActions, item =>
            item.GetProperty("payload").GetProperty("slotId").GetInt64() == slot.Id);
        var payload = action.GetProperty("payload");
        Assert.Equal(DoctorEntityId, payload.GetProperty("doctorId").GetInt64());
        Assert.Equal(CardiologySpecialtyId, payload.GetProperty("specialtyId").GetInt64());
        Assert.Equal(date.ToString("yyyy-MM-dd"), payload.GetProperty("slotDate").GetString());
        Assert.DoesNotContain(GetActions(data), item =>
            item.GetProperty("type").GetString() is AiActionTypes.ConfirmBooking or AiActionTypes.ReviewBooking);
        Assert.False(data.GetProperty("bookingDraft").GetProperty("isComplete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("bookingDraft").GetProperty("reason").ValueKind);

        var canonicalResponse = await Client.GetAsync(
            $"/api/v1/doctors/{DoctorEntityId}/available-slots?fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}&specialtyId={CardiologySpecialtyId}");
        Assert.Equal(HttpStatusCode.OK, canonicalResponse.StatusCode);
        using var canonicalDocument = JsonDocument.Parse(await canonicalResponse.Content.ReadAsStringAsync());
        Assert.Contains(canonicalDocument.RootElement.GetProperty("data").EnumerateArray(), item =>
            item.GetProperty("slotId").GetInt64() == slot.Id);

        using var afterScope = Factory.Services.CreateScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(appointmentsBefore, await afterDb.Appointments.CountAsync());
        Assert.Equal(notificationsBefore, await afterDb.Notifications.CountAsync());
        VerifyProviderWasNotCalled();
    }

    [Fact]
    public async Task FindEarliest_AcrossDoctors_UsesChronologicalOrderStableTieBreaker_AndRespectsSelectedDoctor()
    {
        await AuthenticateAsync("pat1@test.com");
        ConfigureProviderToFailIfCalled();
        var date = GetFutureWorkingDate(50);
        var later = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var tiedDoctorOne = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(8, 0), new TimeOnly(8, 30));
        var tiedDoctorTwo = await CreateAvailableSlotAsync(Doctor2EntityId, date, new TimeOnly(8, 0), new TimeOnly(8, 30));

        var allDoctorsResponse = await SendEarliestRequestAsync(SpecialtyEntityId, date);
        var allData = allDoctorsResponse.RootElement.GetProperty("data");
        var allActions = GetActions(allData, AiActionTypes.SelectSlot);
        var orderedIds = allActions
            .Select(item => item.GetProperty("payload").GetProperty("slotId").GetInt64())
            .ToList();

        Assert.True(orderedIds.IndexOf(tiedDoctorOne.Id) < orderedIds.IndexOf(tiedDoctorTwo.Id));
        Assert.True(orderedIds.IndexOf(tiedDoctorTwo.Id) < orderedIds.IndexOf(later.Id));

        var selectedDoctorResponse = await SendEarliestRequestAsync(SpecialtyEntityId, date, Doctor2EntityId);
        var selectedData = selectedDoctorResponse.RootElement.GetProperty("data");
        var selectedActions = GetActions(selectedData, AiActionTypes.SelectSlot);
        Assert.NotEmpty(selectedActions);
        Assert.All(selectedActions, item =>
            Assert.Equal(Doctor2EntityId, item.GetProperty("payload").GetProperty("doctorId").GetInt64()));
        Assert.Equal(tiedDoctorTwo.Id, selectedActions[0].GetProperty("payload").GetProperty("slotId").GetInt64());
        Assert.Contains(date.ToString("dd/MM/yyyy"), selectedData.GetProperty("message").GetString());
        VerifyProviderWasNotCalled();
    }

    [Fact]
    public async Task FindEarliest_ReportsMissingInvalidUnsupportedAndEmptyScopesTruthfully()
    {
        await AuthenticateAsync("pat1@test.com");
        ConfigureProviderToFailIfCalled();

        var missing = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot
        });
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("chưa chọn chuyên khoa", await missing.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var unknown = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = long.MaxValue
        });
        Assert.Contains("không tồn tại hoặc đã ngừng hoạt động", await unknown.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        long unsupportedSpecialtyId;
        long doctorlessSpecialtyId;
        using (var setupScope = Factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unsupported = new Specialty
            {
                SpecialtyCode = "P0AI-OFF",
                Name = "Khoa thử nghiệm chưa hỗ trợ AI",
                IsActive = true,
                AiEnabled = false
            };
            var doctorless = new Specialty
            {
                SpecialtyCode = "P0AI-NODOC",
                Name = "Khoa thử nghiệm chưa có bác sĩ",
                IsActive = true,
                AiEnabled = true
            };
            db.Specialties.AddRange(unsupported, doctorless);
            await db.SaveChangesAsync();
            unsupportedSpecialtyId = unsupported.Id;
            doctorlessSpecialtyId = doctorless.Id;
        }

        var unsupportedResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = unsupportedSpecialtyId
        });
        Assert.Contains("chưa hỗ trợ tìm lịch", await unsupportedResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var doctorlessResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = doctorlessSpecialtyId
        });
        Assert.Contains("không có bác sĩ đang hoạt động", await doctorlessResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var mismatch = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = CardiologySpecialtyId,
            PendingDoctorId = Doctor2EntityId
        });
        Assert.Contains("không thuộc chuyên khoa", await mismatch.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var emptyDate = GetFutureWorkingDate(200);
        var emptyResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = CardiologySpecialtyId,
            PendingSlotDate = emptyDate.ToString("yyyy-MM-dd")
        });
        var emptyBody = await emptyResponse.Content.ReadAsStringAsync();
        Assert.Contains("Không có lịch trống", emptyBody);
        Assert.Contains(emptyDate.ToString("dd/MM/yyyy"), emptyBody);

        var invalidIntent = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = "ConfirmBooking"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidIntent.StatusCode);
        Assert.Contains("INVALID_AI_INTENT", await invalidIntent.Content.ReadAsStringAsync());
        VerifyProviderWasNotCalled();
    }

    [Fact]
    public async Task FindEarliest_ExcludesBookedLeaveInactiveSundayAndPatientConflictSlots()
    {
        await AuthenticateAsync("pat1@test.com");
        ConfigureProviderToFailIfCalled();
        var monday = GetFutureMonday(70);
        var booked = await CreateAvailableSlotAsync(DoctorEntityId, monday, new TimeOnly(8, 0), new TimeOnly(8, 30));
        var onLeave = await CreateAvailableSlotAsync(DoctorEntityId, monday, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var patientConflict = await CreateAvailableSlotAsync(DoctorEntityId, monday, new TimeOnly(10, 0), new TimeOnly(10, 30));
        var conflictSource = await CreateAvailableSlotAsync(Doctor2EntityId, monday, new TimeOnly(10, 0), new TimeOnly(10, 30));
        var inactiveDoctor = await CreateAvailableSlotAsync(Doctor2EntityId, monday, new TimeOnly(11, 0), new TimeOnly(11, 30));
        var sunday = monday.AddDays(6);
        var closedDay = await CreateAvailableSlotAsync(DoctorEntityId, sunday, new TimeOnly(8, 0), new TimeOnly(8, 30));
        var valid = await CreateAvailableSlotAsync(DoctorEntityId, monday, new TimeOnly(12, 0), new TimeOnly(12, 30));

        using (var setupScope = Factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            booked.IsBooked = true;
            db.AppointmentSlots.Update(booked);
            db.DoctorLeaveRequests.Add(new DoctorLeaveRequest
            {
                DoctorId = DoctorEntityId,
                StartDateTime = monday.ToDateTime(new TimeOnly(8, 45)),
                EndDateTime = monday.ToDateTime(new TimeOnly(9, 45)),
                Reason = "Nghỉ thử nghiệm policy P0-1",
                Status = DoctorLeaveRequestStatus.Approved
            });
            db.Appointments.Add(new Appointment
            {
                AppointmentCode = "P0AI-CONFLICT",
                PatientId = Patient1EntityId,
                DoctorId = Doctor2EntityId,
                SpecialtyId = SpecialtyEntityId,
                AppointmentSlotId = conflictSource.Id,
                AppointmentDate = monday,
                StartTime = conflictSource.StartTime,
                EndTime = conflictSource.EndTime,
                Reason = "Lịch dùng để kiểm tra xung đột bệnh nhân",
                Status = AppointmentStatus.Pending
            });
            var doctorTwo = await db.Doctors.FindAsync(Doctor2EntityId);
            Assert.NotNull(doctorTwo);
            doctorTwo.IsActive = false;
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await SendEarliestRequestAsync(SpecialtyEntityId, monday);
            var data = response.RootElement.GetProperty("data");
            var returnedIds = GetActions(data, AiActionTypes.SelectSlot)
                .Select(item => item.GetProperty("payload").GetProperty("slotId").GetInt64())
                .ToList();

            Assert.Contains(valid.Id, returnedIds);
            Assert.DoesNotContain(booked.Id, returnedIds);
            Assert.DoesNotContain(onLeave.Id, returnedIds);
            Assert.DoesNotContain(patientConflict.Id, returnedIds);
            Assert.DoesNotContain(inactiveDoctor.Id, returnedIds);
            Assert.DoesNotContain(closedDay.Id, returnedIds);
            VerifyProviderWasNotCalled();
        }
        finally
        {
            using var restoreScope = Factory.Services.CreateScope();
            var db = restoreScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doctorTwo = await db.Doctors.FindAsync(Doctor2EntityId);
            if (doctorTwo != null)
            {
                doctorTwo.IsActive = true;
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task BookingActions_RequireReasonWithinTenToFiveHundredCharacters()
    {
        await AuthenticateAsync("pat1@test.com");
        var date = GetFutureWorkingDate(90);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(14, 0), new TimeOnly(14, 30));
        Factory.MockAiProvider.Reset();
        Factory.MockAiProvider
            .Setup(provider => provider.ChatWithAiAsync(
                It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatProviderResult
            {
                Reply = "Đã ghi nhận lựa chọn lịch khám của bạn.",
                Urgency = "ROUTINE"
            });

        foreach (var invalidReason in new string?[] { null, new string('a', 9), new string('a', 501) })
        {
            var invalidResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
            {
                Message = "Tôi chọn khung giờ này",
                PendingSpecialtyId = CardiologySpecialtyId,
                PendingDoctorId = DoctorEntityId,
                PendingSlotId = slot.Id,
                PendingSlotDate = date.ToString("yyyy-MM-dd"),
                Reason = invalidReason
            });
            Assert.Equal(invalidReason?.Length == 501 ? HttpStatusCode.BadRequest : HttpStatusCode.OK, invalidResponse.StatusCode);
            using var invalidDocument = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync());
            var invalidActions = invalidResponse.StatusCode == HttpStatusCode.OK
                ? GetActions(invalidDocument.RootElement.GetProperty("data"))
                : new List<JsonElement>();
            Assert.DoesNotContain(invalidActions, item =>
                item.GetProperty("type").GetString() is AiActionTypes.ConfirmBooking or AiActionTypes.ReviewBooking);
        }

        foreach (var validReason in new[] { new string('a', 10), new string('a', 500) })
        {
            var validResponse = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
            {
                Message = "Tôi chọn khung giờ này",
                PendingSpecialtyId = CardiologySpecialtyId,
                PendingDoctorId = DoctorEntityId,
                PendingSlotId = slot.Id,
                PendingSlotDate = date.ToString("yyyy-MM-dd"),
                Reason = validReason
            });
            Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
            using var validDocument = JsonDocument.Parse(await validResponse.Content.ReadAsStringAsync());
            var validActions = GetActions(validDocument.RootElement.GetProperty("data"));
            Assert.Contains(validActions, item => item.GetProperty("type").GetString() == AiActionTypes.ReviewBooking);
            var confirm = Assert.Single(validActions, item => item.GetProperty("type").GetString() == AiActionTypes.ConfirmBooking);
            Assert.True(confirm.GetProperty("requiresConfirmation").GetBoolean());
        }

        var finderWithReason = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = CardiologySpecialtyId,
            PendingSlotDate = date.ToString("yyyy-MM-dd"),
            Reason = new string('a', 10)
        });
        using var finderDocument = JsonDocument.Parse(await finderWithReason.Content.ReadAsStringAsync());
        Assert.DoesNotContain(GetActions(finderDocument.RootElement.GetProperty("data")), item =>
            item.GetProperty("type").GetString() is AiActionTypes.ConfirmBooking or AiActionTypes.ReviewBooking);
    }

    [Fact]
    public async Task FindEarliest_RequiresPatientAuthentication()
    {
        var anonymous = Factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = CardiologySpecialtyId
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<JsonDocument> SendEarliestRequestAsync(long specialtyId, DateOnly date, long? doctorId = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/ai/chat", new AiChatRequestDto
        {
            Message = "Tìm lịch khám sớm nhất.",
            Intent = AiChatIntentTypes.FindEarliestAvailableSlot,
            PendingSpecialtyId = specialtyId,
            PendingDoctorId = doctorId,
            PendingSlotDate = date.ToString("yyyy-MM-dd")
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private void ConfigureProviderToFailIfCalled()
    {
        Factory.MockAiProvider.Reset();
        Factory.MockAiProvider
            .Setup(provider => provider.ChatWithAiAsync(
                It.IsAny<string>(),
                It.IsAny<List<ChatMessageDto>>(),
                It.IsAny<List<WhitelistItemDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Operational earliest-slot intent must bypass the provider."));
    }

    private void VerifyProviderWasNotCalled()
    {
        Factory.MockAiProvider.Verify(provider => provider.ChatWithAiAsync(
            It.IsAny<string>(),
            It.IsAny<List<ChatMessageDto>>(),
            It.IsAny<List<WhitelistItemDto>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static List<JsonElement> GetActions(JsonElement data, string? actionType = null)
    {
        var actions = data.GetProperty("actions").EnumerateArray().ToList();
        return actionType == null
            ? actions
            : actions.Where(item => item.GetProperty("type").GetString() == actionType).ToList();
    }

    private static DateOnly GetFutureMonday(int minimumDaysFromNow)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(minimumDaysFromNow));
        while (date.DayOfWeek != DayOfWeek.Monday)
        {
            date = date.AddDays(1);
        }
        return date;
    }
}
