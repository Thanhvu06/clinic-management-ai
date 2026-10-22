const decimal = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 });

export const formatDecimalVi = (value: number | null | undefined, fallback = '--'): string =>
    value === null || value === undefined || !Number.isFinite(value) ? fallback : decimal.format(value);

export const formatVitalSummaryVi = (value: string): string =>
    value.replace(/\b\d+\.\d+\b/g, number => formatDecimalVi(Number(number)));
