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
| `reception.get_today_appointments` | Receptionist | Read | Assigned facility | No | Appointment + facility assignment |
| `reception.get_queue` | Receptionist | Read | Assigned facility | No | Patient visit queue |
| `reception.lookup_appointment` | Receptionist | Read | Assigned facility | No | Appointment code + facility assignment |
| `doctor.get_my_queue` | Doctor | Read | Assigned doctor | No | Patient visit queue |
| `doctor.get_patient_summary` | Doctor | Read | Assigned appointment | No | Appointment/encounter |
| `doctor.get_diagnostic_orders` | Doctor | Read | Ordering doctor | No | Diagnostic orders |
| `technician.get_worklist` | Technician | Read | Assigned facility | No | Diagnostic orders |
| `pharmacist.get_prescription_queue` | Pharmacist | Read | Assigned facility | No | Issued prescriptions + visit |
| `pharmacist.get_inventory_status` | Pharmacist | Read | Pharmacy workspace | No | Medicine catalog |
| `admin.get_dashboard_metrics` | Admin | Read | Admin role | No | Aggregate domain counts |
| `admin.get_ai_health` | Admin | Read | Admin role | No | Aggregate AI audit metrics |

No role catalog entry is a write operation. A role tool may not expose raw
medical prompts, tokens, passwords, or secret configuration.
