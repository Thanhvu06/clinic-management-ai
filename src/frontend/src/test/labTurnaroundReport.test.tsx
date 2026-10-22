import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MainLayout } from '../layouts/MainLayout';
import { LabTurnaroundReport } from '../pages/lab/LabTurnaroundReport';
import type { DiagnosticTurnaroundReportDto } from '../types/diagnostic';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ report: vi.fn(), copilot: vi.fn(), logout: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTurnaroundReport: mocks.report } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { id: 'lab', fullName: 'Trưởng khoa Đỗ Thị Lan', role: 'LabSupervisor' }, logout: mocks.logout, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showToast: vi.fn() }) }));
vi.mock('../components/RoleCopilotPanel', () => ({ RoleCopilotPanel: () => { mocks.copilot(); return null; } }));
vi.mock('../components/common/NotificationBell', () => ({ NotificationBell: () => <span>Thông báo</span> }));

const report: DiagnosticTurnaroundReportDto = {
    from: '2026-10-10', to: '2026-10-16', category: null, orderCount: 12,
    segments: [
        { key: 'OrderToReceive', count: 9, medianMinutes: 12.5, p90Minutes: 30 },
        { key: 'ReceiveToResult', count: 9, medianMinutes: 25, p90Minutes: 48 },
        { key: 'ResultToVerify', count: 0, medianMinutes: null, p90Minutes: null },
        { key: 'Total', count: 12, medianMinutes: 41.5, p90Minutes: 75 },
    ],
    withinTargetCount: 10, withinTargetPercent: 83.3,
    byService: [
        { serviceCode: 'US-ABD', serviceName: 'Siêu âm bụng', category: 'Ultrasound', orderCount: 4, medianTotalMinutes: 40, p90TotalMinutes: 52, targetMinutes: 45, withinTargetPercent: 75 },
        { serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', orderCount: 8, medianTotalMinutes: 38, p90TotalMinutes: 50, targetMinutes: 60, withinTargetPercent: 100 },
    ],
    slowestOrders: [
        { orderId: 9, orderCode: 'DX-SLOW-9', patientName: 'Trần Thị An', serviceNames: ['Siêu âm bụng', 'Tổng phân tích tế bào máu'],
            orderedAtUtc: '2026-10-15T16:10:00Z', completedAtUtc: '2026-10-15T17:25:00Z', totalMinutes: 75, targetMinutes: 60 },
    ],
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });

function mount(path = '/lab/tat', layout = false) {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[path]}><Routes>
        {layout
            ? <Route element={<MainLayout />}><Route path="/lab/tat" element={<div>Báo cáo TAT</div>} /></Route>
            : <Route path="/lab/tat" element={<LabTurnaroundReport />} />}
        <Route path="/lab/orders/:id" element={<div>Chi tiết phiếu</div>} />
    </Routes></MemoryRouter></ConfigProvider>);
}
// "Đạt chỉ tiêu" is also a table header, so pick the match that sits inside a StatCard.
const card = (title: string) => screen.getAllByText(title).map(node => node.closest('.ant-card')).find(Boolean)! as HTMLElement;
const table = (name: string) => screen.getByRole('table', { name });

