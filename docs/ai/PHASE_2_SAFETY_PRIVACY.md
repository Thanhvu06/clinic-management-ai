# Phase 2 safety and privacy

Safety runs before provider calls and before tool execution. Emergency reports
take priority over booking or prompt-injection text. Negation is clause-aware,
so `tôi không đau ngực, chỉ đau bụng nhẹ` is not treated as an emergency while
an affirmative red flag in a later clause is.

Prompt injection, requests to impersonate an admin, requests to print patient
records, and direct `patient.execute_confirmed_action` requests are rejected.
Planner preflight validates every call before any call executes.

Audit events contain action type, outcome, role/capability, correlation ID and
safe metadata. They do not contain bearer tokens, confirmation tokens,
idempotency keys, passwords, API keys or unnecessary raw medical conversation.

Live Gemini is optional. Tests use the fake provider and deterministic local
pipeline when no key is configured. No patient data is used for training.
