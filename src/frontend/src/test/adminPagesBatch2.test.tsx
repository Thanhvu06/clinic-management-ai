import { act, cleanup, fireEvent, render as renderPage, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import type { ReactNode } from 'react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AdminDoctors } from '../pages/admin/AdminDoctors';
import { AdminHealthPackages } from '../pages/admin/AdminHealthPackages';
import { AdminMedicines } from '../pages/admin/AdminMedicines';
import { AdminLeaves } from '../pages/admin/AdminLeaves';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => dialog }));

const doctor = { id: 1, userId: 'doctor-1', fullName: 'Nguyễn Minh', email: 'minh@example.com', experienceYears: 0, description: '', isActive: true, specialties: [
    { specialtyId: 1, specialtyName: 'Tim mạch', isPrimary: true },
    { specialtyId: 2, specialtyName: 'Nội tổng quát', isPrimary: false }
] };
const users = [{ id: 'doctor-1', fullName: 'Đã có hồ sơ', email: 'assigned@example.com' }, { id: 'doctor-2', fullName: 'Chưa có hồ sơ', email: 'free@example.com' }];
const healthPackage = { id: 3, code: 'PKG01', name: 'Gói khám mẫu', description: '', targetGroup: '', price: 1000000, includedServices: '', sortOrder: 1, isActive: true };
const medicine = { id: 7, code: 'MED01', name: 'Paracetamol', unit: 'Viên', stockQuantity: 40, reorderLevel: 40, isActive: true, isPrescriptionRequired: false, categoryId: 2, categoryName: 'Giảm đau' };
const leave = { id: 1, doctorId: 1, doctorName: 'Bác sĩ Minh', startDateTime: '2026-10-11T08:00:00', endDateTime: '2026-10-11T17:00:00', reason: 'Nghỉ phép', status: 'Pending', adminNote: null };
const get = vi.mocked(axiosClient.get);
const pageResponse = (items: unknown[], totalItems = 25) => ({ success: true, data: { items, totalItems } });
const defaultGet = async (url: string) => {
    if (url.startsWith('/admin/users?')) return pageResponse(users);
    if (url.startsWith('/admin/specialties?')) return pageResponse([{ id: 1, name: 'Tim mạch' }, { id: 2, name: 'Nội tổng quát' }]);
    if (url.startsWith('/admin/doctors?')) return pageResponse([doctor]);
    if (url.startsWith('/admin/health-packages?')) return pageResponse([healthPackage]);
    if (url.includes('medicine-categories')) return { success: true, data: [{ id: 2, name: 'Giảm đau', sortOrder: 0, isActive: true }] };
    if (url.startsWith('/admin/medicines?')) return pageResponse([medicine]);
    return pageResponse([leave]);
};
const render = (page: ReactNode) => renderPage(<ConfigProvider theme={{ token: { motion: false } }}>{page}</ConfigProvider>);

beforeEach(() => {
    vi.resetAllMocks();
    dialog.showConfirm.mockImplementation((_message: string, callback: () => void) => callback());
    get.mockImplementation(defaultGet);
    vi.mocked(axiosClient.put).mockResolvedValue({ success: true });
    vi.mocked(axiosClient.post).mockResolvedValue({ success: true, data: { id: 8 } });
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
});
afterEach(() => cleanup());

async function openDoctorEdit() {
    render(<AdminDoctors />);
    await userEvent.click(await screen.findByRole('button', { name: 'Sửa' }));
    return screen.findByRole('dialog');
}

