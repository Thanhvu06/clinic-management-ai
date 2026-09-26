using System;

namespace ClinicManagement.Domain.Entities;

public class IdempotencyRecord
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
}
