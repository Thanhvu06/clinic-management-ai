# Phase 2 evaluation report

## Test assets

The existing synthetic copilot benchmark remains under
`src/tools/ClinicManagement.AI.Training/data/patient_copilot_benchmark.json`.
The new independent contract tests are in
`src/backend/ClinicManagement.IntegrationTests/AiPhase2ConversationIntelligenceTests.cs`.
They cover interrogative doctor-name rejection, reason extraction, trailing
escape cleanup, gibberish/command rejection, safety ordering, typed entities,
provider-state mapping and read-only role catalog invariants.
The HTTP-level role tests are in
`src/backend/ClinicManagement.IntegrationTests/AiPhase2RoleCopilotIntegrationTests.cs`;
they authenticate synthetic receptionist, doctor, technician, pharmacist and
admin users against the SQLite test host, execute the real role gateway, and
verify safety/argument fail-closed behavior.

The benchmark currently contains 70 patient cases and is therefore not claimed
as the Phase 2 target of 120 patient + 30/30/20/20/20 actor cases. This is an
explicit limitation, not a fabricated metric. Live Gemini execution is
`liveGeminiExecuted=false` unless a safe CI secret is configured.

## Acceptance gates

- deterministic safety and must-not-execute cases: exercised by existing AI
  safety/contract suites plus the new pipeline tests;
- role tools: server-owned catalog, facility/assignment scope and read-only
  execution path;
- full cross-actor business state: existing integration workflows remain the
  source of evidence and are not replaced by mocked cards;
- semantic benchmark expansion and live provider: pending follow-up work.
