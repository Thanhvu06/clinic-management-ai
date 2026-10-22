import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getApiErrorMessage } from '../utils/apiError';
import { getAppointmentHistoryActionLabel } from '../utils/appointmentHistoryLabels';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { organizationApi } from '../api/organizationApi';
import { mpiApi } from '../api/mpiApi';
import { DialogProvider } from '../contexts/DialogContext';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { ReceptionPackageRegistrations } from '../pages/reception/ReceptionPackageRegistrations';
import { DoctorAppointments } from '../pages/doctor/DoctorAppointments';
import { AppointmentLookupModal } from '../components/AppointmentLookupModal';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getSchedule: vi.fn(), previewLeave: vi.fn(), createLeaveRequest: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn() } }));
vi.mock('../api/mpiApi', () => ({ mpiApi: { getPatientById: vi.fn() } }));

describe('getApiErrorMessage', () => {
    const fallback = 'Thông báo dự phòng';
    it.each([
        [{ message: 'Lỗi máy chủ', status: 400 }, 'Lỗi máy chủ'],
        [{ response: { data: { message: 'Lỗi mock cũ' } } }, 'Lỗi mock cũ'],
        [{ response: { data: { message: 'Ưu tiên response' } }, message: 'Payload', status: 422 }, 'Ưu tiên response'],
        [new Error('x'), fallback],
        [Object.assign(new Error('x'), { status: 400 }), fallback],
        [{ message: 'Không có response' }, fallback],
        [null, fallback], [undefined, fallback], ['Network Error', fallback],
        [{ message: '', status: 400 }, fallback],
        [{ message: '   ', status: 400 }, fallback],
        [{ message: 42, status: 400 }, fallback],
        [{ message: 'Sai kiểu status', status: '400' }, fallback],
        [{ response: { data: { message: '' } }, message: 'Payload hợp lệ', status: 400 }, 'Payload hợp lệ'],
        [{ response: { data: { message: 42 } } }, fallback],
        [Object.assign(new Error('x'), { response: { data: { message: 'Legacy Error response' } } }), 'Legacy Error response'],
        [{ message: '  Giữ nội dung backend  ', status: 500 }, '  Giữ nội dung backend  '],
    ])('selects a supported server message for %j', (error, expected) => {
        expect(getApiErrorMessage(error, fallback)).toBe(expected);
    });
});

describe('Shared appointment history labels', () => {
    it.each([
        ['Created', 'Tạo mới'], ['StatusChanged', 'Cập nhật trạng thái'], ['Cancelled', 'Hủy lịch'],
        ['Rescheduled', 'Đổi lịch'], ['Confirmed', 'Xác nhận'], ['Completed', 'Hoàn thành'],
        ['NoShow', 'Đánh dấu vắng'], ['RescheduleRequested', 'Yêu cầu đổi lịch'], ['CancelRequested', 'Yêu cầu hủy'],
        ['FutureAction', 'FutureAction'], ['toString', 'toString']
    ])('%s becomes %s', (action, label) => expect(getAppointmentHistoryActionLabel(action)).toBe(label));
});

const ok = <T,>(data: T) => ({ success: true, message: '', data });
const appointment = { id: 81, appointmentCode: 'APT-API-81', patientId: 17, patientName: 'Người bệnh API', patientPhone: '0901234567', patientGender: 'Female', patientDob: null,
    doctorId: 51, doctorName: 'Bác sĩ API', specialtyId: 3, appointmentDate: '2026-10-20', startTime: '09:00:00', endTime: '09:30:00', reason: 'Khám sức khỏe', status: 'Confirmed' };
const registration = { id: 21, registrationCode: 'PKG-API-21', patientId: 17, patientName: 'Người bệnh API', healthPackageId: 1, packageName: 'Gói khám API', preferredDate: '2026-10-20', contactPhone: '0901234567', status: 'Pending', createdAt: '2026-10-16T03:00:00Z' };
const mount = (page: React.ReactNode, route = '/') => render(
    <ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><DialogProvider>{page}</DialogProvider></MemoryRouter></ConfigProvider>
);

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockImplementation(async url => {
        if (url === '/doctors') return ok([{ id: 51, fullName: 'Bác sĩ API' }]);
        if (url.startsWith('/doctor/appointments?')) return ok({ items: [appointment], totalItems: 1 });
        if (url.endsWith('/history')) return ok([{ id: 1, action: 'Cancelled', note: 'Bệnh nhân yêu cầu', createdAt: '2026-10-16T03:00:00Z' }]);
        if (url.endsWith('/prescription')) return ok(null);
        if (url.startsWith('/reception/health-package-registrations?')) return ok({ items: [registration], totalItems: 1 });
        if (url.startsWith('/appointments/lookup?')) return ok([{ ...appointment, maskedPatientName: 'Người bệnh API', maskedPhoneNumber: '090***4567', specialtyName: 'Nội khoa' }]);
        return ok([]);
    });
    vi.mocked(organizationApi.getFacilities).mockResolvedValue(ok([{ id: 1, code: 'F', name: 'Cơ sở API', address: '', city: '', phone: '', isActive: true, buildingCount: 0, departmentCount: 1 }]));
    vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([{ id: 3, facilityId: 1, facilityName: 'Cơ sở API', code: 'N', name: 'Khoa Nội', departmentType: 0, departmentTypeName: 'Lâm sàng', isActive: true, roomCount: 0 }]));
    vi.mocked(organizationApi.getRooms).mockResolvedValue(ok([]));
    vi.mocked(mpiApi.getPatientById).mockResolvedValue(ok({ id: 17, fullName: 'Người bệnh API', medicalRecordNumber: 'API-17', allergies: [], emergencyContacts: [] }));
    vi.mocked(doctorApi.getSchedule).mockResolvedValue(ok([{ date: '2026-10-20', dayOfWeekName: 'Thứ Ba', shifts: [] }]));
    vi.mocked(doctorApi.previewLeave).mockResolvedValue(ok({ startDate: '2026-10-20', endDate: '2026-10-21', affectedAppointmentCount: 1,
        affectedAppointments: [{ ...appointment }] }));
});
afterEach(cleanup);

