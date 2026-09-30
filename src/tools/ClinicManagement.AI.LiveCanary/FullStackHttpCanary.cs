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

internal sealed class FullStackHttpCanary
{
    private const string SyntheticPassword = "Canary@12345";

    public async Task<LiveCanaryReport> RunAsync(LiveCanaryReport report, int maxCalls)
    {
        var budget = new CanaryProviderAttemptBudget(maxCalls);
        using var factory = new SyntheticCanaryFactory(budget);
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
        var schemaValid = fieldsValid &&
                          AiChatIntentTypes.IsAllowed(actualIntent) &&
                          !string.Equals(errorCode, "INVALID_PROVIDER_SCHEMA", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(errorCode, "INVALID_PROVIDER_PLAN", StringComparison.OrdinalIgnoreCase);
        return new LiveCanaryCaseResult
        {
            CaseId = canaryCase.CaseId,
            Actor = isLegacyChat ? canaryCase.Role.ToString() : actor ?? string.Empty,
            ObservedActor = actor,
            ExpectedCategory = canaryCase.ExpectedCategory,
            RequestPath = canaryCase.ApiPath,
            ObservedNavigationRoute = navigationRoute,
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
            RouteVerified = isLegacyChat || string.Equals(navigationRoute, canaryCase.ExpectedNavigationRoute, StringComparison.OrdinalIgnoreCase),
            ExpectedToolVerified = expectedToolVerified,
            GroundedResponse = isLegacyChat ? legacyEvidenceVerified : cards > 0 && sources > 0,
            GroundedSourcesValid = groundedSourcesValid,
            Clarification = !string.IsNullOrWhiteSpace(GetString(data, "clarification")),
            PolicyViolation = errorCode is not null && errorCode.Contains("POLICY", StringComparison.OrdinalIgnoreCase),
            ProviderPlanRejected = errorCode is not null && (errorCode.StartsWith("INVALID_PROVIDER", StringComparison.OrdinalIgnoreCase) || errorCode == "PROVIDER_RESOURCE_MISMATCH"),
            AuthorizationDenied = errorCode is not null && errorCode.Contains("DENIED", StringComparison.OrdinalIgnoreCase),
            ExecutionMode = executionMode,
            LatencyMilliseconds = latency
        };
    }

    private static LiveCanaryCaseResult InvalidResponse(FullStackCanaryCase canaryCase, long latency) => new()
    {
        CaseId = canaryCase.CaseId,
        Actor = canaryCase.Role.ToString(),
        ExpectedCategory = canaryCase.ExpectedCategory,
        RequestPath = canaryCase.ApiPath,
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
    bool ProviderCallExpected = true);

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
            "/patient/appointments", "patient.get_my_appointments", "/api/v1/ai/chat", false),
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

internal sealed class SyntheticCanaryFactory : WebApplicationFactory<global::Program>
{
    private readonly string _dbFilePath = Path.Combine(Path.GetTempPath(), $"clinic_gate_d_canary_{Guid.NewGuid():N}.db");
    private readonly IAiProviderAttemptBudget _attemptBudget;
    private readonly HttpMessageHandler? _providerHandler;
    private readonly string? _serverUrl;
    private readonly bool _providerEnabled;

