# AI Safety and Evaluation

## Pre-provider guard

IAiSafetyGuard runs before Gemini and before planner execution. It detects Vietnamese/English prompt-injection requests and emergency red flags including severe chest pain, severe dyspnea, syncope, stroke signs, seizure, uncontrolled bleeding, anaphylaxis, self-harm, pregnancy emergencies and dangerous child symptoms. Negated phrases such as “không đau ngực” and “không bị khó thở” are excluded by a bounded negation window.

Emergency responses use EMERGENCY, direct the patient to call 115 or go to the nearest emergency facility, and do not diagnose, prescribe, or ask the patient to wait for chat completion. Injection responses remain in the ClinicCare scope.

## Gemini and ML.NET

Gemini is a structured planner/grounded response provider. Its tool plan is allowlisted, capped at three calls and never receives authority fields. Malformed, unknown or over-limit plans fail closed. The backend executor, not the model, owns identity, role, ownership, confirmation and facility policy.

The existing ML.NET intent/specialty classifier remains Shadow/local support. It may provide comparison and degraded-mode suggestions; it cannot authenticate, authorize, write appointments, execute change requests or diagnose.

## Benchmark

src/tools/ClinicManagement.AI.Training/data/patient_copilot_benchmark.json contains 60 non-sensitive structured cases covering emergency positives, negation, injection, catalog reads, owned appointment reads, booking preparation and appointment changes. Run:

~~~text
dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj -- --evaluate-copilot
~~~

The runner prints machine-readable JSON with actual safety, route, outcome and safety-content metrics. It is a deterministic safety/allowlist evaluator, not a claim of Gemini semantic accuracy.

