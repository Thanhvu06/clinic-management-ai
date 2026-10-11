import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent, within, cleanup } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import '../theme/browserCompat';
import { DoctorAppointments } from '../pages/doctor/DoctorAppointments';
import { DialogProvider } from '../contexts/DialogContext';
import axiosClient from '../api/axiosClient';
import { getAppointmentStatusLabel } from '../utils/appointmentStatusLabels';

vi.mock('../api/axiosClient', () => ({
    default: { get: vi.fn(), post: vi.fn() }
}));

const appointments = ['CheckedIn', 'InConsultation', 'PendingReschedule', 'Confirmed', 'Completed'].map((status, index) => ({
    id: index + 1,
    appointmentCode: `APPT-${index + 1}`,
    patientId: index + 10,
    patientName: `Bệnh nhân ${index + 1}`,
    patientPhone: '0901234567',
    patientGender: 'Nam',
    patientDob: null,
    specialtyId: 1,
    appointmentDate: '2026-10-11',
    startTime: '08:30',
    endTime: '09:00',
    reason: 'Khám định kỳ',
    status
}));

const RouteProbe = () => <output data-testid="route">{useLocation().pathname}</output>;

const renderPage = () => render(
    <ConfigProvider theme={{ token: { motion: false } }}>
        <MemoryRouter initialEntries={['/doctor/appointments']}>
            <DialogProvider>
                <DoctorAppointments />
                <RouteProbe />
            </DialogProvider>
        </MemoryRouter>
    </ConfigProvider>
);

