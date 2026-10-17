import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';
import type { CreateMedicineBatch, MedicineBatch, MedicineBatchFilter, MedicineBatchPage } from '../types/pharmacyBatch';

export const pharmacyBatchApi = {
    list: (params: { status?: MedicineBatchFilter; search?: string; page?: number; pageSize?: number }) =>
        axiosClient.get<unknown, ApiResponse<MedicineBatchPage>>('/pharmacy/batches', { params }),
    create: (payload: CreateMedicineBatch) =>
        axiosClient.post<unknown, ApiResponse<MedicineBatch>>('/pharmacy/batches', payload),
    dispose: (id: number, reason: string) =>
        axiosClient.post<unknown, ApiResponse<MedicineBatch>>(`/pharmacy/batches/${id}/dispose`, { reason })
};
