export type DiagnosticCategory = 'Laboratory' | 'Ultrasound' | 'Imaging' | 'Other';
export type DiagnosticOrderStatus = 'Ordered' | 'InProgress' | 'Completed' | 'Cancelled';
export type DiagnosticItemStatus = 'Ordered' | 'InProgress' | 'Completed' | 'Cancelled';

export interface DiagnosticServiceDto {
    id: number;
    code: string;
    name: string;
    category: DiagnosticCategory;
    preparationInstructions?: string | null;
    isActive: boolean;
}

export interface DiagnosticResultDto {
    id: number;
    diagnosticOrderItemId: number;
    resultText: string;
    conclusion?: string | null;
    referenceRange?: string | null;
    unit?: string | null;
    resultedAtUtc: string;
    resultedByUserId: string;
    resultedByUserName: string;
    rowVersion?: string | null;
}

export interface DiagnosticOrderItemDto {
    id: number;
    diagnosticOrderId: number;
    diagnosticServiceId: number;
    serviceCode: string;
    serviceName: string;
    category: DiagnosticCategory;
    preparationInstructions?: string | null;
    status: DiagnosticItemStatus;
    result?: DiagnosticResultDto | null;
    rowVersion?: string | null;
}

export interface DiagnosticOrderDto {
    id: number;
    appointmentId: number;
    appointmentCode: string;
    orderCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    patientGender?: string | null;
    patientAge?: number | null;
    patientDob?: string | null;
    orderingDoctorId: number;
    orderingDoctorName: string;
    specialtyName: string;
    clinicalIndication: string;
    note?: string | null;
    status: DiagnosticOrderStatus;
    orderedAtUtc: string;
    startedAtUtc?: string | null;
    completedAtUtc?: string | null;
    cancelledAtUtc?: string | null;
    startedByUserName?: string | null;
    completedByUserName?: string | null;
    reviewedAtUtc?: string | null;
    reviewedByDoctorName?: string | null;
    rowVersion?: string | null;
    items: DiagnosticOrderItemDto[];
}

export interface CreateDiagnosticOrderRequest {
    clinicalIndication: string;
    note?: string;
    serviceIds: number[];
}

export interface RecordDiagnosticResultRequest {
    resultText: string;
    conclusion?: string;
    referenceRange?: string;
    unit?: string;
    rowVersion?: string | null;
}

export interface TransitionDiagnosticOrderRequest {
    rowVersion?: string | null;
}

export interface CancelDiagnosticOrderRequest {
    reason?: string;
    rowVersion?: string | null;
}

export interface TechnicianDiagnosticStatsDto {
    orderedCount: number;
    inProgressCount: number;
    completedTodayCount: number;
}
