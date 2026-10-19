using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.AI.LiveCanary;

internal sealed class FullStackHttpCanary(bool roleIntentEnabled = true)
{
    private const string SyntheticPassword = "Canary@12345";

    public async Task<LiveCanaryReport> RunAsync(
        LiveCanaryReport report,
        int maxCalls,
        HttpMessageHandler? providerHandler = null)
    {
        var budget = new CanaryProviderAttemptBudget(maxCalls);
        using var factory = new SyntheticCanaryFactory(budget, providerHandler, roleIntentEnabled: roleIntentEnabled);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedAsync();

        var before = await factory.BusinessFingerprintAsync();
        var cases = Cases().ToArray();
        var results = new List<LiveCanaryCaseResult>(cases.Length);

        foreach (var canaryCase in cases)
        {
            if (budget.Remaining == 0) break;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await AuthenticateAsync(client, canaryCase.Email);
                var request = new
                {
                    message = canaryCase.Message,
                    conversationId = $"conv_gate_d_{canaryCase.CaseId}",
                    sessionId = $"sess_gate_d_{canaryCase.CaseId}",
                    currentRoute = canaryCase.Route,
                    locale = "vi-VN",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                using var response = await client.PostAsJsonAsync(canaryCase.ApiPath, request);
                stopwatch.Stop();
                results.Add(await ReadCaseResultAsync(canaryCase, response, stopwatch.ElapsedMilliseconds));
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                results.Add(new LiveCanaryCaseResult
                {
                    CaseId = canaryCase.CaseId,
                    Actor = canaryCase.Role.ToString(),
                    ExpectedCategory = canaryCase.ExpectedCategory,
                    RequestPath = canaryCase.ApiPath,
                    RequestCurrentRoute = canaryCase.Route,
                    ExpectedNavigationRoute = canaryCase.ExpectedNavigationRoute,
                    NavigationExpectation = canaryCase.NavigationExpectation.ToString(),
                    ProviderCallExpected = canaryCase.ProviderCallExpected,
                    ProviderState = AiProviderStatusContract.NotCalled,
                    FailureCode = AiProviderStatusContract.FailureClientCancelled,
                    HttpSucceeded = false,
                    LatencyMilliseconds = stopwatch.ElapsedMilliseconds
                });
            }
            catch
            {
                stopwatch.Stop();
                results.Add(new LiveCanaryCaseResult
                {
                    CaseId = canaryCase.CaseId,
                    Actor = canaryCase.Role.ToString(),
                    ExpectedCategory = canaryCase.ExpectedCategory,
                    RequestPath = canaryCase.ApiPath,
                    RequestCurrentRoute = canaryCase.Route,
                    ExpectedNavigationRoute = canaryCase.ExpectedNavigationRoute,
                    NavigationExpectation = canaryCase.NavigationExpectation.ToString(),
                    ProviderCallExpected = canaryCase.ProviderCallExpected,
                    ProviderState = AiProviderStatusContract.Degraded,
                    FailureCode = AiProviderStatusContract.FailureUnknown,
                    HttpSucceeded = false,
                    LatencyMilliseconds = stopwatch.ElapsedMilliseconds
                });
            }
        }

        var after = await factory.BusinessFingerprintAsync();
        var latencies = results.Select(x => x.LatencyMilliseconds).OrderBy(x => x).ToArray();
        var completed = report with
        {
            CanaryLayer = "FullStackHttp",
            CanaryLayers = new[] { "FullStackHttp", "AuthenticatedHttp", "RealOrchestrator", "ReadOnlyToolGateway" },
            LiveGeminiExecuted = budget.Consumed > 0,
            CallsBudget = maxCalls,
            CallsPlanned = cases.Length,
            CallsAttempted = results.Count,
            CallsExecuted = results.Count,
            ProviderCallsExecuted = results.Count(x => x.ProviderCalled),
            ProviderAttemptsExecuted = budget.Consumed,
            UnexecutedCaseCount = cases.Length - results.Count,
            HttpCanaryRequests = results.Count,
            HttpSuccessCount = results.Count(x => x.HttpSucceeded),
            StructuredSchemaValidCount = results.Count(x => x.SchemaValid),
            PlannerSchemaValidCount = results.Count(x => x.SchemaValid),
            AllowedToolCount = results.Sum(x => x.AllowedTools),
            AllowedToolExecutionCount = results.Sum(x => x.ToolExecutions),
            GroundedResponseCount = results.Count(x => x.GroundedResponse),
            ClarificationCount = results.Count(x => x.Clarification),
            DeterministicFallbackCount = results.Count(x => !x.ProviderCalled),
            ProviderPlanRejectedCount = results.Count(x => x.ProviderPlanRejected),
            ApplicationAuthorizationDenialCount = results.Count(x => x.AuthorizationDenied),
            RateLimitCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureRateLimited),
            TimeoutCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureTimeout),
            ServerFailureCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureServerError),
            ModelFailureCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureModelUnavailable),
            AuthenticationFailureCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureAuthenticationFailed),
            SafetyPolicyViolationCount = results.Count(x => x.PolicyViolation),
            DatabaseFingerprintBefore = before,
            DatabaseFingerprintAfter = after,
            DatabaseUnchanged = string.Equals(before, after, StringComparison.Ordinal),
            P50LatencyMilliseconds = Percentile(latencies, .50),
            P95LatencyMilliseconds = Percentile(latencies, .95),
            Cases = results
        };
        return completed with { AcceptanceStatus = LiveCanaryAcceptanceEvaluator.Evaluate(completed) };
    }

    private static async Task AuthenticateAsync(HttpClient client, string email)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            emailOrPhone = email,
            password = SyntheticPassword
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("data").GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Synthetic canary login returned no token.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<LiveCanaryCaseResult> ReadCaseResultAsync(FullStackCanaryCase canaryCase, HttpResponseMessage response, long latency)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new LiveCanaryCaseResult
            {
                CaseId = canaryCase.CaseId,
                Actor = canaryCase.Role.ToString(),
                ExpectedCategory = canaryCase.ExpectedCategory,
                RequestPath = canaryCase.ApiPath,
                RequestCurrentRoute = canaryCase.Route,
                ExpectedNavigationRoute = canaryCase.ExpectedNavigationRoute,
                NavigationExpectation = canaryCase.NavigationExpectation.ToString(),
                ProviderCallExpected = canaryCase.ProviderCallExpected,
                ProviderState = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? AiProviderStatusContract.NotCalled
                    : AiProviderStatusContract.Degraded,
                FailureCode = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiProviderStatusContract.FailureAuthenticationFailed,
                    HttpStatusCode.TooManyRequests => AiProviderStatusContract.FailureRateLimited,
                    HttpStatusCode.RequestTimeout => AiProviderStatusContract.FailureTimeout,
                    >= HttpStatusCode.InternalServerError => AiProviderStatusContract.FailureServerError,
                    _ => AiProviderStatusContract.FailureUnknown
                },
                AuthorizationDenied = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                HttpSucceeded = false,
                LatencyMilliseconds = latency
            };
        }

        JsonElement payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        catch (JsonException)
        {
            return InvalidResponse(canaryCase, latency);
        }

        if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return InvalidResponse(canaryCase, latency);

        var isLegacyChat = string.Equals(canaryCase.ApiPath, "/api/v1/ai/chat", StringComparison.OrdinalIgnoreCase);
        var providerCalled = false;
        string? providerState = null;
        string? failureCode = null;
        string? actualIntent = null;
        string? executionMode = null;
        string? actor = null;
        string? navigationRoute = null;
        var providerAttemptCount = 0;
        var availableToolsElement = default(JsonElement);
        var executedToolNamesElement = default(JsonElement);
        var cardsElement = default(JsonElement);
        var sourcesElement = default(JsonElement);
        var fieldsValid = TryGetBool(data, "providerWasCalled", out providerCalled) &&
                          TryGetString(data, "providerFailureCode", out failureCode) &&
                          TryGetString(data, "executionMode", out executionMode) &&
                          TryGetNonNegativeInt(data, "providerAttemptCount", out providerAttemptCount);
        if (isLegacyChat)
        {
            fieldsValid &= TryGetString(data, "primaryIntent", out actualIntent);
            providerState = GetString(data, "providerState") ?? GetString(data, "providerStatus");
        }
        else
        {
            fieldsValid &= TryGetString(data, "providerState", out providerState) &&
                           TryGetString(data, "intent", out actualIntent) &&
                           TryGetArray(data, "availableTools", out availableToolsElement) &&
                           TryGetArray(data, "executedToolNames", out executedToolNamesElement) &&
                           TryGetArray(data, "cards", out cardsElement) &&
                           TryGetArray(data, "sources", out sourcesElement);
        }

        providerState ??= AiProviderStatusContract.NotCalled;
        failureCode ??= AiProviderStatusContract.FailureUnknown;
        actualIntent ??= string.Empty;
        executionMode ??= string.Empty;
        var errorCode = GetString(data, "errorCode");
        actor = GetString(data, "role");
        navigationRoute = GetString(data, "navigationRoute");
        var navigationVerified = CanaryNavigationContract.IsValid(
            canaryCase.NavigationExpectation,
            canaryCase.ExpectedNavigationRoute,
            navigationRoute);
        var cards = cardsElement.ValueKind == JsonValueKind.Array ? cardsElement.GetArrayLength() : 0;
        var sources = sourcesElement.ValueKind == JsonValueKind.Array ? sourcesElement.GetArrayLength() : 0;
        var toolNames = ReadStringArray(executedToolNamesElement);
        var availableToolNames = ReadToolNames(availableToolsElement);
        var allowedDefinitions = AiRoleToolCatalog.Definitions
            .Where(x => x.AllowedRoles.Contains(canaryCase.Role))
            .Concat(canaryCase.Role == AiActorRole.Patient
                ? ClinicManagement.Infrastructure.AI.Tools.PatientCopilotToolHandler.Definitions()
                    .Where(x => x.Name.StartsWith("patient.get_", StringComparison.OrdinalIgnoreCase) || x.AccessMode == AiToolAccessMode.Public)
                : Array.Empty<AiToolDefinition>());
        var allowedNames = allowedDefinitions
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toolScopeValid = isLegacyChat || (availableToolNames.Count > 0 &&
                             availableToolNames.All(allowedNames.Contains) &&
                             toolNames.All(allowedNames.Contains));
        var legacyEvidenceVerified = isLegacyChat &&
            GetString(data, "message") is { } legacyMessage &&
            legacyMessage.Contains("CANARY-APT-001", StringComparison.Ordinal) &&
            data.TryGetProperty("actions", out var actions) &&
            actions.ValueKind == JsonValueKind.Array &&
            actions.EnumerateArray().Any(action =>
                action.TryGetProperty("payload", out var payloadElement) &&
                payloadElement.ValueKind == JsonValueKind.Object &&
                payloadElement.TryGetProperty("appointmentCode", out var code) &&
                string.Equals(code.GetString(), "CANARY-APT-001", StringComparison.Ordinal));
        var expectedToolVerified = isLegacyChat
            ? legacyEvidenceVerified
            : toolNames.Contains(canaryCase.ExpectedToolName, StringComparer.OrdinalIgnoreCase);
        var groundedSourcesValid = isLegacyChat ? legacyEvidenceVerified : HasVerifiedSources(sourcesElement);
        var diagnostic = CanaryDiagnosticSanitizer.FromResponse(data, allowedNames);
        // Diagnostics can only make a case stricter: an envelope, JSON or
        // planner-schema rejection is never counted as schema-valid.
        var schemaValid = fieldsValid &&
                          AiChatIntentTypes.IsAllowed(actualIntent) &&
                          !string.Equals(errorCode, "INVALID_PROVIDER_SCHEMA", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(errorCode, "INVALID_PROVIDER_PLAN", StringComparison.OrdinalIgnoreCase) &&
                          !diagnostic.IsPlannerSchemaOrJson;
        return new LiveCanaryCaseResult
        {
            CaseId = canaryCase.CaseId,
            Actor = isLegacyChat ? canaryCase.Role.ToString() : actor ?? string.Empty,
            ObservedActor = actor,
            ExpectedCategory = canaryCase.ExpectedCategory,
            RequestPath = canaryCase.ApiPath,
            RequestCurrentRoute = canaryCase.Route,
            ExpectedNavigationRoute = canaryCase.ExpectedNavigationRoute,
            NavigationExpectation = canaryCase.NavigationExpectation.ToString(),
            ObservedNavigationRoute = navigationRoute,
            NavigationVerified = canaryCase.NavigationExpectation == CanaryNavigationExpectation.NotApplicable
                ? null
                : navigationVerified,
            ActualIntent = actualIntent,
            ToolNames = toolNames,
            ProviderState = providerState,
            FailureCode = failureCode,
            ProviderCalled = providerCalled,
            ProviderAttemptCount = providerAttemptCount,
            ProviderCallExpected = canaryCase.ProviderCallExpected,
            HttpSucceeded = true,
            SchemaValid = schemaValid,
            AllowedTools = availableToolNames.Count,
            ToolExecutions = toolNames.Count,
            ToolScopeValid = toolScopeValid,
            ActorVerified = isLegacyChat || string.Equals(actor, canaryCase.Role.ToString(), StringComparison.OrdinalIgnoreCase),
            RouteVerified = canaryCase.NavigationExpectation != CanaryNavigationExpectation.NotApplicable && navigationVerified,
            ExpectedToolVerified = expectedToolVerified,
            GroundedResponse = isLegacyChat ? legacyEvidenceVerified : cards > 0 && sources > 0,
            GroundedSourcesValid = groundedSourcesValid,
            Clarification = !string.IsNullOrWhiteSpace(GetString(data, "clarification")),
            PolicyViolation = errorCode is not null && errorCode.Contains("POLICY", StringComparison.OrdinalIgnoreCase),
            ProviderPlanRejected = errorCode is not null && (errorCode.StartsWith("INVALID_PROVIDER", StringComparison.OrdinalIgnoreCase) || errorCode == "PROVIDER_RESOURCE_MISMATCH") ||
                                   providerCalled && diagnostic.IsToolPlanOrBinding,
            AuthorizationDenied = errorCode is not null && errorCode.Contains("DENIED", StringComparison.OrdinalIgnoreCase),
            ExecutionMode = executionMode,
            LatencyMilliseconds = latency,
            ApplicationErrorCode = diagnostic.ApplicationErrorCode,
            ValidationStage = diagnostic.ValidationStage,
            ValidationReason = diagnostic.ValidationReason,
            ProviderFinishReason = diagnostic.FinishReason,
            CorrelationId = diagnostic.CorrelationId,
            RejectedToolIndex = diagnostic.RejectedToolIndex,
            RejectedPlanToolCount = diagnostic.ToolCount,
            RejectedToolName = diagnostic.RejectedToolName,
            ProviderHttpStatus = diagnostic.ProviderHttpStatus,
            ProviderErrorStatus = diagnostic.ProviderErrorStatus,
            ProviderRejectedRequestPart = diagnostic.ProviderRejectedRequestPart,
            ProviderRejectionKind = diagnostic.ProviderRejectionKind,
            ProviderRejectedName = diagnostic.ProviderRejectedName,
            ProviderRejectedFieldPath = diagnostic.ProviderRejectedFieldPath,
            RequestSchemaSizeBytes = diagnostic.RequestSchemaSizeBytes,
            RequestSchemaToolBranches = diagnostic.RequestSchemaToolBranches,
            RequestSchemaMaxDepth = diagnostic.RequestSchemaMaxDepth
        };
    }

    private static LiveCanaryCaseResult InvalidResponse(FullStackCanaryCase canaryCase, long latency) => new()
    {
        CaseId = canaryCase.CaseId,
        Actor = canaryCase.Role.ToString(),
        ExpectedCategory = canaryCase.ExpectedCategory,
        RequestPath = canaryCase.ApiPath,
        RequestCurrentRoute = canaryCase.Route,
        ExpectedNavigationRoute = canaryCase.ExpectedNavigationRoute,
        NavigationExpectation = canaryCase.NavigationExpectation.ToString(),
        ProviderCallExpected = canaryCase.ProviderCallExpected,
        ProviderState = AiProviderStatusContract.Degraded,
        FailureCode = AiProviderStatusContract.FailureInvalidResponse,
        HttpSucceeded = true,
        LatencyMilliseconds = latency
    };

    private static IReadOnlyList<FullStackCanaryCase> Cases() => FullStackCanaryCaseCatalog.Cases;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        value = GetString(element, name);
        return value is not null;
    }

    private static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetNonNegativeInt(JsonElement element, string name, out int value)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value) && value >= 0)
            return true;

        value = 0;
        return false;
    }

    private static bool TryGetArray(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
            return true;

        value = default;
        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadToolNames(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            .Select(x => x.GetProperty("name").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasVerifiedSources(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) return false;
        foreach (var source in value.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
                !source.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(kind.GetString()))
                return false;

            if (!source.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.String ||
                !string.Equals(status.GetString(), "verified", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static int GetArrayLength(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    private static long? Percentile(long[] sorted, double percentile) =>
        sorted.Length == 0 ? null : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile) - 1)];
}

public sealed record FullStackCanaryCase(
    string CaseId,
    string Email,
    AiActorRole Role,
    string ExpectedCategory,
    string Message,
    string Route,
    string ExpectedNavigationRoute,
    string ExpectedToolName,
    string ApiPath = "/api/v1/ai/copilot/chat",
    bool ProviderCallExpected = true,
    CanaryNavigationExpectation NavigationExpectation = CanaryNavigationExpectation.Optional);

public enum CanaryNavigationExpectation
{
    NotApplicable,
    Optional,
    Required
}

public static class CanaryNavigationContract
{
    public static bool IsValid(
        CanaryNavigationExpectation expectation,
        string? expectedDestination,
        string? observedNavigationRoute)
    {
        if (expectation == CanaryNavigationExpectation.NotApplicable)
            return observedNavigationRoute is null;

        if (observedNavigationRoute is null)
            return expectation == CanaryNavigationExpectation.Optional;

        return IsInternalRoute(expectedDestination) &&
               string.Equals(observedNavigationRoute, expectedDestination, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternalRoute(string? route) =>
        !string.IsNullOrWhiteSpace(route) &&
        route.StartsWith("/", StringComparison.Ordinal) &&
        !route.Contains("://", StringComparison.Ordinal);
}

public static class FullStackCanaryCaseCatalog
{
    public static IReadOnlyList<FullStackCanaryCase> Cases { get; } = new[]
    {
        new FullStackCanaryCase(
            "patient-http-read", "canary.patient@synthetic.invalid", AiActorRole.Patient, "patient_read",
            "Các buổi khám tôi đã đặt trong thời gian tới có những gì?", "/patient/appointments",
            "/patient/appointments", "patient.get_my_appointments"),
        new FullStackCanaryCase(
            "patient-legacy-http-read", "canary.patient@synthetic.invalid", AiActorRole.Patient, "patient_legacy_read",
            "Mở các cuộc hẹn sắp tới gắn với tài khoản của tôi.", "/patient/appointments",
            "/patient/appointments", "patient.get_my_appointments", "/api/v1/ai/chat", false,
            CanaryNavigationExpectation.NotApplicable),
        new FullStackCanaryCase(
            "reception-http-read", "canary.reception@synthetic.invalid", AiActorRole.Receptionist, "reception_read",
            "Hôm nay quầy tiếp đón có những lượt hẹn nào?", "/reception/appointments",
            "/reception/appointments", "reception.get_today_appointments"),
        new FullStackCanaryCase(
            "doctor-http-read", "canary.doctor@synthetic.invalid", AiActorRole.Doctor, "doctor_read",
            "Hôm nay tôi có những lượt đang chờ được phân công nào?", "/doctor/appointments",
            "/doctor/queue", "doctor.get_my_queue"),
        new FullStackCanaryCase(
            "technician-http-read", "canary.technician@synthetic.invalid", AiActorRole.DiagnosticTechnician, "technician_read",
            "Các yêu cầu xét nghiệm đang nằm trong hàng đợi của tôi là gì?", "/diagnostics",
            "/diagnostics", "technician.get_worklist"),
        new FullStackCanaryCase(
            "pharmacist-http-read", "canary.pharmacist@synthetic.invalid", AiActorRole.Pharmacist, "pharmacy_read",
            "Những toa đang chờ xử lý ở quầy thuốc gồm những toa nào?", "/pharmacy/prescriptions",
            "/pharmacy/prescriptions", "pharmacist.get_prescription_queue"),
        new FullStackCanaryCase(
            "admin-http-read", "canary.admin@synthetic.invalid", AiActorRole.Admin, "admin_read",
            "Tình hình vận hành hôm nay của hệ thống thế nào?", "/admin",
            "/admin", "admin.get_dashboard_metrics")
    };
}

internal sealed class CanaryProviderAttemptBudget : IAiProviderAttemptBudget
{
    private int _remaining;
    private int _consumed;

    public CanaryProviderAttemptBudget(int budget)
    {
        if (budget is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(budget));
        _remaining = budget;
    }

    public int Consumed => Volatile.Read(ref _consumed);
    public int Remaining => Volatile.Read(ref _remaining);

    public bool TryReserveAttempt()
    {
        while (true)
        {
            var remaining = Volatile.Read(ref _remaining);
            if (remaining <= 0) return false;
            if (Interlocked.CompareExchange(ref _remaining, remaining - 1, remaining) != remaining) continue;
            Interlocked.Increment(ref _consumed);
            return true;
        }
    }
}

internal static class CanarySeedAssertions
{
    public static void ShouldBeTrue(this bool value, string context)
    {
        if (!value) throw new InvalidOperationException($"Synthetic canary seed failed: {context}");
    }
}
