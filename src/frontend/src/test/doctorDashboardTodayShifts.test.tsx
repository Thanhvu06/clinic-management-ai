import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { doctorApi } from '../api/doctorApi';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import type { DoctorDashboardShiftDto, DoctorQueueItemDto } from '../types';

vi.mock('../api/doctorApi', () => ({ doctorApi: { getDashboard: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }) }));

const patient = (index: number): DoctorQueueItemDto => ({
    appointmentId: index, appointmentCode: `APT-261022-0000000${index}`, patientId: index, patientName: `Bệnh nhân ${index}`,
    patientPhone: '0900000000', appointmentDate: '2026-10-24', startTime: '08:00:00', endTime: '08:30:00',
    status: 'Completed', queueOrder: index, isVitalsRecorded: false,
});
const morning: DoctorDashboardShiftDto = { shiftName: 'Ca sáng', startTime: '08:00:00', endTime: '11:30:00' };
const afternoon: DoctorDashboardShiftDto = { shiftName: 'Ca chiều', startTime: '13:30:00', endTime: '17:00:00' };
const mount = (todayShifts: DoctorDashboardShiftDto[]) => {
    vi.mocked(doctorApi.getDashboard).mockResolvedValue({
        success: true, message: '',
        data: {
            currentShift: null, todayShifts, nextPatient: null, upcomingAppointments: [],
            kpis: { totalAppointmentsToday: 6, waitingCount: 0, inConsultationCount: 0, completedCount: 6, noShowCount: 0 },
            todayQueue: [1, 2, 3, 4, 5, 6].map(patient),
        },
    } as never);
    render(<MemoryRouter><DoctorDashboard /></MemoryRouter>);
};

afterEach(cleanup);

describe('Doctor dashboard header shifts', () => {
    it('counts today work shifts, not queued patients, on a Saturday morning-only schedule', async () => {
        mount([morning]);
        expect(await screen.findByText('· 1 ca hôm nay')).toBeInTheDocument();
        expect(screen.getByText('Ca sáng 08:00–11:30')).toBeInTheDocument();
        expect(screen.queryByText(/Ca chiều/)).not.toBeInTheDocument();
        expect(screen.queryByText('· 6 ca hôm nay')).not.toBeInTheDocument();
        expect(screen.getByText('Hàng đợi khám hôm nay (6 bệnh nhân)')).toBeInTheDocument();
    });

    it('lists both shifts on a full working day', async () => {
        mount([morning, afternoon]);
        expect(await screen.findByText('· 2 ca hôm nay')).toBeInTheDocument();
        expect(screen.getByText('Ca sáng 08:00–11:30 · Ca chiều 13:30–17:00')).toBeInTheDocument();
    });

    it('omits the count when there is no shift today', async () => {
        mount([]);
        expect(await screen.findByText('Hôm nay không có ca trực')).toBeInTheDocument();
        expect(screen.queryByText(/ca hôm nay/)).not.toBeInTheDocument();
    });
});
