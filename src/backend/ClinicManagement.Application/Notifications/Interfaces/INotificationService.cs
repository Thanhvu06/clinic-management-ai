using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Notifications.Interfaces;

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetCurrentUserNotificationsAsync(int page = 1, int pageSize = 20, bool? isRead = null, CancellationToken cancellationToken = default);
    Task<UnreadNotificationCountDto> GetCurrentUserUnreadCountAsync(CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(long id, CancellationToken cancellationToken = default);
    Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken = default);
    Task<NotificationDto?> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default);
    Task CreateNotificationsForRolesAsync(IEnumerable<string> roles, NotificationType type, string title, string message, string? route = null, string? relatedEntityType = null, string? relatedEntityId = null, string? dedupeKeyPrefix = null, CancellationToken cancellationToken = default);
}
