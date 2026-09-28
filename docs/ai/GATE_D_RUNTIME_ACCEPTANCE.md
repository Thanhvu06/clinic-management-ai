# Gate D runtime acceptance

Gate D keeps normal push/PR CI deterministic. Gemini live execution is isolated in the manual-only workflow `.github/workflows/ai-live-canary.yml`.

## Live canary safety contract

The canary runs only when all of these are true:

- `RUN_LIVE_GEMINI_CANARY=true`.
- `AiProvider__IsEnabled=true`.
- `AiProvider__ApiKey` is present.
- `AiProvider__ModelName` is non-empty and passes the workflow character allow-list.
- The canary uses synthetic actor prompts and never builds an API/DB host.
- `LIVE_GEMINI_MAX_CALLS` is bounded to 1–12; the default is 12.

The report contains only case IDs, actor/category labels, intent/tool names, stable failure codes, status, counts, and latency percentiles. It does not store prompts, provider responses, headers, keys, tokens, patient identifiers, or database identifiers.

Local opt-in example (keep the key in the process environment and never place it in a file or command history):

```powershell
$env:RUN_LIVE_GEMINI_CANARY = "true"
$env:AiProvider__IsEnabled = "true"
$env:AiProvider__ApiKey = "<configured-out-of-band>"
$env:AiProvider__ModelName = "<explicit-model-name>"
$env:LIVE_GEMINI_MAX_CALLS = "12"
dotnet run --project src/tools/ClinicManagement.AI.LiveCanary/ClinicManagement.AI.LiveCanary.csproj --configuration Release -- --require-live --report "$env:TEMP\cliniccare-gate-d-report.json"
```

The GitHub workflow is the preferred execution path: it is `workflow_dispatch` only, uses `GEMINI_API_KEY`, validates the model/call count without echoing the key, and uploads only the sanitized report. It is not a required push/PR gate.

## Deterministic evidence

The existing HTTP integration suite already contains the cross-actor acceptance workflow in `AiPhase2RoleActionGatewayTests.Cross_actor_schedule_reception_doctor_technician_pharmacist_admin_uses_the_real_workflow_boundary`. It creates synthetic state once, then crosses the real HTTP prepare/confirm/domain boundaries for reception, doctor, technician, pharmacist, patient publication, and admin reads. Related Phase 3.2 tests cover patient/doctor isolation, reassignment, facility/department scope, publication gates, and degraded reads.

The Gate D CI step runs provider resilience, configuration redaction, degraded capability, and cross-actor tests. The live workflow is intentionally separate.

## Manual browser check

This repository has Vitest but no Playwright/Cypress browser harness. For a synthetic Development seed, manually verify:

1. Patient booking retains the selected draft after a provider timeout/503 response; no confirmation is sent automatically.
2. Copilot shows the stable degraded/disabled/circuit message and continues to render deterministic read cards.
3. Aborting a request produces no error bubble and does not clear the draft.
4. Switching account or route while a response is pending does not render the old response.
5. Prepare and confirm remain separate requests; double-clicking does not create duplicate side effects.

Manual browser execution is evidence for UI runtime behavior only; it must not be described as browser E2E automation.
