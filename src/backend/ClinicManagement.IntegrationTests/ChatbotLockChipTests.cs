using System.Net.Http.Json;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicManagement.IntegrationTests;

[Collection(AiPhase12AcceptanceCollection.Name)]
public sealed class ChatbotLockChipTests(CustomWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string WizardUrl = "/api/v1/ai/booking-wizard";

    private static async Task<JsonElement> WizardAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(WizardUrl, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task J3_booking_chip_start_without_typed_text_returns_the_lock_card_every_time()
    {
        var created = new List<Appointment>();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            for (var i = 1; i <= 3; i++)
            {
                var date = clock.VietnamToday.AddDays(-i);
                var slot = new AppointmentSlot { DoctorId = DoctorEntityId, SlotDate = date, StartTime = new TimeOnly(22, i), EndTime = new TimeOnly(22, i + 30) };
                var appointment = new Appointment
                {
                    AppointmentCode = "APT-J3-" + Guid.NewGuid().ToString("N")[..10], PatientId = Patient2EntityId, DoctorId = DoctorEntityId,
                    SpecialtyId = SpecialtyEntityId, AppointmentSlot = slot, AppointmentDate = date, StartTime = slot.StartTime, EndTime = slot.EndTime,
                    Status = AppointmentStatus.NoShow,
                    Histories = [new AppointmentHistory { Action = AppointmentHistoryAction.NoShow, NewStatus = AppointmentStatus.NoShow, CreatedAt = clock.UtcNow.AddDays(-i) }]
                };
                db.Appointments.Add(appointment);
                created.Add(appointment);
            }
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = await CreateAuthenticatedClientAsync("pat2@test.com");
            var typed = await WizardAsync(client, new { sessionId = $"sess_{Guid.NewGuid():N}", step = "start", typedMessage = "tôi muốn đặt lịch khám" });
            Assert.Equal("ONLINE_BOOKING_BLOCKED", typed.GetProperty("errorCode").GetString());

            for (var click = 0; click < 2; click++)
            {
                var chip = await WizardAsync(client, new { sessionId = $"sess_{Guid.NewGuid():N}", step = "start", currentRoute = "/patient", locale = "vi-VN" });
                Assert.Equal("ONLINE_BOOKING_BLOCKED", chip.GetProperty("errorCode").GetString());
                Assert.Equal("Tạm khóa đặt lịch online", chip.GetProperty("title").GetString());
                Assert.Equal("stopped", chip.GetProperty("step").GetString());
                Assert.Equal(typed.GetProperty("message").GetString(), chip.GetProperty("message").GetString());
            }
        }
        finally
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = created.Select(a => a.Id).ToList();
            var slotIds = created.Select(a => a.AppointmentSlotId).ToList();
            db.AppointmentHistories.RemoveRange(await db.AppointmentHistories.Where(h => ids.Contains(h.AppointmentId)).ToListAsync());
            db.Appointments.RemoveRange(await db.Appointments.Where(a => ids.Contains(a.Id)).ToListAsync());
            db.AppointmentSlots.RemoveRange(await db.AppointmentSlots.Where(s => slotIds.Contains(s.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
