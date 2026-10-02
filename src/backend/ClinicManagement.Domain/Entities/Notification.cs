using System;
using System.ComponentModel.DataAnnotations;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Route { get; set; }

    [MaxLength(100)]
    public string? RelatedEntityType { get; set; }

    [MaxLength(100)]
    public string? RelatedEntityId { get; set; }

    public bool IsRead { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }

    [MaxLength(150)]
    public string? DedupeKey { get; set; }
}
