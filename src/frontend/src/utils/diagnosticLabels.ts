import type { DiagnosticCategory, DiagnosticOrderStatus } from '../types';

export const diagnosticStatusLabels: Record<DiagnosticOrderStatus, string> = {
    Ordered: 'Chờ thực hiện',
    InProgress: 'Đang thực hiện',
    Completed: 'Đã hoàn tất',
    Cancelled: 'Đã hủy',
};

export const diagnosticStatusTone: Record<DiagnosticOrderStatus, string> = {
    Ordered: 'pending', InProgress: 'confirmed', Completed: 'completed', Cancelled: 'cancelled',
};

export const diagnosticCategoryLabels: Record<DiagnosticCategory, string> = {
    Laboratory: 'Xét nghiệm', Ultrasound: 'Siêu âm', Imaging: 'Chẩn đoán hình ảnh', Other: 'Khác',
};
