import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { organizationApi } from '../api/organizationApi';
import { mpiApi } from '../api/mpiApi';
import { ReceptionChangeRequests } from '../pages/reception/ReceptionChangeRequests';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }));
vi.mock('antd', async importOriginal => ({
    ...await importOriginal<typeof import('antd')>(),
    Select: ({ options, value, onChange, disabled, placeholder, 'aria-label': label }: { options: { value: string | number; label: string }[]; value?: string | number; onChange: (value: string | number) => void; disabled?: boolean; placeholder?: string; 'aria-label'?: string }) =>
        <select aria-label={label} disabled={disabled} value={value ?? ''} onChange={event => onChange(event.target.value)}>
            {placeholder && <option value="">{placeholder}</option>}
            {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>,
}));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn() } }));
vi.mock('../api/mpiApi', () => ({ mpiApi: { getPatientById: vi.fn(), searchPatients: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => dialog }));
vi.mock('../components/CheckInTicketModal', () => ({ CheckInTicketModal: () => null }));

const ok = <T,>(data: T) => ({ success: true, message: '', data });

beforeEach(() => {
    vi.mocked(organizationApi.getFacilities).mockResolvedValue(ok([{ id: 1, code: 'CS1', name: 'Cơ sở 1' }]) as never);
    vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([
        { id: 5, code: 'KCXK', name: 'Khoa Cơ xương khớp', specialtyId: 1 },
        { id: 6, code: 'KNOI', name: 'Khoa Nội', specialtyId: 2 },
    ]) as never);
    vi.mocked(organizationApi.getRooms).mockImplementation(async query => ok(query?.departmentId === 6 ? [{ id: 61, roomNumber: 'P201', name: 'Buồng Nội' }] : [{ id: 51, roomNumber: 'P303', name: 'Buồng CXK' }]) as never);
    vi.mocked(mpiApi.getPatientById).mockResolvedValue(ok({ id: 9, fullName: 'Người bệnh K2' }) as never);
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('K1 change requests show appointment time in the list', () => {
    it('shows the original slot time on every row and the requested time for reschedules', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue(ok({ page: 1, pageSize: 10, totalPages: 1, totalItems: 2, items: [
            { id: 1, appointmentId: 11, appointmentCode: 'APT-261126-D720F6F0', patientName: 'BN Hủy', doctorName: 'BS A', requestType: 'Cancellation', requestedSlotId: null, reason: 'Bận việc', status: 'Pending', createdAt: '2026-11-26T03:00:00Z',
                currentSlotDate: '2026-11-27', currentStartTime: '12:00:00', currentEndTime: '12:30:00' },
            { id: 2, appointmentId: 12, appointmentCode: 'APT-261126-AAAA0000', patientName: 'BN Đổi', doctorName: 'BS B', requestType: 'Reschedule', requestedSlotId: 99, reason: 'Đổi giờ', status: 'Pending', createdAt: '2026-11-26T03:00:00Z',
                currentSlotDate: '2026-11-28', currentStartTime: '08:00:00', currentEndTime: '08:30:00',
                requestedSlotDate: '2026-12-01', requestedStartTime: '14:00:00', requestedEndTime: '14:30:00' },
        ] }));
        render(<ReceptionChangeRequests />);
        const cancelRow = (await screen.findByText('APT-261126-D720F6F0')).closest('tr')!;
        expect(within(cancelRow).getByText('27/11/2026 12:00–12:30')).toBeInTheDocument();
        const rescheduleRow = screen.getByText('APT-261126-AAAA0000').closest('tr')!;
        expect(within(rescheduleRow).getByText('28/11/2026 08:00–08:30')).toBeInTheDocument();
        expect(within(rescheduleRow).getByText('01/12/2026 14:00–14:30')).toBeInTheDocument();
    });
});

describe('K2 walk-in department must be chosen explicitly', () => {
    async function openStep2() {
        vi.mocked(axiosClient.get).mockResolvedValue(ok([]));
        render(<MemoryRouter initialEntries={['/reception/walk-in?existingPatientId=9']}><WalkInPatientRegistration /></MemoryRouter>);
        await screen.findByText('Người bệnh K2');
        fireEvent.click(screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/));
        await screen.findByRole('option', { name: 'Khoa Cơ xương khớp (KCXK)' });
    }

    it('leaves the department empty and the room locked until a department is chosen', async () => {
        await openStep2();
        expect(screen.getByRole('combobox', { name: 'Khoa phòng tiếp nhận' })).toHaveValue('');
        expect(screen.getByRole('combobox', { name: 'Phòng bệnh / buồng khám' })).toBeDisabled();
        expect(organizationApi.getRooms).not.toHaveBeenCalled();
        fireEvent.change(screen.getByRole('combobox', { name: 'Khoa phòng tiếp nhận' }), { target: { value: '6' } });
        await screen.findByRole('option', { name: 'P201 - Buồng Nội' });
        expect(screen.getByRole('combobox', { name: 'Phòng bệnh / buồng khám' })).toBeEnabled();
        expect(screen.queryByRole('option', { name: 'P303 - Buồng CXK' })).not.toBeInTheDocument();
    });

    it('shows an inline error at the field when continuing without a department', async () => {
        await openStep2();
        fireEvent.change(screen.getByPlaceholderText(/Mô tả lý do đến khám/), { target: { value: 'Đau gối' } });
        fireEvent.click(screen.getByText(/Tiếp tục: Xác nhận thông tin/));
        const department = screen.getByRole('combobox', { name: 'Khoa phòng tiếp nhận' });
        const field = department.closest('[class*="formField"]') as HTMLElement;
        expect(within(field).getByRole('alert')).toHaveTextContent('Vui lòng chọn khoa phòng tiếp nhận.');
        expect(screen.queryByText('Kiểm tra & xác nhận thông tin lượt khám')).not.toBeInTheDocument();
        expect(dialog.showAlert).not.toHaveBeenCalled();
        fireEvent.change(department, { target: { value: '5' } });
        expect(within(field).queryByRole('alert')).not.toBeInTheDocument();
        fireEvent.click(screen.getByText(/Tiếp tục: Xác nhận thông tin/));
        expect(screen.getByText('Kiểm tra & xác nhận thông tin lượt khám')).toBeInTheDocument();
    });
});

