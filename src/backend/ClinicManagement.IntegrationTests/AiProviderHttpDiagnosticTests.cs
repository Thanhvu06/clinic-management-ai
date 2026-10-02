using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.AI.LiveCanary;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// Provider HTTP rejections must be reported with closed, sanitized codes:
/// HTTP status, Gemini error.status and the rejected request part. The raw
/// error body is only ever read to pick one of those closed values.
/// </summary>
public sealed class AiProviderHttpDiagnosticTests
{
    private const string SecretMarker = "SECRET-MARKER-123";
    private const string ResponseFormatField = "generation_config.response_format";
    private static readonly JsonSerializerOptions NamedEnums = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    // ------------------------------------------------ probes (red before fix)

    [Fact]
    public async Task Planner_result_for_400_is_distinguishable_from_404()
    {
        var badRequest = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField)));
        var notFound = await PlanAsync(new StaticHandler(HttpStatusCode.NotFound, GoogleError(404, "NOT_FOUND", null)));

        Assert.NotEqual(WithoutCorrelation(badRequest), WithoutCorrelation(notFound));
    }

    [Fact]
    public async Task Rejected_400_carries_http_status_error_status_and_request_part_to_the_planner_result()
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField)));
        var serialized = JsonSerializer.Serialize(result, NamedEnums);

        Assert.Contains("\"400\"", serialized, StringComparison.Ordinal);
        Assert.Contains("InvalidArgument", serialized, StringComparison.Ordinal);
        Assert.Contains("ResponseFormatSchema", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMarker, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_stack_http_400_reaches_the_copilot_response_and_canary_report_without_raw_text()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField));

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);
        var patient = Assert.Single(report.Cases, x => x.CaseId == "patient-http-read");
        var serialized = JsonSerializer.Serialize(patient);

        Assert.Contains("ResponseFormatSchema", serialized, StringComparison.Ordinal);
        Assert.Contains("InvalidArgument", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    // L4 probes: a response-format rename and a bad schema node used to collapse
    // into the same ResponseFormatSchema value with no path or rejection kind.

    private const string UnknownResponseFormatMessage =
        "Invalid JSON payload received. Unknown name \"responseFormat\" at 'generation_config': Cannot find field.";

    [Fact]
    public async Task Planner_result_names_the_rejected_field_path_and_rejection_kind()
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest,
            GoogleError(400, "INVALID_ARGUMENT", "generation_config", UnknownResponseFormatMessage)));
        var providerHttp = JsonNode.Parse(JsonSerializer.Serialize(result, NamedEnums))![nameof(AiRolePlannerProviderResult.ProviderHttp)]!;

        Assert.Equal("UnknownField", (string?)providerHttp["RejectionKind"]);
        Assert.Equal("responseFormat", (string?)providerHttp["RejectedName"]);
        Assert.Equal("generation_config", (string?)providerHttp["RejectedFieldPath"]);
    }

    [Fact]
    public async Task Full_stack_canary_report_names_the_rejected_field_path_and_rejection_kind()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest,
            GoogleError(400, "INVALID_ARGUMENT", "generation_config", UnknownResponseFormatMessage));

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);
        var patient = JsonNode.Parse(JsonSerializer.Serialize(Assert.Single(report.Cases, x => x.CaseId == "patient-http-read")))!;

        Assert.Equal("UnknownField", (string?)patient["ProviderRejectionKind"]);
        Assert.Equal("responseFormat", (string?)patient["ProviderRejectedName"]);
        Assert.Equal("generation_config", (string?)patient["ProviderRejectedFieldPath"]);
    }

    // ------------------------------------- rejected path / kind / schema size

    private const string ClarificationTypePath = "generation_config.response_format.text.schema.properties[clarification].type";

    [Fact]
    public async Task Unknown_response_format_name_reports_kind_name_and_path()
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest,
            GoogleError(400, "INVALID_ARGUMENT", "generation_config", UnknownResponseFormatMessage)));

        Assert.Equal(AiProviderRejectionKind.UnknownField, result.ProviderHttp!.RejectionKind);
        Assert.Equal("responseFormat", result.ProviderHttp.RejectedName);
        Assert.Equal("generation_config", result.ProviderHttp.RejectedFieldPath);
        // Existing wire values are unchanged: the violation field is classified first.
        Assert.Equal(AiProviderRejectedRequestPart.GenerationConfig, result.ProviderHttp.RejectedRequestPart);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task Field_violation_path_through_a_sent_schema_property_is_kept()
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest,
            GoogleError(400, "INVALID_ARGUMENT", ClarificationTypePath, "Invalid value at 'generation_config.response_format' (type), \"string\"")));

        Assert.Equal(ClarificationTypePath, result.ProviderHttp!.RejectedFieldPath);
        Assert.Equal(AiProviderRejectionKind.InvalidValue, result.ProviderHttp.RejectionKind);
        Assert.Equal(AiProviderHttpDiagnostic.NotAvailable, result.ProviderHttp.RejectedName);
    }

    [Theory]
    [InlineData("generation_config.response_format.text.schema.properties[SECRET-MARKER-123].type", "generation_config.response_format.text.schema.properties[?].type")]
    [InlineData("generation_config.response_format.text.schema.properties[patientFullName].type", "generation_config.response_format.text.schema.properties[?].type")]
    [InlineData("generation_config.response_format.text.schema.properties.toolCalls.items.any_of[11].properties.arguments.properties.query.type",
                "generation_config.response_format.text.schema.properties.toolCalls.items.any_of[11].properties.arguments.properties.query.type")]
    [InlineData("generation_config.SECRET-MARKER-123[1000].properties['reply']", "generation_config.?[?].properties[reply]")]
    [InlineData("contents[0].parts[0].text", "contents[0].?[0].text")]
    public async Task Field_violation_segments_outside_the_allowlist_and_sent_schema_become_question_marks(string field, string expected)
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", field)));

        Assert.Equal(expected, result.ProviderHttp!.RejectedFieldPath);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(result, NamedEnums), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Path_and_name_from_free_text_message_are_sanitized()
    {
        var message = $"Invalid JSON payload received. Unknown name \"{SecretMarker}\" at 'generation_config.{SecretMarker}': Cannot find field. {SecretMarker}";

        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", null, message)));

        Assert.Equal(AiProviderRejectionKind.UnknownField, result.ProviderHttp!.RejectionKind);
        Assert.Equal(AiProviderHttpDiagnostic.Other, result.ProviderHttp.RejectedName);
        Assert.Equal("generation_config.?", result.ProviderHttp.RejectedFieldPath);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(result, NamedEnums), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Invalid JSON payload received. Unknown name \"responseFormat\" at 'generation_config': Cannot find field.", AiProviderRejectionKind.UnknownField, "responseFormat")]
    [InlineData("Invalid value at 'generation_config.response_format' (oneOf), \"x\"", AiProviderRejectionKind.InvalidValue, "NotAvailable")]
    [InlineData("Keyword \"prefixItems\" is not supported in response schema.", AiProviderRejectionKind.UnsupportedKeyword, "NotAvailable")]
    [InlineData("The specified schema produces a constraint that has too many states for serving.", AiProviderRejectionKind.SchemaTooComplex, "NotAvailable")]
    [InlineData("Schema nesting depth exceeds the limit.", AiProviderRejectionKind.SchemaTooComplex, "NotAvailable")]
    [InlineData("Unsupported keyword \"const\" in schema.", AiProviderRejectionKind.UnsupportedKeyword, "const")]
    [InlineData("Request contains an invalid argument.", AiProviderRejectionKind.Other, "NotAvailable")]
    public async Task Rejection_kind_and_name_follow_the_message_patterns(string message, AiProviderRejectionKind expectedKind, string expectedName)
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", null, message)), maxAttempts: 1);

        Assert.Equal(expectedKind, result.ProviderHttp!.RejectionKind);
        Assert.Equal(expectedName, result.ProviderHttp.RejectedName);
    }

    public static IEnumerable<object[]> UnparsedBodies() => new[]
    {
        new object[] { string.Empty, AiProviderRejectionKind.NotAvailable },
        new object[] { "<html>" + SecretMarker + "</html>", AiProviderRejectionKind.Other },
        new object[] { "{\"error\":{\"code\":400}}", AiProviderRejectionKind.NotAvailable },
        new object[] { "{\"error\":{\"message\":\"Unknown name", AiProviderRejectionKind.Other },
        new object[] { GoogleError(400, "INVALID_ARGUMENT", ClarificationTypePath, UnknownResponseFormatMessage + new string('x', 20 * 1024)), AiProviderRejectionKind.Other }
    };

    [Theory]
    [MemberData(nameof(UnparsedBodies))]
    public async Task Empty_non_json_or_oversized_bodies_never_throw_and_report_no_name_or_path(string body, AiProviderRejectionKind expectedKind)
    {
        var result = await PlanAsync(new StaticHandler(HttpStatusCode.BadRequest, body), maxAttempts: 1);

        Assert.Equal(expectedKind, result.ProviderHttp!.RejectionKind);
        Assert.Equal(AiProviderHttpDiagnostic.NotAvailable, result.ProviderHttp.RejectedName);
        Assert.Equal(AiProviderHttpDiagnostic.NotAvailable, result.ProviderHttp.RejectedFieldPath);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
    }

    [Fact]
    public void Field_path_is_capped_at_twenty_four_segments_and_three_hundred_characters()
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { new string('a', 60) };
        var deep = string.Join(".", Enumerable.Repeat("items", 40));
        var wide = string.Join(".", Enumerable.Repeat(new string('a', 60), 10));

        var deepPath = AiProviderHttpDiagnostic.NormalizeFieldPath(deep, names);
        var widePath = AiProviderHttpDiagnostic.NormalizeFieldPath(wide, names);

        Assert.Equal(string.Join(".", Enumerable.Repeat("items", 24)) + AiProviderHttpDiagnostic.Truncated, deepPath);
        Assert.True(widePath.Length <= AiProviderHttpDiagnostic.MaxFieldPathLength);
        Assert.EndsWith(AiProviderHttpDiagnostic.Truncated, widePath, StringComparison.Ordinal);
        Assert.True(AiProviderHttpDiagnostic.IsNormalizedFieldPath(deepPath, names));
        Assert.True(AiProviderHttpDiagnostic.IsNormalizedFieldPath(widePath, names));
        Assert.Equal(AiProviderHttpDiagnostic.NotAvailable, AiProviderHttpDiagnostic.NormalizeFieldPath("  ", names));
    }

    [Theory]
    [InlineData(AiActorRole.Patient, 12)]
    [InlineData(AiActorRole.DiagnosticTechnician, 2)]
    public async Task Schema_metrics_match_the_schema_actually_sent_and_appear_only_on_http_rejection(AiActorRole role, int expectedTools)
    {
        using var handler = new CapturingHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField));
        var tools = AiRolePlannerContractTests.ToolsFor(role);
        var request = new AiRolePlannerProviderRequest
        {
            Role = role,
            Message = "synthetic",
            AllowedTools = AiRolePlannerContract.BuildToolContracts(tools, tools.Select(x => x.Name)),
            AllowedIntents = AiRolePlannerContract.AllowedIntents
        };
        Assert.Equal(expectedTools, request.AllowedTools.Count);

        var result = await CreateProvider(handler, maxAttempts: 1).PlanRoleCopilotAsync(request);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var schema = sent.RootElement.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text").GetProperty("schema");
        Assert.Equal(Encoding.UTF8.GetByteCount(schema.GetRawText()), result.ProviderHttp!.RequestSchemaSizeBytes);
        Assert.Equal(expectedTools, schema.GetProperty("properties").GetProperty("toolCalls").GetProperty("items").GetProperty("anyOf").GetArrayLength());
        Assert.Equal(expectedTools, result.ProviderHttp.RequestSchemaToolBranches);
        // root > properties > toolCalls > items > anyOf > tool > properties > arguments > properties > argument
        Assert.Equal(10, MaxDepth(schema));
        Assert.Equal(10, result.ProviderHttp.RequestSchemaMaxDepth);

        // Legacy chat has no schema; a successful call has no HTTP diagnostic.
        using var legacyHandler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", "contents[0]"));
        var legacy = await CreateProvider(legacyHandler, maxAttempts: 1).ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);
        Assert.Null(legacy.ProviderHttp!.RequestSchemaSizeBytes);
        Assert.Null(legacy.ProviderHttp.RequestSchemaToolBranches);
        Assert.Null(legacy.ProviderHttp.RequestSchemaMaxDepth);
        Assert.Null(AiCopilotPlannerDiagnosticDto.From(new AiPlannerValidationDiagnostic(AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MalformedJson))!.RequestSchemaSizeBytes);
    }

    [Fact]
    public async Task Retryable_status_keeps_attempts_and_wire_values_and_carries_the_new_fields()
    {
        using var handler = new StaticHandler(HttpStatusCode.ServiceUnavailable,
            GoogleError(503, "UNAVAILABLE", null, "The model is overloaded. Please try again later."));

        var result = await PlanAsync(handler, maxAttempts: 2);

        Assert.Equal("ProviderServerError", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureServerError, result.FailureCode);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, result.ProviderAttemptCount);
        Assert.Equal("503", result.ProviderHttp!.HttpStatus);
        Assert.Equal(AiProviderRejectedRequestPart.Unknown, result.ProviderHttp.RejectedRequestPart);
        Assert.Equal(AiProviderRejectionKind.Other, result.ProviderHttp.RejectionKind);
        Assert.Equal(AiProviderHttpDiagnostic.NotAvailable, result.ProviderHttp.RejectedFieldPath);
        Assert.NotNull(result.ProviderHttp.RequestSchemaSizeBytes);
    }

    [Fact]
    public async Task Rejected_path_kind_and_schema_size_reach_the_api_logs_and_full_stack_canary_report_without_the_marker()
    {
        var field = "generation_config.response_format.text.schema.properties[" + SecretMarker + "].type";
        var message = $"Invalid JSON payload received. Unknown name \"additionalProperties\" at '{field}': Cannot find field. {SecretMarker}";
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", field, message));
        var sanitizedPath = "generation_config.response_format.text.schema.properties[?].type";

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.Fail, report.AcceptanceStatus);
        var roleCases = report.Cases.Where(x => x.ProviderHttpStatus == "400").ToArray();
        Assert.NotEmpty(roleCases);
        foreach (var roleCase in roleCases)
        {
            Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, roleCase.FailureCode);
            Assert.Equal(1, roleCase.ProviderAttemptCount);
            Assert.Equal(nameof(AiProviderRejectedRequestPart.ResponseFormatSchema), roleCase.ProviderRejectedRequestPart);
            Assert.Equal(nameof(AiProviderRejectionKind.UnknownField), roleCase.ProviderRejectionKind);
            Assert.Equal("additionalProperties", roleCase.ProviderRejectedName);
            Assert.Equal(sanitizedPath, roleCase.ProviderRejectedFieldPath);
            Assert.True(roleCase.RequestSchemaSizeBytes > 0);
            Assert.True(roleCase.RequestSchemaToolBranches > 0);
            Assert.True(roleCase.RequestSchemaMaxDepth > 0);
        }
        var patient = Assert.Single(report.Cases, x => x.CaseId == "patient-http-read");
        Assert.Equal(12, patient.RequestSchemaToolBranches);
        Assert.Equal(10, patient.RequestSchemaMaxDepth);

        var legacy = Assert.Single(report.Cases, x => x.CaseId == "patient-legacy-http-read");
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, legacy.ProviderRejectionKind);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, legacy.ProviderRejectedName);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, legacy.ProviderRejectedFieldPath);
        Assert.Null(legacy.RequestSchemaSizeBytes);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(report), StringComparison.Ordinal);

        // Same body straight through the API and the provider logs.
        using var factory = new SyntheticCanaryFactory(new CanaryProviderAttemptBudget(12), handler);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedAsync();
        using (var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = "canary.patient@synthetic.invalid", password = "Canary@12345" }))
        {
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        using var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message = "Các buổi khám tôi đã đặt trong thời gian tới có những gì?",
            conversationId = "conv_http_path_api",
            sessionId = "sess_http_path_api",
            currentRoute = "/patient/appointments",
            locale = "vi-VN",
            timezone = "Asia/Ho_Chi_Minh"
        });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SecretMarker, raw, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(raw);
        var diagnostic = document.RootElement.GetProperty("data").GetProperty("plannerDiagnostic");
        Assert.Equal("UnknownField", diagnostic.GetProperty("providerRejectionKind").GetString());
        Assert.Equal("additionalProperties", diagnostic.GetProperty("providerRejectedName").GetString());
        Assert.Equal(sanitizedPath, diagnostic.GetProperty("providerRejectedFieldPath").GetString());
        Assert.Equal(12, diagnostic.GetProperty("requestSchemaToolBranches").GetInt32());

        var providerLog = new ListLogger<GeminiAiProvider>();
        var planned = await CreateProvider(handler, 1, providerLog).PlanRoleCopilotAsync(new AiRolePlannerProviderRequest
        {
            Role = AiActorRole.Patient,
            Message = "lịch hẹn của tôi",
            AllowedTools = AiRolePlannerContract.BuildToolContracts(
                AiRolePlannerContractTests.ToolsFor(AiActorRole.Patient),
                AiRolePlannerContractTests.ToolsFor(AiActorRole.Patient).Select(x => x.Name)),
            AllowedIntents = AiRolePlannerContract.AllowedIntents
        });
        var logs = string.Join("\n", providerLog.Entries);
        Assert.Contains(sanitizedPath, logs, StringComparison.Ordinal);
        Assert.Contains(planned.CorrelationId!, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMarker, logs, StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_sanitizer_keeps_only_allowlisted_rejection_names_paths_and_bounded_metrics()
    {
        using var document = JsonDocument.Parse("""
        {"plannerDiagnostic":{"stage":"NotAvailable","providerRejectionKind":"SECRET-MARKER-123","providerRejectedName":"SECRET-MARKER-123","providerRejectedFieldPath":"generation_config.SECRET-MARKER-123","requestSchemaSizeBytes":-1,"requestSchemaToolBranches":100000,"requestSchemaMaxDepth":10}}
        """);
        var rejected = CanaryDiagnosticSanitizer.FromResponse(document.RootElement, new HashSet<string>());
        Assert.Equal("Other", rejected.ProviderRejectionKind);
        Assert.Equal("Other", rejected.ProviderRejectedName);
        Assert.Equal("Other", rejected.ProviderRejectedFieldPath);
        Assert.Null(rejected.RequestSchemaSizeBytes);
        Assert.Null(rejected.RequestSchemaToolBranches);
        Assert.Equal(10, rejected.RequestSchemaMaxDepth);

        using var valid = JsonDocument.Parse("""
        {"plannerDiagnostic":{"stage":"NotAvailable","providerRejectionKind":"UnsupportedKeyword","providerRejectedName":"anyOf","providerRejectedFieldPath":"generation_config.response_format.text.schema.properties.toolCalls.items.anyOf[3].properties[?].description"}}
        """);
        var kept = CanaryDiagnosticSanitizer.FromResponse(valid.RootElement, new HashSet<string>());
        Assert.Equal("UnsupportedKeyword", kept.ProviderRejectionKind);
        Assert.Equal("anyOf", kept.ProviderRejectedName);
        Assert.Equal("generation_config.response_format.text.schema.properties.toolCalls.items.anyOf[3].properties[?].description", kept.ProviderRejectedFieldPath);

        using var absent = JsonDocument.Parse("""{"plannerDiagnostic":{"stage":"PlannerSchema"}}""");
        var none = CanaryDiagnosticSanitizer.FromResponse(absent.RootElement, new HashSet<string>());
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, none.ProviderRejectionKind);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, none.ProviderRejectedName);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, none.ProviderRejectedFieldPath);
        Assert.Null(none.RequestSchemaSizeBytes);
    }

    private static int MaxDepth(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => 1 + element.EnumerateObject().Select(x => MaxDepth(x.Value)).DefaultIfEmpty(0).Max(),
        JsonValueKind.Array => 1 + element.EnumerateArray().Select(MaxDepth).DefaultIfEmpty(0).Max(),
        _ => 0
    };

    // ------------------------------------------------------- provider level

    [Fact]
    public async Task Http_400_invalid_argument_on_response_format_keeps_the_wire_failure_code_and_does_not_retry()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField));

        var result = await PlanAsync(handler, maxAttempts: 3);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidModelOrEndpoint", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
        Assert.False(result.Retryable);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, result.ProviderAttemptCount);
        Assert.Null(result.Diagnostic);
        Assert.NotNull(result.ProviderHttp);
        Assert.Equal("400", result.ProviderHttp!.HttpStatus);
        Assert.Equal(AiProviderErrorStatus.InvalidArgument, result.ProviderHttp.ErrorStatus);
        Assert.Equal(AiProviderRejectedRequestPart.ResponseFormatSchema, result.ProviderHttp.RejectedRequestPart);
    }

    [Fact]
    public async Task Http_404_not_found_is_reported_distinctly_from_400_with_the_same_wire_code()
    {
        using var handler = new StaticHandler(HttpStatusCode.NotFound, GoogleError(404, "NOT_FOUND", null, "models/synthetic is not found."));

        var result = await PlanAsync(handler, maxAttempts: 3);

        Assert.Equal("InvalidModelOrEndpoint", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("404", result.ProviderHttp!.HttpStatus);
        Assert.Equal(AiProviderErrorStatus.NotFound, result.ProviderHttp.ErrorStatus);
        Assert.Equal(AiProviderRejectedRequestPart.Unknown, result.ProviderHttp.RejectedRequestPart);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "InvalidModelOrEndpoint", AiProviderStatusContract.FailureModelUnavailable, 1, "400", AiProviderErrorStatus.InvalidArgument)]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", "InvalidModelOrEndpoint", AiProviderStatusContract.FailureModelUnavailable, 1, "404", AiProviderErrorStatus.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "AuthFailure", AiProviderStatusContract.FailureAuthenticationFailed, 1, "401", AiProviderErrorStatus.Unauthenticated)]
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "AuthFailure", AiProviderStatusContract.FailureAuthenticationFailed, 1, "403", AiProviderErrorStatus.PermissionDenied)]
    [InlineData(HttpStatusCode.RequestTimeout, "DEADLINE_EXCEEDED", "Timeout", AiProviderStatusContract.FailureTimeout, 2, "408", AiProviderErrorStatus.DeadlineExceeded)]
    [InlineData(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "RateLimited", AiProviderStatusContract.FailureRateLimited, 2, "429", AiProviderErrorStatus.ResourceExhausted)]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL", "ProviderServerError", AiProviderStatusContract.FailureServerError, 2, "500", AiProviderErrorStatus.Internal)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", "ProviderServerError", AiProviderStatusContract.FailureServerError, 2, "503", AiProviderErrorStatus.Unavailable)]
    public async Task Retry_behaviour_and_attempt_count_are_unchanged_and_the_diagnostic_is_closed(
        HttpStatusCode status,
        string googleStatus,
        string expectedStatus,
        string expectedFailureCode,
        int expectedAttempts,
        string expectedHttpStatus,
        AiProviderErrorStatus expectedErrorStatus)
    {
        using var handler = new StaticHandler(status, GoogleError((int)status, googleStatus, null));

        var result = await PlanAsync(handler, maxAttempts: 2);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedFailureCode, result.FailureCode);
        Assert.Equal(expectedAttempts, handler.CallCount);
        Assert.Equal(expectedAttempts, result.ProviderAttemptCount);
        Assert.Equal(expectedHttpStatus, result.ProviderHttp!.HttpStatus);
        Assert.Equal(expectedErrorStatus, result.ProviderHttp.ErrorStatus);
    }

    [Fact]
    public async Task Legacy_chat_http_failure_keeps_its_status_and_carries_the_same_closed_diagnostic()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", "contents[0].parts[0]"));

        var result = await CreateProvider(handler, maxAttempts: 3).ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.Equal("InvalidModelOrEndpoint", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("400", result.ProviderHttp!.HttpStatus);
        Assert.Equal(AiProviderRejectedRequestPart.Contents, result.ProviderHttp.RejectedRequestPart);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ErrorBodies() => new[]
    {
        new object[] { HttpStatusCode.BadRequest, "<html>" + SecretMarker + "</html>", "400", AiProviderErrorStatus.Other, AiProviderRejectedRequestPart.NotAvailable },
        new object[] { HttpStatusCode.BadRequest, string.Empty, "400", AiProviderErrorStatus.NotAvailable, AiProviderRejectedRequestPart.NotAvailable },
        new object[] { HttpStatusCode.BadRequest, "{\"unexpected\":\"" + SecretMarker + "\"}", "400", AiProviderErrorStatus.Other, AiProviderRejectedRequestPart.NotAvailable },
        new object[] { HttpStatusCode.BadRequest, "[1,2,3]", "400", AiProviderErrorStatus.Other, AiProviderRejectedRequestPart.NotAvailable },
        new object[] { HttpStatusCode.BadRequest, "{\"error\":{\"code\":400}}", "400", AiProviderErrorStatus.NotAvailable, AiProviderRejectedRequestPart.NotAvailable },
        new object[] { HttpStatusCode.BadRequest, "{\"error\":{\"status\":\"INVALID_ARG", "400", AiProviderErrorStatus.Other, AiProviderRejectedRequestPart.NotAvailable },
        new object[] { (HttpStatusCode)418, GoogleError(418, "I_AM_A_TEAPOT", null), "Other", AiProviderErrorStatus.Other, AiProviderRejectedRequestPart.Unknown },
        new object[] { HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", "system_instruction.parts[0].text"), "400", AiProviderErrorStatus.InvalidArgument, AiProviderRejectedRequestPart.SystemInstruction },
        new object[] { HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", "generation_config.temperature"), "400", AiProviderErrorStatus.InvalidArgument, AiProviderRejectedRequestPart.GenerationConfig },
        new object[] { HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", "contents[0].parts"), "400", AiProviderErrorStatus.InvalidArgument, AiProviderRejectedRequestPart.Contents },
        new object[] { HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", null, "Invalid JSON payload received. Unknown name \"responseFormat\" at 'generationConfig'. " + SecretMarker), "400", AiProviderErrorStatus.InvalidArgument, AiProviderRejectedRequestPart.ResponseFormatSchema },
        new object[] { HttpStatusCode.BadRequest, GoogleError(400, "FAILED_PRECONDITION", null), "400", AiProviderErrorStatus.FailedPrecondition, AiProviderRejectedRequestPart.Unknown }
    };

    [Theory]
    [MemberData(nameof(ErrorBodies))]
    public async Task Unusual_error_bodies_map_to_closed_values_without_throwing(
        HttpStatusCode status,
        string body,
        string expectedHttpStatus,
        AiProviderErrorStatus expectedErrorStatus,
        AiProviderRejectedRequestPart expectedPart)
    {
        using var handler = new StaticHandler(status, body);

        var result = await PlanAsync(handler, maxAttempts: 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedHttpStatus, result.ProviderHttp!.HttpStatus);
        Assert.Equal(expectedErrorStatus, result.ProviderHttp.ErrorStatus);
        Assert.Equal(expectedPart, result.ProviderHttp.RejectedRequestPart);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(result, NamedEnums), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_body_over_sixteen_kilobytes_is_not_parsed_and_reports_other()
    {
        var body = GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField, new string('x', 20 * 1024) + SecretMarker);
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, body);

        var result = await PlanAsync(handler, maxAttempts: 3);

        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("400", result.ProviderHttp!.HttpStatus);
        Assert.Equal(AiProviderErrorStatus.Other, result.ProviderHttp.ErrorStatus);
        Assert.Equal(AiProviderRejectedRequestPart.NotAvailable, result.ProviderHttp.RejectedRequestPart);
    }

    [Fact]
    public async Task Secret_marker_in_the_error_body_never_reaches_planner_results_or_logs()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField));
        var providerLog = new ListLogger<GeminiAiProvider>();
        var plannerLog = new ListLogger<GeminiStructuredPlanner>();
        var planner = new GeminiStructuredPlanner(CreateProvider(handler, 3, providerLog), new AiProviderHealth(), plannerLog);
        var tools = AiRolePlannerContractTests.ToolsFor(AiActorRole.Patient);

        var result = await planner.PlanAsync(new AiStructuredPlannerRequest
        {
            Role = AiActorRole.Patient,
            Message = "lịch hẹn của tôi",
            ConversationId = "conv_http_diagnostic",
            Resource = new AiResolvedResourceContext(),
            AllowedTools = tools,
            AllowedToolNames = tools.Select(x => x.Name).ToArray()
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, result.FailureCode);
        Assert.Equal(1, result.ProviderAttemptCount);
        Assert.Null(result.Diagnostic);
        Assert.Equal("400", result.ProviderHttp!.HttpStatus);
        Assert.Equal(AiProviderErrorStatus.InvalidArgument, result.ProviderHttp.ErrorStatus);
        Assert.Equal(AiProviderRejectedRequestPart.ResponseFormatSchema, result.ProviderHttp.RejectedRequestPart);

        var logs = string.Join("\n", providerLog.Entries.Concat(plannerLog.Entries));
        Assert.NotEmpty(providerLog.Entries);
        Assert.DoesNotContain(SecretMarker, logs, StringComparison.Ordinal);
        // The path may now be logged, but only as the normalized, allowlisted value.
        Assert.Equal(ResponseFormatField, result.ProviderHttp.RejectedFieldPath);
        Assert.DoesNotContain("Invalid value", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMarker, JsonSerializer.Serialize(result, NamedEnums), StringComparison.Ordinal);
    }

    // ------------------------------------------------------- response / DTO

    [Fact]
    public void Copilot_dto_reports_http_rejection_without_borrowing_a_validation_stage()
    {
        Assert.Null(AiCopilotPlannerDiagnosticDto.From(null, null));

        var http = AiCopilotPlannerDiagnosticDto.From(null, new AiProviderHttpDiagnostic
        {
            HttpStatus = "400",
            ErrorStatus = AiProviderErrorStatus.InvalidArgument,
            RejectedRequestPart = AiProviderRejectedRequestPart.ResponseFormatSchema
        })!;
        Assert.Equal("NotAvailable", http.Stage);
        Assert.Equal("NotAvailable", http.Reason);
        Assert.Equal("NotAvailable", http.FinishReason);
        Assert.Equal("400", http.ProviderHttpStatus);
        Assert.Equal("InvalidArgument", http.ProviderErrorStatus);
        Assert.Equal("ResponseFormatSchema", http.ProviderRejectedRequestPart);

        var validation = AiCopilotPlannerDiagnosticDto.From(new AiPlannerValidationDiagnostic(AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MissingSchemaVersion))!;
        Assert.Equal("PlannerSchema", validation.Stage);
        Assert.Equal("NotAvailable", validation.ProviderHttpStatus);
        Assert.Equal("NotAvailable", validation.ProviderErrorStatus);
        Assert.Equal("NotAvailable", validation.ProviderRejectedRequestPart);
    }

    [Fact]
    public async Task Copilot_api_response_carries_the_sanitized_http_diagnostic_and_no_raw_provider_text()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField));
        using var factory = new SyntheticCanaryFactory(new CanaryProviderAttemptBudget(12), handler);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await factory.SeedAsync();

        using (var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { emailOrPhone = "canary.patient@synthetic.invalid", password = "Canary@12345" }))
        {
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.PostAsJsonAsync("/api/v1/ai/copilot/chat", new
        {
            message = "Các buổi khám tôi đã đặt trong thời gian tới có những gì?",
            conversationId = "conv_http_diagnostic_api",
            sessionId = "sess_http_diagnostic_api",
            currentRoute = "/patient/appointments",
            locale = "vi-VN",
            timezone = "Asia/Ho_Chi_Minh"
        });
        var raw = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.DoesNotContain(SecretMarker, raw, StringComparison.Ordinal);
        // The rejected path appears only as the sanitized providerRejectedFieldPath.
        var withoutPath = JsonNode.Parse(raw)!;
        Assert.Equal(ResponseFormatField, (string?)withoutPath["data"]!["plannerDiagnostic"]!["providerRejectedFieldPath"]);
        withoutPath["data"]!["plannerDiagnostic"]!.AsObject().Remove("providerRejectedFieldPath");
        Assert.DoesNotContain(ResponseFormatField, withoutPath.ToJsonString(), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(raw);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, data.GetProperty("providerFailureCode").GetString());
        Assert.Equal(AiProviderStatusContract.Unavailable, data.GetProperty("providerState").GetString());
        Assert.Equal(1, data.GetProperty("providerAttemptCount").GetInt32());
        var diagnostic = data.GetProperty("plannerDiagnostic");
        Assert.Equal("NotAvailable", diagnostic.GetProperty("stage").GetString());
        Assert.Equal("400", diagnostic.GetProperty("providerHttpStatus").GetString());
        Assert.Equal("InvalidArgument", diagnostic.GetProperty("providerErrorStatus").GetString());
        Assert.Equal("ResponseFormatSchema", diagnostic.GetProperty("providerRejectedRequestPart").GetString());
    }

    // ---------------------------------------------------------------- canary

    [Fact]
    public async Task Full_stack_canary_reports_the_http_400_diagnostic_and_stays_fail()
    {
        using var handler = new StaticHandler(HttpStatusCode.BadRequest, GoogleError(400, "INVALID_ARGUMENT", ResponseFormatField));

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.Fail, report.AcceptanceStatus);
        Assert.True(report.DatabaseUnchanged);
        var patient = Assert.Single(report.Cases, x => x.CaseId == "patient-http-read");
        Assert.Equal(AiProviderStatusContract.Unavailable, patient.ProviderState);
        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, patient.FailureCode);
        Assert.Equal(1, patient.ProviderAttemptCount);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, patient.ValidationStage);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, patient.ValidationReason);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, patient.ProviderFinishReason);
        Assert.Equal("400", patient.ProviderHttpStatus);
        Assert.Equal(nameof(AiProviderErrorStatus.InvalidArgument), patient.ProviderErrorStatus);
        Assert.Equal(nameof(AiProviderRejectedRequestPart.ResponseFormatSchema), patient.ProviderRejectedRequestPart);

        var legacy = Assert.Single(report.Cases, x => x.CaseId == "patient-legacy-http-read");
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, legacy.ProviderHttpStatus);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, legacy.ProviderErrorStatus);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, legacy.ProviderRejectedRequestPart);

        var serialized = JsonSerializer.Serialize(report);
        Assert.DoesNotContain(SecretMarker, serialized, StringComparison.Ordinal);
        // The rejected path appears only as the sanitized ProviderRejectedFieldPath.
        Assert.Equal(ResponseFormatField, patient.ProviderRejectedFieldPath);
        var withoutPath = report with { Cases = report.Cases.Select(x => x with { ProviderRejectedFieldPath = string.Empty }).ToArray() };
        Assert.DoesNotContain(ResponseFormatField, JsonSerializer.Serialize(withoutPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_sanitizer_keeps_only_allowlisted_http_codes()
    {
        using var document = JsonDocument.Parse("""
        {"errorCode":"ModelUnavailable","plannerDiagnostic":{"stage":"NotAvailable","reason":"NotAvailable","finishReason":"NotAvailable","providerHttpStatus":"418","providerErrorStatus":"I_AM_A_TEAPOT","providerRejectedRequestPart":"generation_config.response_format SECRET-MARKER-123"}}
        """);

        var diagnostic = CanaryDiagnosticSanitizer.FromResponse(document.RootElement, new HashSet<string>());

        Assert.Equal(AiProviderStatusContract.FailureModelUnavailable, diagnostic.ApplicationErrorCode);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, diagnostic.ValidationStage);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, diagnostic.ValidationReason);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, diagnostic.FinishReason);
        Assert.Equal("Other", diagnostic.ProviderHttpStatus);
        Assert.Equal("Other", diagnostic.ProviderErrorStatus);
        Assert.Equal("Other", diagnostic.ProviderRejectedRequestPart);

        using var validationOnly = JsonDocument.Parse("""{"plannerDiagnostic":{"stage":"PlannerSchema","reason":"MissingSchemaVersion","finishReason":"Stop"}}""");
        var absent = CanaryDiagnosticSanitizer.FromResponse(validationOnly.RootElement, new HashSet<string>());
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, absent.ProviderHttpStatus);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, absent.ProviderErrorStatus);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, absent.ProviderRejectedRequestPart);
    }

    // ---------------------------------------------------------------- helpers

    private static Task<AiRolePlannerProviderResult> PlanAsync(HttpMessageHandler handler, int maxAttempts = 3) =>
        CreateProvider(handler, maxAttempts).PlanRoleCopilotAsync(new AiRolePlannerProviderRequest
        {
            Role = AiActorRole.Patient,
            Message = "lịch hẹn của tôi",
            AllowedTools = AiRolePlannerContract.BuildToolContracts(
                AiRolePlannerContractTests.ToolsFor(AiActorRole.Patient),
                AiRolePlannerContractTests.ToolsFor(AiActorRole.Patient).Select(x => x.Name)),
            AllowedIntents = AiRolePlannerContract.AllowedIntents
        });

    private static GeminiAiProvider CreateProvider(HttpMessageHandler handler, int maxAttempts, ILogger<GeminiAiProvider>? logger = null) => new(
        new HttpClient(handler),
        Options.Create(new AiProviderOptions
        {
            IsEnabled = true,
            ApiKey = "test-only-key",
            ProviderUrl = "https://fake-gemini.test",
            ModelName = "synthetic-contract-model",
            TimeoutSeconds = 2,
            MaxAttempts = maxAttempts,
            RetryBaseDelayMilliseconds = 1
        }),
        logger ?? NullLogger<GeminiAiProvider>.Instance);

    private static string WithoutCorrelation(AiRolePlannerProviderResult result)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(result))!.AsObject();
        node.Remove(nameof(AiRolePlannerProviderResult.CorrelationId));
        return node.ToJsonString();
    }

    /// <summary>Google-style error body; the message and violation carry a marker that must never leak.</summary>
    internal static string GoogleError(int code, string? status, string? field, string? message = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message ?? $"Request rejected for synthetic test {SecretMarker}."
        };
        if (status is not null) error["status"] = status;
        if (field is not null)
            error["details"] = new JsonArray(new JsonObject
            {
                ["@type"] = "type.googleapis.com/google.rpc.BadRequest",
                ["fieldViolations"] = new JsonArray(new JsonObject
                {
                    ["field"] = field,
                    ["description"] = $"Invalid value {SecretMarker}"
                })
            });
        return new JsonObject { ["error"] = error }.ToJsonString();
    }

    internal sealed class StaticHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private int _calls;

        public StaticHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public int CallCount => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Returns a fixed error and keeps the last request body so tests can measure the sent schema.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public CapturingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _entries = new();

        public IReadOnlyList<string> Entries
        {
            get { lock (_entries) return _entries.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add(formatter(state, exception) + " " + state + " " + exception);
        }
    }
}
