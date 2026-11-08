using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Persistence;

public sealed class DevelopmentDemoTopUpWorker(
    IServiceScopeFactory scopes, IHostEnvironment environment, IOptions<DemoTopUpOptions> options,
    ILogger<DevelopmentDemoTopUpWorker> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment()) return;
        var entered = false;
        try
        {
            if (!options.Value.Enabled) return;
            await gate.WaitAsync(cancellationToken);
            entered = true;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.OperatingHours.IsTwentyFourSeven) return;
            await TwentyFourSevenDemoTopUp.ApplyAsync(db,
                scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(), options.Value, logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Không thể bổ sung dữ liệu demo; API tiếp tục hoạt động và sẽ thử ở chu kỳ sau.");
        }
        finally { if (entered) gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment()) return;
        try
        {
            if (!options.Value.Enabled) return;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(options.Value.TopUpIntervalMinutes, 1, 1440)),
                    timeProvider ?? TimeProvider.System, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cấu hình chu kỳ bổ sung demo không khả dụng; API tiếp tục hoạt động.");
        }
    }
}
