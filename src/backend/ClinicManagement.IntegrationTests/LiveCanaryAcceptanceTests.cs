using ClinicManagement.AI.LiveCanary;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI.Planning;

namespace ClinicManagement.IntegrationTests;

public sealed class LiveCanaryAcceptanceTests
{
    [Fact]
    public void Full_stack_catalog_has_six_vietnamese_actor_cases_with_explicit_scope_mapping()
    {
        var cases = FullStackCanaryCaseCatalog.Cases;

        Assert.Equal(6, cases.Count);
        Assert.Equal(
            new[]
            {
                AiActorRole.Patient,
                AiActorRole.Receptionist,
                AiActorRole.Doctor,
                AiActorRole.DiagnosticTechnician,
                AiActorRole.Pharmacist,
                AiActorRole.Admin
            },
            cases.Select(x => x.Role));
        Assert.All(cases, canaryCase =>
        {
            Assert.Contains(canaryCase.Message, character => "ăâđêôơưĂÂĐÊÔƠƯ".Contains(character));
            Assert.False(string.IsNullOrWhiteSpace(canaryCase.Route));
            Assert.False(string.IsNullOrWhiteSpace(canaryCase.ExpectedNavigationRoute));
            Assert.False(string.IsNullOrWhiteSpace(canaryCase.ExpectedToolName));
            Assert.True(
                AiRoleToolCatalog.Definitions
                    .Where(x => x.AllowedRoles.Contains(canaryCase.Role))
                    .Select(x => x.Name)
                    .Concat(canaryCase.Role == AiActorRole.Patient
                        ? ClinicManagement.Infrastructure.AI.Tools.PatientCopilotToolHandler.Definitions().Select(x => x.Name)
                        : Array.Empty<string>())
                    .Contains(canaryCase.ExpectedToolName, StringComparer.OrdinalIgnoreCase),
                $"Missing expected tool {canaryCase.ExpectedToolName} for {canaryCase.Role}.");
        });

        var expectedRoutes = new Dictionary<AiActorRole, string>
        {
            [AiActorRole.Patient] = "/patient/appointments",
            [AiActorRole.Receptionist] = "/reception/appointments",
            [AiActorRole.Doctor] = "/doctor/appointments",
            [AiActorRole.DiagnosticTechnician] = "/diagnostics",
            [AiActorRole.Pharmacist] = "/pharmacy/prescriptions",
            [AiActorRole.Admin] = "/admin"
        };
        Assert.All(cases, canaryCase => Assert.Equal(expectedRoutes[canaryCase.Role], canaryCase.Route));
    }

    [Fact]
    public void Vietnamese_cases_are_intentionally_provider_cases_not_local_fallbacks()
    {
        var planner = new AiDeterministicPlanner();

        Assert.All(FullStackCanaryCaseCatalog.Cases, canaryCase =>
        {
            var decision = planner.Plan(new AiCopilotPlanningContext
            {
                Role = canaryCase.Role,
                NormalizedMessage = AiTextNormalizer.NormalizeForComparison(canaryCase.Message),
                Analysis = new AiConversationAnalysis(),
                Resource = new AiResolvedResourceContext()
            });

            Assert.True(
                decision.RequiresProvider,
                $"{canaryCase.CaseId}: mode={decision.PlannerMode}, subIntent={decision.SubIntent}, clarification={decision.Clarification}, tool={string.Join(',', decision.ToolCalls.Select(x => x.Name))}");
            Assert.Empty(decision.ToolCalls);
        });
    }

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
    public void Budget_one_cannot_accept_six_cases_and_reports_unexecuted_cases()
    {
        var report = FullStackReport(ValidCases().Take(1).ToArray()) with
        {
            CallsBudget = 1,
            CallsPlanned = 6,
            UnexecutedCaseCount = 5,
            ProviderAttemptsExecuted = 1
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PartialDegraded, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(2, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public void Budget_six_with_retry_exhaustion_cannot_accept_missing_actors()
    {
        var cases = ValidCases().Take(4).Select((item, index) => item with { ProviderAttemptCount = index < 2 ? 1 : 2 }).ToArray();
        var report = FullStackReport(cases) with
        {
            CallsBudget = 6,
            CallsPlanned = 6,
            UnexecutedCaseCount = 2,
            ProviderAttemptsExecuted = 6
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PartialDegraded, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(2, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public void Budget_twelve_allows_six_cases_at_two_attempts_without_overflow()
    {
        var cases = ValidCases().Select(x => x with { ProviderAttemptCount = 2 }).ToArray();
        var report = FullStackReport(cases) with
        {
            CallsBudget = 12,
            ProviderAttemptsExecuted = 12
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(0, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Theory]
    [InlineData(AiProviderStatusContract.FailureAuthenticationFailed)]
    [InlineData(AiProviderStatusContract.FailureRateLimited)]
    [InlineData(AiProviderStatusContract.FailureServerError)]
    public void Provider_http_errors_are_fail_not_degraded(string failureCode)
    {
        var cases = ValidCases().ToArray();
        cases[0] = cases[0] with
        {
            ProviderState = AiProviderStatusContract.Degraded,
            FailureCode = failureCode,
            HttpSucceeded = false
        };
        var report = FullStackReport(cases);

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

    [Fact]
    public void Missing_grounded_response_is_not_full_pass()
    {
        var cases = ValidCases().ToArray();
        cases[4] = cases[4] with { GroundedResponse = false, GroundedSourcesValid = false };
        var report = FullStackReport(cases);

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
            ActorVerified = true,
            RouteVerified = true,
            ExpectedToolVerified = true,
            GroundedResponse = true,
            GroundedSourcesValid = true
        })
        .ToArray();
}
