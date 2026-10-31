import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import { LabSupervisorDashboard } from '../pages/lab/LabSupervisorDashboard';
import type { DiagnosticOrderDto } from '../types';

const mocks = vi.hoisted(() => ({ orders: vi.fn(), technicianStats: vi.fn(), queue: vi.fn(), verificationStats: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getTechnicianOrders: mocks.orders, getTechnicianStats: mocks.technicianStats,
    getSharedVerificationQueue: mocks.queue, getVerificationStats: mocks.verificationStats,
} }));
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 1, appointmentCode: 'APT-1', orderCode: 'DX-PENDING', patientId: 1, patientName: 'Bệnh nhân',
    patientPhone: '', patientGender: 'Male', orderingDoctorId: 1, orderingDoctorName: 'Bác sĩ', specialtyName: 'Nội khoa',
    clinicalIndication: '', status: 'InProgress', verificationStatus: 'Pending', orderedAtUtc: '2026-10-15T01:00:00Z', items: [],
};
const ok = <T,>(data: T) => ({ success: true, data });
function mount(lab = false) {
    render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter>{lab ? <LabSupervisorDashboard /> : <TechnicianDashboard />}</MemoryRouter></ConfigProvider>);
}
beforeEach(() => {
    vi.resetAllMocks();
    mocks.orders.mockResolvedValue(ok({ items: [order, { ...order, id: 2, orderCode: 'DX-RETURNED', verificationStatus: 'Returned' }], totalItems: 2 }));
    mocks.technicianStats.mockResolvedValue(ok({ orderedCount: 1, inProgressCount: 2, completedTodayCount: 3, pendingVerificationCount: 7, returnedCount: 8, overdueCount: 9 }));
    mocks.queue.mockResolvedValue(ok({ items: [order], totalItems: 1 }));
    mocks.verificationStats.mockResolvedValue(ok({ pendingCount: 41, criticalPendingCount: 12, overduePendingCount: 13, oldestPendingMinutes: 124, returnedTodayCount: 15, verifiedTodayCount: 16 }));
});
afterEach(cleanup);

describe('Diagnostic dashboard statistics', () => {
    it('keeps legacy pending results read only and returned results editable, without verification filters', async () => {
        mount();
        const pending = await screen.findByRole('button', { name: 'Xem (đang chờ duyệt)' });
        expect(pending).not.toHaveClass('ant-btn-primary');
        expect(screen.getByRole('button', { name: 'Sửa kết quả' })).toHaveClass('ant-btn-primary');
        expect(screen.getByText('Chờ duyệt')).toHaveClass('ant-tag-processing');
        expect(screen.getByText('Bị trả lại')).toHaveClass('ant-tag-warning');
        expect(screen.queryByRole('button', { name: /Chờ trưởng khoa duyệt/ })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Bị trả lại cần sửa/ })).not.toBeInTheDocument();
        expect(within(screen.getByText('Chờ quá 30 phút').parentElement!).getByText('9')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Chờ quá 30 phút/ })).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Lọc: Đang thực hiện' }));
        await waitFor(() => expect(mocks.orders).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'InProgress', page: 1 })));
        expect(screen.getByRole('button', { name: 'Lọc: Đang thực hiện' })).toHaveAttribute('aria-pressed', 'true');
    });
    it('disables refresh while loading and supports legacy technician stats', async () => {
        let resolve!: (value: unknown) => void;
        mocks.orders.mockReturnValueOnce(new Promise(done => { resolve = done; }));
        mocks.technicianStats.mockResolvedValue(ok({ orderedCount: 1, inProgressCount: 2, completedTodayCount: 3 }));
        mount();
        await waitFor(() => expect(mocks.orders).toHaveBeenCalled());
        expect(screen.getAllByRole('button', { name: 'Đang tải...' }).every(button => button.hasAttribute('disabled'))).toBe(true);
        await act(async () => resolve(ok({ items: [], totalItems: 0 })));
        expect(screen.getByRole('button', { name: 'Làm mới' })).toBeEnabled();
        expect(screen.queryByRole('button', { name: /Chờ trưởng khoa duyệt/ })).not.toBeInTheDocument();
    });
    it('shows only the queue on the supervisor dashboard without loading verification stats', async () => {
        mount(true);
        await screen.findByText('DX-PENDING');
        for (const title of ['Chờ duyệt', 'Có giá trị nguy hiểm', 'Chờ lâu nhất (phút)', 'Bị trả lại hôm nay', 'Đã duyệt hôm nay', 'Quá 30 phút chưa duyệt'])
            expect(screen.queryByText(title)).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Làm mới' }));
        await waitFor(() => expect(mocks.queue).toHaveBeenCalledTimes(2));
        expect(mocks.verificationStats).not.toHaveBeenCalled();
    });
    it('keeps exactly one retry button when both queue and stats fail', async () => {
        mocks.queue.mockRejectedValue(new Error('Queue unavailable'));
            mount(true);
        expect(await screen.findByText('Queue unavailable')).toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: /Thử lại/i })).toHaveLength(1);
    });
});
