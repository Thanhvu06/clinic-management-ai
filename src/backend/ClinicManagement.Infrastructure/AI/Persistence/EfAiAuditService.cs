using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI.Persistence;

public class EfAiAuditService : IAiAuditService
{
    private readonly AppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<EfAiAuditService> _logger;

    public EfAiAuditService(
        AppDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        ILogger<EfAiAuditService> logger)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<AiAuditWriteResult> LogActionAsync(AiAuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        AiAuditLog? log = null;
        try
        {
            var sanitizedMetadata = SanitizeAuditMetadata(entry.MetadataJson);

            log = new AiAuditLog
            {
                UserId = entry.UserId,
                SessionId = !string.IsNullOrWhiteSpace(entry.SessionId) ? entry.SessionId.Trim() : null,
                DraftId = !string.IsNullOrWhiteSpace(entry.DraftId) ? entry.DraftId.Trim() : null,
                DraftVersion = entry.DraftVersion,
                FacilityId = entry.FacilityId,
                ActionType = entry.ActionType,
                Outcome = entry.Outcome,
                ErrorCode = entry.ErrorCode,
                CorrelationId = entry.CorrelationId,
                MetadataJson = sanitizedMetadata,
                TimestampUtc = _dateTimeProvider.UtcNow
            };

            _dbContext.AiAuditLogs.Add(log);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return AiAuditWriteResult.Success();
        }
        catch (Exception ex)
        {
            if (log != null)
            {
                // Do not leave a failed Added entity in the scoped DbContext. A later
                // SaveChanges in the same request must not retry the broken audit row.
                _dbContext.Entry(log).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
            _logger.LogError(ex, "Failed to persist AI audit action {ActionType} for user {UserId}", entry.ActionType, entry.UserId);
            return AiAuditWriteResult.Failed("AUDIT_WRITE_FAILED");
        }
    }

    private static string? SanitizeAuditMetadata(string? rawMetadata)
    {
        if (string.IsNullOrWhiteSpace(rawMetadata)) return null;

        try
        {
            using var document = JsonDocument.Parse(rawMetadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedFields.Contains(property.Name)) continue;
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        var value = property.Value.GetString();
                        if (value is { Length: <= 128 }) safe[property.Name] = value;
                        break;
                    case JsonValueKind.Number when property.Value.TryGetInt64(out var number):
                        safe[property.Name] = number;
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        safe[property.Name] = property.Value.GetBoolean();
                        break;
                }
            }

            return safe.Count == 0
                ? null
                : JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            // Invalid JSON is not truncated into another invalid JSON value and is
            // never persisted as if it were a successful audit payload.
            return null;
        }
    }

    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "source", "operation", "provider", "status", "errorCode", "retryCount",
        "appointmentId", "confirmationState"
    };
}
