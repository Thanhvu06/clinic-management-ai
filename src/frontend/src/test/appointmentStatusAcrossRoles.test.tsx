import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { DoctorAppointments } from '../pages/doctor/DoctorAppointments';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { AppointmentStatusBadge } from '../components/common/AppointmentStatusBadge';
import badgeStyles from '../components/common/AppointmentStatusBadge.module.css';
import { toLocalDateString } from '../utils/formatters';

const mocks = vi.hoisted(() => ({ get: vi.fn(), showAlert: vi.fn(), showConfirm: vi.fn(), setSelection: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => mocks }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: mocks.setSelection }) }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getMyFacilities: async () => ({ success: true, data: [{ id: 1, name: 'Cơ sở kiểm thử', isActive: true }] }),
} }));

const statuses = [
    ['Pending', 'Chờ xác nhận', badgeStyles.amber],
    ['Confirmed', 'Đã xác nhận', badgeStyles.blue],
    ['PendingReschedule', 'Chờ duyệt đổi lịch', badgeStyles.orange],
    ['PendingCancellation', 'Chờ duyệt hủy', badgeStyles.deepOrange],
    ['Cancelled', 'Đã hủy', badgeStyles.gray],
    ['Completed', 'Đã khám xong', badgeStyles.green],
    ['NoShow', 'Vắng mặt', badgeStyles.red],
    ['CheckedIn', 'Đã tiếp nhận', badgeStyles.teal],
    ['InConsultation', 'Đang khám', badgeStyles.purple],
    ['InProgress', 'Đang khám', badgeStyles.purple],
] as const;
const appointment = {
    id: 91, appointmentCode: 'APT-SAME-91', status: 'CheckedIn', patientId: 1, patientName: 'Người bệnh',
    patientPhone: '0900000004', doctorId: 1, doctorName: 'Nguyễn Minh Khải', specialtyId: 1,
    specialtyName: 'Nội tổng quát', appointmentDate: toLocalDateString(), startTime: '08:00:00', endTime: '08:30:00',
};

beforeEach(() => {
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data:
        url.startsWith('/reception/stats') ? { appointmentsToday: 1 }
            : ['/appointments/my?', '/reception/appointments?', '/doctor/appointments?'].some(prefix => url.startsWith(prefix))
                ? { items: [appointment], totalItems: 1 }
                : { items: [] },
    }));
});
afterEach(cleanup);

it.each(statuses)('uses the shared label and badge color for %s', (status, label, color) => {
    render(<AppointmentStatusBadge status={status} />);
    expect(screen.getByText(label)).toHaveClass(color);
});

it.each([PatientAppointments, DoctorAppointments, ReceptionWorkspace, ReceptionAppointments])(
    'shows the same checked-in badge in $name', async Page => {
        render(<MemoryRouter><Page /></MemoryRouter>);
        await screen.findByText(appointment.appointmentCode);
        expect(screen.getByText('Đã tiếp nhận')).toHaveClass(badgeStyles.teal);
    },
);
