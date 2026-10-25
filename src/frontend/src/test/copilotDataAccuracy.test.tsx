import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AiCopilotCard } from '../api/aiCopilotApi';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { formatCopilotClock, formatCopilotDateTime } from '../components/copilot/displayFormatters';
import { isPatientReadAlias } from '../components/copilot/patientPresentation';

const card = (type: string, data: unknown): AiCopilotCard => ({ type, title: 'Dữ liệu', data, sources: [] });

describe('copilot data accuracy', () => {
    it.each(['Lịch hẹn sắp tới', 'Lịch khám sắp tới', 'Lịch sắp tới'])('preserves the upcoming condition when typing %s in the patient widget', question => {
        expect(isPatientReadAlias(question)).toBe(true);
    });
    it.each([
        ['unpaid', 'Chưa thanh toán'],
        ['paid_in_full', 'Đã thanh toán'],
        ['partially_paid', 'Thanh toán một phần'],
        ['unverifiable', 'Chưa xác minh']
    ])('localizes %s on both prescription and item without exposing a raw tooltip', (status, label) => {
        const { container } = render(<>{renderCopilotCardData(card('pharmacist_prescription_queue', [{
            status: 'Issued', paymentStatus: status,
            paymentItems: [{ medicine: 'Thuốc thử', requiredQuantity: 2, paidQuantity: 1, itemPaymentStatus: status }]
        }]))}</>);
        expect(screen.getAllByText(label)).toHaveLength(2);
        expect(container.innerHTML).not.toContain(status);
    });

    it.each([
        ['Pending', 'Chờ xác nhận'], ['Confirmed', 'Đã xác nhận'],
        ['PendingReschedule', 'Chờ duyệt đổi lịch'], ['PendingCancellation', 'Chờ duyệt hủy']
    ])('uses the patient appointment page label for %s', (status, label) => {
        render(<>{renderCopilotCardData(card('appointments', { items: [{ appointmentCode: 'APT-1', status }] }))}</>);
        expect(screen.getByText(label)).toBeInTheDocument();
        expect(screen.queryByText('Chờ xử lý')).not.toBeInTheDocument();
    });

    it.each(['appointments', 'available_slots', 'booking_preview'])('shows 15:00 and a complete calendar date for %s', type => {
        const item = { appointmentCode: 'APT-1', appointmentDate: '2026-10-24', slotDate: '2026-10-24', startTime: '15:00:00', endTime: '15:30:59' };
        const data = type === 'appointments' ? { items: [item] } : type === 'booking_preview' ? item : [item];
        const { container } = render(<>{renderCopilotCardData(card(type, data))}</>);
        expect(container).toHaveTextContent('15:00 – 15:30');
        expect(container).toHaveTextContent('24/10/2026');
        expect(container).not.toHaveTextContent('15:00:00');
    });

    it('shows unreceived appointments and received visits distinctly in the doctor queue', () => {
        render(<>{renderCopilotCardData(card('doctor_queue', [
            { appointmentCode: 'APT-1', patientName: 'Người bệnh A', intakeStatus: 'Chưa tiếp nhận', startTime: '15:00:00' },
            { visitCode: 'VIS-1', patientName: 'Người bệnh B', intakeStatus: 'Đã tiếp nhận', queueNumber: 1 }
        ]))}</>);
        expect(screen.getByText('APT-1')).toBeInTheDocument();
        expect(screen.getByText('VIS-1')).toBeInTheDocument();
        expect(screen.getByText('Chưa tiếp nhận')).toBeInTheDocument();
        expect(screen.getByText('Đã tiếp nhận')).toBeInTheDocument();
        expect(screen.getByText('15:00')).toBeInTheDocument();
    });

    it('formats UTC timestamps in Vietnam time with minutes and padded calendar dates', () => {
        const value = formatCopilotDateTime('2026-10-24T08:00:49Z');
        expect(value).toContain('15:00');
        expect(value).toContain('24/10/2026');
        expect(value).not.toContain(':49');
        expect(formatCopilotClock('15:00:49')).toBe('15:00');
    });
});
