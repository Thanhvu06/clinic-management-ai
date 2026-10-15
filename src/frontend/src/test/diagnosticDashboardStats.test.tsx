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
function card(title: string) { return screen.getByText(title).closest('.ant-card')! as HTMLElement; }
beforeEach(() => {
    vi.resetAllMocks();
    mocks.orders.mockResolvedValue(ok({ items: [order, { ...order, id: 2, orderCode: 'DX-RETURNED', verificationStatus: 'Returned' }], totalItems: 2 }));
    mocks.technicianStats.mockResolvedValue(ok({ orderedCount: 1, inProgressCount: 2, completedTodayCount: 3, pendingVerificationCount: 7, returnedCount: 8, overdueCount: 9 }));
    mocks.queue.mockResolvedValue(ok({ items: [order], totalItems: 1 }));
    mocks.verificationStats.mockResolvedValue(ok({ pendingCount: 41, criticalPendingCount: 12, overduePendingCount: 13, oldestPendingMinutes: 124, returnedTodayCount: 15, verifiedTodayCount: 16 }));
});
afterEach(cleanup);

describe('Diagnostic dashboard statistics', () => {
    it('shows pending results as read only and returned results as editable, with colored tags', async () => {
        mount();
        const pending = await screen.findByRole('button', { name: 'Xem (đang chờ duyệt)' });
        expect(pending).not.toHaveClass('ant-btn-primary');
        expect(screen.getByRole('button', { name: 'Sửa kết quả' })).toHaveClass('ant-btn-primary');
        expect(screen.getByText('Chờ duyệt')).toHaveClass('ant-tag-processing');
        expect(screen.getByText('Bị trả lại')).toHaveClass('ant-tag-warning');
        expect(within(card('Chờ trưởng khoa duyệt')).getByText('7')).toBeInTheDocument();
        expect(within(card('Bị trả lại cần sửa')).getByText('8')).toBeInTheDocument();
        expect(within(card('Chờ quá 30 phút')).getByText('9')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Bị trả lại cần sửa/ })).not.toBeInTheDocument();
        const ordersRequests = mocks.orders.mock.calls.length;
        fireEvent.click(card('Bị trả lại cần sửa'));
        expect(mocks.orders).toHaveBeenCalledTimes(ordersRequests);
        expect(screen.getByRole('button', { name: 'Lọc: Đang thực hiện' })).toHaveAttribute('aria-pressed', 'false');
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
        expect(within(card('Chờ trưởng khoa duyệt')).getByText('0')).toBeInTheDocument();
    });
    it('uses scope wide supervisor stats rather than the current queue page and refreshes both APIs', async () => {
        mount(true);
        await screen.findByText('DX-PENDING');
        for (const [title, value] of [['Chờ duyệt', '41'], ['Có giá trị nguy hiểm', '12'], ['Chờ lâu nhất (phút)', '124'], ['Bị trả lại hôm nay', '15'], ['Đã duyệt hôm nay', '16'], ['Quá 30 phút chưa duyệt', '13']]) {
            expect(within(card(title)).getByText(value)).toBeInTheDocument();
        }
        expect(screen.queryByText('Trong trang hiện tại')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Làm mới' }));
        await waitFor(() => expect(mocks.queue).toHaveBeenCalledTimes(2));
        expect(mocks.verificationStats).toHaveBeenCalledTimes(2);
    });
    it.each(['reject', 'sync throw', 'unsuccessful'])('keeps queue available and shows unavailable stats on %s', async failure => {
        if (failure === 'reject') mocks.verificationStats.mockRejectedValue(new Error('stats unavailable'));
        else if (failure === 'sync throw') mocks.verificationStats.mockImplementation(() => { throw new Error('missing mock'); });
        else mocks.verificationStats.mockResolvedValue({ success: false });
        mount(true);
        await screen.findByText('DX-PENDING');
        expect(within(card('Chờ duyệt')).getByText('—')).toBeInTheDocument();
        expect(screen.getAllByText('Không tải được số liệu')).toHaveLength(6);
        expect(screen.queryByRole('button', { name: /Thử lại/i })).not.toBeInTheDocument();
    });
    it('renders the queue before a slow stats request finishes', async () => {
        let resolve!: (value: unknown) => void;
        mocks.verificationStats.mockReturnValue(new Promise(done => { resolve = done; }));
        mount(true);
        expect(await screen.findByText('DX-PENDING')).toBeInTheDocument();
        expect(screen.getAllByText('Đang tải số liệu...')).toHaveLength(6);
        await act(async () => resolve(ok({ pendingCount: 41 })));
        expect(within(card('Chờ duyệt')).getByText('41')).toBeInTheDocument();
    });
    it('keeps exactly one retry button when both queue and stats fail', async () => {
        mocks.queue.mockRejectedValue(new Error('Queue unavailable'));
        mocks.verificationStats.mockRejectedValue(new Error('Stats unavailable'));
        mount(true);
        expect(await screen.findByText('Queue unavailable')).toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: /Thử lại/i })).toHaveLength(1);
    });
});
