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
}

export const doctorApi = {
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

    getAppointments: async (params: { date?: string; status?: string; search?: string; page?: number; pageSize?: number } = {}): Promise<ApiResponse<any>> => {
        const queryParams = new URLSearchParams();
        if (params.date) queryParams.append('date', params.date);
        if (params.status) queryParams.append('status', params.status);
        if (params.search) queryParams.append('search', params.search);
        queryParams.append('page', (params.page || 1).toString());
        queryParams.append('pageSize', (params.pageSize || 10).toString());
        return axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments?${queryParams.toString()}`);
    },

    getAppointmentById: async (id: number): Promise<ApiResponse<any>> => {
        return axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${id}`);
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

    getEncounter: async (id: number): Promise<ApiResponse<ClinicalEncounterDto | null>> => {
        return axiosClient.get<any, ApiResponse<ClinicalEncounterDto | null>>(`/doctor/appointments/${id}/encounter`);
    },

    saveEncounter: async (id: number, request: SaveEncounterRequest): Promise<ApiResponse<ClinicalEncounterDto>> => {
        return axiosClient.put<any, ApiResponse<ClinicalEncounterDto>>(`/doctor/appointments/${id}/encounter`, request);
    },

    getVitalSigns: async (id: number): Promise<ApiResponse<VitalSignsDto | null>> => {
        return axiosClient.get<any, ApiResponse<VitalSignsDto | null>>(`/doctor/appointments/${id}/vitals`);
    },

    saveVitalSigns: async (id: number, request: SaveVitalSignsRequest): Promise<ApiResponse<VitalSignsDto>> => {
        return axiosClient.put<any, ApiResponse<VitalSignsDto>>(`/doctor/appointments/${id}/vitals`, request);
    },

    getPrescriptionDraft: async (id: number): Promise<ApiResponse<PrescriptionDraftDto | null>> => {
        return axiosClient.get<any, ApiResponse<PrescriptionDraftDto | null>>(`/doctor/appointments/${id}/prescription-draft`);
    },

    savePrescriptionDraft: async (id: number, request: SavePrescriptionDraftRequest): Promise<ApiResponse<PrescriptionDraftDto>> => {
        return axiosClient.put<any, ApiResponse<PrescriptionDraftDto>>(`/doctor/appointments/${id}/prescription-draft`, request);
    },

    completeConsultation: async (id: number, request: CompleteConsultationRequest): Promise<ApiResponse<any>> => {
        return axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${id}/complete`, request);
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
