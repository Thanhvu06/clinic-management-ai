import type { DiagnosticCategory, DiagnosticOrderStatus } from '../types';
import type { DiagnosticResultFlag, DiagnosticServiceParameterDto } from '../types/diagnostic';

export const diagnosticResultFlagLabels: Record<DiagnosticResultFlag, string> = {
    Normal: 'Bình thường', Low: 'Thấp', High: 'Cao',
    CriticalLow: 'Thấp – NGUY HIỂM', CriticalHigh: 'Cao – NGUY HIỂM',
};

export function isCriticalDiagnosticFlag(flag: DiagnosticResultFlag): boolean {
    return flag === 'CriticalLow' || flag === 'CriticalHigh';
}

export function diagnosticFlagTone(flag: DiagnosticResultFlag): 'normal' | 'warning' | 'critical' {
    return isCriticalDiagnosticFlag(flag) ? 'critical' : flag === 'Normal' ? 'normal' : 'warning';
}

export function patientDiagnosticFlagLabel(flag: DiagnosticResultFlag): string {
    return isCriticalDiagnosticFlag(flag) ? 'Cần trao đổi với bác sĩ' : diagnosticResultFlagLabels[flag];
}

export function previewDiagnosticFlag(value: number, parameter: DiagnosticServiceParameterDto): DiagnosticResultFlag {
    if (parameter.criticalLow != null && value <= parameter.criticalLow) return 'CriticalLow';
    if (parameter.criticalHigh != null && value >= parameter.criticalHigh) return 'CriticalHigh';
    if (parameter.refLow != null && value < parameter.refLow) return 'Low';
    if (parameter.refHigh != null && value > parameter.refHigh) return 'High';
    return 'Normal';
}

export function diagnosticNotificationTime(value: string): string {
    const parts = new Intl.DateTimeFormat('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', day: '2-digit', month: '2-digit',
    }).formatToParts(new Date(value));
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value;
    return `${part('hour')}:${part('minute')} ${part('day')}/${part('month')}`;
}

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
