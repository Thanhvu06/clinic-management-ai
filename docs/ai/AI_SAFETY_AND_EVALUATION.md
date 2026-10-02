# AI Safety and Evaluation

## Pre-provider guard

IAiSafetyGuard runs before Gemini and before planner execution. It detects Vietnamese/English prompt-injection requests and emergency red flags including severe chest pain, severe dyspnea, syncope, stroke signs, seizure, uncontrolled bleeding, anaphylaxis, self-harm, pregnancy emergencies and dangerous child symptoms. Negated phrases such as “không đau ngực” and “không bị khó thở” are excluded by clause-aware negation. Every occurrence is evaluated, so a later positive clause (for example “không đau ngực hôm qua nhưng giờ đau ngực dữ dội”) dominates. Emergency wins over mixed prompt injection and no Gemini/tool/diagnosis/prescription path is entered.

Emergency responses use EMERGENCY, direct the patient to call 115 or go to the nearest emergency facility, and do not diagnose, prescribe, or ask the patient to wait for chat completion. Injection responses remain in the ClinicCare scope.

## Gemini and ML.NET

Gemini is a structured planner/grounded response provider. Its tool plan is allowlisted by the immutable backend `AiPlannerPolicy` to exactly seven read tools, capped at three calls, and never receives authority fields or prepare/write tools. The backend executor preflights the complete plan and fails closed before any handler on malformed, unknown, version-mismatched or over-limit plans. The backend executor, not the model, owns identity, role, capability, ownership, session, confirmation and facility policy.

The existing ML.NET intent/specialty classifier remains Shadow/local support. It may provide comparison and degraded-mode suggestions; it cannot authenticate, authorize, write appointments, execute change requests or diagnose.

## Benchmark

src/tools/ClinicManagement.AI.Training/data/patient_copilot_benchmark.json contains deterministic non-sensitive fixtures covering emergency positives/repeats/mixed injection, negation, authority injection, planner contracts, role/session/expiry failures, malformed arguments, pricing grounding, empty data and provider-unavailable behavior. The safety layer directly evaluates `IAiSafetyGuard`; the routing layer is explicitly named `heuristicBaselineRouting` and does not exercise Gemini, HTTP, authorization, session or DB. A separate runtime contract suite exercises those boundaries. Live Gemini is off unless an explicit environment opt-in exists and reports `liveGeminiExecuted: false` when off. Run:

~~~text
dotnet run --project src/tools/ClinicManagement.AI.Training/ClinicManagement.AI.Training.csproj -- --evaluate-copilot
~~~

The runner prints machine-readable JSON with separate safety, `heuristicBaselineRouting`, outcome and safety-content metrics. It is not an AI-accuracy claim and cannot substitute for the HTTP/runtime contract suite.
