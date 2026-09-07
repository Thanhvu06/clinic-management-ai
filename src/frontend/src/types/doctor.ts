export interface DoctorShiftSummaryDto {
    id: number;
    date: string;
    shiftName: string;
    startTime: string;
    endTime: string;
    room: string;
    status: string;
}

export interface DoctorDashboardKpisDto {
    totalAppointmentsToday: number;
    waitingCount: number;
    inConsultationCount: number;
    completedCount: number;
    noShowCount: number;
}

export interface DoctorQueueItemDto {
    appointmentId: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    patientGender?: string | null;
    patientDob?: string | null;
    patientAge?: number | null;
    reason?: string | null;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    status: string;
    queueOrder: number;
    isVitalsRecorded: boolean;
    vitalSummaryText?: string | null;
    chiefComplaint?: string | null;
}

export interface DoctorDashboardDto {
    currentShift?: DoctorShiftSummaryDto | null;
    kpis: DoctorDashboardKpisDto;
    nextPatient?: DoctorQueueItemDto | null;
    todayQueue: DoctorQueueItemDto[];
    upcomingAppointments?: DoctorQueueItemDto[];
}

export interface DoctorScheduleSlotDto {
    id: number;
    startTime: string;
    endTime: string;
    isBooked: boolean;
    isAvailable: boolean;
    appointmentId?: number | null;
    patientName?: string | null;
    patientPhone?: string | null;
    appointmentStatus?: string | null;
    reason?: string | null;
}

export interface DoctorScheduleShiftDto {
    workScheduleId: number;
    shiftName: string;
    startTime: string;
    endTime: string;
    room: string;
    isActive: boolean;
    slots: DoctorScheduleSlotDto[];
}

export interface DoctorScheduleDayDto {
    date: string;
    dayOfWeekName: string;
    shifts: DoctorScheduleShiftDto[];
}

export interface ClinicalEncounterDto {
    id: number;
    appointmentId: number;
    doctorId: number;
    doctorName: string;
    chiefComplaint?: string | null;
    clinicalFindings?: string | null;
    diagnosis?: string | null;
    diagnosisCode?: string | null;
    treatmentPlan?: string | null;
    summary?: string | null;
    followUpInstruction?: string | null;
    createdAtUtc: string;
    updatedAtUtc?: string | null;
    completedAtUtc?: string | null;
    rowVersion?: string | null;
}

export interface VitalSignsDto {
    id: number;
    appointmentId: number;
    temperature?: number | null;
    bloodPressureSystolic?: number | null;
    bloodPressureDiastolic?: number | null;
    heartRate?: number | null;
    respiratoryRate?: number | null;
    weight?: number | null;
    height?: number | null;
    bmi?: number | null;
    spO2?: number | null;
    recordedAtUtc: string;
    recordedByUserName: string;
    rowVersion?: string | null;
}

export interface PrescriptionDraftItemDto {
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    quantity: number;
    availableStock: number;
    dosage?: string | null;
    frequency?: string | null;
    durationDays?: number | null;
    instructions?: string | null;
}

export interface PrescriptionDraftDto {
    id: number;
    appointmentId: number;
    doctorId: number;
    doctorName: string;
    patientId: number;
    patientName: string;
    status: string;
    notes?: string | null;
    createdAt: string;
    rowVersion?: string | null;
    items: PrescriptionDraftItemDto[];
}

export interface PastVisitSummaryDto {
    appointmentId: number;
    appointmentCode: string;
    date: string;
    doctorName: string;
    specialtyName: string;
    diagnosis?: string | null;
    summary?: string | null;
    prescriptionItemNames: string[];
}

export interface PatientClinicalContextDto {
    patientId: number;
    patientName: string;
    patientPhone: string;
    patientGender?: string | null;
    patientDob?: string | null;
    address?: string | null;
    totalPastVisits: number;
    pastVisits: PastVisitSummaryDto[];
    currentAppointment: {
        id: number;
        appointmentCode: string;
        patientId: number;
        doctorId: number;
        doctorName: string;
        specialtyId: number;
        specialtyName: string;
        appointmentDate: string;
        startTime: string;
        endTime: string;
        reason?: string | null;
        status: string;
    };
    vitalSigns?: VitalSignsDto | null;
    encounter?: ClinicalEncounterDto | null;
    prescription?: PrescriptionDraftDto | null;
    anthropometricComparison?: AnthropometricComparisonDto | null;
    vitalHistory?: PatientVitalHistoryItemDto[];
    latestKnownVitals?: VitalSignsDto | null;
}

export interface SaveEncounterRequest {
    chiefComplaint?: string;
    clinicalFindings?: string;
    diagnosis?: string;
    diagnosisCode?: string;
    treatmentPlan?: string;
    summary?: string;
    followUpInstruction?: string;
    rowVersion?: string | null;
}

export interface SaveVitalSignsRequest {
    temperature?: number | null;
    bloodPressureSystolic?: number | null;
    bloodPressureDiastolic?: number | null;
    heartRate?: number | null;
    respiratoryRate?: number | null;
    weight?: number | null;
    height?: number | null;
    spO2?: number | null;
    rowVersion?: string | null;
}

export interface SavePrescriptionItemRequest {
    medicineId: number;
    quantity: number;
    dosage?: string;
    frequency?: string;
    durationDays?: number | null;
    instructions?: string;
}

export interface SavePrescriptionDraftRequest {
    notes?: string;
    rowVersion?: string | null;
    items: SavePrescriptionItemRequest[];
}

export interface CompleteConsultationRequest {
    chiefComplaint?: string;
    clinicalFindings?: string;
    diagnosis?: string;
    diagnosisCode?: string;
    treatmentPlan?: string;
    summary?: string;
    followUpInstruction?: string;
    encounterRowVersion?: string | null;
    issuePrescription: boolean;
    prescriptionNotes?: string;
    prescriptionRowVersion?: string | null;
    prescriptionItems?: SavePrescriptionItemRequest[];
}

export interface AffectedAppointmentDto {
    id: number;
    appointmentCode: string;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    patientName: string;
    patientPhone: string;
    status: string;
}

export interface LeavePreviewDto {
    startDate: string;
    endDate: string;
    affectedAppointmentCount: number;
    affectedAppointments: AffectedAppointmentDto[];
}

export interface CreateLeaveRequestPayload {
    startDate: string;
    endDate: string;
    reason: string;
}

export interface PatientVitalHistoryItemDto {
    appointmentId: number;
    appointmentCode: string;
    appointmentDate: string;
    recordedAtUtc: string;
    height?: number | null;
    weight?: number | null;
    bmi?: number | null;
    temperature?: number | null;
    bloodPressureSystolic?: number | null;
    bloodPressureDiastolic?: number | null;
    heartRate?: number | null;
    respiratoryRate?: number | null;
    spO2?: number | null;
    recordedByUserName: string;
}

export interface AnthropometricComparisonDto {
    currentMeasurement?: PatientVitalHistoryItemDto | null;
    previousMeasurement?: PatientVitalHistoryItemDto | null;
    weightDeltaKg?: number | null;
    heightDeltaCm?: number | null;
    bmiDelta?: number | null;
    hasComparableData: boolean;
}
