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
    previousValue?: number | null;
    previousResultedAtUtc?: string | null;
    previousOrderCode?: string | null;
    deltaChange?: number | null;
    deltaPercent?: number | null;
    isDeltaFailure?: boolean;
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
    facilityName?: string | null;
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
    verificationStatus?: DiagnosticVerificationStatus | null;
    submittedForVerificationAtUtc?: string | null;
    submittedForVerificationByUserName?: string | null;
    verifiedAtUtc?: string | null;
    verifiedByUserName?: string | null;
    verifiedByRole?: 'DiagnosticTechnician' | 'LabSupervisor' | 'Doctor' | null;
    verificationNote?: string | null;
    returnedAtUtc?: string | null;
    returnedByUserName?: string | null;
    returnReason?: string | null;
    returnCount?: number;
    canCurrentUserVerify?: boolean;
    hasDeltaFailure?: boolean;
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

export type DiagnosticVerificationStatus = 'NotRequired' | 'Pending' | 'Verified' | 'Returned';
export interface VerifyDiagnosticOrderRequest { note?: string; rowVersion?: string | null; }
export interface ReturnDiagnosticOrderRequest { reason: string; rowVersion?: string | null; }

export interface CancelDiagnosticOrderRequest {
    reason?: string;
    rowVersion?: string | null;
}

export interface TechnicianDiagnosticStatsDto {
    orderedCount: number;
    inProgressCount: number;
    completedTodayCount: number;
    pendingVerificationCount?: number;
    returnedCount?: number;
    overdueCount?: number;
}

export interface VerificationStatsDto {
    pendingCount: number;
    criticalPendingCount: number;
    overduePendingCount: number;
    oldestPendingMinutes: number;
    returnedTodayCount: number;
    verifiedTodayCount: number;
}

export type DiagnosticTurnaroundSegmentKey = 'OrderToReceive' | 'ReceiveToResult' | 'ResultToVerify' | 'Total';

export interface DiagnosticTurnaroundSegmentDto {
    key: DiagnosticTurnaroundSegmentKey;
    count: number;
    medianMinutes: number | null;
    p90Minutes: number | null;
}

export interface DiagnosticTurnaroundServiceDto {
    serviceCode: string;
    serviceName: string;
    category: DiagnosticCategory;
    orderCount: number;
    medianTotalMinutes: number | null;
    p90TotalMinutes: number | null;
    targetMinutes: number;
    withinTargetPercent: number | null;
}

export interface DiagnosticTurnaroundOrderDto {
    orderId: number;
    orderCode: string;
    patientName: string;
    serviceNames: string[];
    orderedAtUtc: string;
    completedAtUtc: string;
    totalMinutes: number;
    targetMinutes: number;
}

export interface DiagnosticTurnaroundReportDto {
    from: string;
    to: string;
    category: DiagnosticCategory | null;
    orderCount: number;
    segments: DiagnosticTurnaroundSegmentDto[];
    withinTargetCount: number;
    withinTargetPercent: number | null;
    byService: DiagnosticTurnaroundServiceDto[];
    slowestOrders: DiagnosticTurnaroundOrderDto[];
}

export interface DiagnosticTurnaroundReportParams {
    from: string;
    to: string;
    category?: DiagnosticCategory;
}
