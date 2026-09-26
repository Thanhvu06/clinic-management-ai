import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';
import { DialogProvider } from '../contexts/DialogContext';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({
    default: {
        get: vi.fn(),
        post: vi.fn(),
    }
}));

const mockPrescriptions = [
    {
        id: 10,
        appointmentId: 101,
        appointmentCode: 'APT-101',
        appointmentDate: '2026-09-07',
        patientName: 'Nguyễn Văn Test',
        patientPhone: '0901234567',
        doctorName: 'BS.CKI Nguyễn Minh Khải',
        status: 'Issued',
        itemCount: 2,
        createdAt: '2026-09-06T10:00:00Z'
    }
];

const mockDetailValid = {
    id: 10,
    appointmentId: 101,
    appointmentCode: 'APT-101',
    patientId: 1,
    patientName: 'Nguyễn Văn Test',
    patientPhone: '0901234567',
    doctorId: 1,
    doctorName: 'BS.CKI Nguyễn Minh Khải',
    status: 'Issued',
    createdAt: '2026-09-06T10:00:00Z',
    items: [
        {
            medicineId: 1,
            medicineCode: 'MED01',
            medicineName: 'Paracetamol 500mg',
            unit: 'Viên',
            quantity: 10,
            availableStock: 100,
            isActive: true,
            dosage: '1 viên',
            frequency: '2 lần/ngày'
        }
    ]
};

const mockDetailLowStock = {
    ...mockDetailValid,
    items: [
        {
            medicineId: 1,
            medicineCode: 'MED01',
            medicineName: 'Paracetamol 500mg',
            unit: 'Viên',
            quantity: 50,
            availableStock: 5, // insufficient
            isActive: true,
            dosage: '1 viên',
            frequency: '2 lần/ngày'
        }
    ]
};

const mockDetailInactive = {
    ...mockDetailValid,
    items: [
        {
            medicineId: 2,
            medicineCode: 'MED02',
            medicineName: 'Thuốc Cũ Hết Hạn',
            unit: 'Hộp',
            quantity: 1,
            availableStock: 10,
            isActive: false, // inactive
            dosage: '1 gói',
            frequency: '1 lần/ngày'
        }
    ]
};

describe('PharmacyPrescriptions Component', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('renders prescription list from API', async () => {
        vi.mocked(axiosClient.get).mockResolvedValueOnce({
            success: true,
            data: { items: mockPrescriptions, totalItems: 1 }
        });

        render(
            <DialogProvider>
                <PharmacyPrescriptions />
            </DialogProvider>
        );

        expect(screen.getByText('Đang tải danh sách đơn thuốc...')).toBeInTheDocument();

        await waitFor(() => {
            expect(screen.getByText('Nguyễn Văn Test')).toBeInTheDocument();
            expect(screen.getByText('APT-101')).toBeInTheDocument();
            expect(screen.getByText('Chờ cấp thuốc')).toBeInTheDocument();
        });
    });

    it('opens detail modal and displays items with sufficient stock', async () => {
        vi.mocked(axiosClient.get)
            .mockResolvedValueOnce({
                success: true,
                data: { items: mockPrescriptions, totalItems: 1 }
            })
            .mockResolvedValueOnce({
                success: true,
                data: mockDetailValid
            });

        render(
            <DialogProvider>
                <PharmacyPrescriptions />
            </DialogProvider>
        );

        await waitFor(() => {
            expect(screen.getByText('Xem & Cấp phát')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xem & Cấp phát'));

        await waitFor(() => {
            expect(screen.getByText('Chi tiết đơn thuốc #10')).toBeInTheDocument();
            expect(screen.getByText('Paracetamol 500mg')).toBeInTheDocument();
            expect(screen.getByText('10 Viên')).toBeInTheDocument();
            expect(screen.getByText('100 Viên')).toBeInTheDocument();
        });

        const confirmBtn = screen.getByText('Xác nhận cấp thuốc & Trừ kho');
        expect(confirmBtn).not.toBeDisabled();
    });

    it('disables dispense button when medicine has insufficient stock', async () => {
        vi.mocked(axiosClient.get)
            .mockResolvedValueOnce({
                success: true,
                data: { items: mockPrescriptions, totalItems: 1 }
            })
            .mockResolvedValueOnce({
                success: true,
                data: mockDetailLowStock
            });

        render(
            <DialogProvider>
                <PharmacyPrescriptions />
            </DialogProvider>
        );

        await waitFor(() => {
            expect(screen.getByText('Xem & Cấp phát')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xem & Cấp phát'));

        await waitFor(() => {
            expect(screen.getByText(/Có thuốc trong đơn không đủ số lượng tồn kho/)).toBeInTheDocument();
        });

        const confirmBtn = screen.getByText('Xác nhận cấp thuốc & Trừ kho');
        expect(confirmBtn).toBeDisabled();
    });

    it('disables dispense button and warns when medicine is inactive', async () => {
        vi.mocked(axiosClient.get)
            .mockResolvedValueOnce({
                success: true,
                data: { items: mockPrescriptions, totalItems: 1 }
            })
            .mockResolvedValueOnce({
                success: true,
                data: mockDetailInactive
            });

        render(
            <DialogProvider>
                <PharmacyPrescriptions />
            </DialogProvider>
        );

        await waitFor(() => {
            expect(screen.getByText('Xem & Cấp phát')).toBeInTheDocument();
        });

        fireEvent.click(screen.getByText('Xem & Cấp phát'));

        await waitFor(() => {
            expect(screen.getByText(/Có thuốc trong đơn đã ngừng cung cấp hoặc ngừng hoạt động/)).toBeInTheDocument();
            expect(screen.getByText('Ngừng hoạt động')).toBeInTheDocument();
        });

        const confirmBtn = screen.getByText('Xác nhận cấp thuốc & Trừ kho');
        expect(confirmBtn).toBeDisabled();
    });
});
