import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { SpecimenLabelPrint } from '../pages/technician/SpecimenLabelPrint';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto } from '../types';
import type { SpecimenStatus } from '../types/diagnostic';
import { encodeCode39 } from '../utils/code39';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ detail: vi.fn(), collect: vi.fn(), receive: vi.fn(), reject: vi.fn(), alert: vi.fn(), toast: vi.fn(), confirm: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getTechnicianOrderById: mocks.detail, collectSpecimen: mocks.collect, receiveSpecimen: mocks.receive, rejectSpecimen: mocks.reject,
} }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast, showConfirm: mocks.confirm }) }));

const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-SAMPLE-001', orderCode: 'DX-SAMPLE-001', patientId: 2,
    patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female', patientAge: 35, orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa',
    clinicalIndication: 'Kiểm tra sức khỏe', status: 'InProgress', orderedAtUtc: '2026-10-13T01:15:00Z', rowVersion: 'order-v1',
    items: [{ id: 123, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', status: 'InProgress', rowVersion: 'item-v1',
        specimenStatus: 'PendingCollection', specimenType: 'Máu toàn phần', containerType: 'Ống EDTA (nắp tím)', recollectCount: 0 }],
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function withStatus(status?: SpecimenStatus): DiagnosticOrderDto {
    return { ...order, items: [{ ...order.items[0], specimenStatus: status, ...(status === 'Collected' || status === 'Received' || status === 'Rejected' ? {
        specimenBarcode: 'S0000123-1', collectedAtUtc: '2026-10-13T01:30:00Z', collectedByUserName: 'KTV Trần Khoa',
    } : {}) }] };
}
function mount(labels = false) {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[labels ? '/diagnostics/orders/1/labels' : '/diagnostics/orders/1']}>
        <Routes><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /><Route path="/diagnostics/orders/:id/labels" element={<SpecimenLabelPrint />} /></Routes>
    </MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}
