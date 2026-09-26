# ClinicCare AI Phase 1.2 — Architecture

## Scope

Phase 1.2 adds an explicit, role-aware AI Tool Gateway and the Patient Copilot surface. The activated role is Patient; the capability vocabulary already names Receptionist, Doctor, Diagnostic Technician, Pharmacist and Admin so later phases can add handlers without granting them Patient tools.

## Request path

1. POST /api/v1/ai/chat performs the existing session/draft checks and runs IAiSafetyGuard before Gemini.
2. Gemini returns a bounded JSON planner contract (responseMode, toolCalls, clarification, safety, intent and urgency).
3. The chat service accepts at most three planner calls and sends each call to IAiToolExecutor.
4. The executor derives the actor and roles from the authenticated backend claims and ICurrentUserService; caller-supplied actor fields are not trusted.
5. AiToolRegistry resolves only canonical, explicitly registered names. It does not reflect arbitrary methods.
6. Handlers call existing ClinicCare services and the canonical availability policy. Their result carries status, data-source metadata and retrieval time.

The direct gateway surface is GET /api/v1/ai/tools and POST /api/v1/ai/tools/execute. Public catalog tools are anonymous; patient-owned tools require an authenticated Patient claim.

## Writes and concurrency

AiPendingToolAction is a short-lived server record with action/user/session/tool/version/request hash/resource/normalized allowlisted arguments/expiry/confirmation/execution/cancellation/idempotency hash/result reference and a row-version token. Raw prompts, model output, passwords, tokens and full medical text are not stored. Cancellation/reschedule preparation uses a 12-minute TTL. Execution:

- requires the same user and session;
- requires an explicit confirm: true;
- re-reads the patient appointment and delegates final validation to the existing change-request service;
- returns the prior result for an already executed action;
- rejects expiry, replay, account mismatch and an active conflicting logical action.

AiSessionCleanupWorker purges only expired or retained pending-action rows. Permanent AI cancellation/session tombstone tables are not touched.

## Data grounding

The Patient catalog delegates to ISpecialtyService, IDoctorService, IAppointmentAvailabilityPolicy, IAppointmentService, IOrganizationService and IDiagnosticWorkflowService. No fake catalog or availability data is introduced. The Phase 1.1 booking confirmation store remains the source of truth for booking confirmation.

## Persistence

Migration AddAiPendingToolAction is included in source. It must be applied by deployment, not by the Phase 1.2 development task. An idempotent SQL Server script is generated as a verification artifact during the release gate.

