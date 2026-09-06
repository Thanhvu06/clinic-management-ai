using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Notifications.DTOs;

public class NotificationDto
{
    public long Id { get; set; }
    public NotificationType Type { get; set; }
    public string TypeName => Type.ToString();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Route { get; set; }
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public class UnreadNotificationCountDto
{
    public int UnreadCount { get; set; }

    public UnreadNotificationCountDto()
    {
    }

    public UnreadNotificationCountDto(int unreadCount)
    {
        UnreadCount = unreadCount;
    }
}

public class CreateNotificationRequest
{
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Route { get; set; }
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }
    public string? DedupeKey { get; set; }
}
