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
import type { DiagnosticTurnaroundReportDto, DiagnosticTurnaroundReportParams } from '../types/diagnostic';

export interface PagedResult<T> {
    items: T[];
    totalItems: number;
    page: number;
    pageSize: number;
}

export const diagnosticApi = {
    uploadAttachment: async (orderId: number, itemId: number, file: File): Promise<ApiResponse<DiagnosticOrderDto>> => {
        const form = new FormData();
        form.append('file', file);
        return axiosClient.post<FormData, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/items/${itemId}/attachments`, form, { headers: { 'Content-Type': 'multipart/form-data' } });
    },
    deleteAttachment: async (orderId: number, itemId: number, attachmentId: number): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.delete<unknown, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/items/${itemId}/attachments/${attachmentId}`);
    },
    getAttachmentBlob: async (attachmentId: number): Promise<Blob> => {
        return axiosClient.get<unknown, Blob>(`/diagnostic-attachments/${attachmentId}/content`, { responseType: 'blob' });
    },
    getVerificationStats: async (): Promise<ApiResponse<import('../types/diagnostic').VerificationStatsDto>> => {
        return axiosClient.get<any, ApiResponse<import('../types/diagnostic').VerificationStatsDto>>('/diagnostic-verification/stats');
    },
    getSharedVerificationQueue: async (params: { page?: number; pageSize?: number } = {}): Promise<ApiResponse<PagedResult<DiagnosticOrderDto>>> => {
        return axiosClient.get<any, ApiResponse<PagedResult<DiagnosticOrderDto>>>('/diagnostic-verification/queue', { params });
    },
    getVerificationOrder: async (orderId: number): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostic-verification/orders/${orderId}`);
    },
    verifySharedOrder: async (orderId: number, data: import('../types/diagnostic').VerifyDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostic-verification/orders/${orderId}/verify`, data);
    },
    returnSharedOrder: async (orderId: number, data: import('../types/diagnostic').ReturnDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostic-verification/orders/${orderId}/return`, data);
    },
    getVerificationQueue: async (params: { page?: number; pageSize?: number } = {}): Promise<ApiResponse<PagedResult<DiagnosticOrderDto>>> => {
        return axiosClient.get<any, ApiResponse<PagedResult<DiagnosticOrderDto>>>('/diagnostics/orders/verification-queue', { params });
    },
    verifyOrder: async (orderId: number, data: import('../types/diagnostic').VerifyDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/verify`, data);
    },
    returnOrder: async (orderId: number, data: import('../types/diagnostic').ReturnDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/return`, data);
    },
    collectSpecimen: async (orderId: number, itemId: number, data: import('../types/diagnostic').CollectSpecimenRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/items/${itemId}/specimen/collect`, data);
    },
    receiveSpecimen: async (orderId: number, itemId: number, data: import('../types/diagnostic').ReceiveSpecimenRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/items/${itemId}/specimen/receive`, data);
    },
    rejectSpecimen: async (orderId: number, itemId: number, data: import('../types/diagnostic').RejectSpecimenRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/diagnostics/orders/${orderId}/items/${itemId}/specimen/reject`, data);
    },
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

    createDoctorOrderForVisit: async (visitId: number, data: CreateDiagnosticOrderRequest): Promise<ApiResponse<DiagnosticOrderDto>> => {
        return axiosClient.post<any, ApiResponse<DiagnosticOrderDto>>(`/doctor/visits/${visitId}/diagnostic-orders`, data);
    },

    getDoctorOrdersByVisit: async (visitId: number): Promise<ApiResponse<DiagnosticOrderDto[]>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticOrderDto[]>>(`/doctor/visits/${visitId}/diagnostic-orders`);
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
    getTurnaroundReport: async ({ from, to, category }: DiagnosticTurnaroundReportParams): Promise<ApiResponse<DiagnosticTurnaroundReportDto>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticTurnaroundReportDto>>('/lab-reports/turnaround', { params: { from, to, ...(category ? { category } : {}) } });
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

    getPatientVitals: async (limit = 20): Promise<ApiResponse<any[]>> => {
        return axiosClient.get<any, ApiResponse<any[]>>('/patients/me/vitals', { params: { limit } });
    },

    // Admin pricing endpoints
    getDiagnosticPricing: async (): Promise<ApiResponse<DiagnosticServiceDto[]>> => {
        return axiosClient.get<any, ApiResponse<DiagnosticServiceDto[]>>('/admin/diagnostic-services/pricing');
    },

    updateDiagnosticPrice: async (id: number, price: number): Promise<ApiResponse<DiagnosticServiceDto>> => {
        return axiosClient.put<any, ApiResponse<DiagnosticServiceDto>>(`/admin/diagnostic-services/${id}/pricing`, { price });
    }
};