beforeEach(() => {
    vi.clearAllMocks();
    mocks.detail.mockResolvedValue(ok(order));
    mocks.collect.mockResolvedValue(ok(withStatus('Collected')));
    mocks.receive.mockResolvedValue(ok(withStatus('Received')));
    mocks.reject.mockResolvedValue(ok({ ...order, items: [{ ...withStatus('Rejected').items[0], rejectionReason: 'Other', rejectionNote: 'Ống mẫu không đạt' }] }));
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Specimen collection workflow', () => {
    it('collects a pending sample with its note and row version, hiding result entry until received', async () => {
        mount();
        await screen.findByRole('button', { name: 'Xác nhận đã lấy mẫu' });
        expect(screen.getByText('0/1 mẫu đã nhận')).toBeInTheDocument();
        expect(screen.getByRole('region', { name: 'Mẫu bệnh phẩm: Tổng phân tích tế bào máu' })).toHaveTextContent('Máu toàn phần');
        expect(screen.getByText('Cần nhận mẫu đạt trước khi nhập kết quả')).toBeInTheDocument();
        expect(screen.queryByLabelText('Kết quả chi tiết / Mô tả tổn thương *')).not.toBeInTheDocument();
        fireEvent.change(screen.getByLabelText('Ghi chú lấy mẫu (không bắt buộc)'), { target: { value: ' Đã nhịn ăn ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận đã lấy mẫu' }));
        await waitFor(() => expect(mocks.collect).toHaveBeenCalledWith(1, 123, { rowVersion: 'item-v1', note: 'Đã nhịn ăn' }));
        expect(await screen.findByRole('button', { name: 'Nhận mẫu đạt' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'In nhãn' })).toHaveAttribute('href', '/diagnostics/orders/1/labels');
    }, 15000);

    it('disables specimen actions and changes submit text while collecting', async () => {
        let resolveCollect!: (value: unknown) => void;
        mocks.collect.mockImplementation(() => new Promise(resolve => { resolveCollect = resolve; }));
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận đã lấy mẫu' }));
        expect(screen.getByRole('button', { name: 'Đang xác nhận...' })).toBeDisabled();
        resolveCollect(ok(withStatus('Collected')));
        await screen.findByRole('button', { name: 'Nhận mẫu đạt' });
    }, 15000);

    it('requires an Other rejection note and posts the numeric reason with row version', async () => {
        mocks.detail.mockResolvedValue(ok(withStatus('Collected')));
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Từ chối mẫu' }));
        const dialog = screen.getByRole('dialog');
        fireEvent.mouseDown(within(dialog).getByRole('combobox', { name: 'Lý do từ chối' }));
        fireEvent.click(await screen.findByText('Khác'));
        fireEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận từ chối' }));
        expect(within(dialog).getByRole('alert')).toHaveTextContent('Lý do Khác cần ghi chú ít nhất 5 ký tự.');
        expect(mocks.reject).not.toHaveBeenCalled();
        fireEvent.change(within(dialog).getByRole('textbox', { name: 'Ghi chú từ chối' }), { target: { value: ' Ống mẫu không đạt ' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Xác nhận từ chối' }));
        await waitFor(() => expect(mocks.reject).toHaveBeenCalledWith(1, 123, { rowVersion: 'item-v1', reason: 99, note: 'Ống mẫu không đạt' }));
        expect(await screen.findByRole('button', { name: 'Lấy lại mẫu' })).toBeInTheDocument();
    }, 15000);

    it('receives a collected sample and opens the unchanged result form', async () => {
        mocks.detail.mockResolvedValue(ok(withStatus('Collected')));
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Nhận mẫu đạt' }));
        await waitFor(() => expect(mocks.receive).toHaveBeenCalledWith(1, 123, { rowVersion: 'item-v1' }));
        expect(await screen.findByLabelText('Kết quả chi tiết / Mô tả tổn thương *')).toBeInTheDocument();
        expect(screen.getByText('1/1 mẫu đã nhận')).toBeInTheDocument();
    }, 15000);

    it('keeps the GĐ1 numeric entry table available for received samples', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...withStatus('Received').items[0], parameters: [{ id: 11, code: 'WBC', name: 'Bạch cầu', unit: '10^9/L', refLow: 4, refHigh: 10, criticalLow: 2, criticalHigh: 30, referenceRangeText: '4 – 10' }] }] }));
        mount();
        expect(await screen.findByRole('table', { name: 'Nhập chỉ số: Tổng phân tích tế bào máu' })).toBeInTheDocument();
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' }), { target: { value: '2' } });
        expect(screen.getByRole('alert')).toHaveTextContent('Có giá trị nguy hiểm');
    }, 15000);

    it('shows the rejection reason and note, and permits recollection', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...withStatus('Rejected').items[0], rejectionReason: 'Hemolyzed', rejectionNote: 'Cần lấy lại', rejectedAtUtc: '2026-10-13T01:35:00Z', rejectedByUserName: 'KTV Trần Khoa' }] }));
        mount();
        expect(await screen.findByRole('alert')).toHaveTextContent('Mẫu bị từ chối: Mẫu tán huyết — Cần lấy lại');
        fireEvent.click(screen.getByRole('button', { name: 'Lấy lại mẫu' }));
        await waitFor(() => expect(mocks.collect).toHaveBeenCalledWith(1, 123, { rowVersion: 'item-v1', note: undefined }));
    }, 15000);

    it.each([undefined, 'NotRequired'] as const)('preserves the original form for specimenStatus %s', async status => {
        mocks.detail.mockResolvedValue(ok(withStatus(status)));
        mount();
        expect(await screen.findByLabelText('Kết quả chi tiết / Mô tả tổn thương *')).toBeInTheDocument();
        expect(screen.queryByText('Mẫu bệnh phẩm')).not.toBeInTheDocument();
        expect(screen.queryByText('0/1 mẫu đã nhận')).not.toBeInTheDocument();
    }, 15000);

    it('reloads the order after a row version conflict and reports it inline', async () => {
        mocks.collect.mockRejectedValue({ errorCode: 'STATE_CONFLICT', message: 'Dữ liệu đã thay đổi.' });
        mocks.detail.mockResolvedValueOnce(ok(order)).mockResolvedValue(ok(withStatus('Collected')));
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận đã lấy mẫu' }));
        expect(await screen.findByRole('button', { name: 'Nhận mẫu đạt' })).toBeInTheDocument();
        expect(screen.getByRole('alert')).toHaveTextContent('Phiếu đã được tải lại.');
        expect(mocks.detail).toHaveBeenCalledTimes(2);
    }, 15000);

    it('reports API failures without changing the current sample state', async () => {
        mocks.collect.mockRejectedValue({ message: 'Không thể lấy mẫu tại cơ sở này.' });
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận đã lấy mẫu' }));
        expect(await screen.findByRole('alert')).toHaveTextContent('Không thể lấy mẫu tại cơ sở này.');
        expect(screen.getByRole('button', { name: 'Xác nhận đã lấy mẫu' })).toBeEnabled();
    }, 15000);
});

