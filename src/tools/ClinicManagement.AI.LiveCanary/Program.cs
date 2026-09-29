using System.Diagnostics;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.AI.LiveCanary;

internal static class Program
{
    private const int DefaultMaxCalls = 12;
    private const int AbsoluteMaxCalls = 12;

    public static async Task<int> Main(string[] args)
    {
        var reportPath = Option(args, "--report") ?? Environment.GetEnvironmentVariable("GATE_D_REPORT_PATH") ?? "gate-d-live-canary-report.json";
        var requireLive = args.Contains("--require-live", StringComparer.OrdinalIgnoreCase);
        var configuration = BuildConfiguration();
        var options = configuration.GetSection(AiProviderOptions.SectionName).Get<AiProviderOptions>() ?? new AiProviderOptions();
        var inspector = new AiProviderConfigurationInspector(configuration, Options.Create(options));
        var snapshot = inspector.GetSnapshot();
        var report = new LiveCanaryReport
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            CommitSha = ReadCommitSha(),
            ModelName = snapshot.ModelName,
            ProviderEnabled = snapshot.IsEnabled,
            KeyConfigured = snapshot.KeyConfigured,
            Configuration = snapshot
        };

        var runFlag = IsTrue(Environment.GetEnvironmentVariable("RUN_LIVE_GEMINI_CANARY"));
        var maxCalls = ParseMaxCalls(Environment.GetEnvironmentVariable("LIVE_GEMINI_MAX_CALLS"));
        if (!runFlag)
            return await FinishSkippedAsync(report, "RUN_LIVE_GEMINI_CANARY is not true.", reportPath, requireLive);
        if (!snapshot.IsEnabled)
            return await FinishSkippedAsync(report, "AiProvider:IsEnabled is false.", reportPath, requireLive);
        if (!snapshot.KeyConfigured)
            return await FinishSkippedAsync(report, "AiProvider API key is not configured.", reportPath, requireLive);
        if (string.IsNullOrWhiteSpace(snapshot.ModelName))
            return await FinishSkippedAsync(report, "AiProvider model name is empty.", reportPath, requireLive);
        if (maxCalls == 0)
            return await FinishSkippedAsync(report, "LIVE_GEMINI_MAX_CALLS must be between 1 and 12.", reportPath, requireLive);

        if (!args.Contains("--planner-only", StringComparer.OrdinalIgnoreCase))
        {
            var fullStackReport = await new FullStackHttpCanary().RunAsync(report, maxCalls);
            return await FinishAsync(fullStackReport, reportPath, LiveCanaryAcceptanceEvaluator.ExitCode(fullStackReport, requireLive));
        }

        var budget = new CanaryProviderAttemptBudget(maxCalls);
        var cases = Cases().ToArray();
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new GeminiAiProvider(httpClient, Options.Create(options), NullLogger<GeminiAiProvider>.Instance, attemptBudget: budget);
        var health = new AiProviderHealth(
            TimeSpan.FromSeconds(snapshot.CircuitCooldownSeconds),
            snapshot.CircuitFailureThreshold);
        var planner = new GeminiStructuredPlanner(provider, health, NullLogger<GeminiStructuredPlanner>.Instance);
        var results = new List<LiveCanaryCaseResult>(cases.Length);

