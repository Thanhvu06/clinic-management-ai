import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getMyFacilities: vi.fn().mockResolvedValue({ success: true, data: [{ id: 1, name: 'Cơ sở kiểm thử', isActive: true }] })
} }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => dialog }));

it('shows Confirmed appointments as Chờ tiếp nhận with HH:mm appointment times', async () => {
    vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, message: '', data:
        url.startsWith('/reception/appointments?') ? { items: [{
            id: 91, appointmentCode: 'APT-RECEPTION-91', patientId: 11, patientName: 'Người bệnh tiếp nhận',
            patientPhone: '0901234567', doctorId: 21, doctorName: 'Bác sĩ tiếp nhận', specialtyId: 31,
            specialtyName: 'Nội khoa', appointmentDate: '2026-10-21', startTime: '09:00:00', endTime: '09:30:00',
            reason: 'Khám tổng quát', status: 'Confirmed'
        }], totalItems: 1 } : { appointmentsToday: 1, pendingAppointmentsToday: 0, confirmedAppointmentsToday: 1, completedAppointmentsToday: 0 }
    }));

    render(<MemoryRouter><ReceptionWorkspace /></MemoryRouter>);

    const row = (await screen.findByText('APT-RECEPTION-91')).closest('tr');
    expect(within(row!).getByText('Chờ tiếp nhận')).toBeInTheDocument();
    expect(within(row!).getByText('09:00 - 09:30')).toBeInTheDocument();
    expect(within(row!).queryByText('Đã xác nhận (Chờ tiếp nhận)')).not.toBeInTheDocument();
    expect(row).not.toHaveTextContent('09:00:00 - 09:30:00');
    expect(row).not.toHaveTextContent(/\d{2}:\d{2}:\d{2}\s*-/);
});
