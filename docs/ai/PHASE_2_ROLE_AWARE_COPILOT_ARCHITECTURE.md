# ClinicCare AI Phase 2 — Role-aware copilot architecture

## Scope

Phase 2 adds one conversation and policy foundation for all actors. Patient
booking remains backed by the Phase 1.2 session/snapshot/pending-action state
machine. Professional workspaces use the same gateway for scoped reads and a
separate server-owned prepare/confirm surface for role actions backed by an
existing domain service.

```text
raw message
  -> AiTextNormalizer (NFC, controls, whitespace, trailing escape)
  -> AiSafetyGuard (emergency / prompt injection)
  -> RoleAwareCopilotOrchestrator
  -> AiConversationPipeline
       -> server-owned intent classifier
       -> typed entity extraction
  -> AiCopilotContextResolver (actor/role/facility/resource/version)
  -> AiDeterministicPlanner for explicit high-confidence commands
  -> GeminiStructuredPlanner only for ambiguous/complex language
  -> immutable role tool allowlist and whole-plan schema validation
  -> AiToolExecutor preflight (all calls before the first execution)
  -> AiGroundedResponseComposer (local composition of DB-backed results)
  -> sanitized memory projection and safe audit event
```

`AiConversationPipeline` is shared by the patient service and role copilot.
Gemini and ML.NET are untrusted enrichers. They cannot select a user, role,
facility, entity ownership, or confirmation channel.
Gemini calls retain the bounded provider timeout/retry policy, cap structured
output tokens, and pass through a process-wide three-failure circuit breaker.
Provider health state contains counters only and never request content.

## Conversation memory

`EfAiConversationMemoryStore` reuses `AiSessions`; it does not introduce an
in-memory or parallel session authority. The stored JSON contains only bounded
intent/sub-intent metadata, safe entity keys, pending clarification and a
server-validated resource reference. It never stores the raw user message or
tool result. The existing 24-hour session TTL and cleanup worker cover restart
and multi-instance behavior. `RowVersion` plus `ConversationVersion` provides
optimistic concurrency, while every load is bound to `SessionId + UserId +
Role`. Resource hints and versions are revalidated on every turn.

## Boundaries

- Read tools may execute only after role, capability, argument and resource
  scope checks.
- Planner allowlist contains read tools only. Unknown tools, write tools and
  invalid mixed plans are rejected before the first call.
- Patient write operations produce a server-bound `AiPendingToolAction`; only
  the dedicated patient confirmation endpoint can execute them.
- Professional prepare-write operations use the same pending-action entity.
  They are available only through `POST /api/v1/ai/copilot/actions/prepare`;
  the model/planner channel cannot reach them. Execution is available only
  through `POST /api/v1/ai/copilot/actions/{actionId}/confirm`.
- A pending role action binds actor, role, session/conversation, facility,
  resource/version, normalized arguments, request hash, tool version,
  confirmation-token hash, expiry, source action id and idempotency identity.
  Confirmation rechecks assignment, facility, ownership, state,
  schedule/leave/conflict, resource version and domain state before claiming a
  short lease.
- `Appointment.FacilityId` is the durable facility binding for new bookings.
  The server selects it only from an active doctor/specialty assignment; when
  more than one eligible facility exists and no single primary assignment can
  resolve it, booking fails closed until the caller chooses a facility. Role
  reads, check-in, doctor draft actions and context validation use this stored
  binding rather than deriving a facility from any current doctor assignment.
  Legacy unbound appointments are denied for role actions and remain readable
  only through the narrowly scoped legacy authorization fallback.
- Implemented role actions call existing domain services for appointment
  check-in, verified-patient walk-in intake, diagnostic-order creation,
  prescription draft, technician start/result/complete, pharmacy reservation
  and eligible dispense. They cannot issue a prescription, publish a
  technician result, or change roles/permissions.
- Diagnostic-order side effects use `SourceAiActionId` for crash-window replay;
  pending-action and domain uniqueness/notification dedupe prevent duplicate
  downstream records. Patient diagnostic result text remains redacted until a
  doctor review publishes it.
- No frontend-provided user ID, role, facility ID or raw entity snapshot is an
  authorization input.

## Provider state

`AssistantMode` reports `Ready`, `Clarifying`, `Processing`, `Degraded`,
`Unavailable`, or `SafetyBlocked`. `ProviderState` separately reports `NotCalled`, `Online`,
`Degraded`, `Unavailable`, and `SafetyBlocked`. `NotCalled` explicitly means
the turn was handled by deterministic internal logic; it is not displayed as
Gemini being online. Legacy `AssistantStatus` and `ProviderStatus` aliases are
retained for backward compatibility.
