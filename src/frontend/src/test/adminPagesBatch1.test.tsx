import { cleanup, fireEvent, render as renderPage, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import type { ReactNode } from 'react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AdminUsers } from '../pages/admin/AdminUsers';
import { AdminSpecialties } from '../pages/admin/AdminSpecialties';
import { AdminLeaves } from '../pages/admin/AdminLeaves';
import { AdminAuditLogs } from '../pages/admin/AdminAuditLogs';
import { AdminWorkSchedules } from '../pages/admin/AdminWorkSchedules';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => dialog }));

const account = { id: 'staff-1', fullName: 'Nhân sự mẫu', email: 'staff@example.com', phoneNumber: '0901234567', roles: ['Pharmacist'], isActive: true, createdAt: '2026-10-10T08:00:00' };
const specialty = { id: 1, specialtyCode: 'CARDIO', name: 'Tim mạch', description: 'Chuyên khoa tim mạch', isActive: true, aiEnabled: true };
const leave = { id: 1, doctorId: 1, doctorName: 'Bác sĩ Minh', startDateTime: '2026-10-11T08:00:00', endDateTime: '2026-10-11T17:00:00', reason: 'Nghỉ phép', status: 'Pending', adminNote: null };
const schedules = [3, 1, 2].map(id => ({ id, doctorId: 1, workDate: `2026-10-${10 + id}`, startTime: '08:00:00', endTime: '12:00:00', isActive: true }));
const get = vi.mocked(axiosClient.get);
const pageResponse = (items: unknown[], totalItems: number) => ({ success: true, data: { items, totalItems } });
const render = (page: ReactNode) => renderPage(<ConfigProvider theme={{ token: { motion: false } }}>{page}</ConfigProvider>);

beforeEach(() => {
    vi.resetAllMocks();
    dialog.showConfirm.mockImplementation((_message: string, callback: () => void) => callback());
    get.mockImplementation(async (url: string) => {
        if (url.startsWith('/admin/doctors?')) return pageResponse([{ id: 1, fullName: 'Bác sĩ Minh' }], 1);
        if (url.includes('/work-schedules')) return { success: true, data: schedules };
        if (url.startsWith('/admin/users?')) return pageResponse([account], 25);
        if (url.startsWith('/admin/specialties?')) return pageResponse([specialty], 25);
        if (url.startsWith('/admin/leave-requests?')) return pageResponse([leave], 15);
        return pageResponse([{ id: 1, userFullName: 'Quản trị viên', action: 'CreateUser', entityName: 'User', entityId: 'staff-1', description: 'Tạo nhân sự', createdAt: '2026-10-10T08:00:00' }], 20);
    });
});
afterEach(() => cleanup());

async function chooseDoctor() {
    await waitFor(() => expect(screen.queryByText('Đang tải dữ liệu...')).not.toBeInTheDocument());
    await userEvent.click(screen.getByRole('combobox', { name: 'Chọn Bác sĩ:' }));
    await userEvent.click(await screen.findByText('Bác sĩ Minh', { exact: true }));
    await screen.findByRole('table');
}

