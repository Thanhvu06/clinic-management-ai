# Gate D runtime acceptance

Gate D keeps normal push/PR CI deterministic. Gemini live execution is isolated in the manual-only workflow `.github/workflows/ai-live-canary.yml`.

## Live canary safety contract

The canary runs only when all of these are true:

- `RUN_LIVE_GEMINI_CANARY=true`.
- `AiProvider__IsEnabled=true`.
- `AiProvider__ApiKey` is present.
- `AiProvider__ModelName` is non-empty and passes the workflow character allow-list.
- The canary uses synthetic actors and a temporary SQLite WebApplicationFactory only in full-stack mode; it never uses the development database, OneDrive checkout, real patient data, or a production host.
- `LIVE_GEMINI_MAX_CALLS` is bounded to 1–12; the default is 12.

The default live canary is `FullStackHttp`: six authenticated HTTP calls (Patient, Receptionist, Doctor, DiagnosticTechnician, Pharmacist, Admin) go through `/api/v1/ai/copilot/chat`, the real context resolver, Gemini structured planner, read-only tool gateway, and grounded response composer. It executes no action prepare/confirm/write endpoint and compares a sanitized business-state fingerprint before and after. A separate `--planner-only` switch preserves the lower-level planner probe; its report is explicitly labelled `PlannerOnly`.

The report contains only case IDs, actor/category labels, intent/tool names, execution mode, stable failure codes, status, counts, database fingerprints, and latency percentiles. It does not store prompts, provider responses, headers, keys, tokens, patient identifiers, or database identifiers.

Local opt-in example (keep the key in the process environment and never place it in a file or command history):

```powershell
$env:RUN_LIVE_GEMINI_CANARY = "true"
$env:AiProvider__IsEnabled = "true"
$env:AiProvider__ApiKey = "<configured-out-of-band>"
$env:AiProvider__ModelName = "<explicit-model-name>"
$env:AiProvider__MaxAttempts = "2" # six HTTP actor cases x two attempts <= 12 provider calls
$env:LIVE_GEMINI_MAX_CALLS = "12"
dotnet run --project src/tools/ClinicManagement.AI.LiveCanary/ClinicManagement.AI.LiveCanary.csproj --configuration Release -- --require-live --report "$env:TEMP\cliniccare-gate-d-report.json"
```

The GitHub workflow is the preferred execution path: it is `workflow_dispatch` only, uses `GEMINI_API_KEY`, validates the model/call count without echoing the key, and uploads only the sanitized report. It is not a required push/PR gate.

## Deterministic evidence

The Gate D completion suite adds `AiGateDCrossActorWorkflowTests.Synthetic_patient_to_admin_workflow_preserves_scope_state_and_exactly_once_effects`. It creates one synthetic booking and follows it through the real HTTP/action/domain boundaries: Patient → Receptionist → Doctor → Technician → Doctor review → Pharmacist → Admin → Patient, with duplicate confirms, idempotency conflict, cross-patient/facility/doctor/technician denials, publication gating, stock/billing assertions, and final exactly-once checks. DbContext is used only for seed/setup and post-workflow read/assertions.

The Gate D CI step runs provider resilience, configuration redaction, degraded capability, the existing role-action suite, and the exact quoted filter `FullyQualifiedName~AiGateDCrossActorWorkflowTests`. Normal push/PR CI never enables live Gemini; the full-stack canary is manual-only.

The Gate D CI step runs provider resilience, configuration redaction, degraded capability, and cross-actor tests. The live workflow is intentionally separate.

## Manual browser check

This repository has Vitest but no Playwright/Cypress browser harness. For a synthetic Development seed, manually verify:

1. Patient booking retains the selected draft after a provider timeout/503 response; no confirmation is sent automatically.
2. Copilot shows the stable degraded/disabled/circuit message and continues to render deterministic read cards.
3. Aborting a request produces no error bubble and does not clear the draft.
4. Switching account or route while a response is pending does not render the old response.
5. Prepare and confirm remain separate requests; double-clicking does not create duplicate side effects.

Manual browser execution is evidence for UI runtime behavior only; it must not be described as browser E2E automation.
