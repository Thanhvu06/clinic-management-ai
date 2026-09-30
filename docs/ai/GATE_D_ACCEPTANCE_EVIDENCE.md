# Gate D acceptance evidence

Status: `PARTIAL` — offline, browser, and live smoke evidence are available; the required full budget-12 live run has not been executed.

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
