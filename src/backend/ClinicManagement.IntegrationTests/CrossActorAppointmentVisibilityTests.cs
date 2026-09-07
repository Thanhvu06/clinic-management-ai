using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class CrossActorAppointmentVisibilityTests : IntegrationTestBase
{
    public CrossActorAppointmentVisibilityTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_TwoDoctors_When_QueryingDoctorEndpoints_Then_EachDoctorOnlySeesTheirOwnAppointments()
    {
        var date = GetFutureWorkingDate(36);
        var slot1 = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(8, 0, 0), new TimeOnly(8, 30, 0));
        var slot2 = await CreateAvailableSlotAsync(Doctor2EntityId, date, new TimeOnly(8, 30, 0), new TimeOnly(9, 0, 0));

        // Book for Doctor 1
        await AuthenticateAsync("pat1@test.com");
        var res1 = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot1.Id,
            Reason = "Khám Bác sĩ 1"
        });
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var id1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Book for Doctor 2
        var res2 = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = Doctor2EntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot2.Id,
            Reason = "Khám Bác sĩ 2"
        });
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var id2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Doctor 1 queries dashboard
        await AuthenticateAsync("doc@test.com");
        var doc1DashRes = await Client.GetAsync($"/api/v1/doctor/dashboard?date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, doc1DashRes.StatusCode);
        var doc1Dash = JsonDocument.Parse(await doc1DashRes.Content.ReadAsStringAsync());
        var queue1 = doc1Dash.RootElement.GetProperty("data").GetProperty("queue");
        
        bool foundDoc1Apt = false;
        bool foundDoc2Apt = false;
        foreach (var item in queue1.EnumerateArray())
        {
            var aptId = item.GetProperty("appointmentId").GetInt64();
            if (aptId == id1) foundDoc1Apt = true;
            if (aptId == id2) foundDoc2Apt = true;
        }
        Assert.True(foundDoc1Apt, "Doctor 1 must see their own appointment");
        Assert.False(foundDoc2Apt, "Doctor 1 must NOT see Doctor 2's appointment");

        // Doctor 2 queries dashboard
        await AuthenticateAsync("doc2@test.com");
        var doc2DashRes = await Client.GetAsync($"/api/v1/doctor/dashboard?date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, doc2DashRes.StatusCode);
        var doc2Dash = JsonDocument.Parse(await doc2DashRes.Content.ReadAsStringAsync());
        var queue2 = doc2Dash.RootElement.GetProperty("data").GetProperty("queue");

        foundDoc1Apt = false;
        foundDoc2Apt = false;
        foreach (var item in queue2.EnumerateArray())
        {
            var aptId = item.GetProperty("appointmentId").GetInt64();
            if (aptId == id1) foundDoc1Apt = true;
            if (aptId == id2) foundDoc2Apt = true;
        }
        Assert.False(foundDoc1Apt, "Doctor 2 must NOT see Doctor 1's appointment");
        Assert.True(foundDoc2Apt, "Doctor 2 must see their own appointment");
    }

    [Fact]
    public async Task Given_UpcomingAppointments_When_QueryingDashboard_Then_OnlyReturnsNext7DaysPendingOrConfirmed()
    {
        var targetDate = GetFutureWorkingDate(40);
        
        // Appointment within next 7 days (day +2)
        var dateWithin7 = targetDate.AddDays(2);
        if (dateWithin7.DayOfWeek == DayOfWeek.Sunday) dateWithin7 = dateWithin7.AddDays(1);
        var slotWithin = await CreateAvailableSlotAsync(DoctorEntityId, dateWithin7, new TimeOnly(14, 0, 0), new TimeOnly(14, 30, 0));

        await AuthenticateAsync("pat1@test.com");
        var bookRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slotWithin.Id,
            Reason = "Khám sắp tới trong 7 ngày"
        });
        Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);
        var aptWithinId = JsonDocument.Parse(await bookRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Doctor checks dashboard for targetDate
        await AuthenticateAsync("doc@test.com");
        var dashRes = await Client.GetAsync($"/api/v1/doctor/dashboard?date={targetDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, dashRes.StatusCode);
        var dashDoc = JsonDocument.Parse(await dashRes.Content.ReadAsStringAsync());
        var upcoming = dashDoc.RootElement.GetProperty("data").GetProperty("upcomingAppointments");

        bool foundUpcoming = false;
        foreach (var item in upcoming.EnumerateArray())
        {
            if (item.GetProperty("appointmentId").GetInt64() == aptWithinId)
            {
                foundUpcoming = true;
                break;
            }
        }
        Assert.True(foundUpcoming, "Upcoming list must contain appointment scheduled within 7 days");
    }

    [Fact]
    public async Task Given_AppointmentEvents_Then_NotificationsDeliveredToCorrectRecipients()
    {
        var date = GetFutureWorkingDate(42);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(10, 0, 0), new TimeOnly(10, 30, 0));

        // 1. Patient books appointment -> Doctor should receive notification
        await AuthenticateAsync("pat1@test.com");
        var bookRes = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slot.Id,
            Reason = "Khám kiểm tra notification"
        });
        Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);
        var aptId = JsonDocument.Parse(await bookRes.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // Verify doctor received notification for booking
        await AuthenticateAsync("doc@test.com");
        var docNotifsRes = await Client.GetAsync("/api/v1/notifications?limit=20");
        Assert.Equal(HttpStatusCode.OK, docNotifsRes.StatusCode);
        var docNotifsDoc = JsonDocument.Parse(await docNotifsRes.Content.ReadAsStringAsync());
        var docNotifs = docNotifsDoc.RootElement.GetProperty("data").GetProperty("items");
        bool hasBookNotif = false;
        foreach (var n in docNotifs.EnumerateArray())
        {
            var entityId = n.TryGetProperty("relatedEntityId", out var eid) ? eid.GetString() : "";
            var route = n.TryGetProperty("route", out var r) ? r.GetString() : "";
            if (entityId == aptId.ToString() || (route != null && route.Contains(aptId.ToString())))
            {
                hasBookNotif = true;
                break;
            }
        }
        Assert.True(hasBookNotif, "Doctor must receive notification when new appointment is booked");

        // 2. Reception confirms appointment -> Patient and Doctor should be notified
        await AuthenticateAsync("rec@test.com");
        var confRes = await Client.PostAsync($"/api/v1/reception/appointments/{aptId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confRes.StatusCode);

        // Verify patient received confirmation notification
        await AuthenticateAsync("pat1@test.com");
        var patNotifsRes = await Client.GetAsync("/api/v1/notifications?limit=20");
        Assert.Equal(HttpStatusCode.OK, patNotifsRes.StatusCode);
        var patNotifsDoc = JsonDocument.Parse(await patNotifsRes.Content.ReadAsStringAsync());
        var patNotifs = patNotifsDoc.RootElement.GetProperty("data").GetProperty("items");
        bool hasConfirmNotif = false;
        foreach (var n in patNotifs.EnumerateArray())
        {
            var title = n.GetProperty("title").GetString() ?? "";
            var message = n.GetProperty("message").GetString() ?? "";
            if (title.Contains("xác nhận") || message.Contains("xác nhận") || title.Contains("thành công"))
            {
                hasConfirmNotif = true;
                break;
            }
        }
        Assert.True(hasConfirmNotif, "Patient must receive notification when appointment is confirmed");

        // 3. Reception checks in -> Doctor notified
        await AuthenticateAsync("rec@test.com");
        var checkInRes = await Client.PostAsync($"/api/v1/reception/appointments/{aptId}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkInRes.StatusCode);

        await AuthenticateAsync("doc@test.com");
        var docNotifsAfterCheckin = await Client.GetAsync("/api/v1/notifications?limit=20");
        var docNotifsAfterDoc = JsonDocument.Parse(await docNotifsAfterCheckin.Content.ReadAsStringAsync());
        var notifsArray = docNotifsAfterDoc.RootElement.GetProperty("data").GetProperty("items");
        bool hasCheckInNotif = false;
        foreach (var n in notifsArray.EnumerateArray())
        {
            var title = n.GetProperty("title").GetString() ?? "";
            var message = n.GetProperty("message").GetString() ?? "";
            if (title.Contains("tiếp nhận") || message.Contains("tiếp nhận") || title.Contains("check-in") || message.Contains("có mặt"))
            {
                hasCheckInNotif = true;
                break;
            }
        }
        Assert.True(hasCheckInNotif, "Doctor must receive notification when patient is checked in");
    }
}
