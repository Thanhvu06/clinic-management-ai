using ClinicManagement.Application.Common.Expiry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicManagement.Infrastructure.Expiry;

public sealed class ExpiryWorker(IServiceScopeFactory scopes, IOptions<ExpiryRulesOptions> options, ILogger<ExpiryWorker> logger) : BackgroundService
{
    public static readonly Type[] RuleTypes =
    [
        typeof(ChangeRequestExpiryRule), typeof(PendingAppointmentExpiryRule), typeof(ConfirmedAppointmentExpiryRule),
        typeof(WaitingVisitExpiryRule), typeof(DiagnosticOrderExpiryRule), typeof(PrescriptionExpiryRule),
        typeof(PackageRegistrationExpiryRule), typeof(RevisitExpiryRule), typeof(DoctorLeaveExpiryRule)
    ];

    public Task<int> RunOnceAsync(CancellationToken cancellationToken = default, bool throwOnFailure = false) =>
        RunRulesOnceAsync(RuleTypes, cancellationToken, throwOnFailure);

    internal async Task<int> RunRulesOnceAsync(IReadOnlyList<Type> ruleTypes, CancellationToken cancellationToken = default, bool throwOnFailure = false)
    {
        var total = 0;
        List<Exception> failures = [];
        foreach (var type in ruleTypes)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                total += await ((IExpiryRule)scope.ServiceProvider.GetRequiredService(type)).RunAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failures.Add(ex);
                logger.LogError(ex, "Quy tắc hết hạn {Rule} thất bại; các quy tắc khác tiếp tục.", type.Name);
            }
        }
        if (throwOnFailure && failures.Count > 0) throw new AggregateException(failures);
        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(options.Value.IntervalMinutes), stoppingToken);
        }
    }
}
