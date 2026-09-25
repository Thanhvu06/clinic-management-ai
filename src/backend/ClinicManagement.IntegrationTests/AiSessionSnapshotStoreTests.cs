using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
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
}
