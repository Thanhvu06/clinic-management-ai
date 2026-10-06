import { it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PatientPrescriptions } from '../pages/patient/PatientPrescriptions';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

it('shows prescription unit prices, line totals and total amount, with dashes for missing prices', async () => {
    const prescription = {
        id: 1, code: 'RX-PRICE-1', appointmentId: 1, appointmentCode: 'APT-1', appointmentDate: '2026-10-06',
        doctorName: 'Nguyễn Văn An', specialtyName: 'Nội khoa', diagnosis: 'Khám định kỳ', status: 'Dispensed',
        createdAt: '2026-10-06T08:00:00Z', priceIsReference: false, totalAmount: 7000,
        items: [
            { medicineId: 1, name: 'Thuốc có giá A', unit: 'Viên', quantity: 3, dosage: '1 viên', frequency: '3 lần/ngày', unitPrice: 1000, lineTotal: 3000 },
            { medicineId: 2, name: 'Thuốc có giá B', unit: 'Viên', quantity: 2, dosage: '1 viên', frequency: '2 lần/ngày', unitPrice: 2000, lineTotal: 4000 },
        ],
    };
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: [prescription, {
        ...prescription, id: 2, code: 'RX-PRICE-2', totalAmount: null, priceIsReference: true,
        items: [{ ...prescription.items[0], medicineId: 3, name: 'Thuốc chưa có giá', unitPrice: null, lineTotal: null }],
    }] });

    render(<MemoryRouter><PatientPrescriptions /></MemoryRouter>);

    const pricedRow = await screen.findByRole('row', { name: /Thuốc có giá A/ });
    expect(within(pricedRow).getByText(/1\.000\s*₫/)).toBeInTheDocument();
    expect(within(pricedRow).getByText(/3\.000\s*₫/)).toBeInTheDocument();
    expect(screen.getByText(/Tổng tiền thuốc: 7\.000\s*₫/)).toBeInTheDocument();
    const unpricedRow = screen.getByRole('row', { name: /Thuốc chưa có giá/ });
    expect(within(unpricedRow).getAllByText('—')).toHaveLength(2);
    expect(screen.getByText('Tổng tiền thuốc: —')).toBeInTheDocument();
    expect(screen.getByText('Giá tham khảo theo bảng giá hiện tại, có thể khác khi thanh toán.')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'In đơn thuốc' })).toHaveLength(2);
});