describe('Specimen labels', () => {
    it('renders the Code 39 SVG, patient and sample details, and prints on request', async () => {
        const print = vi.spyOn(window, 'print').mockImplementation(() => {});
        mocks.detail.mockResolvedValue(ok(withStatus('Collected')));
        mount(true);
        const label = await screen.findByRole('article', { name: 'Nhãn S0000123-1' });
        expect(within(label).getByText('Trần Thị An')).toBeInTheDocument();
        expect(label).toHaveTextContent('Nữ / 35 tuổi');
        expect(label).toHaveTextContent('Máu toàn phần / Ống EDTA (nắp tím)');
        expect(label).toHaveTextContent('Lấy mẫu: 08:30 13/10');
        const svg = within(label).getByRole('img', { name: 'Mã vạch S0000123-1' });
        expect(svg.querySelectorAll('rect')).toHaveLength(60);
        expect(svg).toHaveAttribute('viewBox', '0 0 211 28');
        expect(within(label).getByText('S0000123-1')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'In nhãn' }));
        expect(print).toHaveBeenCalledOnce();
    }, 15000);

    it('shows the empty state before any sample is collected', async () => {
        mount(true);
        expect(await screen.findByText('Chưa có mẫu nào được lấy')).toBeInTheDocument();
        expect(screen.queryByRole('img', { name: /Mã vạch/ })).not.toBeInTheDocument();
    }, 15000);

    it('retries label loading after an API error', async () => {
        mocks.detail.mockRejectedValueOnce(new Error('Không thể tải nhãn mẫu.')).mockResolvedValue(ok(withStatus('Collected')));
        mount(true);
        expect(await screen.findByText('Không thể tải nhãn mẫu.')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('img', { name: 'Mã vạch S0000123-1' })).toBeInTheDocument();
        expect(mocks.detail).toHaveBeenCalledTimes(2);
    }, 15000);

    it('encodes the sample identifier with standard start/stop patterns and quiet zones', () => {
        const encoded = encodeCode39('S0000123-1');
        expect(encoded.bars).toHaveLength(60);
        expect(encoded.width).toBe(211);
        expect(encoded.bars.slice(0, 5)).toEqual([{ x: 10, width: 1 }, { x: 14, width: 1 }, { x: 16, width: 3 }, { x: 20, width: 3 }, { x: 24, width: 1 }]);
        expect(encoded.bars.slice(-5).map(bar => bar.width)).toEqual([1, 1, 3, 3, 1]);
        expect(encoded.bars.at(-1)!.x + encoded.bars.at(-1)!.width).toBe(encoded.width - 10);
    });

    it.each(['', 's0000123-1', 'S*123', 'MẪU'])('rejects identifiers unsupported by standard Code 39: %s', value => {
        expect(() => encodeCode39(value)).toThrow('Mã vạch Code 39');
    });
});
