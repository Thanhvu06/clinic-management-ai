using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.AI.LiveCanary;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClinicManagement.IntegrationTests;

/// <summary>
/// Contract tests for the Gemini role planner: the provider-facing request,
/// the generated-JSON parser and the sanitized rejection diagnostics. Every
/// provider response here is a synthetic envelope from a capturing handler;
/// no test talks to Gemini. These fixtures prove the contract and the
/// instrumentation; they do not reproduce any historical live output.
/// </summary>
public sealed class AiRolePlannerContractTests
{
    public static TheoryData<AiActorRole> AllRoles => new()
    {
        AiActorRole.Patient,
        AiActorRole.Receptionist,
        AiActorRole.Doctor,
        AiActorRole.DiagnosticTechnician,
        AiActorRole.Pharmacist,
        AiActorRole.Admin
    };

    // ---------------------------------------------------------------- request

    [Theory]
    [MemberData(nameof(AllRoles))]
    public async Task Role_planner_request_only_names_the_tools_granted_to_that_role(AiActorRole role)
    {
        var handler = new CapturingHandler(_ => Envelope(PlannerJson("Help", toolCalls: "[]")));
        var planner = CreatePlanner(handler);

        await planner.PlanAsync(Request(role, "tôi cần hỗ trợ"));

        var body = Assert.Single(handler.Bodies);
        var granted = ToolsFor(role).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var foreign in AiPlannerPolicy.AllowedToolNames.Where(x => !granted.Contains(x)))
            Assert.DoesNotContain(foreign, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prepare_", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("execute_confirmed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public async Task Role_planner_request_declares_a_response_schema(AiActorRole role)
    {
        var handler = new CapturingHandler(_ => Envelope(PlannerJson("Help", toolCalls: "[]")));
        var planner = CreatePlanner(handler);

        await planner.PlanAsync(Request(role, "tôi cần hỗ trợ"));

        using var document = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var text = document.RootElement.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text");
        Assert.Equal("application/json", text.GetProperty("mimeType").GetString());
        Assert.Equal(JsonValueKind.Object, text.GetProperty("schema").ValueKind);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public async Task Role_planner_schema_is_built_from_the_same_definitions_the_validator_uses(AiActorRole role)
    {
        var handler = new CapturingHandler(_ => Envelope(PlannerJson("Help", toolCalls: "[]")));
        await CreatePlanner(handler).PlanAsync(Request(role, "tôi cần hỗ trợ"));

        var schema = JsonNode.Parse(Assert.Single(handler.Bodies))!["generationConfig"]!["responseFormat"]!["text"]!["schema"]!.AsObject();
        var properties = schema["properties"]!.AsObject();

        Assert.Equal(new[] { AiRolePlannerContract.SchemaVersion }, Strings(properties["plannerSchemaVersion"]!["enum"]));
        Assert.Equal(AiChatIntentTypes.All.OrderBy(x => x, StringComparer.Ordinal), Strings(properties["primaryIntent"]!["enum"]));
        Assert.Equal("number", (string?)properties["plannerConfidence"]!["type"]);
        Assert.Equal(0, (int)properties["plannerConfidence"]!["minimum"]!);
        Assert.Equal(1, (int)properties["plannerConfidence"]!["maximum"]!);
        Assert.Equal("boolean", (string?)properties["isClear"]!["type"]);
        Assert.Equal(AiRolePlannerContract.MaxToolCalls, (int)properties["toolCalls"]!["maxItems"]!);
        Assert.False((bool)schema["additionalProperties"]!);
        Assert.Equal(
            new[] { "plannerSchemaVersion", "primaryIntent", "plannerConfidence", "isClear", "clarification", "toolCalls", "reply" }.Order(),
            Strings(schema["required"]).Order());

        var items = properties["toolCalls"]!["items"]!.AsObject();
        var branches = items.ContainsKey("anyOf") ? items["anyOf"]!.AsArray().Select(x => x!.AsObject()).ToArray() : new[] { items };
        var definitions = ToolsFor(role)
            .Where(x => AiPlannerPolicy.IsAllowed(x.Name))
            .ToDictionary(x => x.Name, StringComparer.Ordinal);
        Assert.Equal(definitions.Keys.Order(StringComparer.Ordinal), branches.Select(x => Strings(x["properties"]!["name"]!["enum"]).Single()).Order(StringComparer.Ordinal));
        foreach (var branch in branches)
        {
            var name = Strings(branch["properties"]!["name"]!["enum"]).Single();
            var definition = definitions[name];
            Assert.Equal(new[] { definition.Version }, Strings(branch["properties"]!["version"]!["enum"]));
            var arguments = branch["properties"]!["arguments"]!.AsObject();
            Assert.Equal("object", (string?)arguments["type"]);
            Assert.False((bool)arguments["additionalProperties"]!);
            Assert.Equal(
                definition.ArgumentSchema.Where(x => !x.ServerBound).Select(x => x.Name).Order(StringComparer.Ordinal),
                arguments["properties"]!.AsObject().Select(x => x.Key).Order(StringComparer.Ordinal));
            Assert.Equal(
                definition.ArgumentSchema.Where(x => x.Required && !x.ServerBound).Select(x => x.Name).Order(StringComparer.Ordinal),
                arguments.ContainsKey("required") ? Strings(arguments["required"]).Order(StringComparer.Ordinal) : Array.Empty<string>());
        }

        AssertOnlySupportedKeywords(schema);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public async Task Role_planner_request_carries_no_identifiers_tokens_or_authority(AiActorRole role)
    {
        var handler = new CapturingHandler(_ => Envelope(PlannerJson("Help", toolCalls: "[]")));
        var resource = new AiResolvedResourceContext
        {
            CurrentRoute = "/workspace/record/424242",
            AppointmentId = 424242,
            VisitId = 515151,
            DiagnosticOrderId = 626262,
            PrescriptionId = 737373,
            ResourceVersion = "rv-909090"
        };

        await CreatePlanner(handler).PlanAsync(Request(role, "cho tôi xem hồ sơ đang mở", resource));

        var body = Assert.Single(handler.Bodies);
        foreach (var forbidden in new[]
                 {
                     "424242", "515151", "626262", "737373", "rv-909090", "test-only-key",
                     "appointmentId", "visitId", "diagnosticOrderId", "prescriptionId", "userId", "actorId",
                     "facilityId", "facilityAuthorization", "idempotencyKey", "confirmationToken", "concurrencyToken"
                 })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_patient_chat_and_role_planner_contracts_are_not_mixed()
    {
        var legacyHandler = new CapturingHandler(_ => Envelope("{\"reply\":\"Xin chào\",\"primaryIntent\":\"Greeting\",\"isClear\":true}"));
        var legacy = await CreateProvider(legacyHandler).ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);
        var roleHandler = new CapturingHandler(_ => Envelope(PlannerJson("Help", toolCalls: "[]")));
        await CreatePlanner(roleHandler).PlanAsync(Request(AiActorRole.Patient, "tôi cần hỗ trợ"));

        Assert.True(legacy.IsSuccess);
        using var legacyBody = JsonDocument.Parse(Assert.Single(legacyHandler.Bodies));
        var legacyConfig = legacyBody.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", legacyConfig.GetProperty("responseMimeType").GetString());
        Assert.False(legacyConfig.TryGetProperty("responseFormat", out _));
        Assert.Contains("extractedDoctorName", legacyBody.RootElement.GetRawText(), StringComparison.Ordinal);

        using var roleBody = JsonDocument.Parse(Assert.Single(roleHandler.Bodies));
        Assert.False(roleBody.RootElement.GetProperty("generationConfig").TryGetProperty("responseMimeType", out _));
        foreach (var legacyField in new[] { "extractedDoctorName", "suggestedSpecialtyCodes", "requestedActionType", "negatedDoctorName", "urgency" })
            Assert.DoesNotContain(legacyField, roleBody.RootElement.GetRawText(), StringComparison.Ordinal);
        var content = Assert.Single(roleBody.RootElement.GetProperty("contents").EnumerateArray());
        Assert.Equal("user", content.GetProperty("role").GetString());
        Assert.Equal("tôi cần hỗ trợ", content.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    // ------------------------------------------------------------------ valid

    public static TheoryData<AiActorRole, string, string, string> ValidPlans => new()
    {
        { AiActorRole.Patient, AiChatIntentTypes.ViewAppointments, "patient.get_my_appointments", "{\"pageSize\":5}" },
        { AiActorRole.DiagnosticTechnician, AiChatIntentTypes.DiagnosticLookup, "technician.get_worklist", "{}" },
        { AiActorRole.Receptionist, AiChatIntentTypes.QueueLookup, "reception.get_today_appointments", "{}" },
        { AiActorRole.Doctor, AiChatIntentTypes.QueueLookup, "doctor.get_my_queue", "{}" },
        { AiActorRole.Pharmacist, AiChatIntentTypes.PrescriptionLookup, "pharmacist.get_prescription_queue", "{}" },
        { AiActorRole.Admin, AiChatIntentTypes.AdminMetrics, "admin.get_dashboard_metrics", "{}" }
    };

    [Theory]
    [MemberData(nameof(ValidPlans))]
    public async Task Valid_role_plan_is_accepted_and_bound(AiActorRole role, string intent, string tool, string arguments)
    {
        var result = await PlanOnceAsync(role, PlannerJson(intent, ToolCall(tool, arguments)));

        Assert.True(result.IsSuccess, $"{result.FailureReason} {result.Diagnostic}");
        Assert.Null(result.Diagnostic);
        Assert.Equal(AiProviderStatusContract.Online, result.ProviderState);
        Assert.Equal(intent, result.Decision.Intent);
        var call = Assert.Single(result.Decision.ToolCalls);
        Assert.Equal(tool, call.Name);
        Assert.Equal("1.0", call.Version);
        Assert.Equal(JsonValueKind.Object, call.Arguments.ValueKind);
    }

    // ---------------------------------------------------------------- invalid

    public static TheoryData<string, AiActorRole, string, string, AiPlannerValidationStage, AiPlannerValidationReason> InvalidOutputs => new()
    {
        { "missing-version", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"), version: null)),
            AiPlannerErrorCodes.InvalidProviderSchema, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MissingSchemaVersion },
        { "wrong-version", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"), version: "2.0")),
            AiPlannerErrorCodes.InvalidProviderSchema, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.UnsupportedSchemaVersion },
        { "missing-confidence", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"), confidence: "null")),
            AiPlannerErrorCodes.InvalidProviderSchema, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MissingConfidence },
        { "confidence-out-of-range", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"), confidence: "1.5")),
            AiPlannerErrorCodes.InvalidProviderSchema, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.InvalidConfidence },
        { "unknown-intent", AiActorRole.DiagnosticTechnician, Json(PlannerJson("ViewWorklist", ToolCall("technician.get_worklist"))),
            AiPlannerErrorCodes.InvalidProviderSchema, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.InvalidIntent },
        { "missing-tool-calls", AiActorRole.Patient, Json("{\"plannerSchemaVersion\":\"1.0\",\"plannerConfidence\":0.9,\"primaryIntent\":\"ViewAppointments\",\"isClear\":true,\"clarification\":null,\"reply\":\"x\"}"),
            AiPlannerErrorCodes.InvalidProviderSchema, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MissingRequiredField },
        { "confidence-as-string", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"), confidence: "\"0.9\"")),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.InvalidFieldType },
        { "is-clear-as-string", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"), isClear: "\"yes\"")),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.InvalidFieldType },
        { "root-array", AiActorRole.Patient, Json("[" + PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments")) + "]"),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.InvalidFieldType },
        { "tool-calls-object", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", "{\"name\":\"patient.get_my_appointments\",\"version\":\"1.0\",\"arguments\":{}}")),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.InvalidFieldType },
        { "duplicate-field", AiActorRole.Patient, Json("{\"plannerSchemaVersion\":\"1.0\"," + PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"))[1..]),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.DuplicateField },
        { "malformed-json", AiActorRole.Patient, Json("{\"plannerSchemaVersion\":\"1.0\",\"primaryIntent\":"),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.GeneratedJson, AiPlannerValidationReason.MalformedJson },
        { "missing-candidate", AiActorRole.Patient, "{\"candidates\":[]}",
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.ProviderEnvelope, AiPlannerValidationReason.MissingCandidate },
        { "missing-text", AiActorRole.Patient, "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[]},\"finishReason\":\"STOP\"}]}",
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.ProviderEnvelope, AiPlannerValidationReason.MissingText },
        { "truncated", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"))[..40], "MAX_TOKENS"),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.ProviderEnvelope, AiPlannerValidationReason.OutputTruncated },
        { "safety-blocked", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments")), "SAFETY"),
            AiProviderStatusContract.FailureInvalidResponse, AiPlannerValidationStage.ProviderEnvelope, AiPlannerValidationReason.UnsupportedFinishReason },
        { "tool-outside-role", AiActorRole.DiagnosticTechnician, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ToolNotAllowed },
        { "prepare-tool", AiActorRole.Patient, Json(PlannerJson("StartBooking", ToolCall("patient.prepare_booking"))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ToolNotAllowed },
        { "direct-confirm-tool", AiActorRole.Pharmacist, Json(PlannerJson("PrescriptionLookup", ToolCall("role.execute_confirmed_action", "{\"actionId\":\"x\"}"))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ToolNotAllowed },
        { "too-many-tools", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", Calls("technician.get_worklist", 4))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ToolLimitExceeded },
        { "wrong-tool-version", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist", version: "\"2.0\""))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.UnsupportedToolVersion },
        { "arguments-array", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist", "[]"))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.InvalidArguments },
        { "arguments-missing", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", "[{\"name\":\"technician.get_worklist\",\"version\":\"1.0\"}]")),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.InvalidArguments },
        { "forbidden-top-level", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist", "{\"facilityId\":1}"))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ForbiddenArgument },
        { "forbidden-nested", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments", "{\"status\":{\"filter\":[{\"userId\":\"u\"}]}}"))),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ForbiddenArgument },
        { "unknown-argument", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist", "{\"shift\":\"night\"}"))),
            AiPlannerErrorCodes.UnknownToolArgument, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.UnknownArgument },
        { "missing-argument", AiActorRole.Receptionist, Json(PlannerJson("QueueLookup", ToolCall("reception.lookup_appointment"))),
            AiPlannerErrorCodes.MissingToolArgument, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.MissingArgument },
        { "wrong-argument-type", AiActorRole.Patient, Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments", "{\"page\":\"1\"}"))),
            AiPlannerErrorCodes.InvalidToolArguments, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.InvalidArguments },
        { "resource-required", AiActorRole.Doctor, Json(PlannerJson("PatientSummary", ToolCall("doctor.get_patient_summary"))),
            AiPlannerErrorCodes.ResourceContextRequired, AiPlannerValidationStage.ResourceBinding, AiPlannerValidationReason.ResourceContextRequired },
        { "clarification-with-tools", AiActorRole.DiagnosticTechnician, Json(PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"), isClear: "false", clarification: "\"Bạn cần xem gì?\"")),
            AiPlannerErrorCodes.InvalidProviderPlan, AiPlannerValidationStage.ToolPlan, AiPlannerValidationReason.ClarificationWithTools },
        { "missing-clarification", AiActorRole.DiagnosticTechnician, Json(PlannerJson("ClarificationRequired", "[]", isClear: "false")),
            AiPlannerErrorCodes.InvalidProviderClarification, AiPlannerValidationStage.PlannerSchema, AiPlannerValidationReason.MissingClarification }
    };

    [Theory]
    [MemberData(nameof(InvalidOutputs))]
    public async Task Invalid_output_is_rejected_before_any_tool_with_a_closed_diagnostic(
        string caseName,
        AiActorRole role,
        string envelope,
        string expectedErrorCode,
        AiPlannerValidationStage expectedStage,
        AiPlannerValidationReason expectedReason)
    {
        var handler = new CapturingHandler(_ => RawEnvelope(envelope));
        var result = await CreatePlanner(handler, maxAttempts: 3).PlanAsync(Request(role, "tôi cần hỗ trợ"));

        Assert.False(result.IsSuccess, caseName);
        Assert.Empty(result.Decision.ToolCalls);
        Assert.Equal(AiPlannerModes.Fallback, result.Decision.PlannerMode);
        Assert.Equal(expectedErrorCode, result.FailureReason);
        Assert.Equal(expectedErrorCode, result.Decision.ErrorCode);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal(expectedStage, result.Diagnostic!.Stage);
        Assert.Equal(expectedReason, result.Diagnostic.Reason);
        Assert.Matches("^[0-9a-f]{8}$", result.CorrelationId);
        // An invalid body is final: no second provider attempt to look for a green output.
        Assert.Single(handler.Bodies);
        Assert.Equal(1, result.ProviderAttemptCount);
        Assert.Equal(AiProviderStatusContract.Degraded, result.ProviderState);
    }

    [Fact]
    public async Task Planner_rejections_for_missing_version_and_unknown_intent_are_distinguishable()
    {
        var missingVersion = await PlanOnceAsync(AiActorRole.DiagnosticTechnician,
            PlannerJson("QueueLookup", toolCalls: ToolCall("technician.get_worklist"), version: null));
        var unknownIntent = await PlanOnceAsync(AiActorRole.DiagnosticTechnician,
            PlannerJson("ViewWorklist", toolCalls: ToolCall("technician.get_worklist")));

        Assert.False(missingVersion.IsSuccess);
        Assert.False(unknownIntent.IsSuccess);
        Assert.Empty(missingVersion.Decision.ToolCalls);
        Assert.Empty(unknownIntent.Decision.ToolCalls);
        Assert.NotEqual(WithoutCorrelation(missingVersion), WithoutCorrelation(unknownIntent));
    }

    [Fact]
    public async Task Mixed_valid_and_invalid_plan_is_rejected_whole_with_the_position_of_the_first_invalid_call()
    {
        var result = await PlanOnceAsync(AiActorRole.DiagnosticTechnician, PlannerJson(
            "DiagnosticLookup",
            "[" + ToolCall("technician.get_worklist")[1..^1] + "," + ToolCall("admin.get_dashboard_metrics")[1..^1] + "]"));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Decision.ToolCalls);
        Assert.Equal(AiPlannerErrorCodes.InvalidProviderPlan, result.FailureReason);
        Assert.Equal(AiPlannerValidationReason.ToolNotAllowed, result.Diagnostic!.Reason);
        Assert.Equal(1, result.Diagnostic.RejectedToolIndex);
        Assert.Equal(2, result.Diagnostic.ToolCount);
        Assert.Null(result.Diagnostic.ToolName);
    }

    [Fact]
    public async Task Resource_mismatch_names_only_the_allowlisted_tool_and_binds_nothing()
    {
        var result = await PlanOnceAsync(
            AiActorRole.Doctor,
            PlannerJson("PatientSummary", ToolCall("doctor.get_patient_summary", "{\"visitId\":999}")),
            new AiResolvedResourceContext { VisitId = 101 });

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Decision.ToolCalls);
        Assert.Equal(AiPlannerErrorCodes.ProviderResourceMismatch, result.FailureReason);
        Assert.Equal(AiPlannerValidationStage.ResourceBinding, result.Diagnostic!.Stage);
        Assert.Equal(AiPlannerValidationReason.ResourceMismatch, result.Diagnostic.Reason);
        Assert.Equal("doctor.get_patient_summary", result.Diagnostic.ToolName);
        Assert.Equal(0, result.Diagnostic.RejectedToolIndex);
    }

    [Fact]
    public async Task Diagnostics_never_echo_provider_supplied_names_or_text()
    {
        var unknownTool = await PlanOnceAsync(AiActorRole.DiagnosticTechnician,
            PlannerJson("DiagnosticLookup", ToolCall("evil.exfiltrate_records")));
        var unknownIntent = await PlanOnceAsync(AiActorRole.DiagnosticTechnician,
            PlannerJson("SecretIntentPayload", ToolCall("technician.get_worklist")));
        var unknownArgument = await PlanOnceAsync(AiActorRole.DiagnosticTechnician,
            PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist", "{\"leakedArgumentName\":\"leaked-value\"}")));

        var serialized = JsonSerializer.Serialize(new[] { unknownTool, unknownIntent, unknownArgument });
        foreach (var providerText in new[] { "evil.exfiltrate_records", "SecretIntentPayload", "leakedArgumentName", "leaked-value", "Tôi sẽ tra cứu" })
            Assert.DoesNotContain(providerText, serialized, StringComparison.Ordinal);
        Assert.Null(unknownTool.Diagnostic!.ToolName);
        Assert.Equal("technician.get_worklist", unknownArgument.Diagnostic!.ToolName);
    }

    [Fact]
    public async Task Envelope_text_is_the_ordered_non_thought_text_of_the_first_candidate_only()
    {
        var json = PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"));
        var envelope = new JsonObject
        {
            ["candidates"] = new JsonArray(
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["parts"] = new JsonArray(
                            new JsonObject { ["text"] = "internal reasoning", ["thought"] = true },
                            new JsonObject { ["text"] = json[..25] },
                            new JsonObject { ["text"] = json[25..] })
                    },
                    ["finishReason"] = "STOP"
                },
                new JsonObject
                {
                    ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = "{\"ignored\":true}" }) }
                })
        }.ToJsonString();

        var result = await CreatePlanner(new CapturingHandler(_ => RawEnvelope(envelope))).PlanAsync(Request(AiActorRole.DiagnosticTechnician, "danh sách chờ"));

        Assert.True(result.IsSuccess, $"{result.FailureReason} {result.Diagnostic}");
        Assert.Equal("technician.get_worklist", Assert.Single(result.Decision.ToolCalls).Name);
    }

    [Fact]
    public async Task Provider_json_cannot_set_server_owned_operational_metadata()
    {
        var generated = """
        {"plannerSchemaVersion":"1.0","plannerConfidence":0.9,"reply":"Xin chào","primaryIntent":"Greeting","isClear":true,
         "retryable":true,"retryAfterSeconds":3600,"retryAfterUtc":"2099-01-01T00:00:00Z","errorMessage":"provider-controlled",
         "providerAttemptCount":42,"providerWasCalled":false,"isSuccess":false,"status":"RateLimited","failureCode":"RateLimited","correlationId":"provider"}
        """;
        var provider = CreateProvider(new CapturingHandler(_ => Envelope(generated)));

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Success", result.Status);
        Assert.Equal(AiProviderStatusContract.FailureNone, result.FailureCode);
        Assert.False(result.Retryable);
        Assert.Null(result.RetryAfterSeconds);
        Assert.Null(result.RetryAfterUtc);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, result.ProviderAttemptCount);
        Assert.True(result.ProviderWasCalled);
        Assert.NotEqual("provider", result.CorrelationId);
    }

    [Fact]
    public async Task Role_planner_output_cannot_set_server_owned_operational_metadata()
    {
        var generated = PlannerJson("DiagnosticLookup", ToolCall("technician.get_worklist"))[..^1] +
                        ",\"isSuccess\":false,\"status\":\"RateLimited\",\"retryable\":true,\"correlationId\":\"provider\",\"providerAttemptCount\":9}";
        var provider = CreateProvider(new CapturingHandler(_ => Envelope(generated)));

        var result = await provider.PlanRoleCopilotAsync(new AiRolePlannerProviderRequest
        {
            Role = AiActorRole.DiagnosticTechnician,
            Message = "danh sách chờ",
            AllowedTools = AiRolePlannerContract.BuildToolContracts(ToolsFor(AiActorRole.DiagnosticTechnician), ToolsFor(AiActorRole.DiagnosticTechnician).Select(x => x.Name)),
            AllowedIntents = AiRolePlannerContract.AllowedIntents
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Success", result.Status);
        Assert.False(result.Retryable);
        Assert.Equal(1, result.ProviderAttemptCount);
        Assert.Matches("^[0-9a-f]{8}$", result.CorrelationId);
    }

    [Fact]
    public async Task Legacy_chat_parse_failure_carries_a_closed_diagnostic()
    {
        var provider = CreateProvider(new CapturingHandler(_ => Envelope("{\"reply\":", "MAX_TOKENS")));

        var result = await provider.ChatWithAiAsync("xin chào", new(), new(), "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, result.FailureCode);
        Assert.Equal(AiPlannerValidationStage.ProviderEnvelope, result.Diagnostic?.Stage);
        Assert.Equal(AiPlannerValidationReason.OutputTruncated, result.Diagnostic?.Reason);
        Assert.Equal(AiProviderFinishReason.MaxTokens, result.Diagnostic?.FinishReason);
    }

    // ----------------------------------------------------------- HTTP / canary

    [Fact]
    public async Task Full_stack_http_valid_role_plans_pass_all_seven_cases_with_no_diagnostic()
    {
        using var handler = new RoleAwareFakeGemini(_ => null);

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.PassFull, report.AcceptanceStatus);
        Assert.Equal(7, report.Cases.Count);
        Assert.Equal(6, report.ProviderCallsExecuted);
        Assert.Equal(6, report.ProviderAttemptsExecuted);
        Assert.True(report.DatabaseUnchanged);
        Assert.All(report.Cases, result =>
        {
            Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, result.ValidationStage);
            Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, result.ValidationReason);
            Assert.False(result.ProviderPlanRejected);
        });
        AssertRequestsAreRoleScoped(handler);
    }

    [Fact]
    public async Task Full_stack_http_rejected_patient_and_technician_plans_reach_the_report_with_diagnostics()
    {
        using var handler = new RoleAwareFakeGemini(role => role switch
        {
            AiActorRole.Patient => Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"), version: null)),
            AiActorRole.DiagnosticTechnician => Json(PlannerJson("DiagnosticLookup", "[" + ToolCall("technician.get_worklist")[1..^1] + "," + ToolCall("patient.get_my_bills")[1..^1] + "]")),
            _ => null
        });

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.Fail, report.AcceptanceStatus);
        Assert.Equal(3, LiveCanaryAcceptanceEvaluator.ExitCode(report, requireLive: true));
        Assert.True(report.DatabaseUnchanged);
        Assert.Equal(6, report.ProviderAttemptsExecuted);
        Assert.Equal(2, report.ProviderPlanRejectedCount);

        var patient = Assert.Single(report.Cases, x => x.CaseId == "patient-http-read");
        Assert.Equal(AiChatIntentTypes.ClarificationRequired, patient.ActualIntent);
        Assert.Equal(AiProviderStatusContract.Degraded, patient.ProviderState);
        Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, patient.FailureCode);
        Assert.False(patient.SchemaValid);
        Assert.True(patient.ProviderPlanRejected);
        Assert.Equal(0, patient.ToolExecutions);
        Assert.Equal(AiPlannerErrorCodes.InvalidProviderSchema, patient.ApplicationErrorCode);
        Assert.Equal(nameof(AiPlannerValidationStage.PlannerSchema), patient.ValidationStage);
        Assert.Equal(nameof(AiPlannerValidationReason.MissingSchemaVersion), patient.ValidationReason);
        Assert.Equal(nameof(AiProviderFinishReason.NotReported), patient.ProviderFinishReason);
        Assert.Matches("^[0-9a-f]{8}$", patient.CorrelationId);

        var technician = Assert.Single(report.Cases, x => x.CaseId == "technician-http-read");
        Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, technician.FailureCode);
        Assert.False(technician.SchemaValid);
        Assert.True(technician.ProviderPlanRejected);
        Assert.Equal(0, technician.ToolExecutions);
        Assert.Empty(technician.ToolNames);
        Assert.Equal(AiPlannerErrorCodes.InvalidProviderPlan, technician.ApplicationErrorCode);
        Assert.Equal(nameof(AiPlannerValidationStage.ToolPlan), technician.ValidationStage);
        Assert.Equal(nameof(AiPlannerValidationReason.ToolNotAllowed), technician.ValidationReason);
        Assert.Equal(1, technician.RejectedToolIndex);
        Assert.Equal(2, technician.RejectedPlanToolCount);
        Assert.Null(technician.RejectedToolName);
        Assert.Matches("^[0-9a-f]{8}$", technician.CorrelationId);

        Assert.All(report.Cases.Where(x => x.CaseId is not ("patient-http-read" or "technician-http-read")), result =>
        {
            Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, result.ValidationStage);
            Assert.False(result.ProviderPlanRejected);
            Assert.True(result.SchemaValid);
        });

        var serialized = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("patient.get_my_bills", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Tôi sẽ tra cứu", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_stack_http_truncated_patient_output_and_unknown_technician_intent_are_reported_distinctly()
    {
        using var handler = new RoleAwareFakeGemini(role => role switch
        {
            AiActorRole.Patient => Json(PlannerJson("ViewAppointments", ToolCall("patient.get_my_appointments"))[..48], "MAX_TOKENS"),
            AiActorRole.DiagnosticTechnician => Json(PlannerJson("WorklistLookupX", ToolCall("technician.get_worklist"))),
            _ => null
        });

        var report = await new FullStackHttpCanary().RunAsync(new LiveCanaryReport(), maxCalls: 12, providerHandler: handler);

        Assert.Equal(LiveCanaryAcceptanceEvaluator.Fail, report.AcceptanceStatus);
        Assert.True(report.DatabaseUnchanged);
        Assert.Equal(6, report.ProviderAttemptsExecuted);

        var patient = Assert.Single(report.Cases, x => x.CaseId == "patient-http-read");
        Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, patient.FailureCode);
        Assert.False(patient.SchemaValid);
        Assert.Equal(0, patient.ToolExecutions);
        Assert.Equal(AiProviderStatusContract.FailureInvalidResponse, patient.ApplicationErrorCode);
        Assert.Equal(nameof(AiPlannerValidationStage.ProviderEnvelope), patient.ValidationStage);
        Assert.Equal(nameof(AiPlannerValidationReason.OutputTruncated), patient.ValidationReason);
        Assert.Equal(nameof(AiProviderFinishReason.MaxTokens), patient.ProviderFinishReason);

        var technician = Assert.Single(report.Cases, x => x.CaseId == "technician-http-read");
        Assert.Equal(nameof(AiPlannerValidationReason.InvalidIntent), technician.ValidationReason);
        Assert.Equal(0, technician.ToolExecutions);
        Assert.DoesNotContain("WorklistLookupX", JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_sanitizer_maps_unknown_and_absent_values_without_keeping_them()
    {
        using var document = JsonDocument.Parse("""
        {"errorCode":"SOMETHING_NEW","correlationId":"<script>","plannerDiagnostic":{"stage":"Injected","reason":"ViewWorklist","finishReason":"STOP","rejectedToolIndex":999,"toolCount":-1,"toolName":"evil.tool"}}
        """);

        var diagnostic = CanaryDiagnosticSanitizer.FromResponse(document.RootElement, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "technician.get_worklist" });

        Assert.Equal(CanaryDiagnosticSanitizer.OtherErrorCode, diagnostic.ApplicationErrorCode);
        Assert.Equal(CanaryDiagnosticSanitizer.Unknown, diagnostic.ValidationStage);
        Assert.Equal(CanaryDiagnosticSanitizer.Unknown, diagnostic.ValidationReason);
        Assert.Equal(CanaryDiagnosticSanitizer.Unknown, diagnostic.FinishReason);
        Assert.Null(diagnostic.CorrelationId);
        Assert.Null(diagnostic.RejectedToolIndex);
        Assert.Null(diagnostic.ToolCount);
        Assert.Null(diagnostic.RejectedToolName);

        using var empty = JsonDocument.Parse("{}");
        var absent = CanaryDiagnosticSanitizer.FromResponse(empty.RootElement, new HashSet<string>());
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, absent.ApplicationErrorCode);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, absent.ValidationStage);
        Assert.Equal(CanaryDiagnosticSanitizer.NotAvailable, absent.ValidationReason);
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertRequestsAreRoleScoped(RoleAwareFakeGemini handler)
    {
        Assert.Equal(6, handler.Requests.Count);
        foreach (var (role, body) in handler.Requests)
        {
            var granted = ToolsFor(role).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var foreign in AiPlannerPolicy.AllowedToolNames.Where(x => !granted.Contains(x)))
                Assert.DoesNotContain(foreign, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("canary.", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CANARY-", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic Patient", body, StringComparison.Ordinal);
        }
    }

    private static void AssertOnlySupportedKeywords(JsonNode? node, bool isPropertyMap = false)
    {
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "enum", "description", "properties", "required", "additionalProperties",
            "items", "maxItems", "minimum", "maximum", "anyOf"
        };
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (!isPropertyMap) Assert.Contains(key, supported);
                    AssertOnlySupportedKeywords(value, !isPropertyMap && key == "properties");
                }
                break;
            case JsonArray array:
                foreach (var item in array) AssertOnlySupportedKeywords(item);
                break;
        }
    }

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(x => (string)x!).ToArray() : node is JsonValue value ? new[] { (string)value! } : Array.Empty<string>();

    private static async Task<AiStructuredPlannerResult> PlanOnceAsync(AiActorRole role, string generatedJson, AiResolvedResourceContext? resource = null)
    {
        var planner = CreatePlanner(new CapturingHandler(_ => Envelope(generatedJson)));
        return await planner.PlanAsync(Request(role, "tôi cần hỗ trợ", resource));
    }

    private static string WithoutCorrelation(AiStructuredPlannerResult result)
    {
        var node = JsonSerializer.SerializeToNode(result)!.AsObject();
        node.Remove(nameof(AiStructuredPlannerResult.CorrelationId));
        return node.ToJsonString();
    }

    internal static AiStructuredPlannerRequest Request(AiActorRole role, string message, AiResolvedResourceContext? resource = null)
    {
        var tools = ToolsFor(role);
        return new AiStructuredPlannerRequest
        {
            Role = role,
            Message = message,
            ConversationId = "conv_contract",
            Resource = resource ?? new AiResolvedResourceContext(),
            AllowedTools = tools,
            AllowedToolNames = tools.Select(x => x.Name).ToArray()
        };
    }

    // Mirrors RoleAwareCopilotOrchestrator.ToolsForRole: the professional
    // catalog plus patient read/public tools for an authenticated patient.
    internal static IReadOnlyList<AiToolDefinition> ToolsFor(AiActorRole role) =>
        AiRoleToolCatalog.Definitions
            .Where(x => x.AllowedRoles.Contains(role))
            .Concat(role == AiActorRole.Patient
                ? ClinicManagement.Infrastructure.AI.Tools.PatientCopilotToolHandler.Definitions()
                    .Where(x => x.Name.StartsWith("patient.get_", StringComparison.OrdinalIgnoreCase) || x.AccessMode == AiToolAccessMode.Public)
                : Enumerable.Empty<AiToolDefinition>())
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

    internal static GeminiStructuredPlanner CreatePlanner(HttpMessageHandler handler, int maxAttempts = 1) =>
        new(CreateProvider(handler, maxAttempts), new AiProviderHealth(), NullLogger<GeminiStructuredPlanner>.Instance);

    internal static GeminiAiProvider CreateProvider(HttpMessageHandler handler, int maxAttempts = 1) => new(
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
        NullLogger<GeminiAiProvider>.Instance);

    internal static string ToolCall(string name, string arguments = "{}", string version = "\"1.0\"") =>
        $"[{{\"name\":\"{name}\",\"version\":{version},\"arguments\":{arguments}}}]";

    private static string Calls(string name, int count) =>
        "[" + string.Join(",", Enumerable.Repeat(ToolCall(name)[1..^1], count)) + "]";

    internal static string PlannerJson(
        string intent,
        string toolCalls,
        string? version = "1.0",
        string confidence = "0.9",
        string isClear = "true",
        string clarification = "null") =>
        "{" +
        (version is null ? string.Empty : $"\"plannerSchemaVersion\":\"{version}\",") +
        $"\"plannerConfidence\":{confidence},\"primaryIntent\":\"{intent}\",\"isClear\":{isClear}," +
        $"\"clarification\":{clarification},\"reply\":\"Tôi sẽ tra cứu dữ liệu được cấp.\",\"toolCalls\":{toolCalls}" +
        "}";

    /// <summary>Synthetic GenerateContent envelope around one generated text.</summary>
    internal static string Json(string generatedText, string? finishReason = "STOP")
    {
        var candidate = new JsonObject
        {
            ["content"] = new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = generatedText })
            }
        };
        if (finishReason is not null) candidate["finishReason"] = finishReason;
        return new JsonObject { ["candidates"] = new JsonArray(candidate) }.ToJsonString();
    }

    internal static HttpResponseMessage Envelope(string generatedText, string? finishReason = "STOP") =>
        RawEnvelope(Json(generatedText, finishReason));

    internal static HttpResponseMessage RawEnvelope(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    internal sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;
        private readonly List<string> _bodies = new();

        public CapturingHandler(Func<string, HttpResponseMessage> respond) => _respond = respond;

        public IReadOnlyList<string> Bodies
        {
            get { lock (_bodies) return _bodies.ToArray(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_bodies) _bodies.Add(body);
            return _respond(body);
        }
    }

    /// <summary>
    /// Fake Gemini for the full-stack canary. It identifies the actor only by
    /// which catalog read tool the request schema grants, returns a valid plan
    /// for that tool unless an override envelope is supplied, and never opens
    /// a socket.
    /// </summary>
    private sealed class RoleAwareFakeGemini : HttpMessageHandler
    {
        private static readonly IReadOnlyList<(AiActorRole Role, string Tool, string Intent)> Catalog = new[]
        {
            (AiActorRole.Patient, "patient.get_my_appointments", AiChatIntentTypes.ViewAppointments),
            (AiActorRole.Receptionist, "reception.get_today_appointments", AiChatIntentTypes.QueueLookup),
            (AiActorRole.Doctor, "doctor.get_my_queue", AiChatIntentTypes.QueueLookup),
            (AiActorRole.DiagnosticTechnician, "technician.get_worklist", AiChatIntentTypes.DiagnosticLookup),
            (AiActorRole.Pharmacist, "pharmacist.get_prescription_queue", AiChatIntentTypes.PrescriptionLookup),
            (AiActorRole.Admin, "admin.get_dashboard_metrics", AiChatIntentTypes.AdminMetrics)
        };

        private readonly Func<AiActorRole, string?> _override;
        private readonly List<(AiActorRole, string)> _requests = new();

        public RoleAwareFakeGemini(Func<AiActorRole, string?> overrideEnvelope) => _override = overrideEnvelope;

        public IReadOnlyList<(AiActorRole Role, string Body)> Requests
        {
            get { lock (_requests) return _requests.ToArray(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var schema = JsonNode.Parse(body)!["generationConfig"]!["responseFormat"]!["text"]!["schema"]!;
            var items = schema["properties"]!["toolCalls"]!["items"]!.AsObject();
            var granted = (items.ContainsKey("anyOf") ? items["anyOf"]!.AsArray().Select(x => x!) : new JsonNode[] { items })
                .Select(x => (string)x["properties"]!["name"]!["enum"]![0]!)
                .ToHashSet(StringComparer.Ordinal);
            var match = Catalog.Single(x => granted.Contains(x.Tool));
            lock (_requests) _requests.Add((match.Role, body));

            return RawEnvelope(_override(match.Role) ?? Json(PlannerJson(match.Intent, ToolCall(match.Tool))));
        }
    }
}
