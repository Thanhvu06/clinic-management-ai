using ClinicManagement.AI.LiveCanary;
using ClinicManagement.Application.AI;

namespace ClinicManagement.IntegrationTests;

public sealed class LiveCanaryAcceptanceTests
{
    [Fact]
    public void All_actor_cases_with_grounded_allowlisted_data_are_full_pass()
    {
        var report = FullStackReport(ValidCases());

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(0, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public void All_actor_errors_with_unchanged_database_are_degraded_not_live_pass()
    {
        var cases = Enumerable.Range(0, 6).Select(index => new LiveCanaryCaseResult
        {
            CaseId = $"case-{index}",
            Actor = "Synthetic",
            ExpectedCategory = "read",
            ProviderState = AiProviderStatusContract.Unavailable,
            FailureCode = AiProviderStatusContract.FailureCircuitOpen,
            HttpSucceeded = true,
            SchemaValid = true
        }).ToArray();
        var report = FullStackReport(cases) with
        {
            LiveGeminiExecuted = false,
            ProviderCallsExecuted = 0,
            ProviderAttemptsExecuted = 0
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PartialDegraded, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.NotEqual(0, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public void One_invalid_provider_schema_is_fail_even_when_database_is_unchanged()
    {
        var cases = ValidCases().ToArray();
        cases[2] = cases[2] with
        {
            SchemaValid = false,
            FailureCode = AiProviderStatusContract.FailureInvalidResponse
        };
        var report = FullStackReport(cases);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.Fail, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(3, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public void A_budget_or_attempt_count_mismatch_is_fail_closed()
    {
        var report = FullStackReport(ValidCases()) with { ProviderAttemptsExecuted = 12 };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.Fail, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(3, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public void No_provider_call_is_partial_and_never_a_live_acceptance()
    {
        var report = FullStackReport(Array.Empty<LiveCanaryCaseResult>()) with
        {
            LiveGeminiExecuted = false,
            CallsPlanned = 6,
            CallsAttempted = 0,
            HttpCanaryRequests = 0,
            ProviderCallsExecuted = 0,
            ProviderAttemptsExecuted = 0,
            UnexecutedCaseCount = 6
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PartialDegraded, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(2, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    private static LiveCanaryReport FullStackReport(IReadOnlyList<LiveCanaryCaseResult> cases) => new()
    {
        CanaryLayer = "FullStackHttp",
        CallsBudget = 12,
        CallsPlanned = cases.Count,
        CallsAttempted = cases.Count,
        HttpCanaryRequests = cases.Count,
        ProviderCallsExecuted = cases.Count(x => x.ProviderCalled),
        ProviderAttemptsExecuted = cases.Sum(x => x.ProviderAttemptCount),
        UnexecutedCaseCount = 0,
        LiveGeminiExecuted = cases.Count > 0,
        DatabaseUnchanged = true,
        Cases = cases
    };

    private static IReadOnlyList<LiveCanaryCaseResult> ValidCases() => Enumerable.Range(0, 6)
        .Select(index => new LiveCanaryCaseResult
        {
            CaseId = $"case-{index}",
            Actor = "Synthetic",
            ExpectedCategory = "read",
            ProviderState = AiProviderStatusContract.Online,
            FailureCode = AiProviderStatusContract.FailureNone,
            ProviderCalled = true,
            ProviderAttemptCount = 1,
            HttpSucceeded = true,
            SchemaValid = true,
            AllowedTools = 1,
            ToolExecutions = 1,
            ToolNames = new[] { "synthetic.read" },
            ToolScopeValid = true,
            GroundedResponse = true,
            GroundedSourcesValid = true
        })
        .ToArray();
}
