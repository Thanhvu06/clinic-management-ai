import type { PatientVitalHistoryItemDto } from '../../types';

const decimal = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 });

const measure = (value: number | null | undefined, unit: string) => value ? `${decimal.format(value)} ${unit}` : '--';

export const formatVitalBodyMeasures = (item: Pick<PatientVitalHistoryItemDto, 'bmi' | 'weight' | 'height'>) =>
    `${item.bmi ? decimal.format(item.bmi) : '--'} · ${measure(item.weight, 'kg')} / ${measure(item.height, 'cm')}`;
