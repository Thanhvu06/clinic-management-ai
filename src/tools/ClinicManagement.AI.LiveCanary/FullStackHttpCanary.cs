using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Identity;
using ClinicManagement.Infrastructure.Persistence;
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
        using var factory = new SyntheticCanaryFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedAsync();

        var before = await factory.BusinessFingerprintAsync();
        var cases = Cases().Take(Math.Min(6, maxCalls)).ToArray();
        var results = new List<LiveCanaryCaseResult>(cases.Length);

        foreach (var canaryCase in cases)
        {
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
                using var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", request);
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

        var after = await factory.BusinessFingerprintAsync();
        var latencies = results.Select(x => x.LatencyMilliseconds).OrderBy(x => x).ToArray();
        return report with
        {
            CanaryLayer = "FullStackHttp",
            CanaryLayers = new[] { "FullStackHttp", "AuthenticatedHttp", "RealOrchestrator", "ReadOnlyToolGateway" },
            LiveGeminiExecuted = results.Any(x => x.ProviderCalled),
            CallsBudget = maxCalls,
            CallsPlanned = cases.Length,
            CallsAttempted = results.Count,
            CallsExecuted = results.Count(x => x.ProviderCalled),
            ProviderCallsExecuted = results.Count(x => x.ProviderCalled),
            HttpCanaryRequests = results.Count,
            HttpSuccessCount = results.Count(x => x.ProviderState == AiProviderStatusContract.Online),
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

    private static async Task<LiveCanaryCaseResult> ReadCaseResultAsync(CanaryHttpCase canaryCase, HttpResponseMessage response, long latency)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new LiveCanaryCaseResult
            {
                CaseId = canaryCase.CaseId,
                Actor = canaryCase.Role.ToString(),
                ExpectedCategory = canaryCase.ExpectedCategory,
                ProviderState = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? AiProviderStatusContract.NotCalled
                    : AiProviderStatusContract.Degraded,
                FailureCode = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => AiProviderStatusContract.FailureAuthenticationFailed,
                    HttpStatusCode.TooManyRequests => AiProviderStatusContract.FailureRateLimited,
                    HttpStatusCode.RequestTimeout => AiProviderStatusContract.FailureTimeout,
                    >= HttpStatusCode.InternalServerError => AiProviderStatusContract.FailureServerError,
                    _ => AiProviderStatusContract.FailureUnknown
                },
                AuthorizationDenied = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                LatencyMilliseconds = latency
            };
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return new LiveCanaryCaseResult
            {
                CaseId = canaryCase.CaseId,
                Actor = canaryCase.Role.ToString(),
                ExpectedCategory = canaryCase.ExpectedCategory,
                ProviderState = AiProviderStatusContract.Degraded,
                FailureCode = AiProviderStatusContract.FailureInvalidResponse,
                LatencyMilliseconds = latency
            };
        }

        var providerCalled = GetBool(data, "providerWasCalled");
        var providerState = GetString(data, "providerState") ?? AiProviderStatusContract.NotCalled;
        var failureCode = GetString(data, "providerFailureCode") ?? AiProviderStatusContract.FailureNone;
        var errorCode = GetString(data, "errorCode");
        var cards = GetArrayLength(data, "cards");
        var sources = GetArrayLength(data, "sources");
        var availableTools = GetArrayLength(data, "availableTools");
        var toolExecutions = cards;
        var schemaValid = !string.Equals(errorCode, "INVALID_PROVIDER_SCHEMA", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(errorCode, "INVALID_PROVIDER_PLAN", StringComparison.OrdinalIgnoreCase);
        return new LiveCanaryCaseResult
        {
            CaseId = canaryCase.CaseId,
            Actor = canaryCase.Role.ToString(),
            ExpectedCategory = canaryCase.ExpectedCategory,
            ActualIntent = GetString(data, "intent"),
            ToolNames = Array.Empty<string>(),
            ProviderState = providerState,
            FailureCode = failureCode,
            ProviderCalled = providerCalled,
            SchemaValid = schemaValid,
            AllowedTools = availableTools,
            ToolExecutions = toolExecutions,
            GroundedResponse = cards > 0 || sources > 0,
            Clarification = !string.IsNullOrWhiteSpace(GetString(data, "clarification")),
            PolicyViolation = errorCode is not null && errorCode.Contains("POLICY", StringComparison.OrdinalIgnoreCase),
            ProviderPlanRejected = errorCode is not null && (errorCode.StartsWith("INVALID_PROVIDER", StringComparison.OrdinalIgnoreCase) || errorCode == "PROVIDER_RESOURCE_MISMATCH"),
            AuthorizationDenied = errorCode is not null && errorCode.Contains("DENIED", StringComparison.OrdinalIgnoreCase),
            ExecutionMode = GetString(data, "executionMode"),
            LatencyMilliseconds = latency
        };
    }

    private static IReadOnlyList<CanaryHttpCase> Cases() => new[]
    {
        new CanaryHttpCase("patient-http-read", "canary.patient@synthetic.invalid", AiActorRole.Patient, "patient_read", "Could you surface the upcoming schedule owned by this account?", "/patient/appointments"),
        new CanaryHttpCase("reception-http-read", "canary.reception@synthetic.invalid", AiActorRole.Receptionist, "reception_read", "Could you surface today's front-desk appointment list?", "/reception/appointments"),
        new CanaryHttpCase("doctor-http-read", "canary.doctor@synthetic.invalid", AiActorRole.Doctor, "doctor_read", "Could you surface the assigned care roster for this clinician?", "/doctor/appointments"),
        new CanaryHttpCase("technician-http-read", "canary.technician@synthetic.invalid", AiActorRole.DiagnosticTechnician, "technician_read", "Could you surface the pending laboratory work queue?", "/diagnostics"),
        new CanaryHttpCase("pharmacist-http-read", "canary.pharmacist@synthetic.invalid", AiActorRole.Pharmacist, "pharmacy_read", "Could you surface prescriptions awaiting fulfillment?", "/pharmacy/prescriptions"),
        new CanaryHttpCase("admin-http-read", "canary.admin@synthetic.invalid", AiActorRole.Admin, "admin_read", "Could you surface an aggregate operations snapshot?", "/admin")
    };

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int GetArrayLength(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    private static long? Percentile(long[] sorted, double percentile) =>
        sorted.Length == 0 ? null : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * percentile) - 1)];
}

internal sealed record CanaryHttpCase(
    string CaseId,
    string Email,
    AiActorRole Role,
    string ExpectedCategory,
    string Message,
    string Route);

internal sealed class SyntheticCanaryFactory : WebApplicationFactory<global::Program>
{
    private readonly string _dbFilePath = Path.Combine(Path.GetTempPath(), $"clinic_gate_d_canary_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
        });
        builder.ConfigureServices(services =>
        {
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
        db.Facilities.Add(facility);
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
        db.Departments.Add(department);
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

        var doctor = new Doctor
        {
            UserId = users[RoleNames.Doctor].Id,
            AcademicTitle = "Synthetic MD",
            ExperienceYears = 1,
            Description = "Synthetic canary doctor",
            IsActive = true
        };
        db.Doctors.Add(doctor);
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
        await db.SaveChangesAsync();
        db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialty.Id, IsPrimary = true });
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
