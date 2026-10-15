import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { Receipt, CreditCard, FlaskConical } from 'lucide-react';
import { NotificationsPage } from '../pages/common/NotificationsPage';
import { NotificationBell } from '../components/common/NotificationBell';
import { notificationApi } from '../api/notificationApi';
import { NotificationType, type NotificationDto } from '../types/notification';
import { notificationTypeIcon, notificationTypeLabel, notificationTypeTone } from '../utils/notificationLabels';

vi.mock('../api/notificationApi', () => ({ notificationApi: {
    getMyNotifications: vi.fn(), getUnreadCount: vi.fn(), markAsRead: vi.fn(), markAllAsRead: vi.fn(),
} }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true, user: { role: 'Patient' } }) }));

const appointment: NotificationDto = { id: 11, type: 1, typeName: 'Appointment', title: 'Cuộc hẹn mới', message: 'Lịch khám đã đặt', route: '/target', isRead: false, createdAtUtc: '2026-10-15T08:00:00Z' };
const diagnostic: NotificationDto = { ...appointment, id: 19, type: 9, typeName: 'Diagnostic', title: 'Phiếu xét nghiệm', isRead: true };
const result = (items: NotificationDto[] = [appointment, diagnostic], totalPages = 1) => ({ success: true, message: '', data: { items, page: 1, pageSize: 15, totalItems: items.length, totalPages } });
type ListResponse = Awaited<ReturnType<typeof notificationApi.getMyNotifications>>;
const deferred = <T,>() => {
    let resolve!: (value: T) => void;
    let reject!: (reason: unknown) => void;
    const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
};
const mountPage = () => render(<MemoryRouter><Routes>
    <Route path="/" element={<NotificationsPage />} />
    <Route path="/target" element={<p>Đích thông báo</p>} />
</Routes></MemoryRouter>);
const chooseType = async (label: string) => {
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Loại thông báo' }));
    fireEvent.click(await screen.findByRole('option', { name: label }));
};

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(notificationApi.getMyNotifications).mockResolvedValue(result());
    vi.mocked(notificationApi.getUnreadCount).mockResolvedValue({ success: true, message: '', data: { unreadCount: 1 } });
    vi.mocked(notificationApi.markAsRead).mockResolvedValue({ success: true, message: '' });
    vi.mocked(notificationApi.markAllAsRead).mockResolvedValue({ success: true, message: '', data: 1 });
});
afterEach(cleanup);

describe('shared notification type contract', () => {
    it.each([
        ['System', 0, 'Hệ thống'], ['Appointment', 1, 'Lịch khám'],
        ['AppointmentChangeRequest', 2, 'Thay đổi lịch'], ['Revisit', 3, 'Tái khám'],
        ['LeaveRequest', 4, 'Nghỉ phép'], ['Prescription', 5, 'Đơn thuốc / Khám'],
        ['HealthPackage', 6, 'Gói khám'], ['Invoice', 7, 'Hóa đơn'],
        ['Payment', 8, 'Thanh toán'], ['Diagnostic', 9, 'Cận lâm sàng'],
    ] as const)('matches backend value and label for %s', (name, value, label) => {
        expect(NotificationType[name]).toBe(value);
        expect(notificationTypeLabel(value)).toBe(label);
        expect(notificationTypeTone(value)).toBe(`tone${name}`);
    });
    it('uses the requested icons for invoice, payment and diagnostics', () => {
        expect(notificationTypeIcon(7)).toBe(Receipt);
        expect(notificationTypeIcon(8)).toBe(CreditCard);
        expect(notificationTypeIcon(9)).toBe(FlaskConical);
    });
    it('renders numeric types 1 and 9 consistently on the page', async () => {
        mountPage();
        expect(within(await screen.findByRole('button', { name: appointment.title })).getByText('Lịch khám')).toBeInTheDocument();
        expect(within(screen.getByRole('button', { name: diagnostic.title })).getByText('Cận lâm sàng')).toBeInTheDocument();
        expect(screen.getByText('1')).toBeInTheDocument();
    });
    it('renders numeric types 1 and 9 consistently in the bell', async () => {
        render(<MemoryRouter><NotificationBell /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Thông báo/ }));
        expect(await screen.findByText('Lịch khám')).toBeInTheDocument();
        expect(screen.getByText('Cận lâm sàng')).toBeInTheDocument();
        expect(notificationApi.getMyNotifications).toHaveBeenCalledWith(1, 5);
    });
});

