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

    it('renders Vietnamese labels and preserves examination actions for received patients', async () => {
        renderPage();
        const checkedIn = (await screen.findByText('Bệnh nhân 1')).closest('tr')!;
        const inConsultation = screen.getByText('Bệnh nhân 2').closest('tr')!;
        expect(within(checkedIn).getByText('Đã tiếp nhận')).toBeInTheDocument();
        expect(within(checkedIn).getByRole('button', { name: 'Vào khám' })).toBeInTheDocument();
        expect(within(inConsultation).getByText('Đang khám')).toBeInTheDocument();
        expect(within(inConsultation).getByRole('button', { name: 'Tiếp tục khám' })).toBeInTheDocument();
        expect(screen.getByText('Chờ duyệt đổi lịch')).toBeInTheDocument();
        expect(screen.queryByText('CheckedIn')).not.toBeInTheDocument();
        expect(screen.queryByText('InConsultation')).not.toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: 'Hồ sơ' })).toHaveLength(5);
        expect(screen.getByRole('button', { name: 'Tái khám' })).toBeInTheDocument();
        fireEvent.click(within(checkedIn).getByRole('button', { name: 'Vào khám' }));
        expect(screen.getByTestId('route')).toHaveTextContent('/doctor/appointments/1/examination');
        fireEvent.click(within(inConsultation).getByRole('button', { name: 'Tiếp tục khám' }));
        expect(screen.getByTestId('route')).toHaveTextContent('/doctor/appointments/2/examination');
    });

    it('opens Confirmed details with reception guidance, no-show action and existing prescription', async () => {
        renderPage();
        const row = (await screen.findByText('Bệnh nhân 4')).closest('tr')!;
        expect(within(row).getByText('Chờ tiếp nhận')).toBeInTheDocument();
        expect(within(row).queryByRole('button', { name: 'Khám' })).not.toBeInTheDocument();
        fireEvent.click(within(row).getByRole('button', { name: 'Chi tiết' }));
        expect(await screen.findByText('Bệnh nhân cần được tiếp nhận tại Hàng đợi khám trước khi bắt đầu khám.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Đánh dấu vắng mặt' })).toBeInTheDocument();
        expect(screen.queryByText('Bắt đầu khám & Kê đơn')).not.toBeInTheDocument();
        expect(screen.queryByText('Tóm tắt kết quả khám')).not.toBeInTheDocument();
        expect(await screen.findByText('Đơn thuốc đã kê')).toBeInTheDocument();
        expect(screen.getByText('Thuốc đã kê (MED-1)')).toBeInTheDocument();
        await waitFor(() => expect(axiosClient.get).toHaveBeenCalledWith('/doctor/appointments/4/history'));
        expect(axiosClient.get).not.toHaveBeenCalledWith('/medicines/active');
        fireEvent.click(screen.getByRole('button', { name: 'Mở hàng đợi' }));
        expect(screen.getByTestId('route')).toHaveTextContent('/doctor/queue');
    });

    it('shows InlineError and reloads the list on retry', async () => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce(new Error('Lỗi kết nối máy chủ 500'));
        renderPage();
        expect(await screen.findByText('Lỗi kết nối máy chủ 500')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('Bệnh nhân 1')).toBeInTheDocument();
        expect(screen.queryByText('Lỗi kết nối máy chủ 500')).not.toBeInTheDocument();
    });

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
    });

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
