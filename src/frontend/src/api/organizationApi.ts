import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';

export interface FacilityDto {
    id: number;
    code: string;
    name: string;
    address: string;
    city: string;
    phone: string;
    email?: string;
    taxCode?: string;
    hospitalLevel?: string;
    description?: string;
    isActive: boolean;
    buildingCount: number;
    departmentCount: number;
}

export interface BuildingDto {
    id: number;
    facilityId: number;
    facilityName: string;
    code: string;
    name: string;
    numberOfFloors: number;
    isActive: boolean;
    roomCount: number;
}

export interface DepartmentDto {
    id: number;
    facilityId: number;
    facilityName: string;
    buildingId?: number;
    buildingName?: string;
    code: string;
    name: string;
    departmentType: number;
    departmentTypeName: string;
    headOfDepartmentDoctorId?: number;
    headOfDepartmentDoctorName?: string;
    description?: string;
    isActive: boolean;
    roomCount: number;
}

export interface RoomDto {
    id: number;
    departmentId: number;
    departmentName: string;
    facilityId: number;
    facilityName: string;
    buildingId?: number;
    buildingName?: string;
    roomNumber: string;
    name: string;
    roomType: number;
    roomTypeName: string;
    floorNumber: number;
    maxCapacity: number;
    isActive: boolean;
    bedCount: number;
    availableBedCount: number;
}

export interface BedDto {
    id: number;
    roomId: number;
    roomNumber: string;
    departmentId: number;
    departmentName: string;
    bedNumber: string;
    bedType: number;
    bedTypeName: string;
    dailyRate: number;
    status: number;
    statusName: string;
    notes?: string;
    isActive: boolean;
}

export interface StaffFacilityAssignmentDto {
    id: number;
    userId: string;
    userName: string;
    userEmail: string;
    roleName: string;
    facilityId: number;
    facilityName: string;
    isPrimary: boolean;
    isActive: boolean;
    assignedAtUtc: string;
    notes?: string;
}

export const organizationApi = {
    getMyFacilities: async (): Promise<ApiResponse<FacilityDto[]>> => {
        return axiosClient.get<any, ApiResponse<FacilityDto[]>>('/facilities/my');
    },

    getFacilities: async (includeInactive = false): Promise<ApiResponse<FacilityDto[]>> => {
        return axiosClient.get<any, ApiResponse<FacilityDto[]>>(`/facilities?includeInactive=${includeInactive}`);
    },

    getFacilityById: async (id: number): Promise<ApiResponse<FacilityDto>> => {
        return axiosClient.get<any, ApiResponse<FacilityDto>>(`/facilities/${id}`);
    },

    createFacility: async (data: Partial<FacilityDto>): Promise<ApiResponse<FacilityDto>> => {
        return axiosClient.post<any, ApiResponse<FacilityDto>>('/facilities', data);
    },

    updateFacility: async (id: number, data: Partial<FacilityDto>): Promise<ApiResponse<FacilityDto>> => {
        return axiosClient.put<any, ApiResponse<FacilityDto>>(`/facilities/${id}`, data);
    },

    toggleFacilityStatus: async (id: number): Promise<ApiResponse<FacilityDto>> => {
        return axiosClient.patch<any, ApiResponse<FacilityDto>>(`/facilities/${id}/toggle-status`);
    },

    getBuildings: async (facilityId: number): Promise<ApiResponse<BuildingDto[]>> => {
        return axiosClient.get<any, ApiResponse<BuildingDto[]>>(`/facilities/${facilityId}/buildings`);
    },

    createBuilding: async (facilityId: number, data: { code: string; name: string; numberOfFloors: number }): Promise<ApiResponse<BuildingDto>> => {
        return axiosClient.post<any, ApiResponse<BuildingDto>>(`/facilities/${facilityId}/buildings`, data);
    },

    getDepartments: async (facilityId?: number): Promise<ApiResponse<DepartmentDto[]>> => {
        const query = facilityId ? `?facilityId=${facilityId}` : '';
        return axiosClient.get<any, ApiResponse<DepartmentDto[]>>(`/departments${query}`);
    },

    createDepartment: async (data: { facilityId: number; buildingId?: number; code: string; name: string; departmentType: number; description?: string }): Promise<ApiResponse<DepartmentDto>> => {
        return axiosClient.post<any, ApiResponse<DepartmentDto>>('/departments', data);
    },

    getRooms: async (params: { departmentId?: number; facilityId?: number } = {}): Promise<ApiResponse<RoomDto[]>> => {
        const queryParams = new URLSearchParams();
        if (params.departmentId) queryParams.append('departmentId', params.departmentId.toString());
        if (params.facilityId) queryParams.append('facilityId', params.facilityId.toString());
        const query = queryParams.toString() ? `?${queryParams.toString()}` : '';
        return axiosClient.get<any, ApiResponse<RoomDto[]>>(`/hospital-rooms${query}`);
    },

    createRoom: async (data: { departmentId: number; buildingId?: number; roomNumber: string; name: string; roomType: number; floorNumber: number; maxCapacity: number }): Promise<ApiResponse<RoomDto>> => {
        return axiosClient.post<any, ApiResponse<RoomDto>>('/hospital-rooms', data);
    },

    getBedsByRoom: async (roomId: number): Promise<ApiResponse<BedDto[]>> => {
        return axiosClient.get<any, ApiResponse<BedDto[]>>(`/beds/by-room/${roomId}`);
    },

    getBedsByDepartment: async (departmentId: number): Promise<ApiResponse<BedDto[]>> => {
        return axiosClient.get<any, ApiResponse<BedDto[]>>(`/beds/by-department/${departmentId}`);
    },

    createBed: async (data: { roomId: number; bedNumber: string; bedType: number; dailyRate: number; notes?: string }): Promise<ApiResponse<BedDto>> => {
        return axiosClient.post<any, ApiResponse<BedDto>>('/beds', data);
    },

    updateBedStatus: async (id: number, data: { status: number; notes?: string }): Promise<ApiResponse<BedDto>> => {
        return axiosClient.patch<any, ApiResponse<BedDto>>(`/beds/${id}/status`, data);
    },

    getStaffAssignments: async (facilityId?: number): Promise<ApiResponse<StaffFacilityAssignmentDto[]>> => {
        const query = facilityId ? `?facilityId=${facilityId}` : '';
        return axiosClient.get<any, ApiResponse<StaffFacilityAssignmentDto[]>>(`/admin/staff-assignments${query}`);
    },

    createStaffAssignment: async (data: { userId: string; facilityId: number; role: string; departmentId?: number; isPrimary?: boolean; notes?: string }): Promise<ApiResponse<StaffFacilityAssignmentDto>> => {
        return axiosClient.post<any, ApiResponse<StaffFacilityAssignmentDto>>('/admin/staff-assignments', data);
    },

    deleteStaffAssignment: async (id: number): Promise<ApiResponse<void>> => {
        return axiosClient.delete<any, ApiResponse<void>>(`/admin/staff-assignments/${id}`);
    }
};
