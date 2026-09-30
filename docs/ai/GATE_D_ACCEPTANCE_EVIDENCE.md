# Gate D acceptance evidence

Status: `PARTIAL` — offline and browser acceptance checks passed; Gemini live smoke/full runs were blocked before any outbound provider request because no API key was available in the local environment and the configured GitHub automation surface could not dispatch the workflow.

## Scope and baseline

- Repository: `Thanhvu06/clinic-management-ai`
- Working branch: `fix/ai-phase2-completion-real-intelligence-gate`
- Baseline HEAD before this patch: `cb831f7683f8ca539a127202a0484e0d02dec808`
- Protected local files were not staged or changed: `.claude/`, `ai_test_output.txt`.
- No real database, OneDrive checkout, patient data, or Gemini request was used.
- ML.NET remains Shadow/Off.

PR #6 was checked as workflow-only and merged separately into `main` at merge commit `72a1716027b104efa62c1eb6cd6b115aa880e028`. The workflow is present on `origin/main`; the current AI checkout remains on the canary branch.

## Acceptance correction

The evaluator previously accepted an arbitrary six-case report because it checked only aggregate counts and caller-provided boolean flags. It did not require the seven-case Gate D catalog (six provider-backed actor cases plus the Patient legacy read), or compare observed actor, route, request path, and expected tool to the catalog.

The patch now requires the exact catalog with unique case IDs, expected actor/category, endpoint, provider-call expectation, observed actor and navigation route, and expected tool evidence. The legacy Patient read remains deterministic and provider-free, but must retain server-derived grounded evidence. Existing safety, schema, allowlist, scope, grounding, database fingerprint, fatal failure, and attempt-budget checks remain in force.

## Verification

Red regression run before the production correction:

```text
dotnet test ... --filter "FullyQualifiedName~LiveCanaryAcceptanceTests.Full_stack_cannot_accept"
Failed: 2, Passed: 0, Total: 2
```

Green focused run:

```text
dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~LiveCanaryAcceptanceTests|FullyQualifiedName~GeminiAiProviderResilienceTests" --logger "console;verbosity=minimal"
Failed: 0, Passed: 46, Skipped: 0, Total: 46
```

Release build:

```text
dotnet build src/backend/ClinicManagement.sln -c Release --no-restore
0 errors; 11 existing nullable warnings in ReceptionWorkspaceRebuildTests.cs
```

Full backend integration suite:

```text
dotnet test src/backend/ClinicManagement.IntegrationTests/ClinicManagement.IntegrationTests.csproj -c Release --no-build --logger "console;verbosity=minimal"
Failed: 0, Passed: 677, Skipped: 0, Total: 677
```

Browser synthetic acceptance (`npm.cmd run e2e`): `29 PASS`, `0 FAIL`, `2 NOT_COVERED`. It used temporary SQLite and `FakeGeminiHttpHandler` with zero Gemini network calls. Both N1 cancellation scenarios passed; write-confirm and diagnostic publication remain not covered. Frontend production build passed as part of the harness.

The local fail-closed configuration check used `RUN_LIVE_GEMINI_CANARY=true`, budget `1`, and a process-local enabled flag with an empty key. It made zero provider attempts and returned exit code `2` with `PARTIAL/DEGRADED`; no secret value was printed or persisted.

## Live gate

No smoke or full Gemini run was dispatched. Local Development effective metadata was `IsEnabled=true` only for the temporary check, model `gemini-1.5-flash`, timeout `10s`, with the model and enabled flag sources reported without secret values. The normal local Development file has `IsEnabled=false`; the manual workflow supplies the selected model through its input/environment override and uses `GEMINI_API_KEY` from Actions secrets.

The required next live actions are one manual smoke run with budget `1`, then—only if smoke proves a provider-backed case online and valid—one full run with budget `12`, both on ref `fix/ai-phase2-completion-real-intelligence-gate`. Their URLs, SHA, model, attempt counts, and sanitized reports are not available in this evidence because dispatch was blocked.