describe('admin batch 2 regression', () => {
    it('Doctors paginates 25 profiles and requests page 2 on Sau', async () => {
        render(<AdminDoctors />);
        await screen.findByRole('table');
        expect(screen.getByText('Tổng cộng: 25 bác sĩ')).toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await waitFor(() => expect(get).toHaveBeenLastCalledWith('/admin/doctors?page=2&pageSize=10'));
    });

    it('Doctors creation lists only accounts without a profile, including inactive profiles in the exclusion query', async () => {
        render(<AdminDoctors />);
        await userEvent.click(screen.getByRole('button', { name: 'Thêm hồ sơ bác sĩ' }));
        const modal = await screen.findByRole('dialog');
        expect(get).toHaveBeenCalledWith('/admin/doctors?pageSize=100');
        await userEvent.click(within(modal).getByRole('combobox', { name: 'Chọn tài khoản User liên kết (*)' }));
        expect(await screen.findByText('Chưa có hồ sơ (free@example.com)', { exact: true })).toBeInTheDocument();
        expect(screen.queryByText('Đã có hồ sơ (assigned@example.com)', { exact: true })).not.toBeInTheDocument();
    });

    it('Doctors edits and saves zero years of experience', async () => {
        const modal = await openDoctorEdit();
        expect(within(modal).getByLabelText('Số năm K.Nghiệm')).toHaveValue('0');
        await userEvent.click(within(modal).getByRole('button', { name: 'Xác nhận lưu' }));
        await waitFor(() => expect(axiosClient.put).toHaveBeenCalledWith('/admin/doctors/1', expect.objectContaining({ experienceYears: 0 })));
    });

    it('Doctors preserves the original table primary specialty after removing it and cancelling edit', async () => {
        const modal = await openDoctorEdit();
        await userEvent.click(within(modal).getByRole('button', { name: 'Gỡ chuyên khoa Tim mạch' }));
        await userEvent.click(within(modal).getByRole('button', { name: 'Hủy' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        const table = screen.getByRole('table');
        expect(within(table).getByText('Tim mạch').closest('.ant-tag')).toHaveAttribute('title', 'Chuyên khoa chính');
        expect(within(table).getByText('Nội tổng quát').closest('.ant-tag')).not.toHaveAttribute('title');
        expect(doctor.specialties[1].isPrimary).toBe(false);
        expect(axiosClient.put).not.toHaveBeenCalled();
    });

    it('Doctors keeps the modal closed and reports failed dependencies', async () => {
        get.mockImplementation(async url => {
            if (url.startsWith('/admin/users?')) throw new Error('Không tải được tài khoản.');
            return defaultGet(url);
        });
        render(<AdminDoctors />);
        await screen.findByRole('table');
        await userEvent.click(screen.getByRole('button', { name: 'Thêm hồ sơ bác sĩ' }));
        await waitFor(() => expect(dialog.showAlert).toHaveBeenCalledWith('Không tải được tài khoản.', 'Lỗi', 'error'));
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thêm hồ sơ bác sĩ' })).not.toBeDisabled();
    });

    it('Doctors shows an info alert when every account already has a profile', async () => {
        get.mockImplementation(async url => url.startsWith('/admin/users?') ? pageResponse([users[0]]) : defaultGet(url));
        render(<AdminDoctors />);
        await userEvent.click(screen.getByRole('button', { name: 'Thêm hồ sơ bác sĩ' }));
        expect(await screen.findByText('Không còn tài khoản Bác sĩ nào chưa có hồ sơ.')).toBeInTheDocument();
    });

    it('HealthPackages exposes the delete action and confirms before deleting', async () => {
        vi.mocked(axiosClient.delete).mockResolvedValue({ success: true });
        render(<AdminHealthPackages />);
        await userEvent.click(await screen.findByRole('button', { name: 'Xóa gói khám' }));
        expect(dialog.showConfirm).toHaveBeenCalled();
        await waitFor(() => expect(axiosClient.delete).toHaveBeenCalledWith('/admin/health-packages/3'));
    });

    it('HealthPackages replaces a failed load with InlineError and retries', async () => {
        get.mockRejectedValueOnce(new Error('Không tải được gói khám.'));
        render(<AdminHealthPackages />);
        expect(await screen.findByText('Không tải được gói khám.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByRole('table');
    });

    it('Medicines makes only one toggle request while a double click is pending', async () => {
        let finish: (value: unknown) => void = () => {};
        vi.mocked(axiosClient.patch).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
        render(<AdminMedicines />);
        const toggle = await screen.findByRole('button', { name: 'Khóa' });
        act(() => { fireEvent.click(toggle); fireEvent.click(toggle); });
        expect(axiosClient.patch).toHaveBeenCalledTimes(1);
        expect(axiosClient.patch).toHaveBeenCalledWith('/admin/medicines/7/toggle-status');
        expect(toggle).toBeDisabled();
        await act(async () => { finish({ success: true }); });
        await waitFor(() => expect(screen.getByRole('button', { name: 'Khóa' })).not.toBeDisabled());
    });

    it('Medicines marks stock at the reorder threshold with a titled warning icon', async () => {
        render(<AdminMedicines />);
        await screen.findByRole('table', { name: 'Danh mục thuốc' });
        expect(screen.getByTitle('Tồn kho dưới ngưỡng cảnh báo!').closest('svg')).toHaveClass('lucide-triangle-alert');
    });

    it.each([
        { name: 'Doctors', Component: AdminDoctors, placeholder: 'Tìm theo tên...', button: 'Lọc', endpoint: '/admin/doctors' },
        { name: 'HealthPackages', Component: AdminHealthPackages, placeholder: 'Tìm theo tên hoặc mã gói...', button: 'Tìm kiếm', endpoint: '/admin/health-packages' },
        { name: 'Medicines', Component: AdminMedicines, placeholder: 'Tìm theo tên thuốc hoặc mã...', button: 'Tìm kiếm', endpoint: '/admin/medicines' }
    ])('$name searches once from page 2 and once from page 1', async ({ Component, placeholder, button, endpoint }) => {
        render(<Component />);
        await screen.findByRole('table');
        await userEvent.click(screen.getByRole('button', { name: 'Sau' }));
        await screen.findByRole('table'); get.mockClear();
        await userEvent.type(screen.getByPlaceholderText(placeholder), 'Minh');
        expect(get).not.toHaveBeenCalled();
        await userEvent.click(screen.getByRole('button', { name: button }));
        await screen.findByRole('table');
        expect(get).toHaveBeenCalledTimes(1);
        expect(get).toHaveBeenLastCalledWith(`${endpoint}?page=1&pageSize=10&search=Minh`);
        get.mockClear();
        await userEvent.type(screen.getByPlaceholderText(placeholder), '{Enter}');
        await screen.findByRole('table'); expect(get).toHaveBeenCalledTimes(1);
    });

    it('Leaves retains the request table when the doctor filter API fails and retries doctors separately', async () => {
        get.mockImplementation(async url => {
            if (url.startsWith('/admin/doctors?')) throw new Error('Không tải được bác sĩ.');
            return defaultGet(url);
        });
        render(<AdminLeaves />);
        expect(await screen.findByText('Danh sách bác sĩ: Không tải được bác sĩ.')).toBeInTheDocument();
        expect(screen.getByRole('table')).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Bác sĩ' })).toBeDisabled();
        get.mockImplementation(defaultGet); get.mockClear();
        await userEvent.click(screen.getByRole('button', { name: 'Thử lại danh sách bác sĩ' }));
        await waitFor(() => expect(screen.getByRole('combobox', { name: 'Bác sĩ' })).not.toBeDisabled());
        expect(get).toHaveBeenCalledTimes(1);
        expect(get).toHaveBeenCalledWith('/admin/doctors?isActive=true&pageSize=100');
    });

    it('Leaves shows affected appointments as a single warning in the modal without showAlert', async () => {
        vi.mocked(axiosClient.post).mockRejectedValue({ errorCode: 'LEAVE_HAS_AFFECTED_APPOINTMENTS', message: 'Backend message' });
        render(<AdminLeaves />);
        await userEvent.click(await screen.findByRole('button', { name: 'Chi tiết' }));
        await userEvent.click(screen.getByRole('button', { name: 'Duyệt yêu cầu' }));
        const modal = screen.getByRole('dialog');
        expect(await within(modal).findByText('Bác sĩ đang có lịch hẹn bị ảnh hưởng. Hãy để lễ tân xử lý các lịch này trước khi duyệt nghỉ.')).toBeInTheDocument();
        expect(within(modal).getByRole('alert')).toHaveClass('ant-alert-warning');
        expect(dialog.showAlert).not.toHaveBeenCalled();
    });
});
