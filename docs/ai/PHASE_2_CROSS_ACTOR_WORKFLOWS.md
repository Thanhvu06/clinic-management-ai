# Phase 2 cross-actor workflow synchronization

The domain database remains the source of truth. Copilot responses only link to
the existing workspace screens and never copy business state into browser
local state.

| Workflow | Producer | Consumer | Source of truth | Exactly-once protection |
|---|---|---|---|---|
| Booking | Patient | Reception, Doctor | Appointment + slot + persisted facility | Existing appointment idempotency/confirmation, unique slot policy and fail-closed multi-facility selection |
| Check-in | Reception | Doctor queue | PatientVisit | Pending-action lease, existing-visit replay, visit/appointment workflow and concurrency checks |
| Walk-in | Reception | Doctor queue | MPI + PatientVisit | AI action idempotency key plus MRN/queue services; AI action uses an existing verified patient profile |
| Diagnostic order | Doctor | Technician | DiagnosticOrder | `SourceAiActionId` unique index, diagnostic workflow service and notification dedupe |
| Diagnostic result | Technician | Doctor/Patient | DiagnosticResult + publication status | Order/item state machine; patient result is redacted until doctor review |
| Prescription draft | Doctor | Doctor workflow | Prescription draft | Doctor domain draft service; AI cannot issue/finalize it |
| Prescription dispense | Pharmacy | Patient | Prescription + stock transaction | Payment/item eligibility, reservation/dispense state machine and stock transaction idempotency |
| Cancellation/reschedule | Patient/Reception | All actors | Change request + appointment status | Change request idempotency and stale-resource checks |
| Admin visibility | All workflows | Admin | Aggregate domain queries | Read-only metrics, no raw medical prompt |

The new professional copilot queries these same entities through role and
facility-scoped read tools and invokes existing domain services only after
direct human confirmation. It does not create a parallel event system.
Existing notification handlers remain the user-facing notification mechanism;
domain screen refresh/invalidation is the authoritative UI update. The API
role-action evidence is covered by `AiPhase2RoleActionGatewayTests` and
`AiPhase2RoleCopilotIntegrationTests`; the broader producer/consumer rows are
covered by the existing domain integration suites listed above, not by mocked
copilot cards. The gateway tests use a doctor assigned to two facilities and
prove that a receptionist cannot pivot a persisted appointment binding by
supplying a department from another facility; they also prove that reception,
technician and pharmacy read tools do not disclose isolated facility data.
A dedicated frontend role-action confirmation/refetch surface is still a
follow-up safety checkpoint; the current role panel remains read-only.
