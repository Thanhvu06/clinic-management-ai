import axiosClient from './axiosClient';
import type { 
    ApiResponse, 
    DoctorDashboardDto, 
    DoctorScheduleDayDto, 
    DoctorQueueItemDto, 
    PatientClinicalContextDto,
    ClinicalEncounterDto,
    VitalSignsDto,
    PrescriptionDraftDto,
    SaveEncounterRequest,
    SaveVitalSignsRequest,
    SavePrescriptionDraftRequest,
    CompleteConsultationRequest,
    LeavePreviewDto,
    CreateLeaveRequestPayload
} from '../types';

export interface ActiveMedicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
    isPrescriptionRequired?: boolean;
    strength?: string | null;
}

export const doctorApi = {
    checkPrescriptionSafety: async (id: number, medicineIds: number[], isVisit = false): Promise<ApiResponse<import('../types/prescriptionSafety').PrescriptionSafetyResultDto>> => {
        return axiosClient.post<unknown, ApiResponse<import('../types/prescriptionSafety').PrescriptionSafetyResultDto>>(`/doctor/${isVisit ? 'visits' : 'appointments'}/${id}/prescription/safety-check`, { medicineIds });
    },
    getDashboard: async (): Promise<ApiResponse<DoctorDashboardDto>> => {
        return axiosClient.get<any, ApiResponse<DoctorDashboardDto>>('/doctor/dashboard');
    },

    getSchedule: async (startDate?: string, endDate?: string): Promise<ApiResponse<DoctorScheduleDayDto[]>> => {
        const params = new URLSearchParams();
        if (startDate) params.append('startDate', startDate);
        if (endDate) params.append('endDate', endDate);
        const query = params.toString() ? `?${params.toString()}` : '';
        return axiosClient.get<any, ApiResponse<DoctorScheduleDayDto[]>>(`/doctor/schedule${query}`);
    },

    getQueue: async (date?: string): Promise<ApiResponse<DoctorQueueItemDto[]>> => {
        const query = date ? `?date=${date}` : '';
        return axiosClient.get<any, ApiResponse<DoctorQueueItemDto[]>>(`/doctor/appointments/queue${query}`);
    },

    getAppointmentHistory: async (id: number): Promise<ApiResponse<any[]>> => {
        return axiosClient.get<any, ApiResponse<any[]>>(`/doctor/appointments/${id}/history`);
    },

    getPatientClinicalContext: async (id: number): Promise<ApiResponse<PatientClinicalContextDto>> => {
        return axiosClient.get<any, ApiResponse<PatientClinicalContextDto>>(`/doctor/appointments/${id}/patient-context`);
    },

    checkInAppointment: async (id: number): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${id}/check-in`);
    },

    startConsultation: async (id: number): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${id}/start-consultation`);
    },

    saveEncounter: async (id: number, request: SaveEncounterRequest): Promise<ApiResponse<ClinicalEncounterDto>> => {
        return axiosClient.put<any, ApiResponse<ClinicalEncounterDto>>(`/doctor/appointments/${id}/encounter`, request);
    },

    saveVitalSigns: async (id: number, request: SaveVitalSignsRequest): Promise<ApiResponse<VitalSignsDto>> => {
        return axiosClient.put<any, ApiResponse<VitalSignsDto>>(`/doctor/appointments/${id}/vitals`, request);
    },

    savePrescriptionDraft: async (id: number, request: SavePrescriptionDraftRequest): Promise<ApiResponse<PrescriptionDraftDto>> => {
        return axiosClient.put<any, ApiResponse<PrescriptionDraftDto>>(`/doctor/appointments/${id}/prescription-draft`, request);
    },

    completeConsultation: async (id: number, request: CompleteConsultationRequest): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${id}/complete`, request);
    },

    // Visit-based doctor examination
    getVisitPatientClinicalContext: async (visitId: number): Promise<ApiResponse<PatientClinicalContextDto>> => {
        return axiosClient.get<any, ApiResponse<PatientClinicalContextDto>>(`/doctor/visits/${visitId}/patient-context`);
    },

    startVisitConsultation: async (visitId: number): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/visits/${visitId}/start-consultation`);
    },

    saveVisitEncounter: async (visitId: number, request: SaveEncounterRequest): Promise<ApiResponse<ClinicalEncounterDto>> => {
        return axiosClient.put<any, ApiResponse<ClinicalEncounterDto>>(`/doctor/visits/${visitId}/encounter`, request);
    },

    saveVisitVitalSigns: async (visitId: number, request: SaveVitalSignsRequest): Promise<ApiResponse<VitalSignsDto>> => {
        return axiosClient.put<any, ApiResponse<VitalSignsDto>>(`/doctor/visits/${visitId}/vitals`, request);
    },

    saveVisitPrescriptionDraft: async (visitId: number, request: SavePrescriptionDraftRequest): Promise<ApiResponse<PrescriptionDraftDto>> => {
        return axiosClient.put<any, ApiResponse<PrescriptionDraftDto>>(`/doctor/visits/${visitId}/prescription-draft`, request);
    },

    completeVisitConsultation: async (visitId: number, request: CompleteConsultationRequest): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/visits/${visitId}/complete`, request);
    },

    markNoShow: async (id: number, reason?: string): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${id}/noshow`, { reason });
    },

    createRevisitRequest: async (id: number, suggestedDate: string, note?: string): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${id}/revisit-requests`, { suggestedDate, note });
    },

    previewLeave: async (startDate: string, endDate: string): Promise<ApiResponse<LeavePreviewDto>> => {
        return axiosClient.get<any, ApiResponse<LeavePreviewDto>>(`/doctor/leave-requests/preview?startDate=${startDate}&endDate=${endDate}`);
    },

    createLeaveRequest: async (payload: CreateLeaveRequestPayload): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>('/doctor/leave-requests', payload);
    },

    getActiveMedicines: async (): Promise<ApiResponse<ActiveMedicine[]>> => {
        return axiosClient.get<any, ApiResponse<ActiveMedicine[]>>('/medicines/active');
    }
};
