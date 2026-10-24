import type { HealthPackageDto } from '../../../types';

export interface LandingDoctor {
    id: number;
    fullName: string;
    academicTitle?: string;
    specialtyId?: number;
    specialtyName?: string;
    experienceYears?: number;
}

export interface LandingSpecialty {
    id: number;
    specialtyName?: string;
    name?: string;
    description?: string;
}

export type LandingPackage = HealthPackageDto & { imageUrl?: string | null; ImageUrl?: string | null };

export interface LandingSlot {
    slotId: number;
    slotDate: string;
    startTime: string;
    endTime: string;
}

export interface DoctorSlot {
    doctor: LandingDoctor;
    slot: LandingSlot;
}
