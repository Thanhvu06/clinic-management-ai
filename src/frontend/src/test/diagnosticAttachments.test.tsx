import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DiagnosticAttachmentList } from '../components/diagnostics/DiagnosticAttachmentList';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { PatientDiagnosticResults } from '../pages/patient/PatientDiagnosticResults';
import { DiagnosticVerificationReview } from '../pages/common/DiagnosticVerificationReview';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticAttachmentDto, DiagnosticOrderDto } from '../types/diagnostic';

const mocks = vi.hoisted(() => ({
    detail: vi.fn(), doctorOrders: vi.fn(), patientOrders: vi.fn(), review: vi.fn(), context: vi.fn(),
    upload: vi.fn(), remove: vi.fn(), blob: vi.fn(), toast: vi.fn(), confirm: vi.fn(),
    createUrl: vi.fn(), revokeUrl: vi.fn(), get: vi.fn(), post: vi.fn(), delete: vi.fn(),
}));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post, delete: mocks.delete } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getTechnicianOrderById: mocks.detail, getDoctorOrdersByAppointment: mocks.doctorOrders,
    getPatientOrders: mocks.patientOrders, getVerificationOrder: mocks.review,
    uploadAttachment: mocks.upload, deleteAttachment: mocks.remove, getAttachmentBlob: mocks.blob,
    getPatientVitals: async () => ({ success: true, data: [] }), getCatalog: async () => ({ success: true, data: [] }),
} }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getPatientClinicalContext: mocks.context, getActiveMedicines: async () => ({ success: true, data: [] }) } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { id: 'reviewer', fullName: 'Bác sĩ', role: 'Doctor' }, logout: vi.fn(), isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: mocks.toast, showConfirm: mocks.confirm }) }));

const attachment: DiagnosticAttachmentDto = { id: 31, originalFileName: 'Ảnh chụp.png', contentType: 'image/png', sizeBytes: 2048, uploadedAtUtc: '2026-10-15T18:15:00Z', uploadedByUserName: 'KTV hình ảnh' };
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-ATT', orderCode: 'DX-ATT', patientId: 2,
    patientName: 'Bệnh nhân ảnh', patientPhone: '0901234567', orderingDoctorId: 5, orderingDoctorName: 'BS. An',
    specialtyName: 'Nội khoa', clinicalIndication: 'Kiểm tra hình ảnh', status: 'InProgress', verificationStatus: 'NotRequired',
    orderedAtUtc: '2026-10-16T01:00:00Z', rowVersion: 'order-v1', items: [{ id: 101, diagnosticOrderId: 1,
        diagnosticServiceId: 11, serviceCode: 'IMG', serviceName: 'X-quang ngực', category: 'Imaging', status: 'InProgress', attachments: [] }],
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function mount(path: string) {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}><Routes>
        <Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} />
        <Route path="/doctor/appointments/:id/examination" element={<DoctorExaminationWorkspace />} />
        <Route path="/patient/results" element={<PatientDiagnosticResults />} />
        <Route path="/lab/orders/:id" element={<DiagnosticVerificationReview />} />
    </Routes></MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}
function list(props: Partial<Parameters<typeof DiagnosticAttachmentList>[0]> = {}) {
    return <ConfigProvider theme={{ token: { motion: false } }}><DiagnosticAttachmentList attachments={[attachment]} {...props} /></ConfigProvider>;
}

