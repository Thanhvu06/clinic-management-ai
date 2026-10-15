using System.Net;
using System.Net.Http.Json;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class NotificationFilterTests : IntegrationTestBase
{
    private static long _nextPhone = 78000000;
    public NotificationFilterTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Theory]
    [InlineData(NotificationType.System)]
    [InlineData(NotificationType.Appointment)]
    [InlineData(NotificationType.Invoice)]
    [InlineData(NotificationType.Payment)]
    [InlineData(NotificationType.Diagnostic)]
    public async Task Type_filter_returns_only_matching_notifications_and_filtered_pagination(NotificationType type)
    {
        var fixture = await CreateUsersAndNotificationsAsync();
        await AuthenticateAsync(fixture.OwnerEmail);
        var result = await GetPageAsync($"?type={(int)type}&pageSize=1");
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(type, Assert.Single(result.Items).Type);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Type_and_read_filters_are_combined(bool isRead)
    {
        var fixture = await CreateUsersAndNotificationsAsync();
        await AuthenticateAsync(fixture.OwnerEmail);
        var result = await GetPageAsync($"?type=9&isRead={isRead.ToString().ToLowerInvariant()}");
        var notification = Assert.Single(result.Items);
        Assert.Equal(1, result.TotalItems);
        Assert.Equal(NotificationType.Diagnostic, notification.Type);
        Assert.Equal(isRead, notification.IsRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Omitting_type_preserves_all_types_and_existing_read_filter(bool? isRead)
    {
        var fixture = await CreateUsersAndNotificationsAsync();
        await AuthenticateAsync(fixture.OwnerEmail);
        var query = isRead.HasValue ? $"?isRead={isRead.Value.ToString().ToLowerInvariant()}" : "";
        var result = await GetPageAsync(query);
        Assert.Equal(isRead.HasValue ? 10 : 20, result.TotalItems);
        Assert.Equal(Enum.GetValues<NotificationType>().Order(), result.Items.Select(n => n.Type).Distinct().Order());
        if (isRead.HasValue) Assert.All(result.Items, n => Assert.Equal(isRead.Value, n.IsRead));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(NotificationType.Diagnostic)]
    public async Task Filters_never_expose_another_users_notifications(NotificationType? type)
    {
        var fixture = await CreateUsersAndNotificationsAsync();
        var query = type.HasValue ? $"?type={(int)type.Value}" : "";
        await AuthenticateAsync(fixture.OwnerEmail);
        var own = await GetPageAsync(query);
        Assert.All(own.Items, n => Assert.StartsWith(fixture.OwnerEmail, n.Title));
        Assert.Equal(type.HasValue ? 2 : 20, own.TotalItems);
        await AuthenticateAsync(fixture.OtherEmail);
        var other = await GetPageAsync(query);
        Assert.All(other.Items, n => Assert.StartsWith(fixture.OtherEmail, n.Title));
        Assert.Empty(own.Items.Select(n => n.Id).Intersect(other.Items.Select(n => n.Id)));
    }

    private async Task<PagedResult<NotificationDto>> GetPageAsync(string query)
    {
        var response = await Client.GetAsync("/api/v1/notifications" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<NotificationDto>>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }

    private async Task<(string OwnerEmail, string OtherEmail)> CreateUsersAndNotificationsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emails = new List<string>();
        foreach (var label in new[] { "owner", "other" })
        {
            var email = $"notification-{label}-{Guid.NewGuid():N}@test.com";
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, FullName = label, IsActive = true,
                PhoneNumber = $"09{Interlocked.Increment(ref _nextPhone)}"
            };
            var created = await manager.CreateAsync(user, "Pass@123");
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            var role = await manager.AddToRoleAsync(user, "Patient");
            Assert.True(role.Succeeded);
            emails.Add(email);
            foreach (var type in Enum.GetValues<NotificationType>())
            foreach (var isRead in new[] { false, true })
            {
                db.Notifications.Add(new Notification
                {
                    UserId = user.Id, Type = type, Title = $"{email}: {type} {isRead}", Message = "Filter test",
                    IsRead = isRead, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-((int)type * 2 + (isRead ? 1 : 0))),
                    ReadAtUtc = isRead ? DateTime.UtcNow : null
                });
            }
        }
        await db.SaveChangesAsync();
        return (emails[0], emails[1]);
    }
}