describe('notification filters and request states', () => {
    it('resets pagination when filtering and clears only the type filter', async () => {
        vi.mocked(notificationApi.getMyNotifications).mockResolvedValue(result([appointment], 3));
        mountPage();
        await screen.findByRole('button', { name: appointment.title });
        fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
        await waitFor(() => expect(notificationApi.getMyNotifications).toHaveBeenLastCalledWith(2, 15, undefined, undefined));
        await screen.findByRole('button', { name: appointment.title });
        await chooseType('Cận lâm sàng');
        await waitFor(() => expect(notificationApi.getMyNotifications).toHaveBeenLastCalledWith(1, 15, undefined, 9));
        fireEvent.click(screen.getByRole('button', { name: 'Xóa lọc' }));
        await waitFor(() => expect(notificationApi.getMyNotifications).toHaveBeenLastCalledWith(1, 15, undefined, undefined));
        expect(screen.queryByRole('button', { name: 'Xóa lọc' })).not.toBeInTheDocument();
    });
    it('filters System=0 and gives the type-specific empty message', async () => {
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        vi.mocked(notificationApi.getMyNotifications).mockResolvedValue(result([]));
        await chooseType('Hệ thống');
        expect(await screen.findByText('Không có thông báo thuộc loại Hệ thống trong mục này.')).toBeInTheDocument();
        expect(notificationApi.getMyNotifications).toHaveBeenLastCalledWith(1, 15, undefined, 0);
    });
    it.each(['reject', 'unsuccessful'] as const)('shows an error and retry instead of empty on list %s', async mode => {
        if (mode === 'reject') vi.mocked(notificationApi.getMyNotifications).mockRejectedValueOnce(new Error('Lỗi tải danh sách'));
        else vi.mocked(notificationApi.getMyNotifications).mockResolvedValueOnce({ success: false, message: 'Lỗi tải danh sách' });
        mountPage();
        expect(await screen.findByRole('alert')).toHaveTextContent('Lỗi tải danh sách');
        expect(screen.queryByText('Không có thông báo nào')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Thử lại/ }));
        expect(await screen.findByRole('button', { name: appointment.title })).toBeInTheDocument();
    });
    it('does not block the list when unread-count fails', async () => {
        vi.mocked(notificationApi.getUnreadCount).mockRejectedValueOnce(new Error('Count unavailable'));
        mountPage();
        expect(await screen.findByRole('button', { name: appointment.title })).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
    it('hides the previous tab list for the entire loading period', async () => {
        const pending = deferred<ListResponse>();
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        vi.mocked(notificationApi.getMyNotifications).mockReturnValueOnce(pending.promise);
        fireEvent.click(screen.getByRole('button', { name: /^Chưa đọc/ }));
        expect(screen.queryByRole('button', { name: appointment.title })).not.toBeInTheDocument();
        expect(screen.getByText('Đang tải thông báo...')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Đang tải...' })).toBeDisabled();
        await act(async () => pending.resolve(result([appointment])));
        expect(await screen.findByRole('button', { name: appointment.title })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^Chưa đọc/ })).toHaveAttribute('aria-pressed', 'true');
    });
    it.each(['success', 'error'] as const)('ignores a stale tab %s after the newer tab has loaded', async outcome => {
        const old = deferred<ListResponse>(), current = deferred<ListResponse>();
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        vi.mocked(notificationApi.getMyNotifications).mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise);
        fireEvent.click(screen.getByRole('button', { name: /^Chưa đọc/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Đã đọc' }));
        await act(async () => current.resolve(result([diagnostic])));
        await screen.findByRole('button', { name: diagnostic.title });
        await act(async () => { if (outcome === 'success') old.resolve(result([appointment])); else old.reject(new Error('Stale error')); });
        expect(screen.queryByRole('button', { name: appointment.title })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: diagnostic.title })).toBeInTheDocument();
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
});

describe('notification keyboard and read actions', () => {
    it.each(['Enter', ' '])('opens the route with the %j key', async key => {
        mountPage();
        const card = await screen.findByRole('button', { name: appointment.title });
        expect(card).toHaveAttribute('tabindex', '0');
        fireEvent.keyDown(card, { key });
        expect(await screen.findByText('Đích thông báo')).toBeInTheDocument();
        expect(notificationApi.markAsRead).toHaveBeenCalledWith(11);
    });
    it('does not activate the card when its nested mark button is used with Enter', async () => {
        const user = userEvent.setup();
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        screen.getByTitle('Đánh dấu đã đọc thông báo này').focus();
        await user.keyboard('{Enter}');
        await waitFor(() => expect(notificationApi.markAsRead).toHaveBeenCalledTimes(1));
        expect(screen.queryByText('Đích thông báo')).not.toBeInTheDocument();
    });
    it.each(['reject', 'unsuccessful'] as const)('keeps the card unread and shows a toast on markAsRead %s', async mode => {
        if (mode === 'reject') vi.mocked(notificationApi.markAsRead).mockRejectedValueOnce(new Error('Không thể cập nhật đã đọc'));
        else vi.mocked(notificationApi.markAsRead).mockResolvedValueOnce({ success: false, message: 'Không thể cập nhật đã đọc' });
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        fireEvent.click(screen.getByTitle('Đánh dấu đã đọc thông báo này'));
        expect(await screen.findByText('Không thể cập nhật đã đọc')).toBeInTheDocument();
        expect(screen.getByTitle('Đánh dấu đã đọc thông báo này')).toBeEnabled();
        expect(screen.getByText('1')).toBeInTheDocument();
    });
    it('keeps unread state and shows a toast when mark-all fails', async () => {
        vi.mocked(notificationApi.markAllAsRead).mockRejectedValueOnce(new Error('Không thể cập nhật tất cả'));
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        fireEvent.click(screen.getByRole('button', { name: 'Đánh dấu tất cả đã đọc' }));
        expect(await screen.findByText('Không thể cập nhật tất cả')).toBeInTheDocument();
        expect(screen.getByTitle('Đánh dấu đã đọc thông báo này')).toBeInTheDocument();
        expect(screen.getByText('1')).toBeInTheDocument();
    });
    it('disables mark-all and changes its label until the request completes', async () => {
        const pending = deferred<Awaited<ReturnType<typeof notificationApi.markAllAsRead>>>();
        vi.mocked(notificationApi.markAllAsRead).mockReturnValueOnce(pending.promise);
        mountPage(); await screen.findByRole('button', { name: appointment.title });
        fireEvent.click(screen.getByRole('button', { name: 'Đánh dấu tất cả đã đọc' }));
        expect(screen.getByRole('button', { name: 'Đang xử lý...' })).toBeDisabled();
        await act(async () => pending.resolve({ success: true, message: '', data: 1 }));
        expect(screen.queryByTitle('Đánh dấu đã đọc thông báo này')).not.toBeInTheDocument();
    });
});
