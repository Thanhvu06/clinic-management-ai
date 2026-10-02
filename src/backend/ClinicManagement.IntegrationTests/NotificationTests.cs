using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class NotificationTests : IntegrationTestBase
{
    public NotificationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_UnauthenticatedUser_When_AccessesNotificationEndpoints_Then_Returns401Unauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var listResponse = await Client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var unreadCountResponse = await Client.GetAsync("/api/v1/notifications/unread-count");
        Assert.Equal(HttpStatusCode.Unauthorized, unreadCountResponse.StatusCode);

        var markReadResponse = await Client.PatchAsync("/api/v1/notifications/1/read", null);
        Assert.Equal(HttpStatusCode.Unauthorized, markReadResponse.StatusCode);

        var markAllResponse = await Client.PatchAsync("/api/v1/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.Unauthorized, markAllResponse.StatusCode);
    }

    [Fact]
    public async Task Given_Patient1Notification_When_Patient2QueriesNotifications_Then_ReturnsOnlyOwnNotifications()
    {
        // 1. Create a notification specifically for Patient 1
        var uniqueTitle = $"Thong bao rieng tu {Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Notifications.Add(new Notification
            {
                UserId = Patient1Id,
                Type = NotificationType.Appointment,
                Title = uniqueTitle,
                Message = "Noi dung rieng cua benh nhan 1",
                Route = "/patient/appointments",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 2. Patient 2 queries their notifications
        await AuthenticateAsync("pat2@test.com");
        var pat2Response = await Client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.OK, pat2Response.StatusCode);

        var pat2Json = await pat2Response.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(pat2Json))
        {
            var items = doc.RootElement.GetProperty("data").GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                var title = item.GetProperty("title").GetString();
                Assert.NotEqual(uniqueTitle, title);
            }
        }

        // 3. Patient 1 queries their notifications -> must find it
        await AuthenticateAsync("pat1@test.com");
        var pat1Response = await Client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.OK, pat1Response.StatusCode);

        var pat1Json = await pat1Response.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(pat1Json))
        {
            var items = doc.RootElement.GetProperty("data").GetProperty("items");
            var found = items.EnumerateArray().Any(x => x.GetProperty("title").GetString() == uniqueTitle);
            Assert.True(found, "Patient 1 should be able to see their own notification.");
        }
    }

    [Fact]
    public async Task Given_DuplicateDedupeKey_When_CreatingNotificationTwice_Then_DiscardsDuplicateGracefully()
    {
        var dedupeKey = $"test_dedupe_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // First creation
        var firstResult = await notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = Patient1Id,
            Type = NotificationType.System,
            Title = "Notification 1",
            Message = "Message 1",
            DedupeKey = dedupeKey
        });
        Assert.NotNull(firstResult);

        // Second creation with the same DedupeKey
        var secondResult = await notificationService.CreateNotificationAsync(new CreateNotificationRequest
        {
            UserId = Patient1Id,
            Type = NotificationType.System,
            Title = "Notification 2 (duplicate)",
            Message = "Message 2",
            DedupeKey = dedupeKey
        });

        // Deduplication should return null and not throw error
        Assert.Null(secondResult);
    }

    [Fact]
    public async Task Given_UnreadNotifications_When_MarkedAsRead_Then_UnreadCountDecrementsAndReadAtUtcUpdated()
    {
        long notificationId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = new Notification
            {
                UserId = Patient1Id,
                Type = NotificationType.Prescription,
                Title = "Don thuoc moi",
                Message = "Don thuoc da san sang",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Notifications.Add(notif);
            await db.SaveChangesAsync();
            notificationId = notif.Id;
        }

        await AuthenticateAsync("pat1@test.com");

        // Mark as read
        var markResponse = await Client.PatchAsync($"/api/v1/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.OK, markResponse.StatusCode);

        // Verify in DB
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notif = await db.Notifications.FindAsync(notificationId);
            Assert.NotNull(notif);
            Assert.True(notif.IsRead);
            Assert.NotNull(notif.ReadAtUtc);
        }

        // Test Mark All As Read
        var markAllResponse = await Client.PatchAsync("/api/v1/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.OK, markAllResponse.StatusCode);

        var unreadCountResponse = await Client.GetAsync("/api/v1/notifications/unread-count");
        Assert.Equal(HttpStatusCode.OK, unreadCountResponse.StatusCode);

        var countDoc = JsonDocument.Parse(await unreadCountResponse.Content.ReadAsStringAsync());
        var unreadCount = countDoc.RootElement.GetProperty("data").GetProperty("unreadCount").GetInt32();
        Assert.Equal(0, unreadCount);
    }
}
