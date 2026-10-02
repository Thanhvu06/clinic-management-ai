# Patient Copilot Tool Catalog

All tools use version 1.0. The registry is explicit and rejects duplicate or unknown names.

| Canonical tool | Access | Risk | Confirmation | Grounding |
|---|---|---:|---|---|
| clinic.search_specialties | Public | Low | None | active specialties |
| clinic.search_doctors | Public | Low | None | active doctors |
| clinic.get_available_slots | Public | Low | None | IAppointmentAvailabilityPolicy |
| clinic.get_facilities | Public | Low | None | active facilities |
| clinic.get_pricing | Public | Low | None | specialty and diagnostic pricing services |
| patient.get_my_appointments | Patient | Low | None | owned appointments only |
| patient.get_appointment_detail | Patient | Low | None | owned appointment only |
| patient.prepare_booking | Patient | Medium | existing booking confirmation | availability preview only |
| patient.prepare_cancel_appointment | Patient | High | explicit user confirmation | creates pending action |
| patient.prepare_reschedule_appointment | Patient | High | explicit user confirmation | creates pending action |
| patient.execute_confirmed_action | Patient | High | direct human confirmation endpoint only | revalidates and creates an idempotent change request |

The gateway does not expose write tools for Receptionist, Doctor, Diagnostic Technician, Pharmacist or Admin in this phase. Their role/capability names are reserved for a later, separately reviewed catalog.

The immutable backend `AiPlannerPolicy` contains exactly these seven read tools and compares canonical names ordinal-case-insensitively. Prepare tools are registry entries for an explicit server-side/user-initiated flow; they are rejected from Gemini planner output and from the generic planner gateway with `PLANNER_TOOL_NOT_ALLOWED`. This phase does not invent a model-driven cancel/reschedule write route: the Patient UI must navigate to the existing appointment/change-request flow until a separately tested prepare surface exists. `patient.execute_confirmed_action` is DirectHumanConfirmation-only. Every tool has a closed JSON schema: unknown fields, nested objects, authority fields, invalid identifiers, dates, ranges, pagination and excessive text are rejected with stable errors. Tool result statuses are completed, pending_confirmation and failed, with a typed resultType/displayText envelope. Results identify verified data sources; they do not expose internal prompts, authorization claims, database credentials, API keys or raw audit payloads.
