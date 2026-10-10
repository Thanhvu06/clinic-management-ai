import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PatientProfile } from '../pages/patient/PatientProfile';
import { PatientInvoices } from '../pages/patient/PatientInvoices';
import { PatientPrescriptions } from '../pages/patient/PatientPrescriptions';
import { PatientRevisit } from '../pages/patient/PatientRevisit';
import { PatientDiagnosticResults } from '../pages/patient/PatientDiagnosticResults';
import { PatientPackageRegistrations } from '../pages/patient/PatientPackageRegistrations';
import { formatDateOnly } from '../utils/formatters';
import { InvoiceStatus } from '../types';
import printStyles from '../pages/patient/PatientPrescriptions.module.css';

const mocks = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), post: vi.fn(), invoices: vi.fn(), detail: vi.fn(), orders: vi.fn(), vitals: vi.fn(), showAlert: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, put: mocks.put, post: mocks.post } }));
vi.mock('../api/billingApi', () => ({ billingApi: { patient: { getMyInvoices: mocks.invoices, getMyInvoiceDetail: mocks.detail } } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getPatientOrders: mocks.orders, getPatientVitals: mocks.vitals } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.showAlert }) }));

const profile = { id: 1, fullName: 'Nguyễn Văn An', email: 'an@example.com', phoneNumber: '0901234567', dateOfBirth: '1990-10-11', gender: '0', address: 'Hồ Chí Minh' };
const prescription = (id: number) => ({ id, code: `RX-${id}`, appointmentId: id, appointmentCode: `APT-${id}`, appointmentDate: '2026-10-11', doctorName: 'Lan', specialtyName: 'Nội khoa', diagnosis: 'Khám định kỳ', status: 'Dispensed', createdAt: '2026-10-11', totalAmount: 7000, items: [{ medicineId: id, name: 'Thuốc A', unit: 'Viên', quantity: 7, dosage: '1 viên', frequency: '2 lần/ngày', unitPrice: 1000, lineTotal: 7000 }] });
const revisit = { id: 1, doctorId: 2, specialtyId: 3, appointmentId: 4, doctorName: 'Lan', specialtyName: 'Nội khoa', suggestedDate: '2026-10-11', status: 'PendingPatientResponse' };
const order = { id: 1, orderCode: 'DX-001', appointmentCode: 'APT-001', orderingDoctorName: 'Lan', orderedAtUtc: '2026-10-11', clinicalIndication: 'Kiểm tra', reviewedAtUtc: null, items: [{ id: 10, serviceName: 'Xét nghiệm A', serviceCode: 'XN01', category: 'Laboratory', result: null }] };
const registration = { id: 1, registrationCode: 'PK-001', healthPackageId: 3, healthPackageName: 'Gói khám tổng quát', preferredDate: '2026-10-11', contactPhone: '0901234567', createdAt: '2026-10-10', status: 'Pending' };
const mount = (node: React.ReactNode) => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter>{node}</MemoryRouter></ConfigProvider>);
beforeEach(() => {
    vi.resetAllMocks();
    mocks.orders.mockResolvedValue({ success: true, data: { items: [order] } });
    mocks.vitals.mockResolvedValue({ success: true, data: [] });
    mocks.post.mockResolvedValue({ success: true });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); document.body.classList.remove('cc-prescription-printing'); });

