using System;
using System.Collections.Generic;
using System.Linq;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using ClinicManagement.Application.AI.Interfaces;

namespace ClinicManagement.IntegrationTests;

internal sealed class TestSelectionSnapshot
{
    public string SnapshotId { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public string? SessionId { get; init; }
    public string? DraftId { get; init; }
    public int? DraftVersion { get; init; }
    public long? FacilityId { get; init; }
    public long? SpecialtyId { get; init; }
    public long? DoctorId { get; init; }
    public string? SlotDate { get; init; }
    public List<long> DoctorIds { get; init; } = new();
    public List<long> SlotIds { get; init; } = new();
    public DateTime ExpiresAtUtc { get; init; }
}

internal static class AiSnapshotTestHelper
{
    public static void Clear(IServiceProvider rootProvider)
    {
        using var scope = rootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiSelectionSnapshots.RemoveRange(db.AiSelectionSnapshots);
        db.AiCancelledDraftScopes.RemoveRange(db.AiCancelledDraftScopes);
        db.AiAuditLogs.RemoveRange(db.AiAuditLogs);
        db.AiSessions.RemoveRange(db.AiSessions);
        db.AiBookingConfirmations.RemoveRange(db.AiBookingConfirmations);
        db.SaveChanges();
    }

    public static void Store(IServiceProvider rootProvider, TestSelectionSnapshot snapshot)
    {
        using var scope = rootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = db.AiSelectionSnapshots.FirstOrDefault(x => x.SnapshotId == snapshot.SnapshotId);
        if (existing == null)
        {
            existing = new AiSelectionSnapshot { SnapshotId = snapshot.SnapshotId };
            db.AiSelectionSnapshots.Add(existing);
        }

        existing.UserId = snapshot.UserId;
        existing.SessionId = snapshot.SessionId;
        existing.DraftId = snapshot.DraftId;
        existing.DraftVersion = snapshot.DraftVersion;
        existing.FacilityId = snapshot.FacilityId;
        existing.SpecialtyId = snapshot.SpecialtyId;
        existing.DoctorId = snapshot.DoctorId;
        existing.SlotDate = snapshot.SlotDate;
        existing.DoctorIdsJson = System.Text.Json.JsonSerializer.Serialize(snapshot.DoctorIds);
        existing.SlotIdsJson = System.Text.Json.JsonSerializer.Serialize(snapshot.SlotIds);
        existing.CreatedAtUtc = DateTime.UtcNow;
        existing.ExpiresAtUtc = snapshot.ExpiresAtUtc;
        existing.IsRevoked = false;
        existing.RevokedAtUtc = null;
        db.SaveChanges();
    }

    public static async System.Threading.Tasks.Task InvalidateAsync(IServiceProvider rootProvider, string draftId, string sessionId, Guid? userId, DateTime? nowUtc = null)
    {
        using var scope = rootProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId, nowUtc: nowUtc);
    }

    public static async System.Threading.Tasks.Task<bool> IsCancelledAsync(IServiceProvider rootProvider, string draftId, Guid? userId, string sessionId, DateTime? nowUtc = null)
    {
        using var scope = rootProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        return await store.IsDraftCancelledAsync(draftId, userId, sessionId, nowUtc);
    }

    public static async System.Threading.Tasks.Task<SnapshotValidationResult> ValidateAsync(IServiceProvider rootProvider, string snapshotId, Guid? userId, int? draftVersion, List<long>? doctorIds, List<long>? slotIds, DateTime nowUtc, string? sessionId = null, string? draftId = null, long? specialtyId = null)
    {
        using var scope = rootProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        return await store.ValidateSnapshotAsync(new ValidateSnapshotRequest
        {
            SnapshotId = snapshotId,
            CurrentUserId = userId,
            CurrentDraftVersion = draftVersion,
            RequestedDoctorIds = doctorIds,
            RequestedSlotIds = slotIds,
            CurrentSessionId = sessionId,
            CurrentDraftId = draftId,
            CurrentSpecialtyId = specialtyId,
            NowUtc = nowUtc
        });
    }
}
