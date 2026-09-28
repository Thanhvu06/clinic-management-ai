/**
 * Shared response-card contract. The backend composer emits one card for every
 * completed tool result, so this list is intentionally explicit and reviewed
 * separately from the JSX switch. Unknown types are rendered fail-closed.
 */
export const CANONICAL_COPILOT_CARD_TYPES = [
    'clinic_knowledge',
    'specialties',
    'doctors',
    'available_slots',
    'facilities',
    'pricing_catalog',
    'appointments',
    'appointment_detail',
    'patient_visits',
    'patient_diagnostic_results',
    'patient_prescriptions',
    'patient_bills',
    'reception_appointments',
    'reception_queue',
    'appointment_lookup',
    'doctor_queue',
    'doctor_summary',
    'doctor_patient_summary',
    'doctor_diagnostic_orders',
    'doctor_prescription_status',
    'technician_worklist',
    'pharmacist_prescription_queue',
    'pharmacy_inventory',
    'admin_dashboard_metrics',
    'admin_ai_health',
    'booking_preview',
    'pending_action',
    'change_request',
    'idempotent_replay'
] as const;

export type CanonicalCopilotCardType = typeof CANONICAL_COPILOT_CARD_TYPES[number];

export const isCanonicalCopilotCardType = (type: string): boolean =>
    (CANONICAL_COPILOT_CARD_TYPES as readonly string[]).includes(type);

export const INTENTIONALLY_HIDDEN_CARD_TYPES = [
    'workspace_result',
    'unknown',
    'failed_tool_result'
] as const;
