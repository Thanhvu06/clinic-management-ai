using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.AI;

public sealed class AiSessionCleanupWorker : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiSessionCleanupWorker> _logger;
    private readonly TimeSpan _firstRunDelay;
    private readonly TimeSpan _runInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public AiSessionCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<AiSessionCleanupWorker> logger)
        : this(scopeFactory, logger, FirstRunDelay, RunInterval, Task.Delay)
    {
    }

    internal AiSessionCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AiSessionCleanupWorker> logger,
        TimeSpan firstRunDelay,
        TimeSpan runInterval,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _firstRunDelay = firstRunDelay;
        _runInterval = runInterval;
        _delayAsync = delayAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _delayAsync(_firstRunDelay, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI cleanup cycle failed; the next cycle will retry.");
            }

            // The first purge happens immediately after FirstRunDelay. Every
            // later delay is after the completed cycle, so a slow/failing
            // cycle cannot postpone the first cleanup by another interval.
            await _delayAsync(_runInterval, stoppingToken);
        }
    }

    private async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var store = scope.ServiceProvider.GetRequiredService<IAiSessionSnapshotStore>();
        await store.PurgeExpiredRecordsAsync(clock.UtcNow, cancellationToken);
        var confirmationStore = scope.ServiceProvider.GetRequiredService<IAiBookingConfirmationStore>();
        await confirmationStore.PurgeExpiredAsync(clock.UtcNow, cancellationToken);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.UtcNow;
        var completedRetentionCutoff = now.AddHours(-24);
        await db.AiPendingToolActions
            .Where(x => x.ExpiresAtUtc <= now &&
                        (x.State == AiPendingToolActionState.PendingConfirmation ||
                         x.State == AiPendingToolActionState.Executing ||
                         x.State == AiPendingToolActionState.FailedRetryable))
            .ExecuteUpdateAsync(x => x
                .SetProperty(a => a.State, AiPendingToolActionState.Expired)
                .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null), cancellationToken);

        // A crashed worker becomes retryable when its lease expires. The action
        // remains a tombstone until its normal terminal retention window.
        await db.AiPendingToolActions
            .Where(x => x.State == AiPendingToolActionState.Executing &&
                        x.ExecutionLeaseExpiresAtUtc <= now && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(x => x
                .SetProperty(a => a.State, AiPendingToolActionState.FailedRetryable)
                .SetProperty(a => a.ExecutionLeaseId, (Guid?)null)
                .SetProperty(a => a.ExecutionLeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(a => a.LastErrorCode, "STALE_EXECUTION_LEASE"), cancellationToken);

        await db.AiPendingToolActions
            .Where(x => ((x.State == AiPendingToolActionState.Completed || x.State == AiPendingToolActionState.Cancelled || x.State == AiPendingToolActionState.Expired) &&
                         ((x.ExecutedAtUtc.HasValue && x.ExecutedAtUtc.Value <= completedRetentionCutoff) ||
                          (x.CancelledAtUtc.HasValue && x.CancelledAtUtc.Value <= completedRetentionCutoff) ||
                          (!x.ExecutedAtUtc.HasValue && !x.CancelledAtUtc.HasValue && x.ExpiresAtUtc <= completedRetentionCutoff))) ||
                        (x.State == AiPendingToolActionState.FailedRetryable && x.ExpiresAtUtc <= completedRetentionCutoff) ||
                        (x.CancelledAtUtc.HasValue && x.CancelledAtUtc.Value <= completedRetentionCutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
