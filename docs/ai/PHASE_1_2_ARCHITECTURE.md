# ClinicCare AI Phase 1.2 — Architecture

## Scope

Phase 1.2 adds an explicit, role-aware AI Tool Gateway and the Patient Copilot surface. The activated role is Patient; the capability vocabulary already names Receptionist, Doctor, Diagnostic Technician, Pharmacist and Admin so later phases can add handlers without granting them Patient tools.

## Request path

1. POST /api/v1/ai/chat performs the existing session/draft checks and runs IAiSafetyGuard before Gemini.
2. Gemini returns a bounded JSON planner contract (responseMode, toolCalls, clarification, safety, intent and urgency).
3. The chat service sends the entire planner list to IAiToolExecutor for preflight; more than three calls, unknown/versioned tools, malformed arguments and direct-only tools fail closed before any handler runs.
4. The executor derives the actor and roles from the authenticated backend claims and ICurrentUserService; caller-supplied actor fields are not trusted.
5. AiToolRegistry resolves only canonical, explicitly registered names. It does not reflect arbitrary methods.
6. Handlers call existing ClinicCare services and the canonical availability policy. Their result carries status, data-source metadata and retrieval time.

The direct gateway surface is GET /api/v1/ai/tools and POST /api/v1/ai/tools/execute. Public catalog tools are anonymous; patient-owned tools require an authenticated Patient claim. Confirmed writes are only reachable at POST /api/v1/ai/tool-actions/{actionId}/confirm; the invocation channel is created by the server.

## Writes and concurrency

AiPendingToolAction is a short-lived server record with action/user/session/tool/version/request hash/resource/normalized allowlisted arguments/expiry/state/lease/attempt/error/confirmation/execution/cancellation/idempotency hash/result reference and a row-version token. Raw prompts, model output, passwords, tokens and full medical text are not stored. Cancellation/reschedule preparation uses a 12-minute TTL. Execution:

- requires the same user and session;
- requires an explicit human confirmation endpoint; the browser cannot select an execution channel;
- re-reads the patient appointment and delegates final validation to the existing change-request service;
- returns the prior result for an already executed action;
- uses an atomic lease claim, reclaims stale leases, and binds the downstream AppointmentChangeRequest to SourceAiActionId so a crash after the side effect is idempotent;
- rejects expiry, replay, account mismatch, session mismatch and an active conflicting logical action.

AiSessionCleanupWorker terminalizes expired actions, converts stale leases to retryable state, and purges only retained terminal rows. Permanent AI cancellation/session tombstone tables are not touched.

## Data grounding

The Patient catalog delegates to ISpecialtyService, IDoctorService, IAppointmentAvailabilityPolicy, IAppointmentService, IOrganizationService and IDiagnosticWorkflowService. No fake catalog or availability data is introduced. Typed result envelopes and deterministic display text are produced from verified Data; a single failed read tool does not replace the whole assistant response. The Phase 1.1 booking confirmation store remains the source of truth for booking confirmation.

## Persistence

Migration AddAiPendingToolAction remains immutable. HardenAiPendingToolActionExecution is the additive migration; it backfills legacy rows into terminal/pending state, adds leases/attempts/errors and SourceAiActionId, and protects its down path from silent idempotency-data loss. It must be applied by deployment, not by the Phase 1.2 development task. The guarded script is `docs/ai/PHASE_1_2_HARDENING_SQLSERVER_IDEMPOTENT.sql`.
