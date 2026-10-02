import axiosClient from './axiosClient';
import type {
    ApiResponse,
    CheckInTicketDto,
    ReceptionIntakeRequest,
    WalkInRegistrationRequest,
    AppointmentCheckInRequest,
    DepartmentQueueItemDto,
    PatientVisitDetailDto,
    VisitStatus
} from '../types';

export const patientVisitApi = {
    receptionIntake: async (request: ReceptionIntakeRequest, idempotencyKey?: string): Promise<ApiResponse<CheckInTicketDto>> => {
        const headers = idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined;
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>('/patient-visits/intake', request, { headers });
    },

    createWalkInVisit: async (request: WalkInRegistrationRequest): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>('/patient-visits/walk-in', request);
    },

    checkInAppointment: async (request: AppointmentCheckInRequest): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>('/patient-visits/check-in-appointment', request);
    },

    receptionCheckInAppointment: async (appointmentId: number): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>(`/reception/appointments/${appointmentId}/check-in`, {});
    },

    getVisitById: async (id: number): Promise<ApiResponse<PatientVisitDetailDto>> => {
        return axiosClient.get<any, ApiResponse<PatientVisitDetailDto>>(`/patient-visits/${id}`);
    },

    getCheckInTicket: async (id: number): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.get<any, ApiResponse<CheckInTicketDto>>(`/patient-visits/${id}/ticket`);
    },

    getDepartmentQueue: async (departmentId: number, date?: string): Promise<ApiResponse<DepartmentQueueItemDto[]>> => {
        const query = date ? `?departmentId=${departmentId}&date=${date}` : `?departmentId=${departmentId}`;
        return axiosClient.get<any, ApiResponse<DepartmentQueueItemDto[]>>(`/patient-visits/department-queue${query}`);
    },

    assignDoctor: async (visitId: number, data: { doctorId: number; roomId?: number }): Promise<ApiResponse<PatientVisitDetailDto>> => {
        return axiosClient.post<any, ApiResponse<PatientVisitDetailDto>>(`/patient-visits/${visitId}/assign-doctor`, data);
    },

    updateVisitStatus: async (visitId: number, status: VisitStatus, reason?: string): Promise<ApiResponse<PatientVisitDetailDto>> => {
        const params = new URLSearchParams({ status });
        if (reason) params.append('reason', reason);
        return axiosClient.put<any, ApiResponse<PatientVisitDetailDto>>(`/patient-visits/${visitId}/status?${params.toString()}`);
    }
};
