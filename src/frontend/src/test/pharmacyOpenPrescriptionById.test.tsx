import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { PharmacyDashboard } from '../pages/pharmacy/PharmacyDashboard';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: vi.fn() }) }));

const ok = <T,>(data: T) => ({ success: true, message: '', data });
const listItem = { id: 42, appointmentId: 1, appointmentCode: 'APT-42', appointmentDate: '2026-10-22', patientName: 'Người nhận thuốc', patientPhone: '0900000000',
    doctorName: 'BS. Hà', status: 'Issued', itemCount: 1, createdAt: '2026-10-22T01:00:00Z' };
const detail = { ...listItem, patientId: 1, doctorId: 2, items: [{ medicineId: 1, medicineCode: 'PARA', medicineName: 'Paracetamol', unit: 'Viên', quantity: 2,
    availableStock: 50, isActive: true, dosage: '1 viên', frequency: 'Ngày 2 lần' }] };
const LocationProbe = () => {
    const location = useLocation();
    return <output data-testid="location">{`${location.pathname}${location.search}`}</output>;
};

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
        if (url === '/pharmacy/prescriptions/42') return ok(detail) as never;
        if (url === '/pharmacy/prescriptions/42/payment-status') return ok({ prescriptionId: 42, paymentStatus: 'paid', isFullyPaid: true, items: [] }) as never;
        if (url === '/pharmacy/dashboard') return ok({ pendingPrescriptionsCount: 1, dispensedTodayCount: 0, lowStockCount: 0, totalActiveMedicines: 1 }) as never;
        return ok({ items: [listItem], totalItems: 1 }) as never;
    });
});
afterEach(cleanup);

const mount = (route: string) => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><LocationProbe />
    <Routes><Route path="/pharmacy" element={<PharmacyDashboard />} /><Route path="/pharmacy/prescriptions" element={<PharmacyPrescriptions />} /></Routes>
</MemoryRouter></ConfigProvider>);

describe('U8 open a prescription from the pharmacist dashboard', () => {
    it('links the dispense button to the prescription id', async () => {
        mount('/pharmacy');
        const row = (await screen.findByText('Người nhận thuốc')).closest('tr')!;
        fireEvent.click(within(row).getByRole('button', { name: 'Cấp phát' }));
        expect(screen.getByTestId('location')).toHaveTextContent('/pharmacy/prescriptions?id=42');
    });

    it('opens the requested prescription after the list loads, with a primary dispense button', async () => {
        mount('/pharmacy/prescriptions?id=42');
        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByText('Chi tiết đơn thuốc RX-261022-0042')).toBeInTheDocument();
        expect(axiosClient.get).toHaveBeenCalledWith('/pharmacy/prescriptions/42');
        const dispense = await within(dialog).findByRole('button', { name: 'Xác nhận cấp thuốc & trừ kho' });
        expect(dispense).toHaveClass('ant-btn-primary');
        await waitFor(() => expect(dispense).toBeEnabled());
    });

    it('does not open a modal without an id', async () => {
        mount('/pharmacy/prescriptions');
        await screen.findByText('Người nhận thuốc');
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(axiosClient.get).not.toHaveBeenCalledWith('/pharmacy/prescriptions/42');
    });
});