        foreach (var canaryCase in cases)
        {
            if (budget.Remaining == 0) break;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await planner.PlanAsync(new AiStructuredPlannerRequest
                {
                    Role = canaryCase.Role,
                    Message = canaryCase.Message,
                    ConversationId = canaryCase.CaseId,
                    LocalIntent = canaryCase.ExpectedCategory,
                    LocalConfidence = 1m,
                    Resource = new AiResolvedResourceContext { CurrentRoute = "/gate-d/synthetic" },
                    AllowedTools = ToolsFor(canaryCase.Role),
                    AllowedToolNames = ToolsFor(canaryCase.Role).Select(x => x.Name).ToArray()
                });
                stopwatch.Stop();

                var toolNames = result.Decision.ToolCalls
                    .Where(x => AiPlannerPolicy.IsAllowed(x.Name))
                    .Select(x => x.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();
                var policyViolation = string.Equals(result.FailureReason, "INVALID_PROVIDER_PLAN", StringComparison.Ordinal) ||
                                      string.Equals(result.FailureReason, "PROVIDER_RESOURCE_MISMATCH", StringComparison.Ordinal);

                results.Add(new LiveCanaryCaseResult
                {
                    CaseId = canaryCase.CaseId,
                    Actor = canaryCase.Role.ToString(),
                    ExpectedCategory = canaryCase.ExpectedCategory,
                    ActualIntent = result.Decision.Intent,
                    ToolNames = toolNames,
                    ProviderState = result.ProviderState,
                    FailureCode = result.FailureCode,
                    ProviderCalled = result.ProviderCalled,
                    ProviderAttemptCount = result.ProviderAttemptCount,
                    SchemaValid = result.IsSuccess,
                    AllowedTools = toolNames.Length,
                    Clarification = !string.IsNullOrWhiteSpace(result.Decision.Clarification),
                    PolicyViolation = policyViolation,
                    LatencyMilliseconds = stopwatch.ElapsedMilliseconds
                });
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                results.Add(new LiveCanaryCaseResult
                {
                    CaseId = canaryCase.CaseId,
                    Actor = canaryCase.Role.ToString(),
                    ExpectedCategory = canaryCase.ExpectedCategory,
                    ProviderState = AiProviderStatusContract.NotCalled,
                    FailureCode = AiProviderStatusContract.FailureClientCancelled,
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
                    ProviderState = AiProviderStatusContract.Degraded,
                    FailureCode = AiProviderStatusContract.FailureUnknown,
                    LatencyMilliseconds = stopwatch.ElapsedMilliseconds
                });
            }
        }

