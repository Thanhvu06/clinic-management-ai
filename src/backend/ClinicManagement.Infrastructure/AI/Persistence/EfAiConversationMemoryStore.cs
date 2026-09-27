using System.Text.Json;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI.Persistence;

/// <summary>
/// Stores a compact, server-owned conversation projection in the existing
/// AiSessions lifecycle. Raw user messages and tool results are never persisted.
/// </summary>
public sealed class EfAiConversationMemoryStore : IAiConversationMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SafeEntityKeys = new(StringComparer.Ordinal)
    {
        "appointmentId", "visitId", "diagnosticOrderId", "prescriptionId",
        "specialtyId", "facilityId", "date", "time"
    };
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<EfAiConversationMemoryStore> _logger;

    public EfAiConversationMemoryStore(AppDbContext db, IDateTimeProvider clock, ILogger<EfAiConversationMemoryStore> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AiConversationMemoryState?> LoadAsync(string sessionId, Guid? userId, AiActorRole role, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        var now = _clock.UtcNow;
        var row = await _db.AiSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == userId && x.IsActive && x.ExpiresAtUtc > now, cancellationToken);
        if (string.IsNullOrWhiteSpace(row?.ConversationStateJson)) return null;

        try
        {
            var state = JsonSerializer.Deserialize<AiConversationMemoryState>(row.ConversationStateJson, JsonOptions);
            return state is not null && string.Equals(state.Role, role.ToString(), StringComparison.Ordinal) ? state : null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring corrupt AI conversation memory for session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<AiConversationMemoryState> SaveTurnAsync(AiConversationMemoryWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 128)
            throw new ArgumentException("A bounded session id is required.", nameof(request));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var now = _clock.UtcNow;
            var expires = now.AddHours(24);
            var row = await _db.AiSessions.SingleOrDefaultAsync(
                x => x.SessionId == request.SessionId && x.UserId == request.UserId, cancellationToken);
            if (row is null)
            {
                row = new AiSession
                {
                    SessionId = request.SessionId,
                    UserId = request.UserId,
                    CreatedAtUtc = now,
                    LastActiveAtUtc = now,
                    ExpiresAtUtc = expires,
                    IsActive = true
                };
                _db.AiSessions.Add(row);
            }

            var version = row.ConversationVersion + 1;
            var state = BuildState(request, version, expires);
            row.ConversationStateJson = JsonSerializer.Serialize(state, JsonOptions);
            row.ConversationVersion = version;
            row.LastActiveAtUtc = now;
            row.ExpiresAtUtc = expires;
            row.IsActive = true;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return state;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                _db.ChangeTracker.Clear();
            }
        }

        throw new DbUpdateConcurrencyException("Conversation memory could not be updated after retrying.");
    }

    private static AiConversationMemoryState BuildState(AiConversationMemoryWriteRequest request, int version, DateTime expires) => new()
    {
        ConversationId = Limit(request.ConversationId, 128) ?? string.Empty,
        Role = request.Role.ToString(),
        LastIntent = Limit(request.Intent, 64),
        LastSubIntent = Limit(request.SubIntent, 96),
        PendingClarification = Limit(request.PendingClarification, 256),
        MissingFields = request.MissingFields.Where(IsSafeKey).Distinct(StringComparer.Ordinal).Take(8).ToArray(),
        ConfirmedEntities = request.ConfirmedEntities.Where(x => SafeEntityKeys.Contains(x.Key) && IsSafeKey(x.Key) && x.Value.Length <= 128)
            .Take(12).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
        CurrentResource = request.CurrentResource,
        SanitizedSummary = $"role={request.Role};intent={Limit(request.Intent, 64) ?? "none"};state={(string.IsNullOrWhiteSpace(request.PendingClarification) ? "resolved" : "clarifying")}",
        Version = version,
        ExpiresAtUtc = expires
    };

    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, length)];
    private static bool IsSafeKey(string value) => value.Length is > 0 and <= 64 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-');
}
