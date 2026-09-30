# Gate D acceptance evidence

Status: `PARTIAL` — the full budget-12 live run was executed once at `a0c8e266` and returned `FAIL` (2 of 7 cases rejected). The role planner contract correction below is verified offline only; a live rerun has not been performed. Gate D is not complete.

## Scope and baseline

- Repository: `Thanhvu06/clinic-management-ai`
- Working branch: `fix/ai-phase2-completion-real-intelligence-gate`
- Baseline HEAD before this patch: `9115b4b9b2cc7b6d535ecddedff57980ff52af68`
- Protected local files were not staged or changed: `.claude/`, `ai_test_output.txt`.
- No real database, OneDrive checkout, or patient data was used. The smoke run used the configured Gemini provider with budget 1 as explicitly supplied evidence.
- ML.NET remains Shadow/Off.

PR #6 was checked as workflow-only and merged separately into `main` at merge commit `72a1716027b104efa62c1eb6cd6b115aa880e028`. The workflow is present on `origin/main`; the current AI checkout remains on the canary branch.

## Acceptance correction

The evaluator previously accepted an arbitrary six-case report because it checked only aggregate counts and caller-provided boolean flags. It did not require the seven-case Gate D catalog (six provider-backed actor cases plus the Patient legacy read), or compare observed actor, route, request path, and expected tool to the catalog.

The patch now requires the exact catalog with unique case IDs, expected actor/category, endpoint, provider-call expectation, observed actor and navigation route, and expected tool evidence. The legacy Patient read remains deterministic and provider-free, but must retain server-derived grounded evidence. Existing safety, schema, allowlist, scope, grounding, database fingerprint, fatal failure, and attempt-budget checks remain in force.

Navigation is now a typed canary contract: provider-backed read cases are `Optional`, so a null navigation is valid; a returned route must be the catalog destination and an internal path. `Required` rejects null and accepts only the exact internal destination. The Patient legacy case is `NotApplicable` and does not pretend that request route or authenticated account is an observed navigation response. Reports separate `RequestPath`, `RequestCurrentRoute`, `ObservedNavigationRoute`, `NavigationExpectation`, and `NavigationVerified` in schema `gate-d-v3-navigation-contract`.

## Verification

Red regression run before the production correction:

```text
dotnet test ... --filter "FullyQualifiedName~LiveCanaryAcceptanceTests.Full_stack_cannot_accept"
Failed: 2, Passed: 0, Total: 2
```

Navigation red regression before the production correction:

```text
dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~LiveCanaryAcceptanceTests.Full_stack_allows_null_navigation_for_an_optional_read_case" --logger "console;verbosity=minimal"
Failed: 1, Passed: 0, Total: 1
```

Green focused run:

```text
dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~LiveCanaryAcceptanceTests|FullyQualifiedName~GeminiAiProviderResilienceTests" --logger "console;verbosity=minimal"
Failed: 0, Passed: 54, Skipped: 0, Total: 54
```

Full-stack fake-provider regression:

