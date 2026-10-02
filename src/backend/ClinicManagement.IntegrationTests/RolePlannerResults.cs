using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;

namespace ClinicManagement.IntegrationTests;

/// <summary>Typed role-planner provider results for mocks of PlanRoleCopilotAsync.</summary>
internal static class RolePlannerResults
{
    public static AiRolePlannerProviderResult Success(
        string intent,
        decimal confidence = .9m,
        bool isClear = true,
        IReadOnlyList<AiPlannerToolCall>? toolCalls = null,
        string? reply = null,
        string? clarification = null,
        string? schemaVersion = AiRolePlannerContract.SchemaVersion) => new()
    {
        IsSuccess = true,
        Status = "Success",
        FailureCode = AiProviderStatusContract.FailureNone,
        ProviderWasCalled = true,
        ProviderAttemptCount = 1,
        CorrelationId = "0a1b2c3d",
        Output = new AiRolePlannerOutput
        {
            PlannerSchemaVersion = schemaVersion,
            PlannerConfidence = confidence,
            PrimaryIntent = intent,
            IsClear = isClear,
            Clarification = clarification,
            Reply = reply,
            ToolCalls = toolCalls ?? Array.Empty<AiPlannerToolCall>()
        }
    };

    public static AiRolePlannerProviderResult Failure(string status, string? failureCode = null) => new()
    {
        IsSuccess = false,
        Status = status,
        FailureCode = failureCode ?? AiProviderStatusContract.FailureCodeFromProviderStatus(status),
        ProviderWasCalled = true,
        ProviderAttemptCount = 1
    };
}