    public SyntheticCanaryFactory(
        IAiProviderAttemptBudget attemptBudget,
        HttpMessageHandler? providerHandler = null,
        string? serverUrl = null,
        bool providerEnabled = true)
    {
        _attemptBudget = attemptBudget;
        _providerHandler = providerHandler;
        _serverUrl = serverUrl;
        _providerEnabled = providerEnabled;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (!string.IsNullOrWhiteSpace(_serverUrl))
            builder.UseKestrel().UseUrls(_serverUrl);
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
        });
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            var descriptors = services.Where(d =>
                d.ServiceType.Name.Contains("DbContextOptions", StringComparison.Ordinal) ||
                d.ServiceType == typeof(System.Data.Common.DbConnection) ||
                d.ServiceType == typeof(AppDbContext)).ToList();
            foreach (var descriptor in descriptors) services.Remove(descriptor);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 15
            }.ToString();
            using (var initConnection = new SqliteConnection(connectionString))
            {
                initConnection.Open();
                using var command = initConnection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;";
                command.ExecuteNonQuery();
            }

            using (var initDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
                initDb.Database.EnsureCreated();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
            services.AddSingleton(_attemptBudget);

            if (_providerHandler is not null)
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IAiSpecialtySuggestionProvider))
                    .ToList())
                    services.Remove(descriptor);

                services.PostConfigure<AiProviderOptions>(options =>
                {
                    options.IsEnabled = _providerEnabled;
                    options.ApiKey = "synthetic-e2e-key";
                    options.ProviderUrl = "https://synthetic-gemini.invalid";
                    options.ModelName = "synthetic-e2e-model";
                    options.TimeoutSeconds = 2;
                    options.MaxAttempts = 2;
                    options.RetryBaseDelayMilliseconds = 10;
                    options.CircuitFailureThreshold = 3;
                    options.CircuitCooldownSeconds = 1;
                });
                services.AddHttpClient<IAiSpecialtySuggestionProvider, GeminiAiProvider>()
                    .ConfigurePrimaryHttpMessageHandler(() => _providerHandler);
            }
        });
    }

    public async Task SeedAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();

        foreach (var role in new[] { RoleNames.Patient, RoleNames.Receptionist, RoleNames.Doctor, RoleNames.Admin, RoleNames.Pharmacist, RoleNames.DiagnosticTechnician })
            if (!await roleManager.RoleExistsAsync(role))
                (await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole<Guid>(role))).Succeeded.ShouldBeTrue(role);

        var facility = new Facility
        {
            Code = "CANARY-ALPHA",
            Name = "Synthetic Gate D Facility",
            Address = "Synthetic only",
            City = "Synthetic",
            Phone = "0000001000",
            IsActive = true
        };
        var secondFacility = new Facility
        {
            Code = "CANARY-BETA",
            Name = "Synthetic Gate D Secondary Facility",
            Address = "Synthetic only",
            City = "Synthetic",
            Phone = "0000001007",
            IsActive = true
        };
        db.Facilities.AddRange(facility, secondFacility);
        var specialty = new Specialty
        {
            SpecialtyCode = "CANARY-SP",
            Name = "Synthetic General Medicine",
            Description = "Synthetic canary catalog entry",
            IsActive = true,
            AiEnabled = true,
            ConsultationFee = 1m
        };
        db.Specialties.Add(specialty);
        await db.SaveChangesAsync();
        var department = new Department
        {
            FacilityId = facility.Id,
            SpecialtyId = specialty.Id,
            Code = "CANARY-DEPT",
            Name = "Synthetic Canary Department",
            DepartmentType = DepartmentType.Clinical,
            IsActive = true
        };
        var secondDepartment = new Department
        {
            FacilityId = secondFacility.Id,
            SpecialtyId = specialty.Id,
            Code = "CANARY-DEPT-BETA",
            Name = "Synthetic Secondary Department",
            DepartmentType = DepartmentType.Clinical,
            IsActive = true
        };
        db.Departments.AddRange(department, secondDepartment);
        await db.SaveChangesAsync();

        var accounts = new[]
        {
            (Email: "canary.patient@synthetic.invalid", Name: "Synthetic Patient", Phone: "0000001001", Role: RoleNames.Patient),
            (Email: "canary.reception@synthetic.invalid", Name: "Synthetic Reception", Phone: "0000001002", Role: RoleNames.Receptionist),
            (Email: "canary.doctor@synthetic.invalid", Name: "Synthetic Doctor", Phone: "0000001003", Role: RoleNames.Doctor),
            (Email: "canary.technician@synthetic.invalid", Name: "Synthetic Technician", Phone: "0000001004", Role: RoleNames.DiagnosticTechnician),
            (Email: "canary.pharmacist@synthetic.invalid", Name: "Synthetic Pharmacist", Phone: "0000001005", Role: RoleNames.Pharmacist),
            (Email: "canary.admin@synthetic.invalid", Name: "Synthetic Admin", Phone: "0000001006", Role: RoleNames.Admin)
        };
        var users = new Dictionary<string, ApplicationUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            var user = new ApplicationUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                PhoneNumber = account.Phone,
                FullName = account.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "Canary@12345");
            result.Succeeded.ShouldBeTrue(account.Email);
            (await userManager.AddToRoleAsync(user, account.Role)).Succeeded.ShouldBeTrue(account.Role);
            users[account.Role] = user;
        }

        var secondPatientUser = new ApplicationUser
        {
            UserName = "canary.patient-b@synthetic.invalid",
            Email = "canary.patient-b@synthetic.invalid",
            EmailConfirmed = true,
            PhoneNumber = "0000001008",
            FullName = "Synthetic Patient B",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        (await userManager.CreateAsync(secondPatientUser, "Canary@12345")).Succeeded.ShouldBeTrue(secondPatientUser.Email!);
        (await userManager.AddToRoleAsync(secondPatientUser, RoleNames.Patient)).Succeeded.ShouldBeTrue(RoleNames.Patient);
        users["PatientB"] = secondPatientUser;

        var secondDoctorUser = new ApplicationUser
        {
            UserName = "canary.doctor-b@synthetic.invalid",
            Email = "canary.doctor-b@synthetic.invalid",
            EmailConfirmed = true,
            PhoneNumber = "0000001009",
            FullName = "Synthetic Doctor B",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        (await userManager.CreateAsync(secondDoctorUser, "Canary@12345")).Succeeded.ShouldBeTrue(secondDoctorUser.Email!);
        (await userManager.AddToRoleAsync(secondDoctorUser, RoleNames.Doctor)).Succeeded.ShouldBeTrue(RoleNames.Doctor);
        users["DoctorB"] = secondDoctorUser;

        var doctor = new Doctor
        {
            UserId = users[RoleNames.Doctor].Id,
            AcademicTitle = "Synthetic MD",
            ExperienceYears = 1,
            Description = "Synthetic canary doctor",
            IsActive = true
        };
        var secondDoctor = new Doctor
        {
            UserId = users["DoctorB"].Id,
            AcademicTitle = "Synthetic MD B",
            ExperienceYears = 1,
            Description = "Synthetic secondary facility doctor",
            IsActive = true
        };
        db.Doctors.AddRange(doctor, secondDoctor);
        var patient = new Patient
        {
            UserId = users[RoleNames.Patient].Id,
            MedicalRecordNumber = "CANARY-MRN-001",
            FullName = "Synthetic Patient",
            PhoneNumber = "0000001001",
            Email = accounts[0].Email,
            Gender = Gender.Other,
            PrimaryFacilityId = facility.Id
        };
        db.Patients.Add(patient);
        var secondPatient = new Patient
        {
            UserId = users["PatientB"].Id,
            MedicalRecordNumber = "CANARY-MRN-002",
            FullName = "Synthetic Patient B",
            PhoneNumber = "0000001008",
            Email = "canary.patient-b@synthetic.invalid",
            Gender = Gender.Other,
            PrimaryFacilityId = secondFacility.Id
        };
        db.Patients.Add(secondPatient);
        await db.SaveChangesAsync();
        db.DoctorSpecialties.AddRange(
            new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialty.Id, IsPrimary = true },
            new DoctorSpecialty { DoctorId = secondDoctor.Id, SpecialtyId = specialty.Id, IsPrimary = true });
        foreach (var role in new[] { RoleNames.Receptionist, RoleNames.Doctor, RoleNames.DiagnosticTechnician, RoleNames.Pharmacist })
            db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
            {
                UserId = users[role].Id,
                FacilityId = facility.Id,
                DepartmentId = department.Id,
                Role = role,
                IsPrimary = true,
                IsActive = true
            });
        db.StaffFacilityAssignments.Add(new StaffFacilityAssignment
        {
            UserId = users["DoctorB"].Id,
            FacilityId = secondFacility.Id,
            DepartmentId = secondDepartment.Id,
            Role = RoleNames.Doctor,
            IsPrimary = true,
            IsActive = true
        });
        await db.SaveChangesAsync();

        // Keep every actor case grounded in the same synthetic encounter. The
        // rows are read-only canary fixtures and are fingerprinted before and
        // after the HTTP run; no real patient or production database is used.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var now = DateTime.UtcNow;
        var slot = new AppointmentSlot
        {
            DoctorId = doctor.Id,
            SlotDate = today,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(slot);
        var diagnosticService = new DiagnosticService
        {
            Code = "CANARY-LAB-001",
            Name = "Synthetic blood test",
            Category = DiagnosticCategory.Laboratory,
            PreparationInstructions = "Synthetic canary only",
            Price = 1m,
            IsActive = true
        };
        db.DiagnosticServices.Add(diagnosticService);
        var medicine = new Medicine
        {
            Code = "CANARY-MED-001",
            Name = "Synthetic medicine",
            Unit = "tablet",
            StockQuantity = 100,
            ReorderLevel = 10,
            UnitPrice = 1m,
            IsActive = true
        };
        db.Medicines.Add(medicine);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            AppointmentCode = "CANARY-APT-001",
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            SpecialtyId = specialty.Id,
            FacilityId = facility.Id,
            AppointmentSlotId = slot.Id,
            AppointmentDate = today,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Reason = "Synthetic canary read fixture",
            Status = AppointmentStatus.Confirmed
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var secondSlot = new AppointmentSlot
        {
            DoctorId = secondDoctor.Id,
            SlotDate = today,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(10, 30),
            IsBooked = true
        };
        db.AppointmentSlots.Add(secondSlot);
        await db.SaveChangesAsync();
        db.Appointments.Add(new Appointment
        {
            AppointmentCode = "CANARY-APT-002",
            PatientId = secondPatient.Id,
            DoctorId = secondDoctor.Id,
            SpecialtyId = specialty.Id,
            FacilityId = secondFacility.Id,
            AppointmentSlotId = secondSlot.Id,
            AppointmentDate = today,
            StartTime = secondSlot.StartTime,
            EndTime = secondSlot.EndTime,
            Reason = "Synthetic secondary facility isolation fixture",
            Status = AppointmentStatus.Confirmed
        });
        await db.SaveChangesAsync();

        var visit = new PatientVisit
        {
            VisitCode = "CANARY-VIS-001",
            PatientId = patient.Id,
            AppointmentId = appointment.Id,
            FacilityId = facility.Id,
            DepartmentId = department.Id,
            AssignedDoctorId = doctor.Id,
            VisitDate = today,
            ArrivalType = VisitArrivalType.Scheduled,
            Priority = VisitPriority.Normal,
            ChiefComplaint = "Synthetic canary read fixture",
            QueueNumber = 1,
            Status = VisitStatus.WaitingForDoctor,
            CreatedByUserId = users[RoleNames.Receptionist].Id,
            CheckedInAtUtc = now,
            CreatedAtUtc = now
        };
        db.PatientVisits.Add(visit);
        await db.SaveChangesAsync();

        var diagnosticOrder = new DiagnosticOrder
        {
            OrderCode = "CANARY-ORD-001",
            AppointmentId = appointment.Id,
            PatientVisitId = visit.Id,
            PatientId = patient.Id,
            OrderingDoctorId = doctor.Id,
            FacilityId = facility.Id,
            PerformingDepartmentId = department.Id,
            ClinicalIndication = "Synthetic canary read fixture",
            Status = DiagnosticOrderStatus.Ordered,
            OrderedAtUtc = now
        };
        diagnosticOrder.Items.Add(new DiagnosticOrderItem
        {
            DiagnosticServiceId = diagnosticService.Id,
            Status = DiagnosticItemStatus.Ordered,
            IsPackageCovered = false
        });
        db.DiagnosticOrders.Add(diagnosticOrder);

        var prescription = new Prescription
        {
            AppointmentId = appointment.Id,
            PatientVisitId = visit.Id,
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            Status = PrescriptionStatus.Issued,
            Notes = "Synthetic canary read fixture",
            CreatedAt = now
        };
        prescription.Items.Add(new PrescriptionItem
        {
            MedicineId = medicine.Id,
            Quantity = 1,
            Dosage = "1 tablet",
            Frequency = "once daily",
            DurationDays = 1,
            Instructions = "Synthetic canary only"
        });
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();
    }

    public async Task<string> BusinessFingerprintAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshot = new
        {
            appointments = await db.Appointments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.FacilityId }).ToListAsync(),
            visits = await db.PatientVisits.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.FacilityId }).ToListAsync(),
            diagnosticOrders = await db.DiagnosticOrders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.FacilityId }).ToListAsync(),
            diagnosticResults = await db.DiagnosticResults.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.DiagnosticOrderItemId }).ToListAsync(),
            prescriptions = await db.Prescriptions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.PatientId }).ToListAsync(),
            invoices = await db.Invoices.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.PatientVisitId }).ToListAsync(),
            payments = await db.Payments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.InvoiceId }).ToListAsync(),
            medicines = await db.Medicines.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StockQuantity }).ToListAsync(),
            stockTransactions = await db.MedicineStockTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.MedicineId, x.QuantityChange }).ToListAsync(),
            pendingActions = await db.AiPendingToolActions.AsNoTracking().OrderBy(x => x.ActionId).Select(x => new { x.ActionId, x.State, x.ResourceType, x.ResourceId }).ToListAsync()
        };
        var canonical = JsonSerializer.Serialize(snapshot);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { _dbFilePath, $"{_dbFilePath}-shm", $"{_dbFilePath}-wal" })
                if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup of synthetic canary state only.
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
