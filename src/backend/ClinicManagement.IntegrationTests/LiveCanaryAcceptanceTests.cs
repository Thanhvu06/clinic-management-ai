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
    public void Full_stack_catalog_has_vietnamese_actor_cases_with_explicit_scope_mapping()
    {
        var cases = FullStackCanaryCaseCatalog.Cases;

        Assert.Equal(7, cases.Count);
        Assert.Equal(
            new[]
            {
                AiActorRole.Patient,
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
        Assert.All(cases.Where(x => x.ProviderCallExpected), canaryCase =>
            Assert.Equal(CanaryNavigationExpectation.Optional, canaryCase.NavigationExpectation));

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

        var legacyPatient = Assert.Single(cases, canaryCase => canaryCase.CaseId == "patient-legacy-http-read");
        Assert.Equal("/api/v1/ai/chat", legacyPatient.ApiPath);
        Assert.False(legacyPatient.ProviderCallExpected);
        Assert.Equal(CanaryNavigationExpectation.NotApplicable, legacyPatient.NavigationExpectation);
    }

    [Fact]
    public void Vietnamese_cases_are_intentionally_provider_cases_not_local_fallbacks()
    {
        var planner = new AiDeterministicPlanner();

        Assert.All(FullStackCanaryCaseCatalog.Cases.Where(x => x.ProviderCallExpected), canaryCase =>
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
        var report = FullStackReport(CatalogValidCases());

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(0, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
    }

    [Fact]
    public async Task Full_stack_fake_provider_accepts_a_read_response_without_navigation()
    {
        using var handler = new FakeGeminiHttpHandler("online");
        var report = await new FullStackHttpCanary().RunAsync(
            new LiveCanaryReport { DatabaseUnchanged = true },
            maxCalls: 12,
            providerHandler: handler);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, report.AcceptanceStatus);
        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(report));
        Assert.Equal(6, report.ProviderCallsExecuted);
        Assert.Equal(6, report.ProviderAttemptsExecuted);
        Assert.All(
            report.Cases.Where(x => x.ProviderCallExpected),
            result =>
            {
                Assert.Null(result.ObservedNavigationRoute);
                Assert.Equal(CanaryNavigationExpectation.Optional.ToString(), result.NavigationExpectation);
                Assert.True(result.NavigationVerified);
            });
    }

    [Fact]
    public void Full_stack_cannot_accept_arbitrary_case_ids_as_the_required_actor_catalog()
    {
        var report = FullStackReport(ValidCases());

        Assert.NotEqual(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(report));
    }

    [Fact]
    public void Full_stack_cannot_accept_wrong_actor_or_tool_when_boolean_flags_claim_success()
    {
        var cases = CatalogValidCases().ToArray();
        cases[0] = cases[0] with
        {
            Actor = AiActorRole.Doctor.ToString(),
            ObservedActor = AiActorRole.Doctor.ToString(),
            ToolNames = new[] { "doctor.get_my_queue" },
            ActorVerified = true,
            ExpectedToolVerified = true
        };

        Assert.NotEqual(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(FullStackReport(cases)));
    }

    [Fact]
    public void Full_stack_cannot_accept_wrong_observed_route_when_boolean_flags_claim_success()
    {
        var cases = CatalogValidCases().ToArray();
        cases[0] = cases[0] with
        {
            ObservedNavigationRoute = "/admin",
            RouteVerified = true
        };

        Assert.NotEqual(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(FullStackReport(cases)));
    }

    [Fact]
    public void Full_stack_allows_null_navigation_for_an_optional_read_case()
    {
        var cases = CatalogValidCases().ToArray();
        cases[0] = cases[0] with
        {
            ObservedNavigationRoute = null,
            NavigationVerified = true,
            RouteVerified = true
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(FullStackReport(cases)));
    }

    [Fact]
    public void Full_stack_accepts_the_catalog_destination_for_an_optional_read_case()
    {
        var cases = CatalogValidCases().ToArray();
        cases[0] = cases[0] with
        {
            ObservedNavigationRoute = "/patient/appointments",
            NavigationVerified = true,
            RouteVerified = true
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(FullStackReport(cases)));
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("https://external.example/appointments")]
    [InlineData("malformed-route")]
    public void Full_stack_rejects_wrong_external_or_malformed_optional_navigation(string observedRoute)
    {
        var cases = CatalogValidCases().ToArray();
        cases[0] = cases[0] with
        {
            ObservedNavigationRoute = observedRoute,
            NavigationVerified = true,
            RouteVerified = true
        };

        Assert.NotEqual(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(FullStackReport(cases)));
    }

    [Fact]
    public void Required_navigation_contract_rejects_null_and_accepts_the_exact_internal_destination()
    {
        Assert.False(CanaryNavigationContract.IsValid(
            CanaryNavigationExpectation.Required,
            "/doctor/queue",
            null));
        Assert.True(CanaryNavigationContract.IsValid(
            CanaryNavigationExpectation.Required,
            "/doctor/queue",
            "/doctor/queue"));
    }

    [Fact]
    public void Full_stack_rejects_a_report_that_changes_the_server_owned_navigation_expectation()
    {
        var cases = CatalogValidCases().ToArray();
        cases[0] = cases[0] with
        {
            NavigationExpectation = CanaryNavigationExpectation.Required.ToString(),
            NavigationVerified = true,
            RouteVerified = true
        };

        Assert.NotEqual(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(FullStackReport(cases)));
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
    public void Budget_twelve_allows_six_provider_cases_at_two_attempts_without_overflow()
    {
        var cases = CatalogValidCases()
            .Select(x => x.ProviderCallExpected ? x with { ProviderAttemptCount = 2 } : x)
            .ToArray();
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

    [Fact]
    public void Legacy_patient_read_can_pass_only_with_server_derived_evidence_and_without_a_provider_call()
    {
        var cases = CatalogValidCases();
        var report = FullStackReport(cases) with
        {
            CallsPlanned = cases.Count,
            CallsAttempted = cases.Count,
            HttpCanaryRequests = cases.Count,
            ProviderCallsExecuted = cases.Count(x => x.ProviderCallExpected),
            ProviderAttemptsExecuted = cases.Sum(x => x.ProviderAttemptCount)
        };

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, LiveCanaryAcceptanceEvaluator.Evaluate(report));
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

    private static IReadOnlyList<LiveCanaryCaseResult> CatalogValidCases() => FullStackCanaryCaseCatalog.Cases
        .Select(canaryCase => new LiveCanaryCaseResult
        {
            CaseId = canaryCase.CaseId,
            Actor = canaryCase.Role.ToString(),
            ObservedActor = canaryCase.ProviderCallExpected ? canaryCase.Role.ToString() : null,
            ExpectedCategory = canaryCase.ExpectedCategory,
            RequestPath = canaryCase.ApiPath,
            RequestCurrentRoute = canaryCase.Route,
            ExpectedNavigationRoute = canaryCase.ExpectedNavigationRoute,
            NavigationExpectation = canaryCase.NavigationExpectation.ToString(),
            ObservedNavigationRoute = canaryCase.ProviderCallExpected ? canaryCase.ExpectedNavigationRoute : null,
            NavigationVerified = canaryCase.ProviderCallExpected ? true : null,
            ProviderState = canaryCase.ProviderCallExpected
                ? AiProviderStatusContract.Online
                : AiProviderStatusContract.NotCalled,
            FailureCode = AiProviderStatusContract.FailureNone,
            ProviderCalled = canaryCase.ProviderCallExpected,
            ProviderAttemptCount = canaryCase.ProviderCallExpected ? 1 : 0,
            ProviderCallExpected = canaryCase.ProviderCallExpected,
            HttpSucceeded = true,
            SchemaValid = true,
            AllowedTools = canaryCase.ProviderCallExpected ? 1 : 0,
            ToolExecutions = canaryCase.ProviderCallExpected ? 1 : 0,
            ToolNames = canaryCase.ProviderCallExpected
                ? new[] { canaryCase.ExpectedToolName }
                : Array.Empty<string>(),
            ToolScopeValid = true,
            ActorVerified = true,
            RouteVerified = canaryCase.ProviderCallExpected,
            ExpectedToolVerified = true,
            GroundedResponse = true,
            GroundedSourcesValid = true
        })
        .ToArray();
}
