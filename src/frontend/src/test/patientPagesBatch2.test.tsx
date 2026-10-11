import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { PackageRegistration } from '../pages/patient/PackageRegistration';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), showAlert: vi.fn(), showConfirm: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.showAlert, showConfirm: mocks.showConfirm }) }));
const appointment = { id: 1, appointmentCode: 'APT-001', doctorId: 2, doctorName: 'Nguyễn Minh', specialtyId: 3, specialtyName: 'Nội khoa', appointmentSlotId: 10, appointmentDate: '2026-10-11', startTime: '08:00', endTime: '08:30', reason: 'Đau đầu kéo dài', status: 'Confirmed' };
const pending = { ...appointment, id: 2, appointmentCode: 'APT-002', status: 'PendingReschedule' };
const pkg = { id: 1, code: 'PKG01', name: 'Gói khám tổng quát', price: 1500000, targetAudience: 'Người trưởng thành', description: 'Kiểm tra sức khỏe', includedServices: ['Khám nội tổng quát'], isActive: true };
const registration = { registrationCode: 'REG-001', healthPackageName: 'Gói khám từ backend', healthPackagePrice: 2300000, preferredDate: '2026-10-13', contactPhone: '0901234567' };
const ok = (data: unknown) => ({ success: true, data });
const mountAppointments = (route = '/patient/appointments') => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><PatientAppointments /></MemoryRouter></ConfigProvider>);
const mountPackage = () => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={['/patient/health-packages/1/register']}><Routes><Route path="/patient/health-packages/:id/register" element={<PackageRegistration />} /></Routes></MemoryRouter></ConfigProvider>);
const defaultGet = async (url: string) => {
    if (url.startsWith('/appointments/my')) return ok({ items: [appointment, pending] });
    if (url.startsWith('/appointment-change-requests')) return ok({ items: [{ id: 20, appointmentId: 2, reason: 'Bận việc gia đình' }] });
    if (url.includes('/history')) return ok([]);
    if (url.includes('/available-slots')) return ok([]);
    if (url.startsWith('/health-packages/')) return ok(pkg);
    throw new Error(`Unexpected GET ${url}`);
};
const reviewPackage = async (phone = '0901 234 567') => {
    fireEvent.click(await screen.findByRole('button', { name: 'Tiếp tục bước 2' }));
    fireEvent.change(screen.getByLabelText('Ngày khám mong muốn'), { target: { value: '2026-10-13' } });
    fireEvent.change(screen.getByLabelText('Số điện thoại liên hệ'), { target: { value: phone } });
    fireEvent.click(screen.getByRole('button', { name: 'Xem lại thông tin' }));
    await screen.findByRole('heading', { name: 'Xác nhận thông tin đăng ký' });
};
const submitPackage = () => {
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận đăng ký gói khám' }));
};
beforeEach(() => {
    vi.resetAllMocks();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-11T03:00:00Z'));
    mocks.get.mockImplementation(defaultGet);
    mocks.post.mockResolvedValue(ok(registration));
    mocks.showConfirm.mockImplementation((_message, callback) => callback());
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Patient appointments batch 2', () => {
    it('uses the appointment DTO calendar date, reason and specialty without a specialties request', async () => {
        mountAppointments();
        await screen.findByText('#APT-001');
        const card = document.getElementById('appointment-card-1')!;
        expect(within(card).getByText('11/10/2026')).toBeInTheDocument();
        expect(within(card).getByText('Đau đầu kéo dài')).toBeInTheDocument();
        expect(within(card).getByText('Nội khoa')).toBeInTheDocument();
        expect(card).toHaveTextContent('Ngày: 11/10/2026');
        expect(card).toHaveTextContent('Chuyên khoa: Nội khoa');
        expect(mocks.get).not.toHaveBeenCalledWith('/specialties');
    });
    it('keeps appointments visible when change requests fail and alerts on unavailable withdrawal', async () => {
        mocks.get.mockImplementation((url: string) => url.startsWith('/appointment-change-requests') ? Promise.reject({ message: 'Lỗi API phụ' }) : defaultGet(url));
        mountAppointments();
        await screen.findByText('#APT-001');
        expect(screen.queryByText('Lỗi API phụ')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thử lại' })).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Rút yêu cầu' }));
        expect(mocks.showAlert).toHaveBeenCalledWith('Không tìm thấy yêu cầu chờ xử lý của lịch hẹn này.', 'Thông báo', 'warning');
    });
    it('replaces the list with a retryable appointments error', async () => {
        mocks.get.mockImplementation((url: string) => url.startsWith('/appointments/my') ? Promise.reject({ message: 'Không tải được lịch hẹn' }) : defaultGet(url));
        mountAppointments();
        await screen.findByText('Không tải được lịch hẹn');
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(screen.queryByText('Bạn chưa có lịch hẹn nào')).not.toBeInTheDocument();
        mocks.get.mockImplementation(defaultGet);
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('#APT-001');
    });
    it('does not reopen a handled cancel deep link after withdrawal refreshes the list', async () => {
        mountAppointments('/patient/appointments?appointmentId=1&action=cancel');
        const dialog = await screen.findByRole('dialog', { name: 'Hủy lịch hẹn #APT-001' });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Đóng' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Rút yêu cầu' }));
        await waitFor(() => expect(mocks.post).toHaveBeenCalledWith('/appointment-change-requests/20/withdraw'));
        await waitFor(() => expect(mocks.get.mock.calls.filter(([url]) => url.startsWith('/appointments/my'))).toHaveLength(2));
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    it('ignores a late slots response for the previously selected date', async () => {
        let resolveA!: (value: unknown) => void;
        let resolveB!: (value: unknown) => void;
        mocks.get.mockImplementation((url: string) => url.includes('fromDate=2026-10-13') ? new Promise(resolve => { resolveA = resolve; }) : url.includes('fromDate=2026-10-14') ? new Promise(resolve => { resolveB = resolve; }) : defaultGet(url));
        mountAppointments();
        fireEvent.click(await screen.findByRole('button', { name: 'Đổi lịch' }));
        const date = screen.getByLabelText('Chọn ngày khám mới (*)');
        expect(date).toHaveAttribute('type', 'date');
        expect(date).toHaveAttribute('min', '2026-10-12');
        fireEvent.change(date, { target: { value: '2026-10-13' } });
        fireEvent.change(date, { target: { value: '2026-10-14' } });
        await act(async () => resolveB(ok([{ slotId: 22, startTime: '15:00', endTime: '15:30' }])));
        await act(async () => resolveA(ok([{ slotId: 21, startTime: '14:00', endTime: '14:30' }])));
        expect(screen.getByRole('button', { name: '15:00 - 15:30' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: '14:00 - 14:30' })).not.toBeInTheDocument();
    });
    it('ignores a stale slots error and rejects a manually entered past day without requesting slots', async () => {
        let rejectA!: (reason: unknown) => void;
        mocks.get.mockImplementation((url: string) => url.includes('fromDate=2026-10-13') ? new Promise((_resolve, reject) => { rejectA = reject; }) : url.includes('fromDate=2026-10-14') ? Promise.resolve(ok([{ slotId: 22, startTime: '15:00', endTime: '15:30' }])) : defaultGet(url));
        mountAppointments();
        fireEvent.click(await screen.findByRole('button', { name: 'Đổi lịch' }));
        const date = screen.getByLabelText('Chọn ngày khám mới (*)');
        fireEvent.change(date, { target: { value: '2026-10-13' } });
        fireEvent.change(date, { target: { value: '2026-10-14' } });
        await screen.findByRole('button', { name: '15:00 - 15:30' });
        await act(async () => rejectA({ message: 'Lỗi ngày cũ' }));
        expect(screen.queryByText('Lỗi ngày cũ')).not.toBeInTheDocument();
        const calls = mocks.get.mock.calls.length;
        fireEvent.change(date, { target: { value: '2026-10-10' } });
        expect(screen.getByText('Vui lòng chọn ngày từ ngày mai trở đi.')).toBeInTheDocument();
        expect(mocks.get).toHaveBeenCalledTimes(calls);
    });
    it('shows a retryable history error instead of an empty history', async () => {
        mocks.get.mockImplementation((url: string) => url.endsWith('/history') ? Promise.reject({ message: 'Lỗi lịch sử' }) : defaultGet(url));
        mountAppointments();
        await screen.findByText('#APT-001');
        fireEvent.click(screen.getAllByRole('button', { name: 'Lịch sử' })[0]);
        const dialog = screen.getByRole('dialog', { name: 'Lịch sử thay đổi #APT-001' });
        await within(dialog).findByText('Lỗi lịch sử');
        expect(within(dialog).getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(within(dialog).queryByText('Không có lịch sử thay đổi.')).not.toBeInTheDocument();
    });
    it('shows a dash for a null history status', async () => {
        mocks.get.mockImplementation((url: string) => url.endsWith('/history') ? Promise.resolve(ok([{ action: 'Created', oldStatus: null, newStatus: 'Pending', createdAt: '2026-10-11T03:00:00Z' }])) : defaultGet(url));
        mountAppointments();
        await screen.findByText('#APT-001');
        fireEvent.click(screen.getAllByRole('button', { name: 'Lịch sử' })[0]);
        await screen.findByText('Tạo mới');
        expect(screen.getByText(/Từ/)).toHaveTextContent('Từ — thành Chờ xác nhận');
    });
    it('blocks cancellation reasons shorter than five characters', async () => {
        mountAppointments();
        fireEvent.click(await screen.findByRole('button', { name: 'Hủy lịch' }));
        fireEvent.change(screen.getByLabelText('Lý do hủy lịch (*)'), { target: { value: 'abc' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận gửi yêu cầu' }));
        expect(screen.getByText('Vui lòng nhập lý do hủy (ít nhất 5 ký tự).')).toBeInTheDocument();
        expect(mocks.post).not.toHaveBeenCalled();
    });
    it('disables withdrawal and guards repeated confirmation callbacks while the request is pending', async () => {
        let complete!: (value: unknown) => void;
        let confirm!: () => Promise<void>;
        mocks.showConfirm.mockImplementation((_message, callback) => { confirm = callback; });
        mocks.post.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
        mountAppointments();
        fireEvent.click(await screen.findByRole('button', { name: 'Rút yêu cầu' }));
        act(() => { void confirm(); void confirm(); });
        expect(mocks.post).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('button', { name: 'Đang xử lý...' })).toBeDisabled();
        await act(async () => complete(ok({})));
    });
});

describe('Package registration batch 2', () => {
    it('normalizes all phone whitespace and keeps ISO dates in the request payload', async () => {
        mountPackage(); await reviewPackage(); submitPackage();
        await waitFor(() => expect(mocks.post).toHaveBeenCalledWith('/patient/health-package-registrations', { healthPackageId: 1, preferredDate: '2026-10-13', contactPhone: '0901234567', note: undefined }));
    });
    it('displays healthPackageName and healthPackagePrice from the successful response', async () => {
        mountPackage(); await reviewPackage(); submitPackage();
        await screen.findByText('Đăng ký gói khám thành công!');
        expect(screen.getByText('Gói khám từ backend')).toBeInTheDocument();
        expect(screen.getByText(/2\.300\.000/)).toBeInTheDocument();
        expect(screen.getByText('13/10/2026')).toBeInTheDocument();
    });
    it('falls back to the loaded package name and price when the result omits them', async () => {
        mocks.post.mockResolvedValue(ok({ ...registration, healthPackageName: undefined, healthPackagePrice: undefined }));
        mountPackage(); await reviewPackage(); submitPackage();
        await screen.findByText('Đăng ký gói khám thành công!');
        expect(screen.getByText(pkg.name)).toBeInTheDocument();
        expect(screen.getByText(/1\.500\.000/)).toBeInTheDocument();
    });
    it('displays the review date as DD/MM/YYYY in a combined label/value element', async () => {
        mountPackage(); await reviewPackage();
        expect(screen.getByText('13/10/2026').parentElement).toHaveTextContent('Ngày khám mong muốn: 13/10/2026');
    });
    it('shows the payload message when submission fails', async () => {
        mocks.post.mockRejectedValue({ message: 'X' });
        mountPackage(); await reviewPackage(); submitPackage();
        expect(await screen.findByText('X')).toBeInTheDocument();
        expect(screen.getByRole('alert')).toHaveTextContent('X');
    });
    it('rejects manually entered past dates and preserves the native date minimum', async () => {
        mountPackage(); fireEvent.click(await screen.findByRole('button', { name: 'Tiếp tục bước 2' }));
        const date = screen.getByLabelText('Ngày khám mong muốn');
        expect(date).toHaveAttribute('type', 'date'); expect(date).toHaveAttribute('min', '2026-10-12');
        fireEvent.change(date, { target: { value: '2026-10-10' } });
        fireEvent.change(screen.getByLabelText('Số điện thoại liên hệ'), { target: { value: '0901234567' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem lại thông tin' }));
        expect(await screen.findByText('Ngày khám mong muốn phải từ ngày mai trở đi.')).toBeInTheDocument();
        expect(mocks.post).not.toHaveBeenCalled();
    });
    it('blocks repeated registration submits while pending', async () => {
        let complete!: (value: unknown) => void;
        mocks.post.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
        mountPackage(); await reviewPackage();
        expect(screen.getByRole('button', { name: 'Xác nhận đăng ký gói khám' })).toBeDisabled();
        submitPackage();
        fireEvent.click(screen.getByRole('button', { name: 'Đang gửi...' }));
        expect(mocks.post).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('button', { name: 'Đang gửi...' })).toBeDisabled();
        await act(async () => complete(ok(registration)));
    });
    it('shows a retryable load error with the catalog link', async () => {
        mocks.get.mockRejectedValue({ message: 'Lỗi tải gói' }); mountPackage();
        expect(await screen.findByText('Lỗi tải gói')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Xem danh sách gói khám' })).toHaveAttribute('href', '/health-packages');
    });
});