beforeEach(() => {
    vi.clearAllMocks();
    // 01:30 on 16/10 in Vietnam while it is still 15/10 in UTC; only Date is faked so antd timers keep running.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-15T18:30:00Z'));
    mocks.report.mockResolvedValue(ok(report));
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Lab turnaround report', () => {
    it('loads the last 7 Vietnam days and renders stats, stages, services and slow orders', async () => {
        mount();
        expect(await screen.findByText('DX-SLOW-9')).toBeInTheDocument();
        expect(mocks.report).toHaveBeenCalledTimes(1);
        expect(mocks.report).toHaveBeenCalledWith({ from: '2026-10-10', to: '2026-10-16' });
        expect(screen.getByLabelText('Từ ngày')).toHaveValue('2026-10-10');
        expect(screen.getByLabelText('Đến ngày')).toHaveValue('2026-10-16');

        expect(within(card('Số phiếu hoàn tất')).getByText('12')).toBeInTheDocument();
        expect(within(card('Trung vị tổng TAT (phút)')).getByText('41,5')).toBeInTheDocument();
        expect(within(card('P90 tổng TAT (phút)')).getByText('75')).toBeInTheDocument();
        expect(within(card('Đạt chỉ tiêu')).getByText('83,3%')).toBeInTheDocument();
        expect(within(card('Đạt chỉ tiêu')).getByText('10/12 phiếu')).toBeInTheDocument();

        const stages = within(table('Theo giai đoạn'));
        for (const header of ['Giai đoạn', 'Số phiếu có dữ liệu', 'Trung vị', 'P90']) expect(stages.getByRole('columnheader', { name: header })).toBeInTheDocument();
        const stageRow = (label: string) => within(stages.getByText(label).closest('tr')!);
        expect(stageRow('Chỉ định → nhận mẫu').getByText('12,5')).toBeInTheDocument();
        expect(stageRow('Nhận mẫu → có kết quả').getByText('48')).toBeInTheDocument();
        expect(stageRow('Gửi duyệt → đã duyệt').getAllByText('—')).toHaveLength(2);
        expect(stageRow('Tổng (chỉ định → trả kết quả)').getByText('41,5')).toBeInTheDocument();

        const services = within(table('Theo dịch vụ'));
        const ultrasoundRow = within(services.getByText('US-ABD').closest('tr')!);
        expect(ultrasoundRow.getByText('Siêu âm')).toBeInTheDocument();
        expect(ultrasoundRow.getByText('52').className).toMatch(/overTarget/);
        expect(ultrasoundRow.getByText('75%')).toBeInTheDocument();
        expect(within(services.getByText('LAB-CBC').closest('tr')!).getByText('50').className).not.toMatch(/overTarget/);

        const slow = within(table('Phiếu vượt chỉ tiêu'));
        expect(slow.getByRole('link', { name: 'DX-SLOW-9' })).toHaveAttribute('href', '/lab/orders/9');
        expect(slow.getByText('Siêu âm bụng, Tổng phân tích tế bào máu')).toBeInTheDocument();
        // Vietnam time (UTC+7): 16:10Z → 23:10 on 15/10, 17:25Z → 00:25 on 16/10.
        expect(slow.getByText('23:10 15/10')).toBeInTheDocument();
        expect(slow.getByText('00:25 16/10')).toBeInTheDocument();
    });

    it('shows a loading state with a disabled refresh button', async () => {
        let resolve!: (value: unknown) => void;
        mocks.report.mockReturnValueOnce(new Promise(done => { resolve = done; }));
        mount();
        expect(await screen.findByText('Đang tải báo cáo thời gian trả kết quả...')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Đang tải...' })).toBeDisabled();
        await act(async () => resolve(ok(report)));
        expect(screen.getByRole('button', { name: 'Làm mới' })).toBeEnabled();
        fireEvent.click(screen.getByRole('button', { name: 'Làm mới' }));
        await waitFor(() => expect(mocks.report).toHaveBeenCalledTimes(2));
        expect(mocks.report).toHaveBeenLastCalledWith({ from: '2026-10-10', to: '2026-10-16' });
    });

    it('shows an inline error with retry, then the report', async () => {
        mocks.report.mockRejectedValueOnce(new Error('Máy chủ không phản hồi'));
        mount();
        expect(await screen.findByText('Máy chủ không phản hồi')).toBeInTheDocument();
        expect(screen.queryByText('Không có phiếu hoàn tất trong khoảng thời gian này')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Thử lại/ }));
        expect(await screen.findByText('DX-SLOW-9')).toBeInTheDocument();
        expect(mocks.report).toHaveBeenCalledTimes(2);
    });

    it('shows the empty state when no order was completed', async () => {
        mocks.report.mockResolvedValue(ok({ ...report, orderCount: 0, withinTargetCount: 0, withinTargetPercent: null, byService: [], slowestOrders: [],
            segments: report.segments.map(segment => ({ ...segment, count: 0, medianMinutes: null, p90Minutes: null })) }));
        mount();
        expect(await screen.findByText('Không có phiếu hoàn tất trong khoảng thời gian này')).toBeInTheDocument();
        expect(screen.queryByText('Số phiếu hoàn tất')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Thử lại/ })).not.toBeInTheDocument();
    });

    it('validates the date range on the client without calling the API', async () => {
        mount();
        await screen.findByText('DX-SLOW-9');
        fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-10-17' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem' }));
        expect(screen.getByRole('alert')).toHaveTextContent('Từ ngày phải trước hoặc bằng Đến ngày.');

        fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-09-15' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem' }));
        expect(screen.getByRole('alert')).toHaveTextContent('Khoảng thời gian tối đa là 31 ngày.');
        expect(mocks.report).toHaveBeenCalledTimes(1);

        // 31 days inclusive is accepted.
        fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-09-16' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem' }));
        await waitFor(() => expect(mocks.report).toHaveBeenCalledTimes(2));
        expect(mocks.report).toHaveBeenLastCalledWith({ from: '2026-09-16', to: '2026-10-16' });
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('requests the report with the chosen dates and category', async () => {
        mount();
        await screen.findByText('DX-SLOW-9');
        fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-10-01' } });
        fireEvent.change(screen.getByLabelText('Đến ngày'), { target: { value: '2026-10-05' } });
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Loại dịch vụ' }));
        fireEvent.click(await screen.findByTitle('Siêu âm'));
        // Changing filters alone does not reload; "Xem" applies them.
        expect(mocks.report).toHaveBeenCalledTimes(1);
        fireEvent.click(screen.getByRole('button', { name: 'Xem' }));
        await waitFor(() => expect(mocks.report).toHaveBeenCalledTimes(2));
        expect(mocks.report).toHaveBeenLastCalledWith({ from: '2026-10-01', to: '2026-10-05', category: 'Ultrasound' });
        await screen.findByText('DX-SLOW-9');

        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Loại dịch vụ' }));
        fireEvent.click(await screen.findByTitle('Tất cả'));
        fireEvent.click(screen.getByRole('button', { name: 'Xem' }));
        await waitFor(() => expect(mocks.report).toHaveBeenCalledTimes(3));
        expect(mocks.report).toHaveBeenLastCalledWith({ from: '2026-10-01', to: '2026-10-05' });
    });

    it('shows the turnaround menu entry and page title to lab supervisors', async () => {
        mount('/lab/tat', true);
        expect(await screen.findByRole('link', { name: 'Thời gian trả kết quả' })).toHaveAttribute('href', '/lab/tat');
        expect(screen.getByRole('link', { name: 'Tổng quan xét nghiệm' })).toHaveAttribute('href', '/lab');
        expect(screen.getAllByText('Thời gian trả kết quả')).toHaveLength(1);
    }, 15000);
});
