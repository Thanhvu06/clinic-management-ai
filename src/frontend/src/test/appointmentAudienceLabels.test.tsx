import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { PatientRevisit } from '../pages/patient/PatientRevisit';
import { PatientPackageRegistrations } from '../pages/patient/PatientPackageRegistrations';
import { AppointmentLookupModal } from '../components/AppointmentLookupModal';
import { getAppointmentStatusLabel } from '../utils/appointmentStatusLabels';
import { toLocalDateString } from '../utils/formatters';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn(), setSelection: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: mocks.setSelection }) }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getMyFacilities: async () => ({ success: true, data: [{ id: 1, name: 'Cơ sở kiểm thử', isActive: true }] }),
} }));

const expected = [
    ['Pending', 'Chờ xác nhận'], ['Confirmed', 'Chờ tiếp nhận'], ['PendingReschedule', 'Chờ duyệt đổi lịch'],
    ['PendingCancellation', 'Chờ duyệt hủy'], ['CheckedIn', 'Đã tiếp nhận'], ['InConsultation', 'Đang khám'],
    ['InProgress', 'Đang khám'], ['Completed', 'Đã khám xong'], ['Cancelled', 'Đã hủy'], ['NoShow', 'Vắng mặt'],
    ['FutureStatus', 'Không xác định'], ['toString', 'Không xác định'],
];
const states = ['Confirmed', 'CheckedIn', 'InConsultation', 'Completed'];
const appointments = states.map((status, index) => ({
    id: index + 1, appointmentCode: `APT-LABEL-${index + 1}`, status,
    patientId: 1, patientName: 'Người bệnh', maskedPatientName: 'Người bệnh', patientPhone: '0900000004',
    maskedPhoneNumber: '090***0004', doctorId: 1, doctorName: 'Nguyễn Minh Khải', specialtyId: 1,
    specialtyName: 'Nội tổng quát', appointmentDate: toLocalDateString(), startTime: '08:00:00', endTime: '08:30:00',
}));
const mount = (element: React.ReactNode) => render(<MemoryRouter>{element}</MemoryRouter>);
beforeEach(() => {
    mocks.get.mockReset();
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data:
        url.startsWith('/reception/appointments?') || url.startsWith('/appointments/my?')
            ? { items: appointments, totalItems: appointments.length }
            : url.startsWith('/appointments/lookup?') ? appointments
                : url.startsWith('/reception/stats') ? { appointmentsToday: 4, pendingAppointmentsToday: 0, confirmedAppointmentsToday: 1, completedAppointmentsToday: 1 }
                    : { items: [] },
    }));
});
afterEach(cleanup);

describe('Shared appointment labels by audience', () => {
    it.each(expected)('labels %s for staff and patients', (status, staff) => {
        expect(getAppointmentStatusLabel(status)).toBe(staff);
        expect(getAppointmentStatusLabel(status, 'staff')).toBe(staff);
        expect(getAppointmentStatusLabel(status, 'patient')).toBe(status === 'Confirmed' ? 'Đã xác nhận' : staff);
    });

    it.each([['workspace', ReceptionWorkspace], ['appointments', ReceptionAppointments]] as const)('renders the four staff labels in reception %s without changing check-in actions', async (_, Page) => {
        mount(<Page />);
        await screen.findByText('APT-LABEL-1');
        for (const [index, label] of ['Chờ tiếp nhận', 'Đã tiếp nhận', 'Đang khám', 'Đã khám xong'].entries()) {
            const row = screen.getByText(`APT-LABEL-${index + 1}`).closest('tr')!;
            expect(within(row).getByText(label)).toBeInTheDocument();
            if (index === 0) expect(within(row).getByRole('button', { name: 'Tiếp nhận' })).toBeInTheDocument();
            else expect(within(row).queryByRole('button', { name: 'Tiếp nhận' })).not.toBeInTheDocument();
            if (index === 1) expect(within(row).queryByText('Đang khám')).not.toBeInTheDocument();
        }
    });

    it('uses staff labels in the reception status filter while preserving API status values', async () => {
        mount(<ReceptionAppointments />);
        await screen.findByText('APT-LABEL-1');
        fireEvent.mouseDown(screen.getByRole('combobox'));
        const dropdown = await screen.findByRole('listbox');
        const option = within(dropdown.parentElement!).getByText('Chờ tiếp nhận');
        fireEvent.click(option);
        await vi.waitFor(() => expect(mocks.get).toHaveBeenCalledWith('/reception/appointments?page=1&pageSize=10&status=Confirmed'));
    });

    it('renders all four patient labels and keeps actions restricted to confirmed appointments', async () => {
        mount(<PatientAppointments />);
        await screen.findByText('APT-LABEL-1');
        for (const [index, label] of ['Đã xác nhận', 'Đã tiếp nhận', 'Đang khám', 'Đã khám xong'].entries()) {
            const card = document.getElementById(`appointment-card-${index + 1}`)!;
            expect(within(card).getByText(label)).toBeInTheDocument();
            expect(within(card).queryByText('Chưa xác định')).not.toBeInTheDocument();
            if (index === 0) expect(within(card).getByRole('button', { name: 'Đổi lịch' })).toBeInTheDocument();
            else expect(within(card).queryByRole('button', { name: 'Đổi lịch' })).not.toBeInTheDocument();
            if (index === 1) expect(within(card).queryByText('Đang khám')).not.toBeInTheDocument();
        }
    });

    it('renders patient labels for the four statuses in appointment lookup results', async () => {
        mount(<AppointmentLookupModal isOpen onClose={vi.fn()} />);
        fireEvent.change(screen.getByPlaceholderText('Ví dụ: APT-260829-0001'), { target: { value: 'APT-LABEL-1' } });
        fireEvent.change(screen.getByLabelText('Số điện thoại chủ lịch hẹn'), { target: { value: '0900000004' } });
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu' }));
        for (const label of ['Đã xác nhận', 'Đã tiếp nhận', 'Đang khám', 'Đã khám xong']) expect(await screen.findByText(label)).toBeInTheDocument();
        expect(screen.queryByText('Chưa xác định')).not.toBeInTheDocument();
    });

    it.each([['revisit', PatientRevisit], ['package', PatientPackageRegistrations]] as const)('uses the safe unknown fallback for %s without changing its separate status map', async (_, Page) => {
        mocks.get.mockResolvedValue({ success: true, data: [{
            id: 1, status: 'FutureStatus', registrationCode: 'PKG-1', healthPackageId: 1,
            preferredDate: toLocalDateString(), createdAt: '2026-10-23T01:00:00Z', suggestedDate: toLocalDateString(),
        }] });
        mount(<Page />);
        expect(await screen.findByText('Không xác định')).toBeInTheDocument();
        expect(screen.queryByText('Chưa xác định')).not.toBeInTheDocument();
    });
});
