import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { LabSupervisorDashboard } from '../pages/lab/LabSupervisorDashboard';

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), queue: vi.fn(), alert: vi.fn(), removeAttempt: vi.fn(),
    chat: { pendingSpecialtyId: null, setPendingSpecialtyId: vi.fn(), activeDraft: null, setActiveDraft: vi.fn(), openChat: vi.fn() } }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getSharedVerificationQueue: mocks.queue } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'regression-patient', role: 'Patient' }, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showConfirm: vi.fn() }) }));
vi.mock('../contexts/ChatContext', async original => ({
    ...await original<typeof import('../contexts/ChatContext')>(),
    useChatContext: () => mocks.chat,
    removePersistedBookingAttempt: mocks.removeAttempt,
}));
const ok = (data: unknown) => ({ success: true, data });
const mount = (element: React.ReactNode) => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter>{element}</MemoryRouter></ConfigProvider>);
beforeEach(() => { vi.clearAllMocks(); localStorage.clear(); sessionStorage.clear(); });
afterEach(cleanup);

describe('Booking server rejection banner', () => {
    it.each([
        [400, 'FACILITY_NOT_AVAILABLE'], [401, 'FACILITY_NOT_AVAILABLE'], [403, 'FACILITY_NOT_AVAILABLE'],
        [404, 'FACILITY_NOT_AVAILABLE'], [409, 'FACILITY_NOT_AVAILABLE'], [422, 'FACILITY_NOT_AVAILABLE'], [429, 'FACILITY_NOT_AVAILABLE'],
        [409, 'SLOT_ALREADY_BOOKED'], [409, 'PATIENT_TIME_CONFLICT'], [422, 'DOCTOR_NOT_AVAILABLE'],
    ])('shows the server message for HTTP %s (%s) without an uncertain network banner', async (status, errorCode) => {
        mocks.get.mockImplementation(async (url: string) => url === '/specialties'
            ? ok([{ id: 1, specialtyCode: 'SP06', specialtyName: 'Tim mạch', aiEnabled: true }])
            : url.includes('/available-slots') ? ok([{ slotId: 10, doctorId: 2, slotDate: '2030-01-02', startTime: '14:00', endTime: '14:30' }])
                : ok([{ id: 2, fullName: 'Nguyễn Minh Khải', experienceYears: 12 }]));
        mocks.post.mockRejectedValue({ status, errorCode, message: 'Không có cơ sở khả dụng cho bác sĩ và chuyên khoa đã chọn.' });
        mount(<BookAppointment />);
        fireEvent.click(await screen.findByRole('button', { name: /^Tim mạch/ }));
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
        fireEvent.click(await screen.findByRole('button', { name: /Nguyễn Minh Khải/ }));
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
        fireEvent.change(screen.getByLabelText('Chọn ngày khám'), { target: { value: '2030-01-02' } });
        fireEvent.click(await screen.findByRole('button', { name: '14:00' }));
        fireEvent.change(screen.getByLabelText(/Triệu chứng hoặc lý do thăm khám/), { target: { value: 'Khám tim mạch và kiểm tra huyết áp' } });
        fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Xác nhận/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Xác nhận & đặt lịch' }));
        await waitFor(() => expect(mocks.post).toHaveBeenCalled());
        expect(await screen.findByText('Không có cơ sở khả dụng cho bác sĩ và chuyên khoa đã chọn.')).toBeInTheDocument();
        expect(screen.queryByText(/gián đoạn kết nối/)).not.toBeInTheDocument();
        expect(mocks.removeAttempt).toHaveBeenCalled();
    });
});

describe('Lab active order overview', () => {
    it('shows Ordered and InProgress orders with service, patient, status and elapsed wait', async () => {
        const common = { patientName: 'Nguyễn Văn An', orderedAtUtc: new Date(Date.now() - 45 * 60000).toISOString(),
            verificationStatus: 'NotRequired', items: [{ id: 1, serviceName: 'Tổng phân tích máu', status: 'Ordered' }] };
        mocks.queue.mockResolvedValue(ok({ items: [
            { ...common, id: 1, orderCode: 'DX-20261031-2000', status: 'Ordered' },
            { ...common, id: 2, orderCode: 'DX-20261031-2001', status: 'InProgress' },
        ], totalItems: 2 }));
        mount(<LabSupervisorDashboard />);
        expect(await screen.findByText('DX-20261031-2000')).toBeInTheDocument();
        expect(screen.getByText('DX-20261031-2001')).toBeInTheDocument();
        expect(screen.getAllByText('Nguyễn Văn An')).toHaveLength(2);
        expect(screen.getAllByText('Tổng phân tích máu')).toHaveLength(2);
        expect(screen.getByText('Chờ thực hiện')).toBeInTheDocument();
        expect(screen.getByText('Đang xử lý')).toBeInTheDocument();
        expect(screen.getAllByText('45 phút')).toHaveLength(2);
        expect(screen.queryByText('Không có phiếu đang xử lý')).not.toBeInTheDocument();
        expect(mocks.queue).toHaveBeenCalledWith({ page: 1, pageSize: 15, active: true });
    });

    it('shows empty text only for an empty successful response', async () => {
        mocks.queue.mockResolvedValue(ok({ items: [], totalItems: 0 }));
        mount(<LabSupervisorDashboard />);
        expect(await screen.findByText('Không có phiếu đang xử lý')).toBeInTheDocument();
    });

    it('shows a retryable server error instead of empty text', async () => {
        mocks.queue.mockRejectedValue({ status: 503, message: 'Không tải được phiếu xét nghiệm' });
        mount(<LabSupervisorDashboard />);
        expect(await screen.findByText('Không tải được phiếu xét nghiệm')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(screen.queryByText('Không có phiếu đang xử lý')).not.toBeInTheDocument();
    });
});

describe('Pending appointment change cards', () => {
    it.each(['Reschedule', 'Cancellation'])('uses a seeded Pending %s request even when the appointment is still Confirmed', async requestType => {
        mocks.get.mockImplementation(async (url: string) => url.includes('/appointments/my')
            ? ok({ items: [{ id: 1, appointmentCode: 'APT-261029-5371EF53', status: 'Confirmed', appointmentDate: '2030-01-02', startTime: '14:00', endTime: '14:30' }] })
            : ok({ items: [{ id: 20, appointmentId: 1, status: 'Pending', requestType, reason: 'Dời sang tuần sau' }] }));
        mount(<PatientAppointments />);
        await screen.findByText('APT-261029-5371EF53');
        expect(screen.getByText(requestType === 'Reschedule' ? 'Chờ duyệt đổi lịch' : 'Chờ duyệt hủy')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Rút yêu cầu' })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Đổi lịch' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Hủy lịch' })).not.toBeInTheDocument();
        expect(within(screen.getByRole('alert')).getByText(/Dời sang tuần sau/)).toBeInTheDocument();
    });
});
