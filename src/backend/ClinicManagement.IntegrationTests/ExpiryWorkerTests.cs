using ClinicManagement.Application.Common.Expiry;
using ClinicManagement.Infrastructure.Expiry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

public sealed class ExpiryWorkerTests
{
    [Fact]
    public async Task Failed_rule_is_isolated_and_every_rule_has_its_own_scope()
    {
        var services = new ServiceCollection();
        var state = new State();
        services.AddSingleton(state);
        services.AddScoped<FailingRule>();
        services.AddScoped<FollowingRule>();
        services.AddScoped<ScopeMarker>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new ExpiryWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExpiryRulesOptions()), NullLogger<ExpiryWorker>.Instance);
        Assert.Equal(1, await worker.RunRulesOnceAsync([typeof(FailingRule), typeof(FollowingRule)]));
        Assert.Equal(2, state.Scopes.Distinct().Count());
        Assert.Equal(1, state.Successes);
        await Assert.ThrowsAsync<AggregateException>(() => worker.RunRulesOnceAsync([typeof(FailingRule), typeof(FollowingRule)], throwOnFailure: true));
        Assert.Equal(2, state.Successes);
    }

    [Fact]
    public async Task Worker_runs_on_startup_without_waiting_for_ten_minute_interval()
    {
        var services = new ServiceCollection();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var type in ExpiryWorker.RuleTypes)
            services.AddScoped(type, _ => { ran.TrySetResult(); throw new InvalidOperationException("Lỗi minh họa"); });
        await using var provider = services.BuildServiceProvider();
        using var worker = new ExpiryWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExpiryRulesOptions { IntervalMinutes = 10 }), NullLogger<ExpiryWorker>.Instance);
        await worker.StartAsync(default);
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
    }

    private sealed class State { public List<Guid> Scopes { get; } = []; public int Successes { get; set; } }
    private sealed class ScopeMarker { public Guid Id { get; } = Guid.NewGuid(); }
    private sealed class FailingRule(State state, ScopeMarker scope) : IExpiryRule
    {
        public Task<int> RunAsync(CancellationToken cancellationToken = default)
        { state.Scopes.Add(scope.Id); throw new InvalidOperationException("Lỗi một quy tắc"); }
    }
    private sealed class FollowingRule(State state, ScopeMarker scope) : IExpiryRule
    {
        public Task<int> RunAsync(CancellationToken cancellationToken = default)
        { state.Scopes.Add(scope.Id); state.Successes++; return Task.FromResult(1); }
    }
}
