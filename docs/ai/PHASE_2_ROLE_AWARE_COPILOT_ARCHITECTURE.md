# ClinicCare AI Phase 2 — Role-aware copilot architecture

## Scope

Phase 2 adds one conversation and policy foundation for all actors. Patient
booking remains backed by the Phase 1.2 session/snapshot/pending-action state
machine. Professional workspaces use the same gateway and a read-only copilot
surface in this phase.

```text
raw message
  -> AiTextNormalizer (NFC, controls, whitespace, trailing escape)
  -> AiSafetyGuard (emergency / prompt injection)
  -> AiConversationPipeline
       -> server-owned intent classifier
       -> typed entity extraction
  -> role/facility capability resolution from authenticated claims + DB
  -> immutable tool allowlist and schema validation
  -> grounded read result OR pending write action
  -> deterministic response and safe audit event
```

`AiConversationPipeline` is shared by the patient service and role copilot.
Gemini and ML.NET are untrusted enrichers. They cannot select a user, role,
facility, entity ownership, or confirmation channel.

## Boundaries

- Read tools may execute only after role, capability, argument and resource
  scope checks.
- Planner allowlist contains read tools only. Unknown tools, write tools and
  invalid mixed plans are rejected before the first call.
- Patient write operations produce a server-bound `AiPendingToolAction`; only
  the dedicated confirmation endpoint can execute it.
- Role copilot writes are intentionally absent. Check-in, clinical drafts,
  diagnostic results, prescriptions and dispense remain domain endpoints with
  their existing human workflow.
- No frontend-provided user ID, role, facility ID or raw entity snapshot is an
  authorization input.

## Provider state

`ProviderState` is the stable client contract: `NotCalled`, `Online`,
`Degraded`, `Unavailable`, and `SafetyBlocked`. `NotCalled` explicitly means
the turn was handled by deterministic internal logic; it is not displayed as
Gemini being online. The legacy `ProviderStatus` detail is retained for
backward compatibility with existing clients and diagnostics.
