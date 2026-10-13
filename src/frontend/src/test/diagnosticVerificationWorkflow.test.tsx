import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { TechnicianVerificationQueue } from '../pages/technician/TechnicianVerificationQueue';
import { DiagnosticResultPrint } from '../pages/common/DiagnosticResultPrint';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto } from '../types';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ detail: vi.fn(), doctorDetail: vi.fn(), queue: vi.fn(), complete: vi.fn(), verify: vi.fn(), returnOrder: vi.fn(), save: vi.fn(), receive: vi.fn(), alert: vi.fn(), toast: vi.fn(), confirm: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getTechnicianOrderById: mocks.detail, getDoctorOrderById: mocks.doctorDetail, getVerificationQueue: mocks.queue,
    completeTechnicianOrder: mocks.complete, verifyOrder: mocks.verify, returnOrder: mocks.returnOrder, recordTechnicianItemResult: mocks.save, receiveSpecimen: mocks.receive,
} }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast, showConfirm: mocks.confirm }) }));
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-VR-001', orderCode: 'DX-VR-001', facilityName: 'Bệnh viện kiểm thử', patientId: 2,
    patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female', patientAge: 35, orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa',
    clinicalIndication: 'Kiểm tra sức khỏe', status: 'InProgress', verificationStatus: 'NotRequired', orderedAtUtc: '2026-10-13T01:15:00Z', rowVersion: 'order-v1',
    items: [{ id: 123, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', status: 'Completed', rowVersion: 'item-v1',
        specimenStatus: 'Received', specimenType: 'Máu toàn phần', containerType: 'Ống EDTA', specimenBarcode: 'S0000123-1',
        parameters: [{ id: 11, code: 'WBC', name: 'Bạch cầu', unit: '10^9/L', refLow: 4, refHigh: 10, criticalLow: 2, criticalHigh: 30, referenceRangeText: '4 – 10' }],
        result: { id: 501, diagnosticOrderItemId: 123, resultText: 'Bạch cầu thấp', conclusion: 'Cần đối chiếu lâm sàng', resultedAtUtc: '2026-10-13T01:30:00Z', resultedByUserId: 'tech1', resultedByUserName: 'KTV 1',
            hasCriticalValue: true, criticalNotifiedAtUtc: '2026-10-13T01:30:00Z', values: [{ parameterId: 11, parameterCode: 'WBC', parameterName: 'Bạch cầu', unit: '10^9/L', value: 2, refLow: 4, refHigh: 10, referenceRangeText: '4 – 10', flag: 'CriticalLow', sortOrder: 0 }] } }],
};
const pending: DiagnosticOrderDto = { ...order, verificationStatus: 'Pending', submittedForVerificationAtUtc: '2026-10-13T01:35:00Z', submittedForVerificationByUserName: 'KTV 1', canCurrentUserVerify: false };
const returned: DiagnosticOrderDto = { ...pending, verificationStatus: 'Returned', returnReason: 'Cần đối chiếu lại WBC', returnedByUserName: 'KTV 2', returnedAtUtc: '2026-10-13T01:40:00Z', returnCount: 1 };
const verified: DiagnosticOrderDto = { ...pending, status: 'Completed', verificationStatus: 'Verified', verifiedByUserName: 'KTV 2', verifiedAtUtc: '2026-10-13T01:45:00Z', verificationNote: 'Đã đối chiếu', completedByUserName: 'KTV 1', completedAtUtc: '2026-10-13T01:45:00Z', canCurrentUserVerify: false };
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function mount(page: 'detail' | 'queue' | 'print' | 'doctorPrint' = 'detail') {
    const path = page === 'queue' ? '/diagnostics/verification' : page === 'print' ? '/diagnostics/orders/1/result-print' : page === 'doctorPrint' ? '/doctor/diagnostic-orders/1/result-print' : '/diagnostics/orders/1';
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}><Routes>
        <Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /><Route path="/diagnostics/verification" element={<TechnicianVerificationQueue />} />
        <Route path="/diagnostics/orders/:id/result-print" element={<DiagnosticResultPrint />} /><Route path="/doctor/diagnostic-orders/:id/result-print" element={<DiagnosticResultPrint actor="doctor" />} />
    </Routes></MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}
