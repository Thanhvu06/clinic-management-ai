import axiosClient from './axiosClient';
import type { ApiResponse, MedicineDto, MedicineCategoryDto, SaveMedicineCategoryDto, PublicMedicineDto, PublicMedicineCategoryDto, MedicinePage } from '../types';

export const medicineApi = {
    getCategories: () => axiosClient.get<unknown, ApiResponse<MedicineCategoryDto[]>>('/admin/medicine-categories'),
    createCategory: (data: SaveMedicineCategoryDto) => axiosClient.post<unknown, ApiResponse<MedicineCategoryDto>>('/admin/medicine-categories', data),
    updateCategory: (id: number, data: SaveMedicineCategoryDto) => axiosClient.put<unknown, ApiResponse<MedicineCategoryDto>>(`/admin/medicine-categories/${id}`, data),
    toggleCategory: (id: number) => axiosClient.patch<unknown, ApiResponse<MedicineCategoryDto>>(`/admin/medicine-categories/${id}/toggle-status`),
    uploadImage: (id: number, file: File) => {
        const form = new FormData();
        form.append('file', file);
        return axiosClient.post<unknown, ApiResponse<MedicineDto>>(`/admin/medicines/${id}/image`, form, { headers: { 'Content-Type': 'multipart/form-data' } });
    },
    deleteImage: (id: number) => axiosClient.delete<unknown, ApiResponse<MedicineDto>>(`/admin/medicines/${id}/image`),
    getPublicMedicines: (params?: { search?: string; categoryId?: number; type?: 'rx' | 'otc'; page?: number; pageSize?: number }) =>
        axiosClient.get<unknown, ApiResponse<MedicinePage<PublicMedicineDto>>>('/public/medicines', { params }),
    getPublicMedicine: (id: number) => axiosClient.get<unknown, ApiResponse<PublicMedicineDto>>(`/public/medicines/${id}`),
    getPublicCategories: () => axiosClient.get<unknown, ApiResponse<PublicMedicineCategoryDto[]>>('/public/medicine-categories'),
};

export function medicineImageUrl(path: string): string {
    const base = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5258';
    return new URL(path, base).toString();
}
