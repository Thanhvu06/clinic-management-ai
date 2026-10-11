import axiosClient from './axiosClient';
import type {
    ApiResponse,
    CheckInTicketDto,
    ReceptionIntakeRequest,
    AppointmentCheckInRequest,
    DepartmentQueueItemDto
} from '../types';

export const patientVisitApi = {
    receptionIntake: async (request: ReceptionIntakeRequest, idempotencyKey?: string): Promise<ApiResponse<CheckInTicketDto>> => {
        const headers = idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined;
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>('/patient-visits/intake', request, { headers });
    },

    checkInAppointment: async (request: AppointmentCheckInRequest): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>('/patient-visits/check-in-appointment', request);
    },

    receptionCheckInAppointment: async (appointmentId: number): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.post<any, ApiResponse<CheckInTicketDto>>(`/reception/appointments/${appointmentId}/check-in`, {});
    },

    getCheckInTicket: async (id: number): Promise<ApiResponse<CheckInTicketDto>> => {
        return axiosClient.get<any, ApiResponse<CheckInTicketDto>>(`/patient-visits/${id}/ticket`);
    },

    getDepartmentQueue: async (departmentId: number, date?: string): Promise<ApiResponse<DepartmentQueueItemDto[]>> => {
        const query = date ? `?departmentId=${departmentId}&date=${date}` : `?departmentId=${departmentId}`;
        return axiosClient.get<any, ApiResponse<DepartmentQueueItemDto[]>>(`/patient-visits/department-queue${query}`);
    }
};