beforeEach(() => {
    vi.clearAllMocks();
    mocks.detail.mockResolvedValue(ok(order)); mocks.doctorDetail.mockResolvedValue(ok(verified)); mocks.queue.mockResolvedValue(ok({ items: [pending], totalItems: 1, page: 1, pageSize: 15 }));
    mocks.complete.mockResolvedValue(ok(pending)); mocks.verify.mockResolvedValue(ok(verified)); mocks.returnOrder.mockResolvedValue(ok(returned));
    mocks.confirm.mockImplementation((_text, callback) => void callback());
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Two-technician result verification', () => {
    it('submits declared specimens for verification through the existing complete endpoint', async () => {
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Gửi duyệt kết quả' }));
        await waitFor(() => expect(mocks.complete).toHaveBeenCalledWith(1, { rowVersion: 'order-v1' }));
        expect(mocks.confirm).toHaveBeenCalledWith('Xác nhận gửi duyệt kết quả cho kỹ thuật viên khác?', expect.any(Function));
        expect(await screen.findByText('Bạn là người nhập kết quả nên không thể tự duyệt.')).toBeInTheDocument();
    }, 15000);

    it('locks result entry and prevents self verification while pending', async () => {
        mocks.detail.mockResolvedValue(ok(pending)); mount();
        await screen.findByText('Đang chờ kỹ thuật viên khác duyệt');
        expect(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' })).toBeDisabled();
        expect(screen.getByLabelText('Nhận xét (không bắt buộc)')).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' })).toBeDisabled();
        expect(screen.queryByRole('button', { name: 'Duyệt kết quả' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Gửi duyệt kết quả' })).not.toBeInTheDocument();
        expect(screen.getByText('Bạn là người nhập kết quả nên không thể tự duyệt.')).toBeInTheDocument();
    }, 15000);

    it('also locks specimen actions when a pending response contains a collected item', async () => {
        mocks.detail.mockResolvedValue(ok({ ...pending, items: [{ ...order.items[0], status: 'InProgress', specimenStatus: 'Collected', result: undefined }] })); mount();
        expect(await screen.findByRole('button', { name: 'Nhận mẫu đạt' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Từ chối mẫu' })).toBeDisabled();
        fireEvent.click(screen.getByRole('button', { name: 'Nhận mẫu đạt' })); expect(mocks.receive).not.toHaveBeenCalled();
    }, 15000);

    it('allows another technician to verify with an optional note and order row version', async () => {
        mocks.detail.mockResolvedValue(ok({ ...pending, canCurrentUserVerify: true })); mount();
        fireEvent.change(await screen.findByLabelText('Ghi chú duyệt (không bắt buộc)'), { target: { value: ' Đã đối chiếu ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Duyệt kết quả' }));
        await waitFor(() => expect(mocks.verify).toHaveBeenCalledWith(1, { note: 'Đã đối chiếu', rowVersion: 'order-v1' }));
        expect(await screen.findByRole('link', { name: 'In phiếu kết quả' })).toHaveAttribute('href', '/diagnostics/orders/1/result-print');
        expect(screen.getByText('Đã duyệt bởi KTV 2 lúc 08:45 13/10')).toBeInTheDocument();
    }, 15000);

    it('requires a trimmed return reason of five characters and sends the correct payload', async () => {
        mocks.detail.mockResolvedValue(ok({ ...pending, canCurrentUserVerify: true })); mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Trả lại' }));
        const dialog = screen.getByRole('dialog');
        fireEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận trả lại' }));
        expect(within(dialog).getByRole('alert')).toHaveTextContent('Lý do trả lại phải có ít nhất 5 ký tự.'); expect(mocks.returnOrder).not.toHaveBeenCalled();
        fireEvent.change(within(dialog).getByLabelText('Lý do trả lại (bắt buộc, ít nhất 5 ký tự)'), { target: { value: ' Cần đối chiếu lại WBC ' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận trả lại' }));
        await waitFor(() => expect(mocks.returnOrder).toHaveBeenCalledWith(1, { reason: 'Cần đối chiếu lại WBC', rowVersion: 'order-v1' }));
        expect(await screen.findByRole('button', { name: 'Gửi duyệt kết quả' })).toBeInTheDocument();
    }, 15000);

    it('disables verification actions and changes submit text during the request', async () => {
        let resolve!: (value: unknown) => void; mocks.verify.mockImplementation(() => new Promise(callback => { resolve = callback; }));
        mocks.detail.mockResolvedValue(ok({ ...pending, canCurrentUserVerify: true })); mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Duyệt kết quả' }));
        expect(screen.getByRole('button', { name: 'Đang cập nhật...' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Trả lại' })).toBeDisabled();
        resolve(ok(verified)); await screen.findByRole('link', { name: 'In phiếu kết quả' });
    }, 15000);

    it('shows who returned the order and why, then permits editing and resubmission', async () => {
        mocks.detail.mockResolvedValue(ok(returned)); mount();
        expect(await screen.findByRole('alert')).toHaveTextContent('Bị trả lại bởi KTV 2 lúc 08:40 13/10: Cần đối chiếu lại WBC');
        fireEvent.click(screen.getByRole('button', { name: 'Xem/Sửa kết quả' }));
        expect(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' })).toBeEnabled();
        expect(screen.getByRole('button', { name: 'Gửi duyệt kết quả' })).toBeEnabled();
    }, 15000);

    it.each([undefined, 'NotRequired'] as const)('preserves the original complete action for undeclared services with verificationStatus %s', async status => {
        mocks.detail.mockResolvedValue(ok({ ...order, verificationStatus: status, items: [{ ...order.items[0], specimenType: undefined, specimenStatus: 'NotRequired' }] })); mount();
        expect(await screen.findByRole('button', { name: 'Hoàn tất và gửi kết quả' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Gửi duyệt kết quả' })).not.toBeInTheDocument();
    }, 15000);

    it('keeps old specimen mocks without verification metadata compatible', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, verificationStatus: undefined })); mount();
        expect(await screen.findByRole('button', { name: 'Hoàn tất và gửi kết quả' })).toBeInTheDocument();
    }, 15000);

    it('reloads the order when verification hits a row version conflict', async () => {
        mocks.detail.mockResolvedValueOnce(ok({ ...pending, canCurrentUserVerify: true })).mockResolvedValue(ok(returned));
        mocks.verify.mockRejectedValue({ errorCode: 'STATE_CONFLICT', message: 'Đã thay đổi' }); mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Duyệt kết quả' }));
        await screen.findByRole('button', { name: 'Gửi duyệt kết quả' });
        expect(screen.getByText('Dữ liệu đã thay đổi. Phiếu đã được tải lại.')).toBeInTheDocument(); expect(mocks.detail).toHaveBeenCalledTimes(2);
    }, 15000);
});

describe('Verification queue', () => {
    it('renders sender, waiting time, critical flag and a link to the order, with pagination', async () => {
        mocks.queue.mockResolvedValue(ok({ items: [pending], totalItems: 31, page: 1, pageSize: 15 })); mount('queue');
        const table = await screen.findByRole('table', { name: 'Phiếu chờ duyệt' });
        expect(within(table).getByText('DX-VR-001')).toBeInTheDocument(); expect(within(table).getByText('KTV 1')).toBeInTheDocument(); expect(within(table).getByText('Có giá trị nguy hiểm')).toBeInTheDocument();
        expect(within(table).getByRole('link', { name: 'Mở phiếu' })).toHaveAttribute('href', '/diagnostics/orders/1');
        fireEvent.click(screen.getByRole('button', { name: 'Sau' })); await waitFor(() => expect(mocks.queue).toHaveBeenLastCalledWith({ page: 2, pageSize: 15 }));
    }, 15000);

    it('shows an empty queue', async () => {
        mocks.queue.mockResolvedValue(ok({ items: [], totalItems: 0, page: 1, pageSize: 15 })); mount('queue');
        expect(await screen.findByText('Không có phiếu chờ duyệt')).toBeInTheDocument();
    }, 15000);

    it('supports retry after a queue loading error', async () => {
        mocks.queue.mockRejectedValueOnce({ message: 'Không thể tải phiếu chờ duyệt.' }).mockResolvedValue(ok({ items: [], totalItems: 0, page: 1, pageSize: 15 })); mount('queue');
        await screen.findByText('Không thể tải phiếu chờ duyệt.'); fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Không có phiếu chờ duyệt'); expect(mocks.queue).toHaveBeenCalledTimes(2);
    }, 15000);
});

describe('Diagnostic result print', () => {
    it('renders numeric results, critical H/L flags, specimen barcode and reviewer, then prints', async () => {
        mocks.detail.mockResolvedValue(ok(verified)); const print = vi.spyOn(window, 'print').mockImplementation(() => {}); mount('print');
        expect(await screen.findByText('PHIẾU KẾT QUẢ CẬN LÂM SÀNG')).toBeInTheDocument(); expect(screen.getByText('Bệnh viện kiểm thử')).toBeInTheDocument();
        const table = screen.getByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('2')).toBeInTheDocument(); expect(within(table).getByText('L *')).toBeInTheDocument(); expect(within(table).getByText('4 – 10')).toBeInTheDocument();
        expect(screen.getByText('* Giá trị nguy hiểm')).toBeInTheDocument(); expect(screen.getByText('KTV 2')).toBeInTheDocument(); expect(screen.getByText('Kết quả chỉ có giá trị khi có chữ ký người duyệt')).toBeInTheDocument();
        expect(screen.getByRole('img', { name: 'Mã vạch S0000123-1' }).querySelectorAll('rect')).toHaveLength(60);
        fireEvent.click(screen.getByRole('button', { name: 'In' })); expect(print).toHaveBeenCalledOnce();
    }, 15000);

    it.each(['Pending', 'Returned'] as const)('blocks printing while verification is %s', async status => {
        mocks.detail.mockResolvedValue(ok({ ...pending, verificationStatus: status })); mount('print');
        expect(await screen.findByText('Kết quả chưa được duyệt, chưa thể in.')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'In' })).not.toBeInTheDocument();
    }, 15000);

    it('uses the existing doctor endpoint for the doctor print route', async () => {
        mount('doctorPrint'); await screen.findByText('PHIẾU KẾT QUẢ CẬN LÂM SÀNG'); expect(mocks.doctorDetail).toHaveBeenCalledWith(1); expect(mocks.detail).not.toHaveBeenCalled();
    }, 15000);

    it('prints the legacy text result and uses the facility fallback for a completed undeclared order', async () => {
        mocks.detail.mockResolvedValue(ok({ ...verified, facilityName: undefined, verificationStatus: undefined, items: [{ ...order.items[0], specimenType: undefined, specimenBarcode: undefined, result: { ...order.items[0].result!, values: [], hasCriticalValue: false, resultText: 'Nhu mô gan đồng nhất' } }] })); mount('print');
        expect(await screen.findByText('Nhu mô gan đồng nhất')).toBeInTheDocument(); expect(screen.getByText('ClinicCare AI')).toBeInTheDocument(); expect(screen.queryByRole('table')).not.toBeInTheDocument();
    }, 15000);
});
