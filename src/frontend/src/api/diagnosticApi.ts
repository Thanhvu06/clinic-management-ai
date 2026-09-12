import axiosClient from './axiosClient';
import type { 
    ApiResponse, 
    DiagnosticServiceDto, 
    DiagnosticOrderDto, 
    CreateDiagnosticOrderRequest, 
    RecordDiagnosticResultRequest, 
    TransitionDiagnosticOrderRequest, 
    CancelDiagnosticOrderRequest, 
    TechnicianDiagnosticStatsDto 
} from '../types';

export interface PagedResult<T> {
    items: T[];
    totalItems: number;
    page: number;
    pageSize: number;
}

export const diagnosticApi = {
    // Catalog
    getCatalog: async (): Promise<ApiResponse<DiagnosticServiceDto[]>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticServiceDto[]>>('/diagnostic-services');
    },

    // Doctor endpoints
    createDoctorOrder: async (appointmentId: number, data: CreateDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/doctor/appointments/${appointmentId}/diagnostic-orders`, data);
    },

    getDoctorOrdersByAppointment: async (appointmentId: number): Promise<ApiResponse<DiagnosticOrderDto[]>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticOrderDto[]>>(`/doctor/appointments/${appointmentId}/diagnostic-orders`);
    },

    getDoctorOrderById: async (orderId: number): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticOrderDto>>(`/doctor/diagnostic-orders/${orderId}`);
    },

    reviewDoctorOrder: async (orderId: number, data?: TransitionDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/doctor/diagnostic-orders/${orderId}/review`, data || {});
    },

    cancelDoctorOrder: async (orderId: number, data?: CancelDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/doctor/diagnostic-orders/${orderId}/cancel`, data || {});
    },

    // Technician endpoints
    getTechnicianOrders: async (params: { status?: string; date?: string; search?: string; page?: number; pageSize?: number } = {}): Promise<ApiResponse<PagedResult<DiagnosticOrderDto>>> => {
        return axiosClient.get<any, ApiResponse<PagedResult<DiagnosticOrderDto>>>('/diagnostics/orders', { params });
    },

    getTechnicianStats: async (): Promise<ApiResponse<TechnicianDiagnosticStatsDto>> => {
        return axiosClient.get<any, ApiResponse<TechnicianDiagnosticStatsDto>>('/diagnostics/orders/stats');
    },

    getTechnicianOrderById: async (orderId: number): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}`);
    },

    startTechnicianOrder: async (orderId: number, data?: TransitionDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/start`, data || {});
    },

    recordTechnicianItemResult: async (orderId: number, itemId: number, data: RecordDiagnosticResultRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.put<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/items/${itemId}/result`, data);
    },

    completeTechnicianOrder: async (orderId: number, data?: TransitionDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/complete`, data || {});
    },

    // Patient endpoints
    getPatientOrders: async (page = 1, pageSize = 20): Promise<ApiResponse<PagedResult<DiagnosticOrderDto>>> => {
        return axiosClient.get<any, ApiResponse<PagedResult<DiagnosticOrderDto>>>('/patients/me/diagnostic-orders', { params: { page, pageSize } });
    },

    getPatientOrderById: async (orderId: number): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticOrderDto>>(`/patients/me/diagnostic-orders/${orderId}`);
    },

    getPatientVitals: async (limit = 20): Promise<ApiResponse<any[]>> => {
        return axiosClient.get<any, ApiResponse<any[]>>('/patients/me/vitals', { params: { limit } });
    }
};
