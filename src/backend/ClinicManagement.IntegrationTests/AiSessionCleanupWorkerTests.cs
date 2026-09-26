using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiSessionCleanupWorkerTests
{
    [Fact]
    public async Task RunsFirstCleanupImmediatelyAfterFirstRunDelay()
    {
        var firstPurge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialDelayReturned = false;
        var delayCalls = 0;
        var snapshotStore = new Mock<IAiSessionSnapshotStore>();
        snapshotStore
            .Setup(x => x.PurgeExpiredRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((_, _) =>
            {
                Assert.True(initialDelayReturned);
                firstPurge.TrySetResult();
            })
            .Returns(Task.CompletedTask);
        var confirmationStore = new Mock<IAiBookingConfirmationStore>();
        confirmationStore
            .Setup(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var services = BuildServices(snapshotStore.Object, confirmationStore.Object);
        using var stopping = new CancellationTokenSource();
        var worker = new AiSessionCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiSessionCleanupWorker>.Instance,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            (delay, token) =>
            {
                var call = Interlocked.Increment(ref delayCalls);
                if (call == 1)
                {
                    initialDelayReturned = true;
                    return Task.CompletedTask;
                }

                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });

        await worker.StartAsync(stopping.Token);
        await firstPurge.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(delayCalls >= 1);
        snapshotStore.Verify(x => x.PurgeExpiredRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        confirmationStore.Verify(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        stopping.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuesWithNextCleanupCycleAfterCycleFailure()
    {
        var secondPurge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeCount = 0;
        using var stopping = new CancellationTokenSource();
        var snapshotStore = new Mock<IAiSessionSnapshotStore>();
        snapshotStore
            .Setup(x => x.PurgeExpiredRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((_, _) =>
            {
                var current = Interlocked.Increment(ref purgeCount);
                if (current == 1)
                {
                    throw new InvalidOperationException("simulated cleanup failure");
                }

                secondPurge.TrySetResult();
                stopping.Cancel();
            })
            .Returns(Task.CompletedTask);
        var confirmationStore = new Mock<IAiBookingConfirmationStore>();
        confirmationStore
            .Setup(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var services = BuildServices(snapshotStore.Object, confirmationStore.Object);
        var worker = new AiSessionCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiSessionCleanupWorker>.Instance,
            TimeSpan.Zero,
            TimeSpan.Zero,
            (_, _) => Task.CompletedTask);

        await worker.StartAsync(stopping.Token);
        await secondPurge.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, purgeCount);
        confirmationStore.Verify(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        stopping.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Transitions_active_actions_and_purges_only_retained_terminal_rows()
    {
        var now = new DateTime(2030, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var snapshotStore = new Mock<IAiSessionSnapshotStore>();
        snapshotStore.Setup(x => x.PurgeExpiredRecordsAsync(now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var confirmationStore = new Mock<IAiBookingConfirmationStore>();
        confirmationStore.Setup(x => x.PurgeExpiredAsync(now, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = new ServiceCollection()
            .AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider(now))
            .AddScoped(_ => snapshotStore.Object)
            .AddScoped(_ => confirmationStore.Object)
            .AddDbContext<AppDbContext>(options => options.UseSqlite(connection))
            .BuildServiceProvider();

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.AiPendingToolActions.AddRange(
                Action("stale-executing", AiPendingToolActionState.Executing, now.AddMinutes(-1), now.AddHours(1)),
                Action("expired-pending", AiPendingToolActionState.PendingConfirmation, now.AddMinutes(1), now.AddMinutes(-1)),
                Action("old-completed", AiPendingToolActionState.Completed, now.AddDays(-2), now.AddDays(-2), executedAt: now.AddDays(-2)),
                Action("old-terminal-failure", AiPendingToolActionState.FailedTerminal, now.AddDays(-2), now.AddDays(-2)),
                Action("recent-completed", AiPendingToolActionState.Completed, now.AddHours(-1), now.AddHours(-1), executedAt: now.AddHours(-1)));
            db.AiCancelledDraftScopes.Add(new AiCancelledDraftScope
            {
                UserId = Guid.NewGuid(),
                SessionId = "sess_tombstone",
                DraftId = "draft_tombstone",
                CancelledAtUtc = now.AddDays(-30),
                ExpiresAtUtc = DateTime.MaxValue
            });
            await db.SaveChangesAsync();
        }

        var delayCount = 0;
        using var stopping = new CancellationTokenSource();
        var worker = new AiSessionCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiSessionCleanupWorker>.Instance,
            TimeSpan.Zero,
            TimeSpan.FromHours(1),
            (_, token) =>
            {
                if (Interlocked.Increment(ref delayCount) == 1) return Task.CompletedTask;
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });

        await worker.StartAsync(stopping.Token);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(20);
            using var pollScope = services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await pollDb.AiPendingToolActions.AnyAsync(x => x.State == AiPendingToolActionState.FailedRetryable))
                break;
        }

        stopping.Cancel();
        await worker.StopAsync(CancellationToken.None);

        using var assertScope = services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await assertDb.AiPendingToolActions.AsNoTracking().ToListAsync();
        Assert.Equal(AiPendingToolActionState.FailedRetryable, Assert.Single(actions, x => x.ResourceId == "stale-executing").State);
        Assert.Equal(AiPendingToolActionState.Expired, Assert.Single(actions, x => x.ResourceId == "expired-pending").State);
        Assert.DoesNotContain(actions, x => x.ResourceId is "old-completed" or "old-terminal-failure");
        Assert.Contains(actions, x => x.ResourceId == "recent-completed");
        Assert.True(await assertDb.AiCancelledDraftScopes.AnyAsync(x => x.DraftId == "draft_tombstone"));
    }

    private static AiPendingToolAction Action(
        string resourceId,
        AiPendingToolActionState state,
        DateTime createdAt,
        DateTime expiresAt,
        DateTime? executedAt = null) => new()
    {
        ActionId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        SessionId = $"sess_{resourceId}",
        ToolName = "patient.prepare_cancel_appointment",
        ToolVersion = "1.0",
        RequestHash = resourceId,
        ResourceType = "appointment",
        ResourceId = resourceId,
        NormalizedArgumentsJson = "{\"appointmentId\":1}",
        CreatedAtUtc = createdAt,
        ExpiresAtUtc = expiresAt,
        ExecutedAtUtc = executedAt,
        ExecutionLeaseExpiresAtUtc = state == AiPendingToolActionState.Executing ? createdAt : null,
        State = state
    };

    private sealed class FixedDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
        public TimeZoneInfo VietnamTimeZone { get; } = TimeZoneInfo.Utc;
        public DateTime VietnamNow => UtcNow;
        public DateOnly VietnamToday => DateOnly.FromDateTime(UtcNow);
        public TimeOnly VietnamTime => TimeOnly.FromDateTime(UtcNow);
        public DateTime ConvertUtcToVietnam(DateTime utcDateTime) => utcDateTime;
        public DateTime ConvertVietnamToUtc(DateTime vnDateTime) => vnDateTime;
    }

    private static ServiceProvider BuildServices(
        IAiSessionSnapshotStore snapshotStore,
        IAiBookingConfirmationStore confirmationStore)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);

        return new ServiceCollection()
            .AddSingleton(clock.Object)
            .AddScoped(_ => snapshotStore)
            .AddScoped(_ => confirmationStore)
            .BuildServiceProvider();
    }
}
