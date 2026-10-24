export const stockTransactionLabels: Record<string, string> = {
    Initial: 'Tồn đầu kỳ', StockIn: 'Nhập kho', Dispense: 'Xuất đơn',
    Adjustment: 'Kiểm kê', Reservation: 'Giữ chỗ',
    ReservationCancelled: 'Hủy giữ chỗ', Disposal: 'Hủy thuốc',
};

export function getStockTransactionLabel(type: string | number): string {
    const names = Object.keys(stockTransactionLabels);
    return stockTransactionLabels[typeof type === 'number' ? names[type - 1] : type] ?? 'Không xác định';
}
