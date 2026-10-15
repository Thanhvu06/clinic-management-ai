import type { DiagnosticCategory, DiagnosticOrderStatus } from '../types';
import type { DiagnosticResultFlag, DiagnosticResultValueDto, DiagnosticServiceParameterDto } from '../types/diagnostic';
import type { SpecimenStatus, SpecimenRejectionReason } from '../types/diagnostic';

export const specimenStatusLabels: Record<SpecimenStatus, string> = {
    NotRequired: 'Không cần mẫu', PendingCollection: 'Chờ lấy mẫu', Collected: 'Đã lấy mẫu', Received: 'Đã nhận mẫu', Rejected: 'Mẫu bị từ chối',
};
export const specimenRejectionLabels: Record<SpecimenRejectionReason, string> = {
    Hemolyzed: 'Mẫu tán huyết', Clotted: 'Mẫu đông', InsufficientVolume: 'Không đủ thể tích', WrongContainer: 'Sai loại ống',
    LeakedOrBroken: 'Ống vỡ/rò rỉ', LabelMismatch: 'Thông tin nhãn không khớp', PatientNotPrepared: 'Bệnh nhân chưa chuẩn bị đúng', Other: 'Khác',
};
export const specimenRejectionValues: Record<SpecimenRejectionReason, number> = {
    Hemolyzed: 1, Clotted: 2, InsufficientVolume: 3, WrongContainer: 4, LeakedOrBroken: 5, LabelMismatch: 6, PatientNotPrepared: 7, Other: 99,
};

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

type DeltaValue = Pick<DiagnosticResultValueDto, 'previousValue' | 'previousResultedAtUtc' | 'deltaChange' | 'deltaPercent'>;

const deltaNumber = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 4 });
const deltaPercentNumber = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 });
const signed = (value: number, format: Intl.NumberFormat) => `${value > 0 ? '+' : value < 0 ? '-' : ''}${format.format(Math.abs(value))}`;

export function formatDelta(value: DeltaValue): string {
    if (value.previousValue == null) return '—';
    const date = value.previousResultedAtUtc
        ? diagnosticNotificationTime(/(?:Z|[+-]\d{2}:\d{2})$/.test(value.previousResultedAtUtc) ? value.previousResultedAtUtc : `${value.previousResultedAtUtc}Z`).split(' ')[1]
        : null;
    const previous = `Trước: ${deltaNumber.format(value.previousValue)}${date ? ` (${date})` : ''}`;
    if (value.deltaChange == null) return previous;
    const percent = value.deltaPercent == null ? '' : ` (${signed(value.deltaPercent, deltaPercentNumber)}%)`;
    return `${previous} → Δ ${signed(value.deltaChange, deltaNumber)}${percent}`;
}

export const diagnosticStatusLabels: Record<DiagnosticOrderStatus, string> = {
    Ordered: 'Chờ thực hiện',
    InProgress: 'Đang thực hiện',
    Completed: 'Đã hoàn tất',
    Cancelled: 'Đã hủy',
};

export const diagnosticVerificationLabels = {
    NotRequired: 'Không cần duyệt', Pending: 'Chờ duyệt', Verified: 'Đã duyệt', Returned: 'Bị trả lại',
};

export const diagnosticStatusTone: Record<DiagnosticOrderStatus, string> = {
    Ordered: 'pending', InProgress: 'confirmed', Completed: 'completed', Cancelled: 'cancelled',
};

export const diagnosticCategoryLabels: Record<DiagnosticCategory, string> = {
    Laboratory: 'Xét nghiệm', Ultrasound: 'Siêu âm', Imaging: 'Chẩn đoán hình ảnh', Other: 'Khác',
};
