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

    public AiSessionCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<AiSessionCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRunDelay, stoppingToken);
        using var timer = new PeriodicTimer(RunInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
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
