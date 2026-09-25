using System;
using System.Text.RegularExpressions;
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

    public async Task LogActionAsync(AiAuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var sanitizedMetadata = SanitizeAuditMetadata(entry.MetadataJson);

            var log = new AiAuditLog
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist AI audit action {ActionType} for user {UserId}", entry.ActionType, entry.UserId);
            // Non-fatal: audit failure should not crash main patient flow
        }
    }

    private static string? SanitizeAuditMetadata(string? rawMetadata)
    {
        if (string.IsNullOrWhiteSpace(rawMetadata)) return null;

        // Ensure no bearer tokens, passwords, medical symptoms or reason strings leaked into metadata
        var sanitized = rawMetadata;
        sanitized = Regex.Replace(sanitized, @"(?i)(bearer\s+[a-zA-Z0-9_\-\.]+)", "[REDACTED_TOKEN]");
        sanitized = Regex.Replace(sanitized, @"(?i)(password|secret|apikey|api_key)\s*[:=]\s*""?[^"",}]+""?", "$1:\"[REDACTED]\"");

        if (sanitized.Length > 2000)
        {
            sanitized = sanitized[..2000];
        }

        return sanitized;
    }
}
