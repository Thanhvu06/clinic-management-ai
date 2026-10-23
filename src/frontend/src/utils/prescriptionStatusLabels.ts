const labels: Readonly<Record<string, string>> = {
    Draft: 'Nháp',
    Issued: 'Đã phát hành',
    Dispensed: 'Đã cấp phát',
    Cancelled: 'Đã hủy',
    ReservedForPurchase: 'Giữ thuốc chờ mua'
};

export type PrescriptionStatusContext = 'default' | 'pharmacy';

// Với dược sĩ, đơn đã phát hành là việc đang chờ mình xử lý.
const pharmacyLabels: Readonly<Record<string, string>> = { ...labels, Issued: 'Chờ cấp thuốc' };

export const getPrescriptionStatusLabel = (status: string, context: PrescriptionStatusContext = 'default'): string => {
    const source = context === 'pharmacy' ? pharmacyLabels : labels;
    return Object.hasOwn(source, status) ? source[status] : 'Không xác định';
};
