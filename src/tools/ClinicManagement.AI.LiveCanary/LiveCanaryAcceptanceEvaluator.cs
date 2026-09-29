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
                report.ProviderCallsExecuted == report.CallsPlanned &&
                report.ProviderAttemptsExecuted > 0 &&
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

    public static int ExitCode(LiveCanaryReport report, bool requireLive)
    {
        var status = Evaluate(report);
        if (status == PassFull)
            return 0;
        if (!requireLive && status == PartialDegraded)
            return 0;
        return status == Fail ? 3 : 2;
    }

    private static bool IsFullStackCaseSuccess(LiveCanaryCaseResult result) =>
        result.HttpSucceeded &&
        result.ProviderCalled &&
        result.ProviderAttemptCount > 0 &&
        result.ProviderState == AiProviderStatusContract.Online &&
        result.FailureCode == AiProviderStatusContract.FailureNone &&
        result.SchemaValid &&
        result.ToolScopeValid &&
        result.GroundedResponse &&
        result.GroundedSourcesValid &&
        result.ToolExecutions > 0 &&
        result.ToolNames.Count > 0 &&
        !result.Clarification &&
        !result.PolicyViolation &&
        !result.ProviderPlanRejected &&
        !result.AuthorizationDenied;

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
