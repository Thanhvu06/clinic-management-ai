import axiosClient from './axiosClient';
import type { ClinicLocationDto } from '../types';

type LocationsResponse = { success: boolean; data: ClinicLocationDto[] };
let pending: Promise<LocationsResponse> | null = null;

export const getPublicLocations = () => {
    if (!pending) {
        pending = axiosClient.get<unknown, LocationsResponse>('/locations').finally(() => { pending = null; });
    }
    return pending;
};