describe('DoctorAppointments flow', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.mocked(axiosClient.get).mockImplementation(async (url) => {
            if (url.startsWith('/doctor/appointments?')) {
                return { success: true, data: { items: [...appointments], totalItems: appointments.length } };
            }
            if (url.endsWith('/history')) return { success: true, data: [] };
            if (url.endsWith('/prescription')) return {
                success: true,
                data: { status: 'Issued', items: [{ medicineId: 1, medicineName: 'Thuốc đã kê', medicineCode: 'MED-1', quantity: 10, unit: 'viên', dosage: '1 viên', frequency: '2 lần/ngày' }] }
            };
            throw new Error(`Unexpected GET ${url}`);
        });
    });

    afterEach(() => {
        expect(vi.mocked(axiosClient.post).mock.calls.some(([url]) => url.includes('/complete'))).toBe(false);
        cleanup();
    });

    it('renders Vietnamese labels for received patients without raw status text', async () => {
        renderPage();
        const checkedIn = (await screen.findByText('Bệnh nhân 1')).closest('tr')!;
        const inConsultation = screen.getByText('Bệnh nhân 2').closest('tr')!;
        expect(within(checkedIn).getByText('Đã tiếp nhận')).toBeInTheDocument();
        expect(within(inConsultation).getByText('Đang khám')).toBeInTheDocument();
        expect(screen.getByText('Chờ duyệt đổi lịch')).toBeInTheDocument();
        expect(screen.queryByText('CheckedIn')).not.toBeInTheDocument();
        expect(screen.queryByText('InConsultation')).not.toBeInTheDocument();
    }, 15000);

    it('preserves examination, revisit and record buttons and navigates to examination routes', async () => {
        renderPage();
        const checkedIn = (await screen.findByText('Bệnh nhân 1')).closest('tr')!;
        const inConsultation = screen.getByText('Bệnh nhân 2').closest('tr')!;
        const enterExamination = within(checkedIn).getByText('Vào khám').closest('button')!;
        const continueExamination = within(inConsultation).getByText('Tiếp tục khám').closest('button')!;
        expect(enterExamination).toBeInTheDocument();
        expect(continueExamination).toBeInTheDocument();
        expect(screen.getAllByText('Hồ sơ').map(text => text.closest('button'))).toHaveLength(5);
        for (const appointment of appointments) {
            const row = screen.getByText(appointment.patientName).closest('tr')!;
            expect(within(row).getByText('Hồ sơ').closest('button')).toBeInTheDocument();
        }
        const completed = screen.getByText('Bệnh nhân 5').closest('tr')!;
        expect(within(completed).getByText('Tái khám').closest('button')).toBeInTheDocument();
        fireEvent.click(enterExamination);
        expect(screen.getByTestId('route')).toHaveTextContent('/doctor/appointments/1/examination');
        fireEvent.click(continueExamination);
        expect(screen.getByTestId('route')).toHaveTextContent('/doctor/appointments/2/examination');
    }, 15000);

    it('opens Confirmed details with reception guidance, no-show action and existing prescription', async () => {
        renderPage();
        const row = (await screen.findByText('Bệnh nhân 4')).closest('tr')!;
        expect(within(row).getByText('Chờ tiếp nhận')).toBeInTheDocument();
        expect(within(row).queryByText('Khám')?.closest('button') ?? null).not.toBeInTheDocument();
        fireEvent.click(within(row).getByText('Chi tiết').closest('button')!);
        expect(await screen.findByText('Bệnh nhân cần được tiếp nhận tại Hàng đợi khám trước khi bắt đầu khám.')).toBeInTheDocument();
        expect(screen.getByText('Đánh dấu vắng mặt').closest('button')).toBeInTheDocument();
        expect(screen.queryByText('Bắt đầu khám & Kê đơn')).not.toBeInTheDocument();
        expect(screen.queryByText('Tóm tắt kết quả khám')).not.toBeInTheDocument();
        expect(await screen.findByText('Đơn thuốc đã kê')).toBeInTheDocument();
        expect(screen.getByText('Thuốc đã kê (MED-1)')).toBeInTheDocument();
        await waitFor(() => expect(axiosClient.get).toHaveBeenCalledWith('/doctor/appointments/4/history'));
        expect(axiosClient.get).not.toHaveBeenCalledWith('/medicines/active');
        fireEvent.click(screen.getByText('Mở hàng đợi').closest('button')!);
        expect(screen.getByTestId('route')).toHaveTextContent('/doctor/queue');
    }, 15000);

    it('shows InlineError and reloads the list on retry', async () => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ 500'));
        renderPage();
        expect(await screen.findByText('Lỗi kết nối máy chủ 500')).toBeInTheDocument();
        fireEvent.click(screen.getByText('Thử lại').closest('button')!);
        expect(await screen.findByText('Bệnh nhân 1')).toBeInTheDocument();
        expect(screen.queryByText('Lỗi kết nối máy chủ 500')).not.toBeInTheDocument();
    }, 15000);

    it('offers exactly the six supported status filters with Vietnamese labels', async () => {
        renderPage();
        await screen.findByText('Bệnh nhân 1');
        fireEvent.mouseDown(screen.getAllByRole('combobox')[1]);
        const options = await screen.findAllByRole('option');
        // Antd exposes a virtual option list; verify all rendered option labels in the popup.
        expect(options.length).toBeGreaterThan(0);
        const popup = document.querySelector('.ant-select-dropdown')!;
        for (const label of ['Chờ tiếp nhận', 'Đã tiếp nhận', 'Đang khám', 'Đã khám xong', 'Vắng mặt', 'Đã hủy']) {
            expect(within(popup as HTMLElement).getByText(label)).toBeInTheDocument();
        }
        expect(popup.querySelectorAll('.ant-select-item-option')).toHaveLength(6);
        fireEvent.click(within(popup as HTMLElement).getByText('Đã tiếp nhận'));
        await waitFor(() => expect(axiosClient.get).toHaveBeenCalledWith('/doctor/appointments?page=1&pageSize=10&status=CheckedIn'));
    }, 15000);

    it.each([
        ['Pending', 'Chờ xác nhận'], ['Confirmed', 'Chờ tiếp nhận'],
        ['PendingReschedule', 'Chờ duyệt đổi lịch'], ['PendingCancellation', 'Chờ duyệt hủy'],
        ['Cancelled', 'Đã hủy'], ['Completed', 'Đã khám xong'], ['NoShow', 'Vắng mặt'],
        ['CheckedIn', 'Đã tiếp nhận'], ['InConsultation', 'Đang khám'],
        ['UnknownStatus', 'Không xác định'], ['toString', 'Không xác định']
    ])('labels %s as %s', (status, label) => {
        expect(getAppointmentStatusLabel(status)).toBe(label);
    });
});
