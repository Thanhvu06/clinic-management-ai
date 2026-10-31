import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { DialogProvider } from '../contexts/DialogContext';

vi.mock('../api/billingApi', () => ({ billingApi: { reception: {
    getTodayKpi: vi.fn().mockResolvedValue({ success: true, data: null }),
    getInvoices: vi.fn().mockResolvedValue({ success: true, data: { items: [], totalItems: 0 } }),
    getUnbilledVisits: vi.fn().mockResolvedValue({ success: true, data: { items: [true, false].map((hasPendingPayment, index) => ({
        visitId: index + 1, visitCode: `VIS-PAY-${index + 1}`, patientId: index + 1, patientName: `Bệnh nhân ${index + 1}`,
        departmentName: 'Khoa Nội', doctorName: 'Nguyễn Văn An', visitDate: '2026-10-31', status: 'InPharmacy',
        hasPendingPayment, unbilledItemCount: 1, estimatedTotal: 200000,
    })), totalItems: 2, page: 1, pageSize: 10 } }),
} } }));
afterEach(cleanup);

it('keeps a pharmacy visit awaiting payment until its invoice has been paid', async () => {
    render(<DialogProvider><ReceptionBilling /></DialogProvider>);
    fireEvent.click(screen.getByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/ }));
    const unpaid = (await screen.findByText('VIS-PAY-1')).closest('tr')!;
    const paid = screen.getByText('VIS-PAY-2').closest('tr')!;
    expect(within(unpaid).getByText('Chờ thanh toán')).toBeInTheDocument();
    expect(within(unpaid).queryByText('Đang tại quầy thuốc')).not.toBeInTheDocument();
    expect(within(paid).getByText('Đang tại quầy thuốc')).toBeInTheDocument();
});
