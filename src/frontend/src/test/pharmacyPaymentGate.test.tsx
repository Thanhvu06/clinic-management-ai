import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { DialogProvider } from '../contexts/DialogContext';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

const prescription = {
    id: 10, appointmentCode: 'APT-10', patientName: 'Người bệnh', patientPhone: '0901234567',
    doctorName: 'Bác sĩ', status: 'Issued', createdAt: '2026-10-22T01:00:00Z', itemCount: 1,
    items: [{ medicineId: 1, medicineCode: 'MED-1', medicineName: 'Thuốc kiểm tra', unit: 'Viên',
        quantity: 2, availableStock: 100, isActive: true, dosage: '1 viên', frequency: '2 lần/ngày' }]
};
let paymentResponse: () => Promise<unknown>;

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockImplementation(async url => {
        if (url?.endsWith('/payment-status')) return await paymentResponse() as never;
        if (url === '/pharmacy/prescriptions/10') return { success: true, data: prescription } as never;
        return { success: true, data: { items: [prescription], totalItems: 1 } } as never;
    });
});
afterEach(cleanup);

const openDetail = async () => {
    render(<MemoryRouter><DialogProvider><PharmacyPrescriptions /></DialogProvider></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Xem & cấp phát' }));
    return await screen.findByRole('button', { name: 'Xác nhận cấp thuốc & trừ kho' });
};
const paidResponse = (isFullyPaid: boolean) => ({ success: true, data: { prescriptionId: 10, isFullyPaid } });

it.each([false, true])('gates dispensing by full payment (%s)', async isFullyPaid => {
    paymentResponse = async () => paidResponse(isFullyPaid);
    const dispense = await openDetail();
    await screen.findByText(`${isFullyPaid ? 'Thanh toán: Đã thanh toán' : 'Đơn chưa thanh toán. Người bệnh cần thanh toán tại quầy thu ngân trước khi cấp thuốc.'}`);
    if (isFullyPaid) expect(dispense).toBeEnabled();
    else {
        expect(dispense).toBeDisabled();
        expect(screen.getByText(/Đơn chưa thanh toán/)).toBeInTheDocument();
    }
    expect(axiosClient.get).toHaveBeenCalledWith('/pharmacy/prescriptions/10/payment-status');
});

it('keeps dispensing disabled while checking payment', async () => {
    let resolvePayment!: (value: unknown) => void;
    paymentResponse = () => new Promise(resolve => { resolvePayment = resolve; });
    const dispense = await openDetail();
    await screen.findByText('Đang kiểm tra thanh toán...');
    expect(dispense).toBeDisabled();
    resolvePayment(paidResponse(true));
    await waitFor(() => expect(dispense).toBeEnabled());
});

it('shows InlineError and retries payment without enabling dispensing on API failure', async () => {
    paymentResponse = async () => { throw { response: { data: { message: 'Không thể kết nối thu ngân' } } }; };
    const dispense = await openDetail();
    await screen.findByText('Không thể kết nối thu ngân');
    expect(dispense).toBeDisabled();
    paymentResponse = async () => paidResponse(true);
    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
    await waitFor(() => expect(dispense).toBeEnabled());
    expect(axiosClient.get).toHaveBeenCalledTimes(4);
});

it('rejects unverified or mismatched payment responses', async () => {
    paymentResponse = async () => ({ success: true, data: { prescriptionId: 11, isFullyPaid: true } });
    const dispense = await openDetail();
    await screen.findByText('Không thể tải trạng thái thanh toán.');
    expect(dispense).toBeDisabled();
});
