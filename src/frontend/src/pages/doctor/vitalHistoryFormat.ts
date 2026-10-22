import type { PatientVitalHistoryItemDto } from '../../types';

import { formatDecimalVi } from '../../utils/formatDecimalVi';

const measure = (value: number | null | undefined, unit: string) => value ? `${formatDecimalVi(value)} ${unit}` : '--';

export const formatVitalBodyMeasures = (item: Pick<PatientVitalHistoryItemDto, 'bmi' | 'weight' | 'height'>) =>
    `${item.bmi ? formatDecimalVi(item.bmi) : '--'} · ${measure(item.weight, 'kg')} / ${measure(item.height, 'cm')}`;
