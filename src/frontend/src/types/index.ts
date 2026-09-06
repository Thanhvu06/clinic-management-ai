export interface ApiResponse<T = any> {
    success: boolean;
    message: string;
    data?: T;
}

export interface ApiErrorResponse {
    success: boolean;
    message: string;
    errorCode: string;
    errors?: Record<string, string[]>;
}

export interface UserDto {
    id?: number;
    userId: string;
    fullName: string;
    role: string;
}

export interface AuthResponse {
    accessToken: string;
    expiresAt: string;
    user: UserDto;
}

export interface ClinicLocationDto {
    id: number;
    code: string;
    name: string;
    address: string;
    city: string;
    phone: string;
    openingHours: string;
    description?: string | null;
    services: string[];
    isActive: boolean;
}

export interface HealthPackageDto {
    id: number;
    code: string;
    name: string;
    targetAudience: string;
    description: string;
    price: number;
    includedServices: string[];
    includedServicesJson?: string;
    isActive: boolean;
}

export interface HealthPackageRegistrationDto {
    id: number;
    registrationCode: string;
    healthPackageId: number;
    healthPackageName: string;
    healthPackageCode: string;
    healthPackagePrice: number;
    patientId: number;
    patientName: string;
    preferredDate: string;
    contactPhone: string;
    note?: string | null;
    adminNotes?: string | null;
    cancellationReason?: string | null;
    status: number;
    createdAt: string;
    updatedAt?: string | null;
}

export interface ScheduleBlockDto {
    startTime: string;
    endTime: string;
}

export interface AvailableSlotDto {
    slotId: number;
    doctorId?: number;
    slotDate?: string;
    startTime: string;
    endTime: string;
}

export interface DoctorDayAvailabilityDto {
    date: string;
    dayOfWeek: string;
    scheduleBlocks: ScheduleBlockDto[];
    availableSlots: AvailableSlotDto[];
}

export interface DoctorAvailabilityDto {
    doctorId: number;
    doctorName: string;
    specialtyName: string;
    days: DoctorDayAvailabilityDto[];
}

export * from './doctor';
export * from './notification';
export * from './billing';

