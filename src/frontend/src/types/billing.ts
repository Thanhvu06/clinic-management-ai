export const InvoiceSourceType = {
    Appointment: 1,
    HealthPackageRegistration: 2,
} as const;
export type InvoiceSourceType = typeof InvoiceSourceType[keyof typeof InvoiceSourceType];

export const InvoiceStatus = {
    Unpaid: 1,
    Paid: 2,
    Cancelled: 3,
} as const;
export type InvoiceStatus = typeof InvoiceStatus[keyof typeof InvoiceStatus];

export const PaymentMethod = {
    Cash: 1,
    ManualBankTransfer: 2,
} as const;
export type PaymentMethod = typeof PaymentMethod[keyof typeof PaymentMethod];

export const PaymentStatus = {
    Succeeded: 1,
    Voided: 2,
} as const;
export type PaymentStatus = typeof PaymentStatus[keyof typeof PaymentStatus];

export interface InvoiceDto {
    id: number;
    invoiceCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    sourceType: InvoiceSourceType;
    sourceTypeName: string;
    appointmentId?: number | null;
    appointmentCode?: string | null;
    healthPackageRegistrationId?: number | null;
    registrationCode?: string | null;
    status: InvoiceStatus;
    statusName: string;
    subtotal: number;
    totalAmount: number;
    createdAtUtc: string;
    paidAtUtc?: string | null;
    cancelledAtUtc?: string | null;
    cancellationReason?: string | null;
    createdByUserName?: string | null;
    paidByUserName?: string | null;
}

export interface InvoiceItemDto {
    id: number;
    invoiceId: number;
    itemCode: string;
    description: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    referenceType: string;
    referenceId: number;
}

export interface PaymentDto {
    id: number;
    paymentCode: string;
    invoiceId: number;
    invoiceCode: string;
    amount: number;
    method: PaymentMethod;
    methodName: string;
    referenceCode?: string | null;
    note?: string | null;
    receivedByUserId: string;
    receivedByUserName?: string | null;
    receivedAtUtc: string;
    status: PaymentStatus;
    statusName: string;
}

export interface InvoiceDetailDto extends InvoiceDto {
    items: InvoiceItemDto[];
    payments: PaymentDto[];
}

export interface DailyRevenueDto {
    date: string;
    revenue: number;
    succeededPaymentsCount: number;
    paidInvoicesCount: number;
}

export interface InvoiceStatusBreakdownDto {
    unpaidCount: number;
    unpaidAmount: number;
    paidCount: number;
    paidAmount: number;
    cancelledCount: number;
    cancelledAmount: number;
}

export interface RevenueReportDto {
    fromDate: string;
    toDate: string;
    totalRevenue: number;
    totalSucceededTransactions: number;
    dailyBreakdown: DailyRevenueDto[];
    statusBreakdown: InvoiceStatusBreakdownDto;
}

export interface BillingKpiDto {
    todayUnpaidInvoices: number;
    todayPaidInvoices: number;
    todayCancelledInvoices: number;
    todayRevenue: number;
}

export interface SpecialtyFeeDto {
    id: number;
    specialtyCode: string;
    name: string;
    consultationFee: number;
    isActive: boolean;
}

export interface CreateAppointmentInvoiceRequest {
    appointmentId: number;
}

export interface CreatePackageInvoiceRequest {
    healthPackageRegistrationId: number;
}

export interface ProcessPaymentRequest {
    amount: number;
    method: PaymentMethod;
    referenceCode?: string;
    note?: string;
}

export interface CancelInvoiceRequest {
    reason: string;
}

export interface UpdateSpecialtyFeeRequest {
    consultationFee: number;
}

export interface InvoiceFilterParams {
    search?: string;
    status?: InvoiceStatus;
    sourceType?: InvoiceSourceType;
    fromDate?: string;
    toDate?: string;
    page?: number;
    pageSize?: number;
}

export interface PagedBillingResult<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalItems: number;
    totalPages: number;
}