```text
dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~LiveCanaryAcceptanceTests.Full_stack_fake_provider_accepts_a_read_response_without_navigation" --logger "console;verbosity=minimal"
Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

Release build:

```text
dotnet build src/tools/ClinicManagement.AI.LiveCanary/ClinicManagement.AI.LiveCanary.csproj -c Release --no-restore
0 errors; 0 warnings
```

Full backend integration suite:

```text
dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Release --no-build --logger "console;verbosity=minimal"
Failed: 0, Passed: 685, Skipped: 0, Total: 685
```

Browser synthetic acceptance (`npm.cmd run e2e`): `29 PASS`, `0 FAIL`, `2 NOT_COVERED`. It used temporary SQLite and `FakeGeminiHttpHandler` with zero Gemini network calls. Both N1 cancellation scenarios passed; write-confirm and diagnostic publication remain not covered. Frontend production build passed as part of the harness.

The local fail-closed configuration check used `RUN_LIVE_GEMINI_CANARY=true`, budget `1`, and a process-local enabled flag with an empty key. It made zero provider attempts and returned exit code `2` with `PARTIAL/DEGRADED`; no secret value was printed or persisted.

## Live gate

Smoke evidence: [GitHub Actions run 36682959355](https://github.com/Thanhvu06/clinic-management-ai/actions/runs/36682959355), ref SHA `9115b4b9b2cc7b6d535ecddedff57980ff52af68`, model `gemini-3.5-flash-lite`. It used budget `1`, executed one provider attempt, remained online with valid schema/grounding/tool scope, and left six HTTP cases unexecuted. `ObservedNavigationRoute` was null and `NavigationVerified` was false under the old contract, so the run correctly reported `PARTIAL/DEGRADED` with exit code `2`; the patch changes the read-case interpretation for the next run without rewriting this historical report.

No additional Gemini request, dispatch, or workflow rerun was performed in this patch. The full budget-12 run remains outstanding and must use ref `fix/ai-phase2-completion-real-intelligence-gate` after CI review.

## Full live run at `a0c8e266` (historical, unchanged)

Evidence: [GitHub Actions run 36686702159](https://github.com/Thanhvu06/clinic-management-ai/actions/runs/36686702159), `workflow_dispatch` on `fix/ai-phase2-completion-real-intelligence-gate`, head SHA `a0c8e266c324f02800f53e595ece7f6acd7ef5bb`, model `gemini-3.5-flash-lite`, conclusion `failure`.

| Field | Value |
| --- | --- |
| CallsBudget | 12 |
| CallsPlanned / CallsExecuted (HTTP cases) | 7 / 7 |
| ProviderCallsExecuted / ProviderAttemptsExecuted | 6 / 6 |
| UnexecutedCaseCount | 0 |
| Rate limit, timeout, authentication, model, server failures | 0 |
| DatabaseUnchanged | true |
| AcceptanceStatus / exit code | `FAIL` / 3 |

`patient-http-read` and `technician-http-read` were rejected: `ActualIntent=ClarificationRequired`, `ProviderState=Degraded`, `FailureCode=InvalidResponse`, `SchemaValid=false`, `ProviderPlanRejected=true`, and no tool ran. Receptionist, Doctor, Pharmacist, Admin, and the provider-free Patient legacy read passed with grounded data. The navigation contract behaved as intended. This report stays `FAIL`; it is not rewritten.

What the report does and does not show:

- Code at `a0c8e266` narrows the two rejections to `INVALID_PROVIDER_SCHEMA` or `INVALID_PROVIDER_PLAN` from the structured planner. A provider-level parse failure would have surfaced `ErrorCode=InvalidResponse`, which the canary counts as `SchemaValid=true, ProviderPlanRejected=false`; preflight codes (for example `PLANNER_TOOL_NOT_ALLOWED`) would have produced `FailureCode=UnknownProviderFailure`. So the generated JSON was syntactically parseable into the chat DTO in both cases.
- The report had no detailed validation code, so the exact field (schema version, confidence, intent, tool name/version, or arguments) behind either historical rejection is **unknown**. The raw outputs were not recorded and cannot be recovered. No root cause is claimed for them.

## Role planner contract correction (offline evidence only)

Code-level defects proven at `a0c8e266` with a capturing fake HTTP handler, before the fix (`AiRolePlannerContractTests`, 14 tests, all red):

- The role planner reused the legacy patient chat prompt, which listed all 25 planner tools from `AiPlannerPolicy` regardless of role (6 roles red).
- The request declared no response schema (6 roles red).
- A missing schema version and an unknown intent produced identical planner results except for correlation, so rejections were not diagnosable (1 red).
- Generated JSON was deserialized into `AiChatProviderResult`, so a provider could set `retryable`, `retryAfterSeconds`, `retryAfterUtc`, and `errorMessage` on a successful result (1 red).

Correction:

- `IAiSpecialtySuggestionProvider.PlanRoleCopilotAsync` is a separate, server-selected typed contract. The legacy patient chat keeps `ChatWithAiAsync`, its booking fields, and its prompt text; only its parser now reads model-facing fields into a private DTO.
- The role planner sends a role-specific `systemInstruction` naming only the tools granted for the request (role catalog ∩ enabled ∩ `AiPlannerPolicy`), with server-bound arguments removed, no resource IDs, route, user/facility data, tokens, or idempotency keys, and only boolean open-resource facts.
- The response schema is sent as `generationConfig.responseFormat.text.{mimeType: "application/json", schema}` per the [structured output guide](https://ai.google.dev/gemini-api/docs/generate-content/structured-output). It is generated from the same definitions the validator uses: `plannerSchemaVersion` enum `1.0`, `plannerConfidence` number in [0, 1], `primaryIntent` enum from `AiChatIntentTypes.All`, `isClear` boolean, nullable `clarification`, `toolCalls` array with `maxItems: 3` and one `anyOf` branch per granted tool (name and version enums, argument object with `additionalProperties: false` and required arguments). Only guide-listed keywords are used.
- Schema limits that stay in the server validator: forbidden authority names at any depth, resource binding and mismatch, `isClear=false` with tool calls, and a non-empty clarification. The model table in the guide lists Gemini 3.5 Flash and 3.1 Flash-Lite; `gemini-3.5-flash-lite` is not listed, so acceptance of this schema by that model is unverified until a live run.
- The envelope reader uses only the first candidate, joins its non-thought text parts in order, and reports `MAX_TOKENS` as `OutputTruncated` and other non-`STOP` finish reasons as `UnsupportedFinishReason`. Invalid output is never repaired or retried; nothing is defaulted, dropped, or renamed; a mixed plan is rejected before the first tool.
- Rejections carry a closed diagnostic: stage (`ProviderEnvelope`, `GeneratedJson`, `PlannerSchema`, `ToolPlan`, `ResourceBinding`), reason, known field, rejected tool position and count, canonical tool name only when allowlisted, and finish reason. It flows planner → `AiCopilotResponseDto.plannerDiagnostic` → canary report (`ApplicationErrorCode`, `ValidationStage`, `ValidationReason`, `ProviderFinishReason`, `CorrelationId`, `RejectedToolIndex`, `RejectedPlanToolCount`, `RejectedToolName`) under schema `gate-d-v4-rejection-diagnostics`. Unknown values become `Unknown`/`Other`; absent diagnostics are `NotAvailable`. Existing `ErrorCode` and `ProviderFailureCode` wire values are unchanged. Diagnostics only tighten the canary: an envelope, JSON, or schema rejection is never schema-valid, and a tool-plan or binding rejection counts as a provider plan rejection. Evaluator acceptance is unchanged.

Synthetic HTTP fixtures (temporary SQLite, real orchestrator, resolver, gateway, and composer) show a valid role plan for all six actors passing `PASS_FULL`, and rejected Patient and Technician plans reaching the report with their stage and reason, zero tool executions, and an unchanged database fingerprint. These fixtures prove the contract and instrumentation; they do not reproduce the lost live outputs.

Verification of this correction (local, no Gemini network):

```text
# Before the fix, at a0c8e266 (Debug)
dotnet test ... --filter "FullyQualifiedName~AiRolePlannerContractTests"
Failed: 14, Passed: 0, Total: 14

# After the fix (Release, --no-build)
AiRolePlannerContractTests                          Failed: 0, Passed: 74,  Total: 74
LiveCanaryAcceptanceTests (incl. fake FullStack 7)  Failed: 0, Passed: 26,  Total: 26
GeminiAiProviderResilienceTests                     Failed: 0, Passed: 28,  Total: 28
Planner/binding/authorization suites                Failed: 0, Passed: 170, Total: 170
Full backend integration suite (Debug and Release)  Failed: 0, Passed: 759, Total: 759
dotnet ef migrations has-pending-model-changes      No changes have been made to the model since the last migration.
git diff --check                                    clean
```

Live attempts during this correction: 0. The next live run should use the same budget 12 on this branch and read `ValidationStage`/`ValidationReason` for any rejected case before any further change.
