using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
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
    }
}
