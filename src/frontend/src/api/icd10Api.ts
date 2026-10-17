import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';
import type { Icd10Code } from '../types/icd10';

export const icd10Api = {
    search: (search: string, limit = 20): Promise<ApiResponse<Icd10Code[]>> =>
        axiosClient.get<unknown, ApiResponse<Icd10Code[]>>('/icd10', { params: { search, limit } })
};
