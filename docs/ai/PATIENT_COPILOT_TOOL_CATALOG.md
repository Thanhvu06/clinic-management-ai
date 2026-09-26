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
| patient.execute_confirmed_action | Patient | High | explicit user confirmation | revalidates and creates change request |

The gateway does not expose write tools for Receptionist, Doctor, Diagnostic Technician, Pharmacist or Admin in this phase. Their role/capability names are reserved for a later, separately reviewed catalog.

Tool result statuses are completed, pending_confirmation and failed. Results identify verified data sources; they do not expose internal prompts, authorization claims, database credentials, API keys or raw audit payloads.

