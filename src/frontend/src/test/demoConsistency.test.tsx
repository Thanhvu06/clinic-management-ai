import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { DoctorAppointments } from '../pages/doctor/DoctorAppointments';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { doctorApi } from '../api/doctorApi';
import { AdminAuditLogs } from '../pages/admin/AdminAuditLogs';
import { DialogProvider } from '../contexts/DialogContext';
import { appointmentStatusLabels, getAppointmentStatusLabel } from '../utils/appointmentStatusLabels';
import { getVisitStatusLabel } from '../utils/visitStatusLabels';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getDashboard: vi.fn() } }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });

it('uses the same staff appointment labels in the workspace and appointment lists', () => {
    for (const status of Object.keys(appointmentStatusLabels).filter(status => status !== 'InProgress')) {
        expect(getVisitStatusLabel(status)).toBe(getAppointmentStatusLabel(status));
    }
    expect(getVisitStatusLabel('Pending')).toBe('Chờ xác nhận');
    expect(getVisitStatusLabel('Confirmed')).toBe('Chờ tiếp nhận');
    expect(getAppointmentStatusLabel('Confirmed', 'patient')).toBe('Đã xác nhận');
});

it('preserves server ordering with upcoming appointments before descending past appointments', async () => {
    const items = ['2026-10-25', '2026-10-26', '2026-10-24', '2026-10-23'].map((date, i) => ({
        id: i + 1, appointmentCode: `APT-261025-0000000${i}`, patientName: `Bệnh nhân ${i + 1}`,
        patientGender: 'Female', appointmentDate: date, startTime: '08:00', status: 'Pending'
    }));
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { items, totalItems: items.length } });
    render(<MemoryRouter><DialogProvider><DoctorAppointments /></DialogProvider></MemoryRouter>);
    await screen.findByText('Bệnh nhân 1');
    expect(screen.getAllByText(/^Bệnh nhân \d$/).map(node => node.textContent)).toEqual(items.map(item => item.patientName));
});

it('uses the shared appointment label for the next patient on the doctor dashboard', async () => {
    vi.mocked(doctorApi.getDashboard).mockResolvedValue({ success: true, message: '', data: {
        kpis: { totalAppointmentsToday: 1, waitingCount: 0, inConsultationCount: 0, completedCount: 0, noShowCount: 0 },
        nextPatient: { appointmentId: 1, appointmentCode: 'APT-261025-ABCDEF12', patientId: 1, patientName: 'Người bệnh tiếp theo',
            patientPhone: '', appointmentDate: '2026-10-25', startTime: '08:00', endTime: '08:30', status: 'Confirmed',
            queueOrder: 1, isVitalsRecorded: false }, todayQueue: []
    } });
    render(<MemoryRouter><DialogProvider><DoctorDashboard /></DialogProvider></MemoryRouter>);
    expect(await screen.findByText('Chờ tiếp nhận')).toBeInTheDocument();
    expect(screen.queryByText('Đã xác nhận')).not.toBeInTheDocument();
});

it('shows business reference codes in audit logs without displaying numeric entity ids', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: { items: [{ id: 1,
        userFullName: 'Lễ tân', action: 'CreateInvoice', entityName: 'Invoice', entityId: '987654',
        referenceCode: 'INV-261025-ABC1234', description: 'Lập hóa đơn: 570.000 ₫', createdAt: '2026-10-25T01:00:00Z'
    }], totalItems: 1 } });
    render(<AdminAuditLogs />);
    expect(await screen.findByText('INV-261025-ABC1234')).toBeInTheDocument();
    expect(screen.queryByText('987654')).not.toBeInTheDocument();
    expect(screen.getByText('Lập hóa đơn: 570.000 ₫')).toBeInTheDocument();
});
