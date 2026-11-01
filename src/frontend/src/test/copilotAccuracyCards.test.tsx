import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AiCopilotCard } from '../api/aiCopilotApi';
import { copilotCardEmptyMessage, renderCopilotCardData } from '../components/copilot/copilotDataRenderers';

describe('read-only copilot accuracy cards', () => {
    it.each([
        ['reception_pending_change_requests', { totalItems: 1, items: [{ appointmentCode: 'APT-READ', patientName: 'Người bệnh A', requestType: 'Reschedule', status: 'Pending' }] }, ['APT-READ', 'Người bệnh A', 'Đổi lịch', 'Chờ xử lý']],
        ['reception_unpaid_invoices', { totalItems: 1, items: [{ invoiceCode: 'INV-READ', patientName: 'Người bệnh B', totalAmount: 12345 }] }, ['INV-READ', 'Người bệnh B', '12.345 ₫', 'Chưa thanh toán']],
        ['technician_service_guidance', [{ code: 'GLU', name: 'Glucose', preparationInstructions: 'Chuẩn bị đã được cơ sở xác nhận', specimenType: 'Máu' }], ['Glucose (GLU)', 'Chuẩn bị đã được cơ sở xác nhận', 'Máu']],
        ['pharmacy_medicine_stock', [{ code: 'PARA', name: 'Paracetamol', availableStock: 10, unit: 'viên' }], ['Paracetamol (PARA)', '10 viên']],
        ['pharmacy_medicine_batches', { totalItems: 1, items: [{ batchNumber: 'BATCH-READ', medicineName: 'Paracetamol', quantityRemaining: 5, unit: 'viên', expiryDate: '2026-12-01' }] }, ['BATCH-READ', 'Paracetamol', '5 viên', '01/12/2026']],
        ['admin_revenue_report', { fromDate: '2026-10-26', toDate: '2026-11-01', totalRevenue: 1000000, totalSucceededTransactions: 2 }, ['26/10/2026', '01/11/2026', '1.000.000 ₫', '2']],
        ['admin_active_doctors', { totalItems: 1, items: [{ fullName: 'Bác sĩ kiểm thử' }] }, ['Bác sĩ kiểm thử']],
        ['admin_pending_leave_requests', { totalItems: 1, items: [{ doctorName: 'Bác sĩ A', startDateTime: '2026-11-04T08:00:00', endDateTime: '2026-11-04T17:00:00', status: 'Pending' }] }, ['Bác sĩ A', '04/11/2026 08:00', '04/11/2026 17:00', 'Chờ duyệt']],
    ])('renders %s using its typed fields', (type, data, values) => {
        const card: AiCopilotCard = { type: type as string, title: 'Dữ liệu', data };
        expect(copilotCardEmptyMessage(card)).toBeNull();
        const { container } = render(<>{renderCopilotCardData(card)}</>);
        for (const value of values as string[]) expect(container).toHaveTextContent(value);
        expect(container).not.toHaveTextContent('chưa hiển thị được');
    });

    it('shows the empty state only for an empty result', () => {
        const card = { type: 'reception_pending_change_requests', title: 'Dữ liệu', data: { totalItems: 0, items: [] } };
        expect(copilotCardEmptyMessage(card)).toBe('Không có bản ghi phù hợp.');
        render(<>{renderCopilotCardData(card)}</>);
        expect(screen.getByRole('status')).toHaveTextContent('Không có bản ghi phù hợp.');
    });

    it('renders zero revenue and does not expose extra fields', () => {
        const { container } = render(<>{renderCopilotCardData({ type: 'admin_revenue_report', title: 'Dữ liệu', data: { totalRevenue: 0, totalSucceededTransactions: 0, privateNote: 'NOT-FOR-DISPLAY' } })}</>);
        expect(container).toHaveTextContent('0 ₫');
        expect(container).not.toHaveTextContent('NOT-FOR-DISPLAY');
    });
});
