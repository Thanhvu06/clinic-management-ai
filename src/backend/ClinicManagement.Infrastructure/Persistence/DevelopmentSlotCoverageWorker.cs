using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

public sealed class DevelopmentSlotCoverageWorker(
    IServiceScopeFactory scopes, IDateTimeProvider clock, ILogger<DevelopmentSlotCoverageWorker> logger) : BackgroundService
{
    private static readonly TimeOnly RunTime = new(0, 5);

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DevelopmentSpecialtyCoverageSeed.EnsureAsync(db, clock, cancellationToken, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(GetDelayUntilNextRun(clock), stoppingToken);
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể bổ sung ca và slot QA; sẽ thử lại ở chu kỳ sau.");
            }
        }
    }

    internal static TimeSpan GetDelayUntilNextRun(IDateTimeProvider clock)
    {
        var nowUtc = clock.UtcNow;
        var now = clock.ConvertUtcToVietnam(nowUtc);
        var next = DateOnly.FromDateTime(now).ToDateTime(RunTime);
        if (next <= now) next = next.AddDays(1);
        return clock.ConvertVietnamToUtc(next) - nowUtc;
    }
}
