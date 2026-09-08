export type AiActionType =
    | "ViewSpecialty"
    | "ViewDoctors"
    | "ViewAvailableSlots"
    | "StartBooking"
    | "SelectDoctor"
    | "SelectSlot"
    | "ReviewBooking"
    | "ConfirmBooking"
    | "ChangePreferredDate"
    | "ViewMyAppointments"
    | "OpenAppointmentDetail"
    | "RequestReschedule"
    | "RequestCancellation"
    | "ViewDiagnosticResults"
    | "ViewPrescriptions"
    | "ViewBills"
    | "ContactReception"
    | "ManualSpecialtySelection"
    | "CallEmergency";

export type AiActionStyle = "primary" | "secondary" | "danger";

export interface AiActionPayload {
    specialtyId?: number;
    specialtyCode?: string;
    specialtyName?: string;
    doctorId?: number;
    doctorName?: string;
    academicTitle?: string;
    slotId?: number;
    slotDate?: string;
    startTime?: string;
    endTime?: string;
    appointmentId?: number;
    appointmentCode?: string;
    reason?: string;
    targetUrl?: string;
}

export interface AiAction {
    id: string;
    type: AiActionType;
    label: string;
    description?: string;
    style: AiActionStyle;
    requiresAuthentication: boolean;
    requiresConfirmation: boolean;
    payload: AiActionPayload;
}

export interface AiSpecialtySuggestion {
    specialtyId: number;
    specialtyCode: string;
    specialtyName: string;
    rank: number;
    reason: string;
}

export interface AiBookingDraft {
    specialtyId?: number;
    specialtyName?: string;
    doctorId?: number;
    doctorName?: string;
    slotId?: number;
    slotDate?: string;
    startTime?: string;
    endTime?: string;
    reason?: string;
    roomNumber?: string;
    isComplete: boolean;
}

export interface AiChatResponse {
    message: string;
    reply?: string;
    urgency: "ROUTINE" | "SOON" | "EMERGENCY";
    safetyNotice?: string;
    specialtySuggestions: AiSpecialtySuggestion[];
    suggestedSpecialties?: AiSpecialtySuggestion[];
    actions: AiAction[];
    missingFields: string[];
    bookingDraft?: AiBookingDraft;
    promptVersion: string;
    manualSelectionRequired: boolean;
}

export interface ChatMessage {
    id?: string;
    role: "user" | "model";
    content: string;
    timestamp?: string;
    urgency?: "ROUTINE" | "SOON" | "EMERGENCY";
    safetyNotice?: string;
    suggestions?: AiSpecialtySuggestion[];
    actions?: AiAction[];
    bookingDraft?: AiBookingDraft;
    missingFields?: string[];
}
