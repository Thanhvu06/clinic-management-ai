export type VisitArrivalType = 'Scheduled' | 'WalkIn';
export type VisitPriority = 'Normal' | 'Priority' | 'Urgent' | 'Emergency';
export type VisitStatus =
    | 'Registered'
    | 'CheckedIn'
    | 'WaitingForDoctor'
    | 'InConsultation'
    | 'WaitingForDiagnostics'
    | 'ResultsReady'
    | 'InPharmacy'
    | 'InBilling'
    | 'Completed'
    | 'Cancelled'
    | 'NoShow'
    | 'Transferred'
    | 'ConsultationCompleted';

export interface CheckInTicketDto {
    visitId: number;
    visitCode: string;
    appointmentId?: number | null;
    appointmentCode?: string | null;
    patientId: number;
    patientName: string;
    medicalRecordNumber: string;
    phoneNumber: string;
    queueNumber: number;
    queueDisplay: string;
    facilityId: number;
    facilityName: string;
    departmentId: number;
    departmentName: string;
    roomId?: number | null;
    roomNumber?: string | null;
    assignedDoctorId?: number | null;
    doctorName?: string | null;
    checkedInAtUtc: string;
    receptionistName: string;
    status: string;
    priority: string;
    arrivalType: string;
}

export interface PatientAllergyInputDto {
    allergen: string;
    severity?: string;
    reaction?: string;
}

export interface EmergencyContactInputDto {
    contactName: string;
    relationship: string;
    phoneNumber: string;
    isGuardian?: boolean;
}

export interface NewPatientProfileDto {
    fullName: string;
    phoneNumber?: string;
    dateOfBirth: string;
    gender: number;
    address?: string;
    identityCardNumber?: string;
    allergies?: PatientAllergyInputDto[];
    emergencyContact?: EmergencyContactInputDto;
}

export interface ReceptionIntakeRequest {
    existingPatientId?: number;
    appointmentId?: number;
    newPatient?: NewPatientProfileDto;
    facilityId: number;
    departmentId: number;
    roomId?: number;
    assignedDoctorId?: number;
    healthPackageRegistrationId?: number;
    chiefComplaint?: string;
    priority?: VisitPriority;
    idempotencyKey?: string;
}

export interface WalkInRegistrationRequest {
    fullName: string;
    phoneNumber: string;
    dateOfBirth?: string | null;
    gender?: number | string | null;
    address?: string | null;
    identityCardNumber?: string | null;
    facilityId?: number | null;
    departmentId: number;
    roomId?: number | null;
    assignedDoctorId?: number | null;
    chiefComplaint?: string | null;
    priority?: VisitPriority;
}

export interface AppointmentCheckInRequest {
    appointmentId: number;
    facilityId?: number | null;
    departmentId?: number | null;
    roomId?: number | null;
    assignedDoctorId?: number | null;
}

export interface DepartmentQueueItemDto {
    visitId: number;
    visitCode: string;
    appointmentId?: number | null;
    appointmentCode?: string | null;
    queueNumber: number;
    queueDisplay: string;
    patientId: number;
    patientName: string;
    medicalRecordNumber: string;
    phoneNumber: string;
    gender?: string | null;
    age?: number | null;
    priority: string;
    status: string;
    arrivalType: string;
    assignedDoctorId?: number | null;
    doctorName?: string | null;
    roomId?: number | null;
    roomNumber?: string | null;
    chiefComplaint?: string | null;
    checkedInAtUtc: string;
    hasVitalSigns: boolean;
}

export interface PatientVisitDetailDto {
    id: number;
    visitCode: string;
    appointmentId?: number | null;
    appointmentCode?: string | null;
    patientId: number;
    patientName: string;
    medicalRecordNumber: string;
    phoneNumber: string;
    dateOfBirth?: string | null;
    gender?: string | null;
    address?: string | null;
    identityCardNumber?: string | null;
    facilityId: number;
    facilityName: string;
    departmentId: number;
    departmentName: string;
    roomId?: number | null;
    roomNumber?: string | null;
    assignedDoctorId?: number | null;
    doctorName?: string | null;
    visitDate: string;
    arrivalType: string;
    priority: string;
    chiefComplaint?: string | null;
    queueNumber: number;
    queueDisplay: string;
    status: string;
    checkedInAtUtc: string;
    consultationStartedAtUtc?: string | null;
    completedAtUtc?: string | null;
    cancelledAtUtc?: string | null;
    cancellationReason?: string | null;
    hasVitalSigns: boolean;
    hasEncounter: boolean;
    diagnosticOrdersCount: number;
    pendingDiagnosticOrdersCount: number;
    hasPrescription: boolean;
    prescriptionStatus?: string | null;
    hasInvoice: boolean;
    invoiceStatus?: string | null;
    rowVersion?: string | null;
}
