import { useMemo } from 'react';
import type { PrescriptionSafetyWarningDto } from '../../types/prescriptionSafety';

export function useDangerMedicineIds(warnings?: PrescriptionSafetyWarningDto[]) {
    return useMemo(() => new Set(warnings?.filter(warning => warning.level === 'Danger').flatMap(warning => warning.medicineIds) || []), [warnings]);
}
