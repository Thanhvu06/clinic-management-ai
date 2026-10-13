export type DiagnosticCategory = 'Laboratory' | 'Ultrasound' | 'Imaging' | 'Other';
export type DiagnosticOrderStatus = 'Ordered' | 'InProgress' | 'Completed' | 'Cancelled';
export type DiagnosticItemStatus = 'Ordered' | 'InProgress' | 'Completed' | 'Cancelled';
export type DiagnosticResultFlag = 'Normal' | 'Low' | 'High' | 'CriticalLow' | 'CriticalHigh';
export type SpecimenStatus = 'NotRequired' | 'PendingCollection' | 'Collected' | 'Received' | 'Rejected';
export type SpecimenRejectionReason = 'Hemolyzed' | 'Clotted' | 'InsufficientVolume' | 'WrongContainer' | 'LeakedOrBroken' | 'LabelMismatch' | 'PatientNotPrepared' | 'Other';

export interface CollectSpecimenRequest { rowVersion?: string | null; note?: string; }
export interface ReceiveSpecimenRequest { rowVersion?: string | null; }
export interface RejectSpecimenRequest { reason: number; note?: string; rowVersion?: string | null; }

export interface DiagnosticServiceParameterDto {
    id: number;
    code: string;
    name: string;
    unit: string;
    refLow: number | null;
    refHigh: number | null;
    criticalLow: number | null;
    criticalHigh: number | null;
    referenceRangeText: string;
}

export interface DiagnosticResultValueDto {
    parameterId: number;
    parameterCode: string;
    parameterName: string;
    unit: string;
    value: number;
    refLow: number | null;
    refHigh: number | null;
    referenceRangeText: string;
    flag: DiagnosticResultFlag;
    sortOrder: number;
}

export interface DiagnosticServiceDto {
    id: number;
    code: string;
    name: string;
    category: DiagnosticCategory;
    preparationInstructions?: string | null;
    price?: number | null;
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
    values?: DiagnosticResultValueDto[];
    hasCriticalValue?: boolean;
    criticalNotifiedAtUtc?: string | null;
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
    price?: number | null;
    isPackageCovered?: boolean;
    status: DiagnosticItemStatus;
    result?: DiagnosticResultDto | null;
    parameters?: DiagnosticServiceParameterDto[];
    specimenStatus?: SpecimenStatus | null;
    specimenType?: string | null;
    containerType?: string | null;
    specimenBarcode?: string | null;
    collectedAtUtc?: string | null;
    collectedByUserName?: string | null;
    receivedAtUtc?: string | null;
    receivedByUserName?: string | null;
    rejectedAtUtc?: string | null;
    rejectedByUserName?: string | null;
    rejectionReason?: SpecimenRejectionReason | null;
    rejectionNote?: string | null;
    recollectCount?: number;
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
    values?: { parameterId: number; value: number }[];
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
