import { describe, expect, it } from 'vitest';
import type { VisitStatus } from '../types/visit';
import { getVisitStatusLabel } from '../utils/visitStatusLabels';

describe('getVisitStatusLabel', () => {
    it('returns all 13 visit status labels and falls back for unknown values', () => {
        const expectedLabels: Record<VisitStatus, string> = {
            Registered: 'Đã đăng ký',
            CheckedIn: 'Đã tiếp nhận',
            WaitingForDoctor: 'Chờ bác sĩ',
            InConsultation: 'Đang khám',
            WaitingForDiagnostics: 'Chờ cận lâm sàng',
            ResultsReady: 'Đã có kết quả',
            InPharmacy: 'Đang tại quầy thuốc',
            InBilling: 'Đang thanh toán',
            Completed: 'Đã hoàn tất',
            Cancelled: 'Đã hủy',
            NoShow: 'Không đến',
            Transferred: 'Đã chuyển',
            ConsultationCompleted: 'Đã kết thúc khám',
        };

        for (const [value, label] of Object.entries(expectedLabels)) {
            expect(getVisitStatusLabel(value)).toBe(label);
        }

        for (const value of ['toString', 'Foo']) {
            expect(getVisitStatusLabel(value)).toBe('Không xác định');
        }
    });
});
