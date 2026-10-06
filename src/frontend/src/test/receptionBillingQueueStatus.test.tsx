import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { DialogProvider } from '../contexts/DialogContext';
import { billingApi } from '../api/billingApi';
import type { UnbilledVisitDto } from '../types';

vi.mock('../api/billingApi', () => ({
    billingApi: {
        reception: {
            getTodayKpi: vi.fn().mockResolvedValue({ success: true, data: null }),
            getInvoices: vi.fn().mockResolvedValue({ success: true, data: { items: [], totalItems: 0 } }),
            getUnbilledVisits: vi.fn(),
        },
    },
}));

describe('Reception billing queue status', () => {
    it('renders WaitingForDoctor in Vietnamese and unknown statuses without raw enum text', async () => {
        const visits: UnbilledVisitDto[] = ['WaitingForDoctor', 'UnexpectedVisitStatus', 'toString'].map((status, index) => ({
            visitId: index + 1,
            visitCode: `VISIT-${index + 1}`,
            patientId: index + 1,
            patientName: `Bệnh nhân ${index + 1}`,
            departmentName: 'Khoa Nội',
            doctorName: 'Nguyễn Văn An',
            visitDate: '2026-10-06',
            status,
            unbilledItemCount: 1,
            estimatedTotal: 200000,
        }));
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({
            success: true,
            message: 'OK',
            data: { items: visits, page: 1, pageSize: 10, totalItems: visits.length, totalPages: 1 },
        });

        render(
            <DialogProvider>
                <ReceptionBilling />
            </DialogProvider>
        );

        fireEvent.click(screen.getByRole('button', { name: /Hàng đợi chờ lập hóa đơn/ }));

        expect(await screen.findByText('Chờ bác sĩ')).toBeInTheDocument();
        expect(screen.queryByText('WaitingForDoctor')).not.toBeInTheDocument();
        expect(screen.getAllByText('Không xác định')).toHaveLength(2);
        expect(screen.queryByText('UnexpectedVisitStatus')).not.toBeInTheDocument();
        expect(screen.queryByText('toString')).not.toBeInTheDocument();
    });
});