describe('admin batch 1 regression', () => {
    it('Users paginates 25 accounts and requests page 2 on Sau', async () => {
        render(<AdminUsers />);
        await screen.findByRole('table');
        expect(screen.getByText(/Tổng cộng: 25 tài khoản/)).toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(get).toHaveBeenLastCalledWith('/admin/users?page=2&pageSize=10'));
    });

    it('Users exposes both new roles in the filter and creation form and labels Pharmacist', async () => {
        render(<AdminUsers />);
        await screen.findByRole('table');
        expect(within(screen.getByRole('table')).getByText('Dược sĩ')).toBeInTheDocument();
        await userEvent.click(screen.getByRole('combobox', { name: 'Vai trò' }));
        expect((await screen.findAllByText('Dược sĩ', { exact: true })).length).toBeGreaterThan(1);
        expect(screen.getByText('Kỹ thuật viên', { exact: true })).toBeInTheDocument();
        await userEvent.click(screen.getAllByText('Kỹ thuật viên', { exact: true }).at(-1)!);
        await waitFor(() => expect(get).toHaveBeenLastCalledWith('/admin/users?page=1&pageSize=10&role=DiagnosticTechnician'));
        await userEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản nhân sự' }));
        const modal = await screen.findByRole('dialog');
        await userEvent.click(within(modal).getByRole('combobox', { name: 'Vai trò (*)' }));
        expect(screen.getAllByText('Dược sĩ', { exact: true }).length).toBeGreaterThan(1);
        expect(screen.getAllByText('Kỹ thuật viên', { exact: true }).length).toBeGreaterThan(1);
        await userEvent.click(screen.getAllByText('Dược sĩ', { exact: true }).at(-1)!);
        expect(within(modal).getAllByText('Dược sĩ').length).toBeGreaterThan(0);
    });

    it('Users replaces the table with InlineError and retries a failed request', async () => {
        get.mockRejectedValueOnce(new Error('Không thể kết nối máy chủ.'));
        render(<AdminUsers />);
        expect(await screen.findByText('Không thể kết nối máy chủ.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
        expect(screen.queryByText('Không tìm thấy tài khoản nào phù hợp.')).not.toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByRole('table');
        expect(get).toHaveBeenCalledTimes(2);
    });

    it.each([
        { name: 'Users', Component: AdminUsers, placeholder: 'Tìm theo tên, email, sđt...', endpoint: '/admin/users' },
        { name: 'Specialties', Component: AdminSpecialties, placeholder: 'Tìm theo mã, tên...', endpoint: '/admin/specialties' },
    ])('$name searches exactly once from page 2 and once from page 1, never per keystroke', async ({ Component, placeholder, endpoint }) => {
        render(<Component />);
        await screen.findByRole('table');
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(get).toHaveBeenLastCalledWith(`${endpoint}?page=2&pageSize=10`));
        await screen.findByRole('table');
        get.mockClear();
        await userEvent.type(screen.getByPlaceholderText(placeholder), 'Minh');
        expect(get).not.toHaveBeenCalled();
        await userEvent.click(screen.getByRole('button', { name: 'Lọc' }));
        await screen.findByRole('table');
        expect(get).toHaveBeenCalledTimes(1);
        expect(get).toHaveBeenLastCalledWith(`${endpoint}?page=1&pageSize=10&search=Minh`);
        get.mockClear();
        await userEvent.type(screen.getByPlaceholderText(placeholder), '{Enter}');
        await screen.findByRole('table');
        expect(get).toHaveBeenCalledTimes(1);
    });

    it('Users keeps Admin locking disabled', async () => {
        get.mockResolvedValue(pageResponse([{ ...account, roles: ['Admin'] }], 1));
        render(<AdminUsers />);
        await screen.findByRole('table');
        expect(screen.getByRole('button', { name: 'Khóa' })).toBeDisabled();
    });

    it('Leaves paginates 15 requests', async () => {
        render(<AdminLeaves />);
        await screen.findByRole('table');
        expect(screen.getByText(/Tổng cộng: 15 yêu cầu/)).toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(get).toHaveBeenLastCalledWith('/admin/leave-requests?page=2&pageSize=10'));
    });

    it('Leaves cannot close a detail modal while approving', async () => {
        let finish: (value: unknown) => void = () => {};
        vi.mocked(axiosClient.post).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
        render(<AdminLeaves />);
        await screen.findByRole('table');
        await userEvent.click(screen.getByRole('button', { name: 'Chi tiết' }));
        await userEvent.click(screen.getByRole('button', { name: 'Duyệt yêu cầu' }));
        const modal = screen.getByRole('dialog');
        expect(within(modal).getByRole('button', { name: /Duyệt yêu cầu/ })).toBeDisabled();
        expect(within(modal).getByRole('button', { name: /Từ chối/ })).toBeDisabled();
        expect(within(modal).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
        fireEvent.keyDown(modal, { key: 'Escape', code: 'Escape', keyCode: 27 });
        expect(modal).toBeInTheDocument();
        finish({ success: true });
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(axiosClient.post).toHaveBeenCalledWith('/admin/leave-requests/1/approve', { adminNote: '' });
    });

    it.each([
        { name: 'Specialties', Component: AdminSpecialties },
        { name: 'Leaves', Component: AdminLeaves },
        { name: 'AuditLogs', Component: AdminAuditLogs },
        { name: 'WorkSchedules', Component: AdminWorkSchedules },
    ])('$name displays a retry error instead of a table on load failure', async ({ Component }) => {
        get.mockRejectedValue(new Error('Tải dữ liệu thất bại.'));
        render(<Component />);
        expect(await screen.findByText('Tải dữ liệu thất bại.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
    });

    it('WorkSchedules displays the local calendar date and Sunday, sorted, with only one slot hint', async () => {
        render(<AdminWorkSchedules />);
        await chooseDoctor();
        const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1);
        expect(within(rows[0]).getByText('11/10/2026')).toBeInTheDocument();
        expect(within(rows[0]).getByText('Chủ nhật')).toBeInTheDocument();
        expect(screen.getAllByText(/cần sinh slot/)).toHaveLength(1);
        expect(rows).toHaveLength(3);
    });

    it('WorkSchedules validates time ordering and sends the original edit payload', async () => {
        vi.mocked(axiosClient.put).mockResolvedValue({ success: true });
        render(<AdminWorkSchedules />);
        await chooseDoctor();
        await userEvent.click(screen.getAllByRole('button', { name: 'Sửa' })[0]);
        fireEvent.change(screen.getByLabelText('Từ giờ (*)'), { target: { value: '12:00' } });
        await userEvent.click(screen.getByRole('button', { name: 'Xác nhận lưu' }));
        expect(await screen.findByText('Giờ bắt đầu phải trước giờ kết thúc.')).toBeInTheDocument();
        expect(axiosClient.put).not.toHaveBeenCalled();
        fireEvent.change(screen.getByLabelText('Từ giờ (*)'), { target: { value: '09:00' } });
        await userEvent.click(screen.getByRole('button', { name: 'Xác nhận lưu' }));
        await waitFor(() => expect(axiosClient.put).toHaveBeenCalledWith('/admin/work-schedules/1', { workDate: '2026-10-11', startTime: '09:00', endTime: '12:00:00' }));
    });

    it('WorkSchedules replaces schedules with a retry error when schedule API fails', async () => {
        get.mockImplementation(async (url: string) => {
            if (url.includes('/work-schedules')) throw new Error('Không thể tải lịch.');
            return pageResponse([{ id: 1, fullName: 'Bác sĩ Minh' }], 1);
        });
        render(<AdminWorkSchedules />);
        await waitFor(() => expect(screen.queryByText('Đang tải dữ liệu...')).not.toBeInTheDocument());
        await userEvent.click(screen.getByRole('combobox', { name: 'Chọn Bác sĩ:' }));
        await userEvent.click(await screen.findByText('Bác sĩ Minh', { exact: true }));
        expect(await screen.findByText('Không thể tải lịch.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
        expect(screen.queryByText(/cần sinh slot/)).not.toBeInTheDocument();
    });
});
