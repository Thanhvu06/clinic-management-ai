# Phase 2 tool catalog

All entries below are registered in the server-owned `AiToolRegistry`. The
planner policy contains read entries only. Data is returned as typed tool
results with a source label; no tool accepts authorization fields from a model.

| Tool | Actor | Read/Write | Permission | Confirmation | Data source |
|---|---|---|---|---|---|
| `clinic.search_specialties` | All | Read | Public catalog | No | Specialty service |
| `clinic.search_doctors` | All | Read | Public catalog | No | Doctor service |
| `clinic.get_available_slots` | All | Read | Availability policy | No | Slot/leave/appointment policy |
| `clinic.get_facilities` | All | Read | Public catalog | No | Organization service |
| `clinic.get_pricing` | All | Read | Published catalog | No | Specialty/diagnostic catalog |
| `patient.get_my_appointments` | Patient | Read | Own patient | No | Appointment service |
| `patient.get_appointment_detail` | Patient | Read | Own patient | No | Appointment service |
| `patient.prepare_booking` | Patient | Prepare write | Own patient + availability | Existing booking confirmation | Availability policy |
| `patient.prepare_cancel_appointment` | Patient | Prepare write | Own appointment | Explicit confirmation | Appointment/change-request service |
| `patient.prepare_reschedule_appointment` | Patient | Prepare write | Own appointment + new slot | Explicit confirmation | Availability policy |
| `patient.execute_confirmed_action` | Patient | Write | Dedicated endpoint only | Backend token | Pending action store + domain service |
| `reception.get_today_appointments` | Receptionist | Read | Appointment's persisted facility + receptionist assignment | No | Appointment + facility binding |
| `reception.get_queue` | Receptionist | Read | Assigned facility | No | Patient visit queue |
| `reception.lookup_appointment` | Receptionist | Read | Appointment's persisted facility + receptionist assignment | No | Appointment code + facility binding |
| `reception.prepare_check_in_appointment` | Receptionist | Prepare write | Persisted appointment facility + active doctor/reception scope | Explicit confirmation | PatientVisit domain service |
| `reception.prepare_create_walk_in` | Receptionist | Prepare write | Assigned facility + verified patient scope | Explicit confirmation | ReceptionIntake domain service |
| `doctor.get_my_queue` | Doctor | Read | Assigned doctor | No | Patient visit queue |
| `doctor.get_patient_summary` | Doctor | Read | Assigned appointment | No | Appointment/encounter |
| `doctor.get_diagnostic_orders` | Doctor | Read | Ordering doctor | No | Diagnostic orders |
| `doctor.prepare_diagnostic_order` | Doctor | Prepare write | Assigned appointment/visit and facility | Explicit confirmation | Diagnostic workflow service |
| `doctor.prepare_prescription_draft` | Doctor | Prepare write | Assigned appointment/visit and facility | Explicit confirmation | Doctor appointment domain service |
| `technician.get_worklist` | Technician | Read | Assigned facility | No | Diagnostic orders |
| `technician.prepare_start_diagnostic_order` | Technician | Prepare write | Assigned facility and order | Explicit confirmation | Diagnostic workflow service |
| `technician.prepare_record_diagnostic_result` | Technician | Prepare write | Assigned facility and order item | Explicit confirmation | Diagnostic workflow service |
| `technician.prepare_complete_diagnostic_order` | Technician | Prepare write | Assigned facility and order | Explicit confirmation | Diagnostic workflow service |
| `pharmacist.get_prescription_queue` | Pharmacist | Read | Assigned facility | No | Issued prescriptions + visit |
| `pharmacist.get_inventory_status` | Pharmacist | Read | Active pharmacist facility assignment required; result is global because Medicine has no facility key | No | Medicine catalog |
| `pharmacist.prepare_reserve_prescription` | Pharmacist | Prepare write | Assigned facility + prescription visit | Explicit confirmation | Pharmacy domain service |
| `pharmacist.prepare_dispense_prescription` | Pharmacist | Prepare write | Assigned facility + paid eligible prescription | Explicit confirmation | Pharmacy domain service |
| `admin.get_dashboard_metrics` | Admin | Read | Admin role | No | Aggregate domain counts |
| `admin.get_ai_health` | Admin | Read | Admin role | No | Aggregate AI audit metrics |

The role read catalog remains planner-visible; `actionTools` in the role
catalog endpoint exposes only the role's prepare actions and never exposes the
internal `role.execute_confirmed_action` dispatcher. A role action may not
expose raw medical prompts, tokens, passwords, or secret configuration, and
the browser cannot supply identity, role, facility or server resource-version
fields. The AI walk-in action intentionally accepts an existing verified
patient profile; creation of a new MPI profile remains the explicit reception
domain flow until the frontend/safety checkpoint adds an appropriate
identity-verification UX.

For doctors assigned to multiple facilities, `FacilityId` is a patient booking
selection rather than a model-controlled argument for a role action. The
backend derives or verifies it during booking and persists it on the
appointment. A role caller cannot send `facilityId` to move an appointment to
another facility, and unbound legacy appointments fail closed for role reads
and writes.
