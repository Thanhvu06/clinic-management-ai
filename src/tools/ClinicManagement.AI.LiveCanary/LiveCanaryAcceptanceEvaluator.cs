using ClinicManagement.Application.AI;

namespace ClinicManagement.AI.LiveCanary;

public static class LiveCanaryAcceptanceEvaluator
{
    public const string PassFull = "PASS_FULL";
    public const string PartialDegraded = "PARTIAL/DEGRADED";
    public const string Fail = "FAIL";

    public static string Evaluate(LiveCanaryReport report)
    {
        if (!report.LiveGeminiExecuted && report.Cases.Count == 0 && !string.IsNullOrWhiteSpace(report.SkipReason))
            return PartialDegraded;

        if (!report.DatabaseUnchanged || report.SafetyPolicyViolationCount > 0)
            return Fail;

        if (report.CallsBudget is < 1 or > 12 ||
            report.ProviderAttemptsExecuted < 0 ||
            report.ProviderAttemptsExecuted > report.CallsBudget ||
            report.ProviderAttemptsExecuted != report.Cases.Sum(x => x.ProviderAttemptCount))
            return Fail;

        if (report.Cases.Any(IsFatalCaseFailure))
            return Fail;

        if (string.Equals(report.CanaryLayer, "FullStackHttp", StringComparison.OrdinalIgnoreCase))
        {
            var fullStackPass = report.LiveGeminiExecuted &&
                report.CallsPlanned > 0 &&
                report.UnexecutedCaseCount == 0 &&
                report.Cases.Count == report.CallsPlanned &&
                report.CallsAttempted == report.CallsPlanned &&
                report.HttpCanaryRequests == report.CallsPlanned &&
                report.ProviderCallsExecuted == report.Cases.Count(x => x.ProviderCallExpected) &&
                HasRequiredFullStackCatalog(report) &&
                report.Cases.All(IsFullStackCaseSuccess);

            return fullStackPass ? PassFull : PartialDegraded;
        }

        if (string.Equals(report.CanaryLayer, "PlannerOnly", StringComparison.OrdinalIgnoreCase))
        {
            var plannerPass = report.LiveGeminiExecuted &&
                report.Cases.Count > 0 &&
                report.UnexecutedCaseCount == 0 &&
                report.Cases.Count == report.CallsPlanned &&
                report.ProviderCallsExecuted == report.Cases.Count &&
                report.ProviderAttemptsExecuted > 0 &&
                report.Cases.All(x =>
                    x.ProviderCalled &&
                    x.ProviderAttemptCount > 0 &&
                    x.ProviderState == AiProviderStatusContract.Online &&
                    x.SchemaValid &&
                    !x.PolicyViolation &&
                    !x.ProviderPlanRejected &&
                    !x.AuthorizationDenied);

            return plannerPass ? PassFull : PartialDegraded;
        }

        return PartialDegraded;
    }

    private static bool HasRequiredFullStackCatalog(LiveCanaryReport report)
    {
        var requiredCases = FullStackCanaryCaseCatalog.Cases;
        if (report.CallsPlanned != requiredCases.Count || report.Cases.Count != requiredCases.Count)
            return false;

        var observedCases = report.Cases
            .GroupBy(x => x.CaseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        if (observedCases.Count != requiredCases.Count)
            return false;

        foreach (var required in requiredCases)
        {
            if (!observedCases.TryGetValue(required.CaseId, out var matches) || matches.Length != 1)
                return false;

            var result = matches[0];
            var expectedActor = required.Role.ToString();
            if (!string.Equals(result.Actor, expectedActor, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.ExpectedCategory, required.ExpectedCategory, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.RequestPath, required.ApiPath, StringComparison.OrdinalIgnoreCase) ||
                result.ProviderCallExpected != required.ProviderCallExpected ||
                !result.ActorVerified ||
                !result.RouteVerified)
                return false;

            if (required.ProviderCallExpected)
            {
                if (!string.Equals(result.ObservedActor, expectedActor, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(result.ObservedNavigationRoute, required.ExpectedNavigationRoute, StringComparison.OrdinalIgnoreCase) ||
                    !result.ToolNames.Contains(required.ExpectedToolName, StringComparer.OrdinalIgnoreCase) ||
                    !result.ExpectedToolVerified)
                    return false;
            }
            else if (result.ToolNames.Count != 0 || !result.ExpectedToolVerified)
            {
                return false;
            }
        }

        return true;
    }

    public static int ExitCode(LiveCanaryReport report, bool requireLive)
    {
        var status = Evaluate(report);
        if (status == PassFull)
            return 0;
        if (!requireLive && status == PartialDegraded)
            return 0;
        return status == Fail ? 3 : 2;
    }

    private static bool IsFullStackCaseSuccess(LiveCanaryCaseResult result)
    {
        var providerExecutionValid = result.ProviderCallExpected
            ? result.ProviderCalled &&
              result.ProviderAttemptCount > 0 &&
              result.ProviderState == AiProviderStatusContract.Online &&
              result.ToolExecutions > 0 &&
              result.ToolNames.Count > 0
            : !result.ProviderCalled &&
              result.ProviderAttemptCount == 0 &&
              result.ProviderState == AiProviderStatusContract.NotCalled &&
              result.ToolExecutions == 0 &&
              result.ToolNames.Count == 0;

        return result.HttpSucceeded &&
               providerExecutionValid &&
               result.FailureCode == AiProviderStatusContract.FailureNone &&
               result.SchemaValid &&
               result.ToolScopeValid &&
               result.GroundedResponse &&
               result.GroundedSourcesValid &&
               result.ActorVerified &&
               result.RouteVerified &&
               result.ExpectedToolVerified &&
               !result.Clarification &&
               !result.PolicyViolation &&
               !result.ProviderPlanRejected &&
               !result.AuthorizationDenied;
    }

    private static bool IsFatalCaseFailure(LiveCanaryCaseResult result)
    {
        if (!result.HttpSucceeded ||
            !result.SchemaValid ||
            result.PolicyViolation ||
            result.ProviderPlanRejected ||
            result.AuthorizationDenied)
            return true;

        return result.FailureCode is
            AiProviderStatusContract.FailureRateLimited or
            AiProviderStatusContract.FailureTimeout or
            AiProviderStatusContract.FailureNetworkError or
            AiProviderStatusContract.FailureServerError or
            AiProviderStatusContract.FailureAuthenticationFailed or
            AiProviderStatusContract.FailureModelUnavailable or
            AiProviderStatusContract.FailureInvalidResponse or
            AiProviderStatusContract.FailureUnknown ||
            result.FailureCode == AiProviderStatusContract.FailureAttemptBudgetExceeded && result.ProviderAttemptCount > 0;
    }
}