        var latencies = results.Select(x => x.LatencyMilliseconds).OrderBy(x => x).ToArray();
        var completedReport = report with
        {
            CanaryLayer = "PlannerOnly",
            CanaryLayers = new[] { "PlannerOnly" },
            LiveGeminiExecuted = budget.Consumed > 0,
            CallsBudget = maxCalls,
            CallsPlanned = cases.Length,
            CallsAttempted = results.Count,
            CallsExecuted = results.Count(x => x.ProviderCalled),
            ProviderCallsExecuted = results.Count(x => x.ProviderCalled),
            ProviderAttemptsExecuted = budget.Consumed,
            UnexecutedCaseCount = cases.Length - results.Count,
            HttpSuccessCount = results.Count(x => x.ProviderState == AiProviderStatusContract.Online),
            StructuredSchemaValidCount = results.Count(x => x.SchemaValid),
            AllowedToolCount = results.Sum(x => x.AllowedTools),
            ClarificationCount = results.Count(x => x.Clarification),
            DeterministicFallbackCount = results.Count(x => !x.SchemaValid),
            RateLimitCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureRateLimited),
            TimeoutCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureTimeout),
            ServerFailureCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureServerError),
            ModelFailureCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureModelUnavailable),
            AuthenticationFailureCount = results.Count(x => x.FailureCode == AiProviderStatusContract.FailureAuthenticationFailed),
            SafetyPolicyViolationCount = results.Count(x => x.PolicyViolation),
            DatabaseUnchanged = true,
            P50LatencyMilliseconds = Percentile(latencies, 0.50),
            P95LatencyMilliseconds = Percentile(latencies, 0.95),
            Cases = results
        };

        completedReport = completedReport with { AcceptanceStatus = LiveCanaryAcceptanceEvaluator.Evaluate(completedReport) };
        return await FinishAsync(completedReport, reportPath, LiveCanaryAcceptanceEvaluator.ExitCode(completedReport, requireLive));
    }

    private static IConfiguration BuildConfiguration()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
                           Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                           "Development";
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("src/backend/ClinicManagement.Api/appsettings.json", optional: true)
            .AddJsonFile($"src/backend/ClinicManagement.Api/appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static IReadOnlyList<AiToolDefinition> ToolsFor(AiActorRole role) =>
        AiRoleToolCatalog.Definitions.Where(x => x.AllowedRoles.Contains(role)).ToArray();

    private static IReadOnlyList<CanaryCase> Cases() => new[]
    {
        new CanaryCase("patient-catalog", AiActorRole.Patient, "catalog_read", "SYN_PATIENT_A muốn biết giờ mở cửa và giá khám tại SYN_FACILITY_ALPHA."),
        new CanaryCase("patient-appointments", AiActorRole.Patient, "own_read", "SYN_PATIENT_A muốn xem lịch khám của chính tài khoản synthetic."),
        new CanaryCase("reception-appointments", AiActorRole.Receptionist, "facility_appointments", "Lễ tân SYN_FACILITY_ALPHA xem lịch hôm nay trong đúng cơ sở."),
        new CanaryCase("reception-queue", AiActorRole.Receptionist, "facility_queue", "Lễ tân xem hàng đợi tiếp nhận của SYN_FACILITY_ALPHA."),
        new CanaryCase("doctor-queue", AiActorRole.Doctor, "assigned_queue", "Bác sĩ hiện tại xem hàng đợi được phân công của mình."),
        new CanaryCase("doctor-summary", AiActorRole.Doctor, "assigned_summary", "Bác sĩ xem tóm tắt lượt khám synthetic đã được phân công."),
        new CanaryCase("technician-worklist", AiActorRole.DiagnosticTechnician, "assigned_worklist", "Kỹ thuật viên xem worklist được giao trong scope hiện hành."),
        new CanaryCase("technician-order", AiActorRole.DiagnosticTechnician, "assigned_order", "Kỹ thuật viên xem chi tiết order synthetic hiện tại."),
        new CanaryCase("pharmacist-queue", AiActorRole.Pharmacist, "prescription_queue", "Dược sĩ xem hàng đợi đơn thuốc trong scope hiện hành."),
        new CanaryCase("pharmacist-payment", AiActorRole.Pharmacist, "payment_status", "Dược sĩ xem trạng thái thanh toán synthetic hiện hành."),
        new CanaryCase("admin-metrics", AiActorRole.Admin, "aggregate_metrics", "Admin xem dashboard metrics tổng hợp."),
        new CanaryCase("admin-health", AiActorRole.Admin, "sanitized_health", "Admin xem tình trạng AI đã sanitize."),
    };

    private static async Task<int> FinishAsync(LiveCanaryReport report, string path, int exitCode)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return exitCode;
    }

    private static Task<int> FinishSkippedAsync(LiveCanaryReport report, string reason, string path, bool requireLive)
    {
        var skipped = report with
        {
            LiveGeminiExecuted = false,
            SkipReason = reason,
            AcceptanceStatus = LiveCanaryAcceptanceEvaluator.PartialDegraded
        };
        return FinishAsync(skipped, path, LiveCanaryAcceptanceEvaluator.ExitCode(skipped, requireLive));
    }

    private static int ParseMaxCalls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultMaxCalls;
        return int.TryParse(value, out var parsed) && parsed is >= 1 and <= AbsoluteMaxCalls ? parsed : 0;
    }

    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static long? Percentile(long[] sorted, double percentile) =>
        sorted.Length == 0 ? null : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile) - 1)];

    private static string ReadCommitSha()
    {
        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha)) return sha[..Math.Min(sha.Length, 64)];

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return "local-unprovided";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return output.Length is >= 7 and <= 64 && output.All(Uri.IsHexDigit) ? output : "local-unprovided";
        }
        catch
        {
            return "local-unprovided";
        }
    }
}