describe('API messages and remaining date displays', () => {
    it('shows the flattened server leave message when walk-in intake fails', async () => {
        const message = 'Bác sĩ đang trong lịch nghỉ đã được duyệt, vui lòng chọn bác sĩ khác.';
        vi.mocked(axiosClient.post).mockRejectedValue({ message, status: 400 });
        mount(<WalkInPatientRegistration />, '/reception/register?existingPatientId=17');
        await screen.findByText('Người bệnh API');
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chuẩn bị lượt khám/ }));
        await screen.findByRole('option', { name: 'Bác sĩ API (Đa khoa)' });
        fireEvent.change(screen.getByLabelText('Bác sĩ phụ trách'), { target: { value: '51' } });
        fireEvent.change(screen.getByPlaceholderText(/Mô tả lý do đến khám/), { target: { value: 'Khám sức khỏe' } });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận thông tin/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận tiếp nhận & cấp STT' }));
        expect(await screen.findByText(message)).toBeInTheDocument();
        expect(axiosClient.post).toHaveBeenCalledWith('/patient-visits/intake', expect.objectContaining({ assignedDoctorId: 51, existingPatientId: 17 }), expect.anything());
        expect(screen.queryByText('Không thể hoàn tất tiếp nhận lượt khám.')).not.toBeInTheDocument();
    });

    it('shows server leave submission errors and formats schedule and affected-appointment dates', async () => {
        vi.useFakeTimers({ toFake: ['Date'], shouldAdvanceTime: true });
        vi.setSystemTime(new Date('2026-10-18T03:00:00Z'));
        try {
            const message = 'Bác sĩ đã có lịch nghỉ trong khoảng thời gian này.';
            vi.mocked(doctorApi.createLeaveRequest).mockRejectedValue({ message, status: 400 });
            mount(<DoctorSchedule />);
            expect(await screen.findByText('20/10')).toBeInTheDocument();
            fireEvent.click(screen.getByRole('button', { name: 'Đăng ký nghỉ phép' }));
            for (const [label, date] of [['Từ ngày', '20/10/2026'], ['Đến ngày', '21/10/2026']]) {
                const input = screen.getByLabelText(label);
                fireEvent.change(input, { target: { value: date } });
                fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' });
                fireEvent.blur(input);
            }
            fireEvent.change(screen.getByLabelText('Lý do nghỉ phép'), { target: { value: 'Việc gia đình' } });
            expect(await screen.findByText(/20\/10\/2026 \(09:00 - 09:30\): Người bệnh API/)).toBeInTheDocument();
            fireEvent.click(screen.getByRole('button', { name: 'Gửi đơn nghỉ phép' }));
            expect(await screen.findByText(message)).toBeInTheDocument();
            expect(doctorApi.createLeaveRequest).toHaveBeenCalledWith({ startDate: '2026-10-20', endDate: '2026-10-21', reason: 'Việc gia đình' });
        } finally {
            vi.useRealTimers();
        }
    });

    it('formats the preferred date inside the package confirmation dialog', async () => {
        mount(<ReceptionPackageRegistrations />);
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận' }));
        const dialog = screen.getByRole('dialog');
        expect(within(dialog).getByText('20/10/2026')).toBeInTheDocument();
        expect(within(dialog).queryByText('2026-10-20')).not.toBeInTheDocument();
    });

    it('localizes the doctor appointment history action to Hủy lịch', async () => {
        mount(<DoctorAppointments />);
        fireEvent.click(await screen.findByRole('button', { name: 'Chi tiết' }));
        expect(await screen.findByText('Hủy lịch')).toBeInTheDocument();
        expect(screen.queryByText('Cancelled')).not.toBeInTheDocument();
    });

    it('formats the appointment lookup result date without changing its API query', async () => {
        mount(<AppointmentLookupModal isOpen onClose={vi.fn()} />);
        fireEvent.change(screen.getByPlaceholderText('Ví dụ: APT-260829-0001'), { target: { value: 'APT-API-81' } });
        fireEvent.change(screen.getByLabelText('Số điện thoại chủ lịch hẹn'), { target: { value: '0901234567' } });
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu' }));
        await waitFor(() => expect(screen.getByText('20/10/2026')).toBeInTheDocument());
        expect(axiosClient.get).toHaveBeenCalledWith('/appointments/lookup?query=APT-API-81&phone=0901234567');
    });
});
