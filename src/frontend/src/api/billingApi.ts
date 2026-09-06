import axiosClient from './axiosClient';
import type {
    ApiResponse,
    InvoiceDto,
    InvoiceDetailDto,
    PaymentDto,
    BillingKpiDto,
    RevenueReportDto,
    SpecialtyFeeDto,
    InvoiceFilterParams,
    CreateAppointmentInvoiceRequest,
    CreatePackageInvoiceRequest,
    ProcessPaymentRequest,
    CancelInvoiceRequest,
    UpdateSpecialtyFeeRequest,
    PagedBillingResult,
    InvoiceStatus,
} from '../types';

export const billingApi = {
    // Receptionist Endpoints
    reception: {
        getInvoices: async (params?: InvoiceFilterParams): Promise<ApiResponse<PagedBillingResult<InvoiceDto>>> => {
            const query = new URLSearchParams();
            if (params?.search) query.append('search', params.search);
            if (params?.status !== undefined && params.status !== null) query.append('status', params.status.toString());
            if (params?.sourceType !== undefined && params.sourceType !== null) query.append('sourceType', params.sourceType.toString());
            if (params?.fromDate) query.append('fromDate', params.fromDate);
            if (params?.toDate) query.append('toDate', params.toDate);
            if (params?.page) query.append('page', params.page.toString());
            if (params?.pageSize) query.append('pageSize', params.pageSize.toString());

            const res = await axiosClient.get(`/reception/billing/invoices?${query.toString()}`);
            return res as unknown as ApiResponse<PagedBillingResult<InvoiceDto>>;
        },

        getInvoiceById: async (id: number): Promise<ApiResponse<InvoiceDetailDto>> => {
            const res = await axiosClient.get(`/reception/billing/invoices/${id}`);
            return res as unknown as ApiResponse<InvoiceDetailDto>;
        },

        createInvoiceFromAppointment: async (data: CreateAppointmentInvoiceRequest): Promise<ApiResponse<InvoiceDetailDto>> => {
            const res = await axiosClient.post('/reception/billing/invoices/appointment', data);
            return res as unknown as ApiResponse<InvoiceDetailDto>;
        },

        createInvoiceFromHealthPackage: async (data: CreatePackageInvoiceRequest): Promise<ApiResponse<InvoiceDetailDto>> => {
            const res = await axiosClient.post('/reception/billing/invoices/health-package', data);
            return res as unknown as ApiResponse<InvoiceDetailDto>;
        },

        processPayment: async (invoiceId: number, data: ProcessPaymentRequest): Promise<ApiResponse<PaymentDto>> => {
            const res = await axiosClient.post(`/reception/billing/invoices/${invoiceId}/pay`, data);
            return res as unknown as ApiResponse<PaymentDto>;
        },

        cancelInvoice: async (invoiceId: number, data: CancelInvoiceRequest): Promise<ApiResponse<InvoiceDetailDto>> => {
            const res = await axiosClient.patch(`/reception/billing/invoices/${invoiceId}/cancel`, data);
            return res as unknown as ApiResponse<InvoiceDetailDto>;
        },

        getTodayKpi: async (): Promise<ApiResponse<BillingKpiDto>> => {
            const res = await axiosClient.get('/reception/billing/kpi');
            return res as unknown as ApiResponse<BillingKpiDto>;
        },
    },

    // Patient Endpoints
    patient: {
        getMyInvoices: async (page = 1, pageSize = 10, status?: InvoiceStatus): Promise<ApiResponse<PagedBillingResult<InvoiceDto>>> => {
            const query = new URLSearchParams({
                page: page.toString(),
                pageSize: pageSize.toString(),
            });
            if (status !== undefined && status !== null) {
                query.append('status', status.toString());
            }

            const res = await axiosClient.get(`/patient/invoices?${query.toString()}`);
            return res as unknown as ApiResponse<PagedBillingResult<InvoiceDto>>;
        },

        getMyInvoiceDetail: async (id: number): Promise<ApiResponse<InvoiceDetailDto>> => {
            const res = await axiosClient.get(`/patient/invoices/${id}`);
            return res as unknown as ApiResponse<InvoiceDetailDto>;
        },
    },

    // Admin Endpoints
    admin: {
        getRevenueReport: async (fromDate?: string, toDate?: string): Promise<ApiResponse<RevenueReportDto>> => {
            const query = new URLSearchParams();
            if (fromDate) query.append('fromDate', fromDate);
            if (toDate) query.append('toDate', toDate);

            const res = await axiosClient.get(`/admin/billing/revenue?${query.toString()}`);
            return res as unknown as ApiResponse<RevenueReportDto>;
        },

        getSpecialtyFees: async (): Promise<ApiResponse<SpecialtyFeeDto[]>> => {
            const res = await axiosClient.get('/admin/billing/specialties');
            return res as unknown as ApiResponse<SpecialtyFeeDto[]>;
        },

        updateSpecialtyFee: async (id: number, data: UpdateSpecialtyFeeRequest): Promise<ApiResponse<SpecialtyFeeDto>> => {
            const res = await axiosClient.patch(`/admin/billing/specialties/${id}/fee`, data);
            return res as unknown as ApiResponse<SpecialtyFeeDto>;
        },
    },
};

export default billingApi;