internal sealed record CanaryCase(string CaseId, AiActorRole Role, string ExpectedCategory, string Message);

public sealed record LiveCanaryCaseResult
{
    public string CaseId { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
    public string ExpectedCategory { get; init; } = string.Empty;
    public string? ActualIntent { get; init; }
    public IReadOnlyList<string> ToolNames { get; init; } = Array.Empty<string>();
    public string ProviderState { get; init; } = AiProviderStatusContract.NotCalled;
    public string FailureCode { get; init; } = AiProviderStatusContract.FailureNone;
    public bool ProviderCalled { get; init; }
    public int ProviderAttemptCount { get; init; }
    public bool HttpSucceeded { get; init; }
    public bool SchemaValid { get; init; }
    public int AllowedTools { get; init; }
    public int ToolExecutions { get; init; }
    public bool GroundedResponse { get; init; }
    public bool GroundedSourcesValid { get; init; }
    public bool ToolScopeValid { get; init; }
    public bool ActorVerified { get; init; }
    public bool RouteVerified { get; init; }
    public bool ExpectedToolVerified { get; init; }
    public bool Clarification { get; init; }
    public bool PolicyViolation { get; init; }
    public bool ProviderPlanRejected { get; init; }
    public bool AuthorizationDenied { get; init; }
    public string? ExecutionMode { get; init; }
    public long LatencyMilliseconds { get; init; }
}

public sealed record LiveCanaryReport
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string CommitSha { get; init; } = string.Empty;
    public string CanaryVersion { get; init; } = "gate-d-v2";
    public string CanaryLayer { get; init; } = "NotRun";
    public IReadOnlyList<string> CanaryLayers { get; init; } = Array.Empty<string>();
    public string ModelName { get; init; } = string.Empty;
    public bool ProviderEnabled { get; init; }
    public bool KeyConfigured { get; init; }
    public bool LiveGeminiExecuted { get; init; }
    public string? SkipReason { get; init; }
    public string AcceptanceStatus { get; init; } = LiveCanaryAcceptanceEvaluator.PartialDegraded;
    public int CallsBudget { get; init; }
    public int CallsPlanned { get; init; }
    public int CallsAttempted { get; init; }
    public int CallsExecuted { get; init; }
    public int ProviderCallsExecuted { get; init; }
    public int ProviderAttemptsExecuted { get; init; }
    public int UnexecutedCaseCount { get; init; }
    public int HttpCanaryRequests { get; init; }
    public int HttpSuccessCount { get; init; }
    public int StructuredSchemaValidCount { get; init; }
    public int PlannerSchemaValidCount { get; init; }
    public int AllowedToolCount { get; init; }
    public int AllowedToolExecutionCount { get; init; }
    public int GroundedResponseCount { get; init; }
    public int ClarificationCount { get; init; }
    public int DeterministicFallbackCount { get; init; }
    public int ProviderPlanRejectedCount { get; init; }
    public int ApplicationAuthorizationDenialCount { get; init; }
    public int RateLimitCount { get; init; }
    public int TimeoutCount { get; init; }
    public int ServerFailureCount { get; init; }
    public int ModelFailureCount { get; init; }
    public int AuthenticationFailureCount { get; init; }
    public int SafetyPolicyViolationCount { get; init; }
    public long? P50LatencyMilliseconds { get; init; }
    public long? P95LatencyMilliseconds { get; init; }
    public string? DatabaseFingerprintBefore { get; init; }
    public string? DatabaseFingerprintAfter { get; init; }
    public bool DatabaseUnchanged { get; init; }
    public bool BrowserE2EExecuted { get; init; }
    public AiProviderConfigurationSnapshot? Configuration { get; init; }
    public IReadOnlyList<LiveCanaryCaseResult> Cases { get; init; } = Array.Empty<LiveCanaryCaseResult>();
}
