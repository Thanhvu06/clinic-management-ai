import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MainLayout } from '../layouts/MainLayout';
import { LabSupervisorDashboard } from '../pages/lab/LabSupervisorDashboard';
import { DoctorDiagnosticVerification } from '../pages/doctor/DoctorDiagnosticVerification';
import { DiagnosticVerificationReview, VerificationResultPrint } from '../pages/common/DiagnosticVerificationReview';
import { getRedirectAfterLogin, getRoleDashboardPath, isPathAllowedForRole } from '../utils/roleRoutes';
import type { DiagnosticOrderDto } from '../types';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ role: 'LabSupervisor', queue: vi.fn(), detail: vi.fn(), verify: vi.fn(), returnOrder: vi.fn(), toast: vi.fn(), copilot: vi.fn(), logout: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getSharedVerificationQueue: mocks.queue, getVerificationOrder: mocks.detail, verifySharedOrder: mocks.verify, returnSharedOrder: mocks.returnOrder } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { id: 'reviewer', fullName: 'Trưởng khoa Đỗ Thị Lan', role: mocks.role }, logout: mocks.logout, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showToast: mocks.toast }) }));
vi.mock('../components/RoleCopilotPanel', () => ({ RoleCopilotPanel: () => { mocks.copilot(); return <div>Existing copilot</div>; } }));
vi.mock('../components/common/NotificationBell', () => ({ NotificationBell: () => <span>Thông báo</span> }));
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-LB-001', orderCode: 'DX-LB-001', facilityName: 'Cơ sở kiểm thử', patientId: 2,
    patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female', patientAge: 35, orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa',
    clinicalIndication: 'Đối chiếu kết quả CBC', status: 'InProgress', verificationStatus: 'Pending', orderedAtUtc: '2026-10-13T01:15:00Z', rowVersion: 'order-v1',
    submittedForVerificationAtUtc: '2026-10-13T01:35:00Z', submittedForVerificationByUserName: 'KTV 1', canCurrentUserVerify: true,
    items: [{ id: 123, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', status: 'Completed', rowVersion: 'item-v1',
        specimenStatus: 'Received', specimenType: 'Máu toàn phần', containerType: 'Ống EDTA', specimenBarcode: 'S0000123-1', collectedByUserName: 'KTV 1', receivedByUserName: 'KTV 2',
        result: { id: 501, diagnosticOrderItemId: 123, resultText: 'Bạch cầu thấp', conclusion: 'Cần đối chiếu lâm sàng', resultedAtUtc: '2026-10-13T01:30:00Z', resultedByUserId: 'tech1', resultedByUserName: 'KTV 1',
            hasCriticalValue: true, values: [{ parameterId: 11, parameterCode: 'WBC', parameterName: 'Bạch cầu', unit: '10^9/L', value: 2, refLow: 4, refHigh: 10, referenceRangeText: '4 – 10', flag: 'CriticalLow', sortOrder: 0 }] } }],
};
const completed: DiagnosticOrderDto = { ...order, status: 'Completed', verificationStatus: 'Verified', canCurrentUserVerify: false, verifiedByRole: 'LabSupervisor', verifiedByUserName: 'Trưởng khoa Xét nghiệm Đỗ Thị Lan', verifiedAtUtc: '2026-10-13T01:45:00Z' };
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function mount(page: 'dashboard' | 'doctor' | 'review' | 'layout' | 'print' = 'review') {
    const path = page === 'dashboard' || page === 'layout' ? '/lab' : page === 'doctor' ? '/doctor/diagnostic-verification' : page === 'print' ? '/diagnostic-verification/orders/1/result-print' : '/lab/orders/1';
    return render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[path]}><Routes>
        <Route path="/lab" element={page === 'layout' ? <MainLayout /> : page === 'review' ? <div>Đã về hàng chờ</div> : <LabSupervisorDashboard />} />
        <Route path="/lab/orders/:id" element={<DiagnosticVerificationReview />} />
        <Route path="/doctor/diagnostic-verification" element={<DoctorDiagnosticVerification />} />
        <Route path="/diagnostic-verification/orders/:id/result-print" element={<VerificationResultPrint />} />
    </Routes></MemoryRouter></ConfigProvider>);
}
beforeEach(() => {
    vi.clearAllMocks(); mocks.role = 'LabSupervisor';
    mocks.queue.mockResolvedValue(ok({ items: [order], totalItems: 1, page: 1, pageSize: 15 }));
    mocks.detail.mockResolvedValue(ok(order)); mocks.verify.mockResolvedValue(ok(completed));
    mocks.returnOrder.mockResolvedValue(ok({ ...order, verificationStatus: 'Returned' }));
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Lab supervisor and doctor verification', () => {
    it('routes the new role home and blocks other roles from lab paths', () => {
        expect(getRoleDashboardPath('LabSupervisor')).toBe('/lab');
        expect(isPathAllowedForRole('LabSupervisor', '/lab/orders/1')).toBe(true);
        expect(isPathAllowedForRole('Doctor', '/lab')).toBe(false);
        expect(isPathAllowedForRole('DiagnosticTechnician', '/lab/orders/1')).toBe(false);
        expect(isPathAllowedForRole('LabSupervisor', '/doctor')).toBe(false);
        expect(getRedirectAfterLogin('LabSupervisor', '/doctor')).toBe('/lab');
        expect(getRedirectAfterLogin('LabSupervisor', '/lab/orders/1')).toBe('/lab/orders/1');
    });
    it('shows supervisor menu and notification access without rendering Copilot', async () => {
        mount('layout');
        expect(await screen.findByRole('link', { name: 'Tổng quan xét nghiệm' })).toHaveAttribute('href', '/lab');
        expect(screen.getAllByText('Trưởng khoa xét nghiệm').length).toBeGreaterThan(0);
        expect(screen.getByText('Thông báo')).toBeInTheDocument();
        expect(mocks.copilot).not.toHaveBeenCalled();
    }, 15000);
    it('retains Copilot for an existing role and hides the doctor verification menu', async () => {
        mocks.role = 'Doctor'; mount('layout');
        expect(await screen.findByRole('link', { name: 'Bàn làm việc' })).toBeInTheDocument();
        expect(screen.queryByRole('link', { name: 'Duyệt kết quả CLS' })).not.toBeInTheDocument();
        expect(mocks.copilot).toHaveBeenCalled();
    }, 15000);
    it('hides the verification menu entry for technicians', async () => {
        mocks.role = 'DiagnosticTechnician'; mount('layout');
        expect(await screen.findByRole('link', { name: 'Hàng đợi chỉ định' })).toHaveAttribute('href', '/diagnostics');
        expect(screen.queryByRole('link', { name: 'Chờ duyệt kết quả' })).not.toBeInTheDocument();
    }, 15000);
    it('shows critical flag and review link on supervisor dashboard without StatCards', async () => {
        mount('dashboard');
        const table = await screen.findByRole('table', { name: 'Phiếu chờ duyệt' });
        expect(screen.queryByText('Chờ lâu nhất (phút)')).not.toBeInTheDocument();
        expect(screen.queryByText('Bị trả lại hôm nay')).not.toBeInTheDocument();
        expect(within(table).getByText('Có giá trị nguy hiểm')).toBeInTheDocument();
        expect(within(table).getByText('Trần Thị An')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Mở để duyệt' })).toHaveAttribute('href', '/lab/orders/1');
    }, 15000);
    it('shows empty supervisor queue without StatCards', async () => {
        mocks.queue.mockResolvedValue(ok({ items: [], totalItems: 0, page: 1, pageSize: 15 })); mount('dashboard');
        expect(await screen.findByText('Không có phiếu đang xử lý')).toBeInTheDocument();
        expect(screen.queryByText('Không có phiếu chờ duyệt')).not.toBeInTheDocument();
        expect(screen.queryByText('Bị trả lại hôm nay')).not.toBeInTheDocument();
    }, 15000);
    it('allows retry after queue loading fails', async () => {
        mocks.queue.mockRejectedValueOnce({ message: 'Không kết nối được' }); mount('dashboard');
        expect(await screen.findByText('Không kết nối được')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }));
        expect(await screen.findByText('DX-LB-001')).toBeInTheDocument();
    }, 15000);
    it('flags waits above 30 minutes', async () => {
        mocks.queue.mockResolvedValue(ok({ items: [{ ...order, submittedForVerificationAtUtc: new Date(Date.now() - 31 * 60000).toISOString() }], totalItems: 1 })); mount('dashboard');
        expect(await screen.findByText('Chờ quá 30 phút')).toBeInTheDocument();
    }, 15000);
    it('flags waits above 60 minutes', async () => {
        mocks.queue.mockResolvedValue(ok({ items: [{ ...order, submittedForVerificationAtUtc: new Date(Date.now() - 61 * 60000).toISOString() }], totalItems: 1 })); mount('dashboard');
        expect(await screen.findByText('Chờ quá 60 phút')).toBeInTheDocument();
    }, 15000);
    it('renders readonly results, specimen details and optional verification note', async () => {
        mount(); expect(await screen.findByRole('button', { name: 'Duyệt kết quả' })).toBeInTheDocument();
        expect(screen.getByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' })).toHaveTextContent('L — NGUY HIỂM');
        expect(screen.getByText(/Mã vạch: S0000123-1/)).toBeInTheDocument();
        expect(screen.getByText(/Người nhập: KTV 1/)).toBeInTheDocument();
        expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
        fireEvent.change(screen.getByLabelText('Ghi chú duyệt (không bắt buộc)'), { target: { value: ' Đã đối chiếu ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Duyệt kết quả' }));
        await waitFor(() => expect(mocks.verify).toHaveBeenCalledWith(1, { note: 'Đã đối chiếu', rowVersion: 'order-v1' }));
        expect(await screen.findByText('Đã về hàng chờ')).toBeInTheDocument();
    }, 15000);
    it('requires five trimmed characters in the return modal', async () => {
        mount(); fireEvent.click(await screen.findByRole('button', { name: 'Trả lại' }));
        const modal = screen.getByRole('dialog');
        const confirm = within(modal).getByRole('button', { name: 'Xác nhận trả lại' });
        expect(confirm).toBeDisabled();
        fireEvent.change(within(modal).getByLabelText('Lý do trả lại (ít nhất 5 ký tự)'), { target: { value: ' abcd ' } }); expect(confirm).toBeDisabled();
        fireEvent.change(within(modal).getByLabelText('Lý do trả lại (ít nhất 5 ký tự)'), { target: { value: ' Đối chiếu lại CBC ' } }); fireEvent.click(confirm);
        await waitFor(() => expect(mocks.returnOrder).toHaveBeenCalledWith(1, { reason: 'Đối chiếu lại CBC', rowVersion: 'order-v1' }));
    }, 15000);
    it('explains self verification and hides mutation actions', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, canCurrentUserVerify: false })); mount();
        expect(await screen.findByText('Bạn là người gửi hoặc nhập kết quả nên không thể tự duyệt.')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Duyệt kết quả' })).not.toBeInTheDocument();
    }, 15000);
    it('shows the ordering doctor restriction without exposing pending results', async () => {
        mocks.role = 'Doctor'; mocks.detail.mockRejectedValue({ message: 'Bác sĩ chỉ định không được tự duyệt kết quả phiếu của mình.', status: 422 }); mount();
        expect(await screen.findByText('Bác sĩ chỉ định không được tự duyệt kết quả phiếu của mình.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
    }, 15000);
    it('shows completed verification identity and common print link', async () => {
        mocks.detail.mockResolvedValue(ok(completed)); mount();
        expect(await screen.findByText(/Đã duyệt bởi Trưởng khoa Xét nghiệm Đỗ Thị Lan/)).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'In phiếu kết quả' })).toHaveAttribute('href', '/diagnostic-verification/orders/1/result-print');
        expect(screen.queryByRole('button', { name: 'Duyệt kết quả' })).not.toBeInTheDocument();
    }, 15000);
    it('shows text results when there are no numeric values', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...order.items[0], result: { ...order.items[0].result, values: [], resultText: 'Kết quả chữ' } }] })); mount();
        expect(await screen.findByText('Kết quả chữ')).toBeInTheDocument();
    }, 15000);
    it('reloads order after a rowVersion conflict and keeps the error visible', async () => {
        mocks.verify.mockRejectedValue({ message: 'Dữ liệu đã thay đổi', status: 409 }); mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Duyệt kết quả' }));
        expect(await screen.findByText('Dữ liệu đã thay đổi')).toBeInTheDocument();
        expect(mocks.detail).toHaveBeenCalledTimes(2);
    }, 15000);
    it('renders the doctor queue without StatCards and links to doctor review', async () => {
        mocks.role = 'Doctor'; mount('doctor');
        expect(await screen.findByText('DX-LB-001')).toBeInTheDocument();
        expect(screen.queryByText('Bị trả lại hôm nay')).not.toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Mở để duyệt' })).toHaveAttribute('href', '/doctor/diagnostic-verification/1');
    }, 15000);
    it('loads shared print data and returns to the supervisor review route', async () => {
        mocks.detail.mockResolvedValue(ok(completed)); mount('print');
        expect(await screen.findByRole('article', { name: 'Phiếu kết quả cận lâm sàng' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Quay lại' })).toHaveAttribute('href', '/lab/orders/1');
    }, 15000);
    it('shared print returns a doctor to the doctor review route', async () => {
        mocks.role = 'Doctor'; mocks.detail.mockResolvedValue(ok(completed)); mount('print');
        expect(await screen.findByRole('article', { name: 'Phiếu kết quả cận lâm sàng' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Quay lại' })).toHaveAttribute('href', '/doctor/diagnostic-verification/1');
    }, 15000);
});
