const labels: Readonly<Record<string, string>> = {
    Draft: 'Nháp',
    Issued: 'Đã phát hành',
    Dispensed: 'Đã cấp phát',
    Cancelled: 'Đã hủy',
    ReservedForPurchase: 'Giữ thuốc chờ mua'
};

export const getPrescriptionStatusLabel = (status: string): string =>
    Object.hasOwn(labels, status) ? labels[status] : 'Không xác định';