describe('DateOnly calendar formatting', () => {
    it.each([['2026-10-11', '11/10/2026'], ['2026-10-11T00:00:00', '11/10/2026'], ['', '']])('formats %s as %s', (input, expected) => {
        expect(formatDateOnly(input)).toBe(expected);
    });
    it('preserves invalid calendar dates and accepts leap days', () => {
        expect(formatDateOnly('2026-02-29')).toBe('2026-02-29');
        expect(formatDateOnly('invalid')).toBe('invalid');
        expect(formatDateOnly(null)).toBe('');
        expect(formatDateOnly('2024-02-29')).toBe('29/02/2024');
    });
});
describe('Patient profile', () => {
    it('replaces missing-profile content with a retryable load error', async () => {
        mocks.get.mockRejectedValueOnce({ message: 'Tải hồ sơ thất bại' }).mockResolvedValueOnce({ success: true, data: profile });
        mount(<PatientProfile />);
        expect(await screen.findByText('Tải hồ sơ thất bại')).toBeInTheDocument();
        expect(screen.queryByText('Không tìm thấy thông tin hồ sơ.')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByDisplayValue(profile.fullName)).toBeInTheDocument();
    });
    it('shows the payload message when saving fails', async () => {
        mocks.get.mockResolvedValue({ success: true, data: profile }); mocks.put.mockRejectedValue({ message: 'X' });
        mount(<PatientProfile />);
        fireEvent.click(await screen.findByRole('button', { name: 'Lưu thay đổi' }));
        await waitFor(() => expect(mocks.showAlert).toHaveBeenCalledWith('X', 'Lỗi', 'error'));
    });
    it('displays birth dates as DD/MM/YYYY and sends the ISO calendar date and numeric gender', async () => {
        mocks.get.mockResolvedValue({ success: true, data: profile }); mocks.put.mockResolvedValue({ success: true });
        mount(<PatientProfile />);
        expect(await screen.findByLabelText('Ngày sinh')).toHaveValue('11/10/1990');
        expect(screen.getByLabelText('Email')).toBeDisabled();
        fireEvent.click(screen.getByRole('button', { name: 'Lưu thay đổi' }));
        await waitFor(() => expect(mocks.put).toHaveBeenCalledWith('/patients/me', { fullName: profile.fullName, phoneNumber: profile.phoneNumber, dateOfBirth: '1990-10-11', gender: 0, address: profile.address }));
    });
    it('shows missing-profile content only after a successful empty response', async () => {
        mocks.get.mockResolvedValue({ success: true, data: null }); mount(<PatientProfile />);
        expect(await screen.findByText('Không tìm thấy thông tin hồ sơ.')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thử lại' })).not.toBeInTheDocument();
    });
});
describe('Patient invoices', () => {
    it('replaces the table with InlineError when fetching fails', async () => {
        mocks.invoices.mockRejectedValue({ message: 'Không tải được hóa đơn' }); mount(<PatientInvoices />);
        expect(await screen.findByText('Không tải được hóa đơn')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument(); expect(mocks.showAlert).not.toHaveBeenCalled();
    });
    it('paginates 25 invoices and resets to page one when the status changes', async () => {
        mocks.invoices.mockResolvedValue({ success: true, data: { items: [{ id: 1, invoiceCode: 'INV-001', status: InvoiceStatus.Unpaid, totalAmount: 200000 }], totalItems: 25 } });
        mount(<PatientInvoices />);
        fireEvent.click(await screen.findByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(mocks.invoices).toHaveBeenCalledWith(2, 10, undefined));
        fireEvent.click(screen.getByRole('radio', { name: 'Đã thanh toán' }));
        await waitFor(() => expect(mocks.invoices).toHaveBeenLastCalledWith(1, 10, InvoiceStatus.Paid));
    });
});
describe('Patient prescriptions', () => {
    it('prints only the second card and cleans up both print markers', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [prescription(1), prescription(2)] });
        const print = vi.spyOn(window, 'print').mockImplementation(() => {
            expect(document.body).toHaveClass('cc-prescription-printing');
            expect(document.querySelector('[data-prescription-id="1"]')).not.toHaveClass(printStyles.printingPrescription);
            expect(document.querySelector('[data-prescription-id="2"]')).toHaveClass(printStyles.printingPrescription);
        });
        mount(<PatientPrescriptions />);
        fireEvent.click((await screen.findAllByRole('button', { name: 'In đơn thuốc' }))[1]);
        expect(print).toHaveBeenCalledTimes(1);
        expect(document.body).not.toHaveClass('cc-prescription-printing');
        expect(document.querySelectorAll(`.${printStyles.printingPrescription}`)).toHaveLength(0);
    });
    it('formats the prescription calendar date', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [prescription(1)] }); mount(<PatientPrescriptions />);
        expect(await screen.findByText('Ngày kê: 11/10/2026')).toBeInTheDocument();
    });
    it('shows a retryable load error without the empty state', async () => {
        mocks.get.mockRejectedValue({ message: 'Lỗi đơn thuốc' }); mount(<PatientPrescriptions />);
        expect(await screen.findByText('Lỗi đơn thuốc')).toBeInTheDocument();
        expect(screen.queryByText('Chưa có đơn thuốc nào')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
    });
});
describe('Patient revisit invitations', () => {
    it('labels rejected and converted history without empty notes or raw status strings', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [{ ...revisit, status: 'Rejected' }, { ...revisit, id: 2, status: 'ConvertedToAppointment' }] });
        mount(<PatientRevisit />);
        expect(await screen.findByText('Đã từ chối')).toBeInTheDocument(); expect(screen.getByText('Đã đặt lịch')).toBeInTheDocument();
        expect(screen.queryByText('Rejected')).not.toBeInTheDocument(); expect(screen.queryByText(/^Lý do:/)).not.toBeInTheDocument();
    });
    it('does not mistake a load failure for having no invitations', async () => {
        mocks.get.mockRejectedValue({ message: 'Lỗi lời mời' }); mount(<PatientRevisit />);
        expect(await screen.findByText('Lỗi lời mời')).toBeInTheDocument();
        expect(screen.queryByText('Không có lời mời mới')).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
    });
    it('rejects reasons shorter than five characters without calling the API', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [revisit] }); mount(<PatientRevisit />);
        fireEvent.click(await screen.findByRole('button', { name: 'Từ chối' }));
        fireEvent.change(screen.getByLabelText('Lý do từ chối (*)'), { target: { value: 'abc' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận từ chối' }));
        expect(await screen.findByText('Vui lòng nhập lý do (ít nhất 5 ký tự).')).toBeInTheDocument(); expect(mocks.post).not.toHaveBeenCalled();
    });
    it('keeps the rejection modal open on payload errors and closes it after success', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [revisit] }); mocks.post.mockRejectedValueOnce({ message: 'Không thể từ chối X' }).mockResolvedValueOnce({ success: true });
        mount(<PatientRevisit />); fireEvent.click(await screen.findByRole('button', { name: 'Từ chối' }));
        fireEvent.change(screen.getByLabelText('Lý do từ chối (*)'), { target: { value: 'Đã khỏe lại' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận từ chối' }));
        expect(await screen.findByText('Không thể từ chối X')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận từ chối' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(mocks.post).toHaveBeenLastCalledWith('/revisit-requests/1/reject', { reason: 'Đã khỏe lại' });
    });
});
describe('Patient diagnostic results', () => {
    it('keeps each reviewed result label and value in one element for exact E2E text matching', async () => {
        mocks.orders.mockResolvedValue({ success: true, data: { items: [{ ...order, note: 'Ghi chú phiếu', reviewedAtUtc: '2026-10-11T08:00:00Z', reviewedByDoctorName: 'Lan', items: [{ ...order.items[0], result: { resultText: 'Kết quả kiểm thử', conclusion: 'Kết luận kiểm thử', referenceRange: '4-10', unit: 'mmol/L', resultedByUserName: 'Kỹ thuật viên', resultedAtUtc: '2026-10-11T07:00:00Z' } }] }] } });
        mount(<PatientDiagnosticResults />);
        for (const [value, fullText] of [
            ['Kiểm tra', 'Chỉ định: Kiểm tra'], ['Ghi chú phiếu', 'Ghi chú: Ghi chú phiếu'],
            ['Kết quả kiểm thử', 'Kết quả: Kết quả kiểm thử'], ['Kết luận kiểm thử', 'Kết luận: Kết luận kiểm thử'],
            ['4-10', 'Chỉ số bình thường: 4-10'], ['mmol/L', 'Đơn vị: mmol/L']
        ]) {
            expect((await screen.findByText(value)).textContent).toBe(fullText);
        }
        expect(screen.getByText(/Bác sĩ Lan đã xác nhận xem kết quả lúc/)).toBeInTheDocument();
    });
    it('keeps orders available when vitals fail and retries only vitals', async () => {
        mocks.vitals.mockRejectedValueOnce({ message: 'Lỗi sinh hiệu' }).mockResolvedValueOnce({ success: true, data: [] });
        mount(<PatientDiagnosticResults />);
        expect(await screen.findByText('DX-001')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('tab', { name: 'Chỉ số sinh hiệu (0)' }));
        expect(await screen.findByText('Lỗi sinh hiệu')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('Chưa có lịch sử sinh hiệu nào')).toBeInTheDocument(); expect(mocks.orders).toHaveBeenCalledTimes(1);
    });
    it('labels an unreviewed incomplete order as waiting for results and opens its first panel', async () => {
        mount(<PatientDiagnosticResults />);
        expect(await screen.findByText('Đang chờ kết quả')).toBeInTheDocument();
        expect(screen.getByText('Đang chờ kỹ thuật viên trả kết quả...')).toBeInTheDocument(); expect(screen.queryByText('Đã có kết quả')).not.toBeInTheDocument();
    });
    it('keeps vitals available when orders fail', async () => {
        mocks.orders.mockRejectedValue({ message: 'Lỗi phiếu' }); mocks.vitals.mockResolvedValue({ success: true, data: [{ recordedAtUtc: '2026-10-11', appointmentCode: 'APT-001', weight: 65 }] });
        mount(<PatientDiagnosticResults />); expect(await screen.findByText('Lỗi phiếu')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('tab', { name: 'Chỉ số sinh hiệu (1)' }));
        expect(await screen.findByText('65 kg')).toBeInTheDocument();
    });
});
describe('Patient package registrations', () => {
    it('shows healthPackageName in the cancel dialog and formats the preferred date', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [registration] }); mount(<PatientPackageRegistrations />);
        fireEvent.click(await screen.findByRole('button', { name: 'Hủy đăng ký' }));
        expect(within(screen.getByRole('dialog')).getByText('Gói khám tổng quát')).toBeInTheDocument();
        expect(screen.getByText('11/10/2026')).toBeInTheDocument();
    });
    it('shows InlineError without an empty registration message', async () => {
        mocks.get.mockRejectedValue({ message: 'Lỗi đăng ký' }); mount(<PatientPackageRegistrations />);
        expect(await screen.findByText('Lỗi đăng ký')).toBeInTheDocument();
        expect(screen.queryByText('Bạn chưa đăng ký gói khám sức khỏe nào')).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
    });
    it('blocks closing while cancelling and displays backend payload errors', async () => {
        let reject!: (reason: unknown) => void;
        mocks.post.mockReturnValue(new Promise((_, rejectPromise) => { reject = rejectPromise; }));
        mocks.get.mockResolvedValue({ success: true, data: [registration] }); mount(<PatientPackageRegistrations />);
        fireEvent.click(await screen.findByRole('button', { name: 'Hủy đăng ký' })); fireEvent.click(screen.getByRole('button', { name: 'Xác nhận hủy' }));
        expect(screen.getByRole('button', { name: 'Đóng' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Đang xử lý...' })).toBeDisabled();
        expect(within(screen.getByRole('dialog')).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
        await act(async () => reject({ message: 'Hủy thất bại X' }));
        expect(await screen.findByText('Hủy thất bại X')).toBeInTheDocument();
        expect(mocks.post).toHaveBeenCalledWith('/patient/health-package-registrations/1/cancel', { cancellationReason: 'Người bệnh chủ động hủy qua trang web' });
    });
});
