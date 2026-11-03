import type { AiCopilotCard } from '../../api/aiCopilotApi';
import type { AiAction, AiToolExecutionResult } from '../../types/ai';
import type { PatientAppointmentChangeData } from './PatientAppointmentChangeForm';

export function patientAppointmentChangeCard(
    action: AiAction, sessionId: string | null, accountKey: string | null,
    onPreview: (sessionId: string, accountKey: string | null, result: AiToolExecutionResult, content: string) => void
): AiCopilotCard | null {
    if (!sessionId || !action.id.startsWith('patient-change-') ||
        (action.type !== 'RequestCancellation' && action.type !== 'RequestReschedule')) return null;
    const payload = action.payload as typeof action.payload & {
        doctorId?: number; specialtyId?: number; slotId?: number; slotDate?: string;
    };
    if (!payload.appointmentId || !payload.appointmentCode) return null;
    const data: PatientAppointmentChangeData = {
        operation: action.type === 'RequestCancellation' ? 'cancel' : 'reschedule',
        appointmentId: payload.appointmentId,
        appointmentCode: payload.appointmentCode,
        doctorId: payload.doctorId,
        specialtyId: payload.specialtyId,
        currentSlotId: payload.slotId,
        day: payload.slotDate,
        sessionId,
        onPreview: (result, content) => onPreview(sessionId, accountKey, result, content)
    };
    return { type: 'patient_appointment_change_form', title: action.label, data };
}