describe('K3 walk-in warnings pass message before title', () => {
    it('uses "Chưa chọn người bệnh" as the title when no patient is selected', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue(ok([]));
        render(<MemoryRouter initialEntries={['/reception/walk-in']}><WalkInPatientRegistration /></MemoryRouter>);
        fireEvent.click(screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/));
        expect(dialog.showAlert).toHaveBeenCalledWith('Vui lòng chọn một hồ sơ người bệnh từ kết quả tra cứu trước khi tiếp tục.', 'Chưa chọn người bệnh', 'warning');
    });

    it('uses short titles for the new-patient and search warnings', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue(ok([]));
        render(<MemoryRouter initialEntries={['/reception/walk-in']}><WalkInPatientRegistration /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Tìm hồ sơ/ }));
        expect(dialog.showAlert).toHaveBeenLastCalledWith('Vui lòng nhập từ khóa tìm kiếm (Mã BN, CCCD, SĐT hoặc Họ tên).', 'Thông báo', 'warning');
        fireEvent.click(screen.getByText(/Đăng ký hồ sơ người bệnh mới/));
        fireEvent.click(screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/));
        expect(dialog.showAlert).toHaveBeenLastCalledWith('Vui lòng nhập Họ và tên người bệnh.', 'Thiếu họ tên', 'warning');
        for (const [message, title] of dialog.showAlert.mock.calls) expect(String(title).length).toBeLessThan(String(message).length);
    });
});
