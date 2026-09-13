import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';

export interface MpiPatientDto {
    id: number;
    userId?: string;
    medicalRecordNumber: string;
    fullName: string;
    phoneNumber?: string;
    email?: string;
    gender?: string;
    dateOfBirth?: string;
    age?: number;
    address?: string;
    nationalId?: string;
    bhytNumber?: string;
    bloodType?: string;
    rhFactor?: string;
    primaryFacilityId?: number;
    primaryFacilityName?: string;
    allergies: PatientAllergyDto[];
    emergencyContacts: EmergencyContactDto[];
}

export interface PatientAllergyDto {
    id: number;
    patientId: number;
    allergenType: number;
    allergenTypeName: string;
    allergenName: string;
    severity: number;
    severityName: string;
    reactionDescription?: string;
    recordedAtUtc: string;
}

export interface EmergencyContactDto {
    id: number;
    patientId: number;
    fullName: string;
    relationship: string;
    phoneNumber: string;
    address?: string;
    isPrimary: boolean;
}

export interface RegisterWalkInPatientPayload {
    fullName: string;
    phoneNumber?: string;
    email?: string;
    gender?: number | string;
    dateOfBirth?: string;
    address?: string;
    nationalId?: string;
    bhytNumber?: string;
    bloodType?: string;
    rhFactor?: string;
    primaryFacilityId?: number;
    allergies?: {
        allergenType: number;
        allergenName: string;
        severity: number;
        reactionDescription?: string;
    }[];
    emergencyContact?: {
        fullName: string;
        relationship: string;
        phoneNumber: string;
        address?: string;
    };
}

export interface PagedPatientResult {
    items: MpiPatientDto[];
    totalItems: number;
    page: number;
    pageSize: number;
    totalPages: number;
}

export const mpiApi = {
    searchPatients: async (params: { searchTerm?: string; medicalRecordNumber?: string; nationalId?: string; bhytNumber?: string; phoneNumber?: string; page?: number; pageSize?: number } = {}): Promise<ApiResponse<PagedPatientResult>> => {
        const queryParams = new URLSearchParams();
        if (params.searchTerm) queryParams.append('searchTerm', params.searchTerm);
        if (params.medicalRecordNumber) queryParams.append('medicalRecordNumber', params.medicalRecordNumber);
        if (params.nationalId) queryParams.append('nationalId', params.nationalId);
        if (params.bhytNumber) queryParams.append('bhytNumber', params.bhytNumber);
        if (params.phoneNumber) queryParams.append('phoneNumber', params.phoneNumber);
        queryParams.append('page', (params.page || 1).toString());
        queryParams.append('pageSize', (params.pageSize || 20).toString());
        return axiosClient.get<any, ApiResponse<PagedPatientResult>>(`/mpi/patients?${queryParams.toString()}`);
    },

    getPatientById: async (id: number): Promise<ApiResponse<MpiPatientDto>> => {
        return axiosClient.get<any, ApiResponse<MpiPatientDto>>(`/mpi/patients/${id}`);
    },

    getPatientByMrn: async (mrn: string): Promise<ApiResponse<MpiPatientDto>> => {
        return axiosClient.get<any, ApiResponse<MpiPatientDto>>(`/mpi/patients/by-mrn/${encodeURIComponent(mrn)}`);
    },

    registerWalkIn: async (payload: RegisterWalkInPatientPayload): Promise<ApiResponse<MpiPatientDto>> => {
        return axiosClient.post<any, ApiResponse<MpiPatientDto>>('/mpi/patients/walk-in', payload);
    },

    updatePatientMpi: async (id: number, payload: Partial<MpiPatientDto>): Promise<ApiResponse<MpiPatientDto>> => {
        return axiosClient.put<any, ApiResponse<MpiPatientDto>>(`/mpi/patients/${id}`, payload);
    },

    addAllergy: async (patientId: number, data: { allergenType: number; allergenName: string; severity: number; reactionDescription?: string }): Promise<ApiResponse<PatientAllergyDto>> => {
        return axiosClient.post<any, ApiResponse<PatientAllergyDto>>(`/mpi/patients/${patientId}/allergies`, data);
    },

    removeAllergy: async (patientId: number, allergyId: number): Promise<ApiResponse<any>> => {
        return axiosClient.delete<any, ApiResponse<any>>(`/mpi/patients/${patientId}/allergies/${allergyId}`);
    }
};
