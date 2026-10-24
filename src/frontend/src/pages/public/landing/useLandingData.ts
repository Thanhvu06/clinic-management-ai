import { useEffect, useState } from 'react';
import axiosClient from '../../../api/axiosClient';
import { medicineApi } from '../../../api/medicineApi';
import { getPublicLocations } from '../../../api/publicLocationsApi';
import type { ApiResponse, ClinicLocationDto, PublicMedicineDto } from '../../../types';
import type { LandingDoctor, LandingPackage, LandingSpecialty } from './landingTypes';

function listOf<T>(result: PromiseSettledResult<{ success: boolean; data?: T[] }>): T[] {
    return result.status === 'fulfilled' && result.value?.success && Array.isArray(result.value.data) ? result.value.data : [];
}

export function useLandingData() {
    const [specialties, setSpecialties] = useState<LandingSpecialty[]>([]);
    const [doctors, setDoctors] = useState<LandingDoctor[]>([]);
    const [healthPackages, setHealthPackages] = useState<LandingPackage[]>([]);
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [medicines, setMedicines] = useState<PublicMedicineDto[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        let active = true;
        void Promise.allSettled([
            axiosClient.get<unknown, ApiResponse<LandingSpecialty[]>>('/specialties'),
            axiosClient.get<unknown, ApiResponse<LandingDoctor[]>>('/doctors'),
            axiosClient.get<unknown, ApiResponse<LandingPackage[]>>('/health-packages'),
            getPublicLocations(),
            medicineApi.getPublicMedicines({ pageSize: 10 }),
        ]).then(([spec, doc, pkg, loc, med]) => {
            if (!active) return;
            setSpecialties(listOf(spec));
            setDoctors(listOf(doc));
            setHealthPackages(listOf(pkg));
            setLocations(listOf(loc));
            setMedicines(med.status === 'fulfilled' && med.value?.success && Array.isArray(med.value.data?.items) ? med.value.data.items : []);
            setLoading(false);
        });
        return () => { active = false; };
    }, []);

    return { specialties, doctors, healthPackages, locations, medicines, loading };
}
