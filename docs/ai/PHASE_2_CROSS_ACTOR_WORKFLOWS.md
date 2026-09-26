# Phase 2 cross-actor workflow synchronization

The domain database remains the source of truth. Copilot responses only link to
the existing workspace screens and never copy business state into browser
local state.

| Workflow | Producer | Consumer | Source of truth | Exactly-once protection |
|---|---|---|---|---|
| Booking | Patient | Reception, Doctor | Appointment + slot | Existing appointment idempotency/confirmation and unique slot policy |
| Check-in | Reception | Doctor queue | PatientVisit | Visit/appointment workflow and concurrency checks |
| Walk-in | Reception | Doctor queue | MPI + PatientVisit | MRN and queue sequence services |
| Diagnostic order | Doctor | Technician | DiagnosticOrder | Diagnostic workflow service |
| Diagnostic result | Technician | Doctor/Patient | DiagnosticResult + publication status | Order/item state machine |
| Prescription | Doctor | Pharmacy/Patient | Prescription + InvoiceItem | Billing and prescription state machine |
| Cancellation/reschedule | Patient/Reception | All actors | Change request + appointment status | Change request idempotency and stale-resource checks |
| Admin visibility | All workflows | Admin | Aggregate domain queries | Read-only metrics, no raw medical prompt |

The new professional copilot queries these same entities through role and
facility-scoped read tools. It does not create a parallel event system. Existing
notification handlers remain the user-facing notification mechanism; domain
screen refresh/invalidation is the authoritative UI update. The API-level
copilot evidence is covered by
`AiPhase2RoleCopilotIntegrationTests`; the broader producer/consumer rows are
covered by the existing domain integration suites listed above, not by mocked
copilot cards.
