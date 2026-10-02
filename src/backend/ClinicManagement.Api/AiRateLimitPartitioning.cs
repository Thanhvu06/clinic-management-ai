using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.Api;

public static class AiRateLimitPartitioning
{
    public static string GetPartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
            return $"user:{userId}";

        // Forwarded headers are intentionally not consumed here. They are only
        // safe after an explicitly configured, trusted proxy middleware.
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        return $"ip:{(string.IsNullOrWhiteSpace(remoteIp) ? "unknown" : remoteIp)}";
    }
}