beforeEach(() => {
    vi.resetAllMocks();
    vi.stubGlobal('URL', class extends URL { static createObjectURL = mocks.createUrl; static revokeObjectURL = mocks.revokeUrl; });
    mocks.createUrl.mockImplementation(() => `blob:private-${mocks.createUrl.mock.calls.length}`);
    mocks.blob.mockResolvedValue(new Blob(['image bytes'], { type: 'image/png' }));
    mocks.detail.mockResolvedValue(ok(order));
    mocks.upload.mockResolvedValue(ok({ ...order, items: [{ ...order.items[0], attachments: [attachment] }] }));
    mocks.remove.mockResolvedValue(ok(order));
    const visible = { ...order, status: 'Completed', items: [{ ...order.items[0], attachments: [attachment] }] };
    mocks.doctorOrders.mockResolvedValue(ok([visible]));
    mocks.patientOrders.mockResolvedValue(ok({ items: [visible], totalItems: 1, page: 1, pageSize: 50 }));
    mocks.review.mockResolvedValue(ok(visible));
    mocks.context.mockResolvedValue(ok({ patientId: 2, patientName: 'Bệnh nhân ảnh', patientPhone: '0901234567', totalPastVisits: 0, pastVisits: [],
        currentAppointment: { id: 10, appointmentCode: 'APT-ATT', patientId: 2, doctorId: 5, doctorName: 'BS. An', specialtyId: 1, specialtyName: 'Nội khoa', appointmentDate: '2026-10-16', status: 'InConsultation', reason: 'Kiểm tra', startTime: '08:00:00', endTime: '08:30:00' } }));
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe('Diagnostic attachment API', () => {
    it('sends multipart file and uses authenticated blob and delete endpoints', async () => {
        const { diagnosticApi } = await vi.importActual<typeof import('../api/diagnosticApi')>('../api/diagnosticApi');
        const file = new File(['image'], 'scan.png', { type: 'image/png' });
        await diagnosticApi.uploadAttachment(1, 101, file);
        expect(mocks.post).toHaveBeenCalledWith('/diagnostics/orders/1/items/101/attachments', expect.any(FormData), expect.objectContaining({ headers: { 'Content-Type': 'multipart/form-data' } }));
        expect((mocks.post.mock.calls[0][1] as FormData).get('file')).toBe(file);
        await diagnosticApi.deleteAttachment(1, 101, 31);
        expect(mocks.delete).toHaveBeenCalledWith('/diagnostics/orders/1/items/101/attachments/31');
        await diagnosticApi.getAttachmentBlob(31);
        expect(mocks.get).toHaveBeenCalledWith('/diagnostic-attachments/31/content', { responseType: 'blob' });
    });
});

describe('Private attachment list', () => {
    it('fetches thumbnails via blob, previews with antd and revokes URLs on list change and unmount', async () => {
        const view = render(list());
        expect(screen.getByRole('status')).toHaveTextContent('Đang tải ảnh...');
        const image = await screen.findByRole('img', { name: attachment.originalFileName });
        expect(mocks.blob).toHaveBeenCalledWith(31);
        expect(image).toHaveAttribute('src', 'blob:private-1');
        expect(screen.getByText(/2 KB • 01:15 16\/10/)).toBeInTheDocument();
        fireEvent.click(image);
        expect(await screen.findByRole('dialog')).toBeInTheDocument();
        view.rerender(list({ attachments: [{ ...attachment, id: 32 }] }));
        await waitFor(() => expect(mocks.revokeUrl).toHaveBeenCalledWith('blob:private-1'));
        await waitFor(() => expect(mocks.createUrl).toHaveBeenCalledTimes(2));
        view.unmount();
        expect(mocks.revokeUrl).toHaveBeenCalledWith('blob:private-2');
    });

    it('keeps thumbnail failures independent and retries only the failed image', async () => {
        mocks.blob.mockRejectedValueOnce(new Error('denied')).mockResolvedValue(new Blob(['ok']));
        render(list({ attachments: [attachment, { ...attachment, id: 32, originalFileName: 'Ảnh thứ hai.png' }] }));
        expect(await screen.findByRole('alert')).toHaveTextContent('Không thể tải ảnh.');
        expect(await screen.findByRole('img', { name: 'Ảnh thứ hai.png' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('img', { name: attachment.originalFileName })).toBeInTheDocument();
        expect(mocks.blob.mock.calls.map(call => call[0])).toEqual([31, 32, 31]);
    });

    it('does not create a URL for a response that arrives after unmount', async () => {
        let finish!: (blob: Blob) => void;
        mocks.blob.mockImplementation(() => new Promise<Blob>(resolve => { finish = resolve; }));
        const view = render(list());
        view.unmount();
        await act(async () => { finish(new Blob(['late'])); });
        expect(mocks.createUrl).not.toHaveBeenCalled();
    });

    it('asks for confirmation before deleting and disables the busy delete button', async () => {
        let finish!: () => void;
        const remove = vi.fn(() => new Promise<void>(resolve => { finish = resolve; }));
        render(list({ canDelete: true, onDelete: remove }));
        fireEvent.click(screen.getByRole('button', { name: `Xóa ${attachment.originalFileName}` }));
        expect(mocks.confirm).toHaveBeenCalledWith(expect.stringContaining(attachment.originalFileName), expect.any(Function), 'Xóa tệp đính kèm');
        expect(remove).not.toHaveBeenCalled();
        act(() => { mocks.confirm.mock.calls[0][1](); });
        expect(remove).toHaveBeenCalledWith(31);
        expect(screen.getByRole('button', { name: 'Đang xóa...' })).toBeDisabled();
        await act(async () => { finish(); });
        expect(screen.getByRole('button', { name: `Xóa ${attachment.originalFileName}` })).toBeEnabled();
    });

    it('opens PDF through a blob and revokes its URL on unmount', async () => {
        const tab = { opener: {}, location: { replace: vi.fn() }, close: vi.fn() };
        vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
        const view = render(list({ attachments: [{ ...attachment, contentType: 'application/pdf', originalFileName: 'Kết quả.pdf', sizeBytes: 1024 * 1024 }] }));
        expect(mocks.blob).not.toHaveBeenCalled();
        fireEvent.click(screen.getByRole('button', { name: 'Kết quả.pdf' }));
        await waitFor(() => expect(tab.location.replace).toHaveBeenCalledWith('blob:private-1'));
        expect(tab.opener).toBeNull();
        expect(screen.getByText(/1.0 MB/)).toBeInTheDocument();
        view.unmount();
        expect(mocks.revokeUrl).toHaveBeenCalledWith('blob:private-1');
    });

    it('shows a PDF retry after a failed request and closes the empty tab', async () => {
        const tab = { opener: {}, location: { replace: vi.fn() }, close: vi.fn() };
        vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
        mocks.blob.mockRejectedValueOnce(new Error('denied')).mockResolvedValue(new Blob(['pdf']));
        render(list({ attachments: [{ ...attachment, contentType: 'application/pdf', originalFileName: 'scan.pdf' }] }));
        fireEvent.click(screen.getByRole('button', { name: 'scan.pdf' }));
        expect(await screen.findByRole('alert')).toHaveTextContent('Không thể tải PDF.');
        expect(tab.close).toHaveBeenCalledOnce();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await waitFor(() => expect(tab.location.replace).toHaveBeenCalledWith('blob:private-1'));
    });

    it('distinguishes an empty list from loading and errors', () => {
        render(list({ attachments: [] }));
        expect(screen.getByText('Chưa có tệp đính kèm.')).toBeInTheDocument();
        expect(mocks.blob).not.toHaveBeenCalled();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });
});

describe('Attachment screens', () => {
    it.each(['Imaging', 'Ultrasound'] as const)('shows an upload area for %s and calls API with order, item and file', async category => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...order.items[0], category }] }));
        const view = mount('/diagnostics/orders/1');
        await screen.findByRole('button', { name: 'Thêm tệp đính kèm' });
        const file = new File(['png'], 'scan.png', { type: 'image/png' });
        fireEvent.change(view.container.querySelector('input[type="file"]')!, { target: { files: [file] } });
        await waitFor(() => expect(mocks.upload).toHaveBeenCalledWith(1, 101, file));
        expect(await screen.findByText(attachment.originalFileName)).toBeInTheDocument();
    }, 15000);

    it('does not show an upload area for Laboratory', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...order.items[0], category: 'Laboratory' }] }));
        mount('/diagnostics/orders/1');
        await screen.findByText('X-quang ngực');
        expect(screen.queryByRole('button', { name: 'Thêm tệp đính kèm' })).not.toBeInTheDocument();
        expect(screen.queryByRole('region', { name: 'Tệp đính kèm: X-quang ngực' })).not.toBeInTheDocument();
    }, 15000);

    it.each(['oversized', 'invalid'] as const)('beforeUpload blocks %s files', async kind => {
        const view = mount('/diagnostics/orders/1');
        await screen.findByRole('button', { name: 'Thêm tệp đính kèm' });
        const file = kind === 'oversized' ? new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'scan.png', { type: 'image/png' }) : new File(['text'], 'scan.txt', { type: 'text/plain' });
        fireEvent.change(view.container.querySelector('input[type="file"]')!, { target: { files: [file] } });
        await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith(kind === 'oversized' ? 'Mỗi tệp không được vượt quá 10 MB.' : 'Chỉ chấp nhận JPG, PNG, WebP hoặc PDF.', 'error'));
        expect(mocks.upload).not.toHaveBeenCalled();
    }, 15000);

    it('disables upload while pending and reports a server error', async () => {
        let fail!: (reason: unknown) => void;
        mocks.upload.mockImplementation(() => new Promise((_, reject) => { fail = reject; }));
        const view = mount('/diagnostics/orders/1');
        await screen.findByRole('button', { name: 'Thêm tệp đính kèm' });
        fireEvent.change(view.container.querySelector('input[type="file"]')!, { target: { files: [new File(['png'], 'scan.png', { type: 'image/png' })] } });
        expect(await screen.findByRole('button', { name: 'Đang tải lên...' })).toBeDisabled();
        await act(async () => { fail({ message: 'Phiếu đang chờ duyệt.' }); });
        await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith('Phiếu đang chờ duyệt.', 'error'));
        expect(screen.getByRole('button', { name: 'Thêm tệp đính kèm' })).toBeEnabled();
    }, 15000);

    it.each(['Ordered', 'Completed', 'Pending', 'CancelledItem'] as const)('renders read-only attachments when %s', async state => {
        mocks.detail.mockResolvedValue(ok({ ...order, status: state === 'Ordered' || state === 'Completed' ? state : 'InProgress', verificationStatus: state === 'Pending' ? 'Pending' : 'NotRequired', items: [{ ...order.items[0], status: state === 'CancelledItem' ? 'Cancelled' : 'Completed', attachments: [attachment] }] }));
        mount('/diagnostics/orders/1');
        expect(await screen.findByText(attachment.originalFileName)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thêm tệp đính kèm' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: `Xóa ${attachment.originalFileName}` })).not.toBeInTheDocument();
    }, 15000);

    it('technician deletion calls the scoped endpoint after confirmation', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...order.items[0], attachments: [attachment] }] }));
        mount('/diagnostics/orders/1');
        fireEvent.click(await screen.findByRole('button', { name: `Xóa ${attachment.originalFileName}` }));
        act(() => { mocks.confirm.mock.calls[0][1](); });
        await waitFor(() => expect(mocks.remove).toHaveBeenCalledWith(1, 101, 31));
        expect(await screen.findByText('Chưa có tệp đính kèm.')).toBeInTheDocument();
    }, 15000);

    it('doctor workspace shows the read-only attachment list', async () => {
        mount('/doctor/appointments/10/examination');
        await screen.findByText('Bệnh nhân ảnh');
        fireEvent.click(screen.getByRole('button', { name: /Cận lâm sàng/ }));
        const attachments = await screen.findByRole('list', { name: 'Tệp đính kèm' });
        expect(within(attachments).getByText(attachment.originalFileName)).toBeInTheDocument();
        expect(within(attachments).queryByRole('button', { name: /Xóa/ })).not.toBeInTheDocument();
    }, 15000);

    it.each(['/patient/results', '/lab/orders/1'])('%s shows attachments returned by the API read-only', async path => {
        mount(path);
        const attachments = await screen.findByRole('list', { name: 'Tệp đính kèm' });
        expect(within(attachments).getByText(attachment.originalFileName)).toBeInTheDocument();
        expect(within(attachments).queryByRole('button', { name: /Xóa/ })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thêm tệp đính kèm' })).not.toBeInTheDocument();
    }, 15000);
});
