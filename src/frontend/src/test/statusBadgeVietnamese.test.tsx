import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { StatusBadge } from '../components/common/StatusBadge';

afterEach(cleanup);

describe('Vietnamese status labels', () => {
    it.each([
        ['Registered', 'Đã đăng ký'], ['WaitingForDoctor', 'Chờ khám'], ['WaitingDoctor', 'Chờ khám'],
        ['WaitingForDiagnostics', 'Đang làm cận lâm sàng'], ['InDiagnostics', 'Đang làm cận lâm sàng'],
        ['ResultsReady', 'Đã có kết quả CLS'], ['ConsultationCompleted', 'Đã khám xong'], ['Transferred', 'Đã chuyển khoa'],
        ['PendingReschedule', 'Chờ đổi lịch'], ['PendingCancellation', 'Chờ hủy lịch'],
        ['InPharmacy', 'Đang cấp thuốc'], ['InBilling', 'Đang thanh toán'],
        ['Pending', 'Chờ xử lý'], ['Confirmed', 'Đã xác nhận'], ['CheckedIn', 'Đã tiếp nhận'],
        ['InConsultation', 'Đang khám'], ['Completed', 'Hoàn thành'], ['Cancelled', 'Đã hủy'], ['NoShow', 'Vắng mặt'],
    ])('renders %s as %s', (status, label) => {
        render(<StatusBadge status={status} />); expect(screen.getByText(label)).toBeInTheDocument();
        expect(screen.queryByText(status, { exact: true })).not.toBeInTheDocument();
    });
    it.each(['UnrecognizedStatus', '', ' SomeNewCode '])('uses a safe Vietnamese fallback for %s', status => {
        render(<StatusBadge status={status} />); expect(screen.getByText('Không xác định')).toBeInTheDocument();
    });
    it('does not expose an unknown code even through a supplied label', () => {
        render(<StatusBadge status="FutureStatus" label="FutureStatus" />); expect(screen.getByText('Không xác định')).toBeInTheDocument();
    });
    it('retains custom labels for known statuses and normalizes aliases', () => {
        render(<StatusBadge status="  inDIAGNOSTICS  " label="Đang thực hiện xét nghiệm" />);
        expect(screen.getByText('Đang thực hiện xét nghiệm')).toBeInTheDocument();
    });
});
