using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiSessionSnapshotStoreTests : IntegrationTestBase
{
    public AiSessionSnapshotStoreTests(CustomWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateSnapshot_PersistsToDatabase_AndSurvivesNewScope()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";
        var draftId = $"draft_{Guid.NewGuid():N}";

        string snapshotId;

        // Act - Create snapshot in first scope
        using (var scope1 = Factory.Services.CreateScope())
        {
            var store1 = scope1.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            var snapshot = await store1.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = userId,
                SessionId = sessionId,
                DraftId = draftId,
                DraftVersion = 1,
                DoctorIds = new List<long> { 1, 2, 3 },
                SlotIds = new List<long> { 10, 11, 12 }
            });

            snapshotId = snapshot.SnapshotId;
            Assert.NotNull(snapshotId);
            Assert.Equal(3, snapshot.DoctorIds.Count);
        }

        // Assert - Read snapshot in new scope (simulates restart)
        using (var scope2 = Factory.Services.CreateScope())
        {
            var store2 = scope2.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            var result = await store2.ValidateSnapshotAsync(new ValidateSnapshotRequest
            {
                SnapshotId = snapshotId,
                CurrentUserId = userId,
                CurrentSessionId = sessionId,
                CurrentDraftId = draftId,
                CurrentDraftVersion = 1,
                NowUtc = DateTime.UtcNow
            });

            Assert.True(result.IsValid);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(3, result.Snapshot!.DoctorIds.Count);
            Assert.Equal(3, result.Snapshot.SlotIds.Count);
        }
    }

    [Fact]
    public async Task ValidateSnapshot_RejectsWrongUser()
    {
        // Arrange
        var ownerUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        var snapshot = await store.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = ownerUserId,
            SessionId = sessionId,
            DoctorIds = new List<long> { 1, 2 }
        });

        // Act - Attacker tries to use owner's snapshot
        var result = await store.ValidateSnapshotAsync(new ValidateSnapshotRequest
        {
            SnapshotId = snapshot.SnapshotId,
            CurrentUserId = attackerUserId,
            CurrentSessionId = sessionId,
            NowUtc = DateTime.UtcNow
        });

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("USER_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateSnapshot_RejectsWrongSession()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionA = $"sess_{Guid.NewGuid():N}";
        var sessionB = $"sess_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        var snapshot = await store.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userId,
            SessionId = sessionA,
            DoctorIds = new List<long> { 1 }
        });

        // Act - Try to use snapshot from different session
        var result = await store.ValidateSnapshotAsync(new ValidateSnapshotRequest
        {
            SnapshotId = snapshot.SnapshotId,
            CurrentUserId = userId,
            CurrentSessionId = sessionB,
            NowUtc = DateTime.UtcNow
        });

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("SESSION_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task CancelDraft_RevokesAllSnapshotsInScope_Idempotent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";
        var draftId = $"draft_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        var snap1 = await store.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userId,
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            DoctorIds = new List<long> { 1, 2 }
        });

        var snap2 = await store.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userId,
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            SlotIds = new List<long> { 10, 11 }
        });

        // Act - Cancel draft twice (idempotency test)
        await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId);
        await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId);

        // Assert - Both snapshots should be revoked
        var result1 = await store.ValidateSnapshotAsync(new ValidateSnapshotRequest
        {
            SnapshotId = snap1.SnapshotId,
            CurrentUserId = userId,
            CurrentSessionId = sessionId,
            CurrentDraftId = draftId,
            CurrentDraftVersion = 1,
            NowUtc = DateTime.UtcNow
        });

        var result2 = await store.ValidateSnapshotAsync(new ValidateSnapshotRequest
        {
            SnapshotId = snap2.SnapshotId,
            CurrentUserId = userId,
            CurrentSessionId = sessionId,
            CurrentDraftId = draftId,
            CurrentDraftVersion = 1,
            NowUtc = DateTime.UtcNow
        });

        Assert.False(result1.IsValid);
        Assert.Equal("REVOKED", result1.ErrorCode);
        Assert.False(result2.IsValid);
        Assert.Equal("REVOKED", result2.ErrorCode);
    }

    [Fact]
    public async Task CancelDraft_BlocksNewSnapshotCreation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";
        var draftId = $"draft_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        // Act - Cancel draft first
        await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId);

        // Assert - Creating new snapshot should fail
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await store.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = userId,
                SessionId = sessionId,
                DraftId = draftId,
                DoctorIds = new List<long> { 1 }
            });
        });
    }

    [Fact]
    public async Task ValidateSnapshot_RejectsTamperedDoctorList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        var snapshot = await store.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userId,
            SessionId = sessionId,
            DoctorIds = new List<long> { 1, 2, 3 }
        });

        // Act - Client sends different doctor IDs than server created
        var result = await store.ValidateSnapshotAsync(new ValidateSnapshotRequest
        {
            SnapshotId = snapshot.SnapshotId,
            CurrentUserId = userId,
            CurrentSessionId = sessionId,
            RequestedDoctorIds = new List<long> { 1, 2, 99 }, // Tampered!
            NowUtc = DateTime.UtcNow
        });

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("DOCTOR_LIST_TAMPERED", result.ErrorCode);
    }

    [Fact]
    public async Task IsDraftCancelled_ReturnsTrueForCancelledDraft()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";
        var draftId = $"draft_{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        // Act
        await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId);
        var isCancelled = await store.IsDraftCancelledAsync(draftId, userId, sessionId);

        // Assert
        Assert.True(isCancelled);
    }

    [Fact]
    public async Task ConcurrentCreateAndCancel_ProducesOneOrderedOutcome_AndNeverLeavesUsableSnapshot()
    {
        var userId = Guid.NewGuid();
        var sessionId = $"sess_{Guid.NewGuid():N}";
        var draftId = $"draft_{Guid.NewGuid():N}";

        async Task<(bool Created, string? SnapshotId, Exception? Error)> CreateAsync()
        {
            try
            {
                using var scope = Factory.Services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
                var snapshot = await store.CreateSnapshotAsync(new CreateSnapshotRequest
                {
                    UserId = userId,
                    SessionId = sessionId,
                    DraftId = draftId,
                    DraftVersion = 1,
                    DoctorIds = new List<long> { 1 },
                    SlotIds = new List<long> { 2 }
                });
                return (true, snapshot.SnapshotId, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex);
            }
        }

        async Task CancelAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId);
        }

        var createTask = CreateAsync();
        var cancelTask1 = CancelAsync();
        var cancelTask2 = CancelAsync();
        await Task.WhenAll(createTask, cancelTask1, cancelTask2);
        var createResult = await createTask;

        if (createResult.Created)
        {
            using var verifyScope = Factory.Services.CreateScope();
            var verifyStore = verifyScope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            var validation = await verifyStore.ValidateSnapshotAsync(new ValidateSnapshotRequest
            {
                SnapshotId = createResult.SnapshotId!,
                CurrentUserId = userId,
                CurrentSessionId = sessionId,
                CurrentDraftId = draftId,
                CurrentDraftVersion = 1,
                NowUtc = DateTime.UtcNow
            });

            Assert.False(validation.IsValid);
            Assert.Contains(validation.ErrorCode, new[] { "REVOKED", "DRAFT_CANCELLED" });
        }
        else
        {
            Assert.NotNull(createResult.Error);
            Assert.IsType<InvalidOperationException>(createResult.Error);
        }
    }

    [Fact]
    public async Task PurgeExpiredRecords_RemovesExpiredSnapshotsAndSessions_ButRetainsCancellationTombstones()
    {
        var now = DateTime.UtcNow;
        var expiredSessionId = $"sess_expired_{Guid.NewGuid():N}";
        var liveSessionId = $"sess_live_{Guid.NewGuid():N}";

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiSessions.AddRange(
                new AiSession
                {
                    SessionId = expiredSessionId,
                    CreatedAtUtc = now.AddHours(-2),
                    LastActiveAtUtc = now.AddHours(-2),
                    ExpiresAtUtc = now.AddMinutes(-1),
                    IsActive = true
                },
                new AiSession
                {
                    SessionId = liveSessionId,
                    CreatedAtUtc = now,
                    LastActiveAtUtc = now,
                    ExpiresAtUtc = now.AddHours(1),
                    IsActive = true
                });
            db.AiSelectionSnapshots.AddRange(
                new AiSelectionSnapshot
                {
                    SnapshotId = $"snap_expired_{Guid.NewGuid():N}",
                    SessionId = expiredSessionId,
                    CreatedAtUtc = now.AddMinutes(-20),
                    ExpiresAtUtc = now.AddMinutes(-1),
                    DoctorIdsJson = "[]",
                    SlotIdsJson = "[]"
                },
                new AiSelectionSnapshot
                {
                    SnapshotId = $"snap_live_{Guid.NewGuid():N}",
                    SessionId = liveSessionId,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(10),
                    DoctorIdsJson = "[]",
                    SlotIdsJson = "[]"
                });
            db.AiCancelledDraftScopes.Add(new AiCancelledDraftScope
            {
                SessionId = expiredSessionId,
                DraftId = "draft_expired",
                CancelledAtUtc = now.AddHours(-2),
                ExpiresAtUtc = now.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.PurgeExpiredRecordsAsync(now);
        }

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.AiSessions.AnyAsync(x => x.SessionId == expiredSessionId));
        Assert.True(await verifyDb.AiSessions.AnyAsync(x => x.SessionId == liveSessionId));
        Assert.False(await verifyDb.AiSelectionSnapshots.AnyAsync(x => x.SessionId == expiredSessionId));
        Assert.True(await verifyDb.AiSelectionSnapshots.AnyAsync(x => x.SessionId == liveSessionId));
        Assert.True(await verifyDb.AiCancelledDraftScopes.AnyAsync(x => x.SessionId == expiredSessionId));
    }

    [Fact]
    public async Task TouchSession_ConcurrentRequests_CreateOneRowPerScope_AndAllowDifferentUsers()
    {
        var sessionId = $"sess_scope_{Guid.NewGuid():N}";
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        async Task<AiSessionTouchResult> TouchAsync(Guid? userId)
        {
            using var scope = Factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            return await store.TouchSessionAsync(sessionId, userId, $"draft_{userId:N}", 1, null);
        }

        var sameScope = await Task.WhenAll(TouchAsync(userA), TouchAsync(userA));
        Assert.All(sameScope, result => Assert.True(result.IsAccepted));

        var anonymousScope = await Task.WhenAll(TouchAsync(null), TouchAsync(null));
        Assert.All(anonymousScope, result => Assert.True(result.IsAccepted));

        var differentUser = await TouchAsync(userB);
        Assert.True(differentUser.IsAccepted);

        using var verifyScope = Factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AiSessions.CountAsync(x => x.SessionId == sessionId && x.UserId == userA));
        Assert.Equal(1, await db.AiSessions.CountAsync(x => x.SessionId == sessionId && x.UserId == userB));
        Assert.Equal(1, await db.AiSessions.CountAsync(x => x.SessionId == sessionId && x.UserId == null));
    }

    [Fact]
    public async Task CancelledDraft_RemainsBlockedAfterSessionCleanup()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var sessionId = $"sess_replay_{Guid.NewGuid():N}";
        var draftId = $"draft_replay_{Guid.NewGuid():N}";

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.TouchSessionAsync(sessionId, userId, draftId, 1, null);
            await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId, nowUtc: now);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AiSessions.SingleAsync(x => x.SessionId == sessionId && x.UserId == userId);
            session.ExpiresAtUtc = now.AddHours(-1);
            await db.SaveChangesAsync();
        }

        using (var cleanupScope = Factory.Services.CreateScope())
        {
            var store = cleanupScope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.PurgeExpiredRecordsAsync(now);
        }

        using var replayScope = Factory.Services.CreateScope();
        var replayStore = replayScope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        var replay = await replayStore.TouchSessionAsync(sessionId, userId, draftId, 1, null);
        Assert.False(replay.IsAccepted);
        Assert.Equal("DRAFT_CANCELLED", replay.ErrorCode);
    }

    [Fact]
    public async Task CancelledDraft_RemainsTerminalAfterThirtyDayCleanup_AndCannotRecreateSessionOrSnapshot()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var sessionId = $"sess_terminal_{Guid.NewGuid():N}";
        var draftId = $"draft_terminal_{Guid.NewGuid():N}";

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.TouchSessionAsync(sessionId, userId, draftId, 1, null);
            await store.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = userId,
                SessionId = sessionId,
                DraftId = draftId,
                DraftVersion = 1,
                DoctorIds = new List<long> { 1 }
            });
            await store.InvalidateDraftSnapshotsForCancelAsync(draftId, sessionId, userId, nowUtc: now);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tombstone = await db.AiCancelledDraftScopes.SingleAsync(x =>
                x.UserId == userId && x.SessionId == sessionId && x.DraftId == draftId);
            Assert.Equal(DateTime.MaxValue, tombstone.ExpiresAtUtc);

            var session = await db.AiSessions.SingleAsync(x => x.SessionId == sessionId && x.UserId == userId);
            session.ExpiresAtUtc = now.AddHours(-1);
            await db.SaveChangesAsync();
        }

        using (var cleanupScope = Factory.Services.CreateScope())
        {
            var store = cleanupScope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.PurgeExpiredRecordsAsync(now.AddDays(30));
        }

        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AiCancelledDraftScopes.AnyAsync(x =>
                x.UserId == userId && x.SessionId == sessionId && x.DraftId == draftId));
            Assert.False(await db.AiSessions.AnyAsync(x => x.UserId == userId && x.SessionId == sessionId));
            Assert.False(await db.AiSelectionSnapshots.AnyAsync(x =>
                x.UserId == userId && x.SessionId == sessionId && x.DraftId == draftId));
        }

        using var replayScope = Factory.Services.CreateScope();
        var replayStore = replayScope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        var replay = await replayStore.TouchSessionAsync(sessionId, userId, draftId, 1, null);
        Assert.False(replay.IsAccepted);
        Assert.Equal("DRAFT_CANCELLED", replay.ErrorCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => replayStore.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userId,
            SessionId = sessionId,
            DraftId = draftId,
            DraftVersion = 1,
            DoctorIds = new List<long> { 1 }
        }));

        using var finalVerifyScope = Factory.Services.CreateScope();
        var finalDb = finalVerifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await finalDb.AiSessions.AnyAsync(x => x.UserId == userId && x.SessionId == sessionId));
        Assert.False(await finalDb.AiSelectionSnapshots.AnyAsync(x =>
            x.UserId == userId && x.SessionId == sessionId && x.DraftId == draftId));
    }

    [Fact]
    public async Task CancelledDraft_BlocksOnlyExactUserSessionDraftScope_AfterCleanup()
    {
        var now = DateTime.UtcNow;
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var sessionA = $"sess_isolated_{Guid.NewGuid():N}";
        var sessionB = $"sess_other_{Guid.NewGuid():N}";
        var draftA = $"draft_isolated_{Guid.NewGuid():N}";
        var draftB = $"draft_other_{Guid.NewGuid():N}";

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
            await store.TouchSessionAsync(sessionA, userA, draftA, 1, null);
            await store.CreateSnapshotAsync(new CreateSnapshotRequest
            {
                UserId = userA,
                SessionId = sessionA,
                DraftId = draftA,
                DraftVersion = 1,
                DoctorIds = new List<long> { 1 }
            });
            await store.InvalidateDraftSnapshotsForCancelAsync(draftA, sessionA, userA, nowUtc: now);
            await store.PurgeExpiredRecordsAsync(now.AddDays(30));
        }

        using var verifyScope = Factory.Services.CreateScope();
        var verifyStore = verifyScope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();

        var exactScope = await verifyStore.TouchSessionAsync(sessionA, userA, draftA, 1, null);
        Assert.False(exactScope.IsAccepted);
        Assert.Equal("DRAFT_CANCELLED", exactScope.ErrorCode);

        var otherUser = await verifyStore.TouchSessionAsync(sessionA, userB, draftA, 1, null);
        Assert.True(otherUser.IsAccepted);
        await verifyStore.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userB,
            SessionId = sessionA,
            DraftId = draftA,
            DraftVersion = 1,
            DoctorIds = new List<long> { 2 }
        });

        var otherSession = await verifyStore.TouchSessionAsync(sessionB, userA, draftA, 1, null);
        Assert.True(otherSession.IsAccepted);
        await verifyStore.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userA,
            SessionId = sessionB,
            DraftId = draftA,
            DraftVersion = 1,
            DoctorIds = new List<long> { 3 }
        });

        var otherDraft = await verifyStore.TouchSessionAsync(sessionA, userA, draftB, 1, null);
        Assert.True(otherDraft.IsAccepted);
        await verifyStore.CreateSnapshotAsync(new CreateSnapshotRequest
        {
            UserId = userA,
            SessionId = sessionA,
            DraftId = draftB,
            DraftVersion = 1,
            DoctorIds = new List<long> { 4 }
        });
    }
}
