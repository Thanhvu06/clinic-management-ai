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

export interface BaseAiAction {
    id: string;
    label: string;
    description?: string;
    style: AiActionStyle;
    requiresAuthentication: boolean;
    requiresConfirmation: boolean;
}

export interface ViewSpecialtyAction extends BaseAiAction {
    type: "ViewSpecialty";
    payload: {
        specialtyId: number;
        specialtyCode?: string;
        specialtyName?: string;
        targetUrl?: string;
    };
}

export interface ViewDoctorsAction extends BaseAiAction {
    type: "ViewDoctors";
    payload: {
        specialtyId?: number;
        specialtyName?: string;
        targetUrl?: string;
    };
}

export interface ViewAvailableSlotsAction extends BaseAiAction {
    type: "ViewAvailableSlots";
    payload: {
        doctorId?: number;
        specialtyId?: number;
        slotDate?: string;
        targetUrl?: string;
    };
}

export interface StartBookingAction extends BaseAiAction {
    type: "StartBooking";
    payload: {
        specialtyId?: number;
        targetUrl?: string;
    };
}

export interface SelectDoctorAction extends BaseAiAction {
    type: "SelectDoctor";
    payload: {
        specialtyId: number;
        specialtyName?: string;
        doctorId: number;
        doctorName?: string;
        academicTitle?: string;
        slotDate?: string;
    };
}

export interface SelectSlotAction extends BaseAiAction {
    type: "SelectSlot";
    payload: {
        specialtyId?: number;
        specialtyName?: string;
        doctorId?: number;
        doctorName?: string;
        slotId: number;
        slotDate: string;
        startTime: string;
        endTime: string;
        reason?: string;
    };
}

export interface ReviewBookingAction extends BaseAiAction {
    type: "ReviewBooking";
    payload: {
        specialtyId?: number;
        specialtyName?: string;
        doctorId?: number;
        doctorName?: string;
        slotId?: number;
        slotDate?: string;
        startTime?: string;
        endTime?: string;
        reason?: string;
    };
}

export interface ConfirmBookingAction extends BaseAiAction {
    type: "ConfirmBooking";
    payload: {
        specialtyId: number;
        specialtyName?: string;
        doctorId: number;
        doctorName?: string;
        slotId: number;
        slotDate: string;
        startTime: string;
        endTime: string;
        reason: string;
    };
}

export interface ChangePreferredDateAction extends BaseAiAction {
    type: "ChangePreferredDate";
    payload: {
        slotDate: string;
        specialtyId?: number;
        specialtyName?: string;
        doctorId?: number;
        doctorName?: string;
        reason?: string;
    };
}

export interface ViewMyAppointmentsAction extends BaseAiAction {
    type: "ViewMyAppointments";
    payload: {
        targetUrl: string;
    };
}

export interface OpenAppointmentDetailAction extends BaseAiAction {
    type: "OpenAppointmentDetail";
    payload: {
        appointmentId?: number;
        appointmentCode?: string;
        targetUrl: string;
    };
}

export interface RequestRescheduleAction extends BaseAiAction {
    type: "RequestReschedule";
    payload: {
        appointmentId?: number;
        appointmentCode?: string;
        targetUrl: string;
    };
}

export interface RequestCancellationAction extends BaseAiAction {
    type: "RequestCancellation";
    payload: {
        appointmentId?: number;
        appointmentCode?: string;
        targetUrl: string;
    };
}

export interface ViewDiagnosticResultsAction extends BaseAiAction {
    type: "ViewDiagnosticResults";
    payload: {
        targetUrl: string;
    };
}

export interface ViewPrescriptionsAction extends BaseAiAction {
    type: "ViewPrescriptions";
    payload: {
        targetUrl: string;
    };
}

export interface ViewBillsAction extends BaseAiAction {
    type: "ViewBills";
    payload: {
        targetUrl: string;
    };
}

export interface ContactReceptionAction extends BaseAiAction {
    type: "ContactReception";
    payload: {
        phoneNumber?: string;
        reason?: string;
        targetUrl?: string;
    };
}

export interface ManualSpecialtySelectionAction extends BaseAiAction {
    type: "ManualSpecialtySelection";
    payload: {
        targetUrl: string;
    };
}

export interface CallEmergencyAction extends BaseAiAction {
    type: "CallEmergency";
    payload: {
        targetUrl: string;
    };
}

export type AiAction =
    | ViewSpecialtyAction
    | ViewDoctorsAction
    | ViewAvailableSlotsAction
    | StartBookingAction
    | SelectDoctorAction
    | SelectSlotAction
    | ReviewBookingAction
    | ConfirmBookingAction
    | ChangePreferredDateAction
    | ViewMyAppointmentsAction
    | OpenAppointmentDetailAction
    | RequestRescheduleAction
    | RequestCancellationAction
    | ViewDiagnosticResultsAction
    | ViewPrescriptionsAction
    | ViewBillsAction
    | ContactReceptionAction
    | ManualSpecialtySelectionAction
    | CallEmergencyAction;

// General generic payload for fallback/utility usage
export type AiActionPayload = Partial<{
    specialtyId: number;
    specialtyCode: string;
    specialtyName: string;
    doctorId: number;
    doctorName: string;
    academicTitle: string;
    slotId: number;
    slotDate: string;
    startTime: string;
    endTime: string;
    appointmentId: number;
    appointmentCode: string;
    reason: string;
    targetUrl: string;
    phoneNumber: string;
}>;

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
