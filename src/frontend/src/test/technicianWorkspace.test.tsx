import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { CopilotResourceProvider, useCopilotResource } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto } from '../types';
import dashboardStyles from '../pages/technician/TechnicianDashboard.module.css';
import metricStyles from '../components/common/MetricStrip.module.css';

const mocks = vi.hoisted(() => ({ orders: vi.fn(), stats: vi.fn(), detail: vi.fn(), alert: vi.fn(), toast: vi.fn(), confirm: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrders: mocks.orders, getTechnicianStats: mocks.stats, getTechnicianOrderById: mocks.detail } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast, showConfirm: mocks.confirm }) }));

const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-WORKSPACE', orderCode: 'DX-LAB-001', patientId: 2,
    patientName: 'Nguyễn Văn An', patientPhone: '0901234567', patientGender: 'Male', patientAge: 35,
    orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa', clinicalIndication: 'Kiểm tra sức khỏe',
    status: 'Ordered', orderedAtUtc: '2026-10-12T01:15:00Z', rowVersion: 'order-v1',
    items: [{ id: 101, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', status: 'Ordered', result: null }],
};
const detailOrder: DiagnosticOrderDto = {
    ...order, status: 'InProgress', startedAtUtc: '2026-10-12T01:30:00Z', startedByUserName: 'KTV Trần Khoa',
    items: [
        { ...order.items[0], status: 'Completed', result: { id: 501, diagnosticOrderItemId: 101, resultText: 'Đã có kết quả CBC', conclusion: 'Trong giới hạn', resultedAtUtc: '2026-10-12T01:40:00Z', resultedByUserId: 'tech-1', resultedByUserName: 'KTV Trần Khoa' } },
        { id: 102, diagnosticOrderId: 1, diagnosticServiceId: 2, serviceCode: 'US', serviceName: 'Siêu âm ổ bụng', category: 'Ultrasound', status: 'InProgress', preparationInstructions: 'Nhịn ăn ít nhất 6 tiếng trước khi siêu âm', result: null },
    ],
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function SelectionProbe() {
    const { selection } = useCopilotResource();
    return <output data-testid="selection">{JSON.stringify(selection)}</output>;
}
function mount(detail = false) {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[detail ? '/diagnostics/orders/1' : '/diagnostics']}>
        {detail && <SelectionProbe />}
        <Routes><Route path="/diagnostics" element={<TechnicianDashboard />} /><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /></Routes>
    </MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}

beforeEach(() => {
    vi.clearAllMocks();
    mocks.orders.mockResolvedValue(ok({ items: [order], totalItems: 20, page: 1, pageSize: 15 }));
    mocks.stats.mockResolvedValue(ok({ orderedCount: 3, inProgressCount: 1, completedTodayCount: 5 }));
    mocks.detail.mockResolvedValue(ok(detailOrder));
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('technician workspace', () => {
    it('paginates all 20 orders with pageSize 15 and requests page 2', async () => {
        mount();
        expect(await screen.findByText('DX-LAB-001')).toBeInTheDocument();
        expect(screen.getByText('20')).toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(mocks.orders).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2, pageSize: 15 })));
    }, 15000);

    it('shows an inline load error and retries successfully without a dialog', async () => {
        mocks.orders.mockRejectedValueOnce(new Error('Không thể tải hàng đợi.'));
        mount();
        expect(await screen.findByRole('alert')).toHaveTextContent('Không thể tải hàng đợi.');
        expect(mocks.alert).not.toHaveBeenCalled();
        await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('DX-LAB-001')).toBeInTheDocument();
        expect(screen.getByText(/Cập nhật lúc/)).toBeInTheDocument();
    }, 15000);

    it('filters InProgress from the clickable statistic and resets page to 1', async () => {
        mount();
        await screen.findByText('DX-LAB-001');
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(mocks.orders).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));
        const stat = screen.getByRole('button', { name: 'Lọc: Đang thực hiện' });
        await userEvent.click(stat);
        await waitFor(() => expect(mocks.orders).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'InProgress', page: 1 })));
        expect(stat).toHaveAttribute('aria-pressed', 'true');
        expect(stat).toHaveClass(metricStyles.pressed);
    }, 15000);

    it('keeps the loaded queue visible when the 25-second silent poll fails', async () => {
        vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
        mount();
        await screen.findByText('DX-LAB-001');
        mocks.orders.mockRejectedValueOnce(new Error('Lỗi polling.'));
        await act(async () => { vi.advanceTimersByTime(25000); });
        expect(mocks.orders).toHaveBeenCalledTimes(2);
        expect(screen.getByText('DX-LAB-001')).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        expect(mocks.alert).not.toHaveBeenCalled();
    }, 15000);

    it('shows a warning after waiting 45 minutes', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-10-12T02:00:00Z'));
        mount();
        expect(await screen.findByText('Chờ 45 phút')).toHaveClass(dashboardStyles.waitWarning);
    }, 15000);

    it('filters Ultrasound on the current page without another API request', async () => {
        const ultrasound: DiagnosticOrderDto = { ...order, id: 2, orderCode: 'DX-US-002', items: [{ ...order.items[0], id: 102, category: 'Ultrasound', serviceName: 'Siêu âm ổ bụng' }] };
        mocks.orders.mockResolvedValue(ok({ items: [order, ultrasound], totalItems: 20, page: 1, pageSize: 15 }));
        mount();
        await screen.findByText('DX-LAB-001');
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Loại dịch vụ' }));
        await userEvent.click(await screen.findByText('Siêu âm', { selector: '.ant-select-item-option-content' }));
        expect(screen.getByText('DX-US-002')).toBeInTheDocument();
        expect(screen.queryByText('DX-LAB-001')).not.toBeInTheDocument();
        expect(mocks.orders).toHaveBeenCalledTimes(1);
    }, 15000);

    it('shows progress, preparation and reception actor; conclusions stay editable', async () => {
        mount(true);
        expect(await screen.findByText('1/2 dịch vụ đã có kết quả')).toBeInTheDocument();
        expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '1');
        expect(screen.getByText(/Nhịn ăn ít nhất 6 tiếng/)).toBeInTheDocument();
        const reception = screen.getByText('Tiếp nhận').closest('li')!;
        expect(within(reception).getByText('KTV Trần Khoa')).toBeInTheDocument();
        expect(screen.queryByDisplayValue('Đã có kết quả CBC')).not.toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Bình thường' }));
        const conclusion = screen.getByLabelText('Kết luận / Đánh giá');
        expect(conclusion).toHaveValue('Bình thường');
        fireEvent.change(conclusion, { target: { value: 'Cần bác sĩ đối chiếu' } });
        expect(conclusion).toHaveValue('Cần bác sĩ đối chiếu');
        await userEvent.click(screen.getByRole('button', { name: 'Xem/Sửa kết quả' }));
        expect(screen.getByDisplayValue('Đã có kết quả CBC')).toBeInTheDocument();
    }, 15000);

    it('retries detail errors with the shared loader', async () => {
        mocks.detail.mockRejectedValueOnce(new Error('Không thể tải phiếu.'));
        mount(true);
        expect(await screen.findByRole('alert')).toHaveTextContent('Không thể tải phiếu.');
        await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('PHIẾU CHỈ ĐỊNH CẬN LÂM SÀNG')).toBeInTheDocument();
        expect(mocks.detail).toHaveBeenCalledTimes(2);
        expect(mocks.alert).not.toHaveBeenCalled();
    }, 15000);

    it('shows an empty state for an order that does not exist', async () => {
        mocks.detail.mockRejectedValueOnce({ message: 'Phiếu chỉ định không tồn tại.', errorCode: 'RESOURCE_NOT_FOUND' });
        mount(true);
        expect(await screen.findByText('Không tìm thấy phiếu chỉ định.')).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        expect(mocks.alert).not.toHaveBeenCalled();
    }, 15000);

    it('preserves Copilot selection arguments and refreshes on completed actions', async () => {
        mount(true);
        await screen.findByText('1/2 dịch vụ đã có kết quả');
        await userEvent.click(screen.getByRole('button', { name: 'Bình thường' }));
        await waitFor(() => expect(JSON.parse(screen.getByTestId('selection').textContent!)).toMatchObject({
            context: { diagnosticOrderId: 1, itemId: 102 }, source: 'technician-order-detail', label: 'Phiếu cận lâm sàng đang mở', resourceVersion: 'order-v1', actionArguments: { conclusion: 'Bình thường' },
        }));
        mocks.detail.mockResolvedValue(ok({ ...detailOrder, rowVersion: 'order-v2' }));
        act(() => { window.dispatchEvent(new Event('cliniccare:copilot-action-completed')); });
        await waitFor(() => expect(JSON.parse(screen.getByTestId('selection').textContent!)).toMatchObject({ resourceVersion: 'order-v2' }));
        expect(mocks.detail).toHaveBeenCalledTimes(2);
    }, 15000);
});
