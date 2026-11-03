import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { doctorApi } from '../api/doctorApi';
import { DialogProvider } from '../contexts/DialogContext';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';

vi.mock('../api/doctorApi', () => ({ doctorApi: { getDashboard: vi.fn() } }));

it('pads the day in the upcoming appointments date column', async () => {
    vi.mocked(doctorApi.getDashboard).mockResolvedValue({
        success: true,
        message: '',
        data: {
            kpis: { totalAppointmentsToday: 0, waitingCount: 0, inConsultationCount: 0, completedCount: 0, noShowCount: 0 },
            todayQueue: [],
            upcomingAppointments: [{
                appointmentId: 1,
                appointmentCode: 'APT-261104-00000001',
                patientId: 1,
                patientName: 'Bệnh nhân hẹn khám',
                patientPhone: '0900000000',
                appointmentDate: '2026-11-04',
                startTime: '08:00:00',
                endTime: '08:30:00',
                status: 'Confirmed',
                queueOrder: 1,
                isVitalsRecorded: false,
            }],
        },
    });

    render(
        <MemoryRouter>
            <DialogProvider>
                <DoctorDashboard />
            </DialogProvider>
        </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { name: 'Lịch khám 7 ngày tới (1 bệnh nhân)' })).toBeInTheDocument();
    const dateHeader = screen.getByRole<HTMLTableCellElement>('columnheader', { name: 'Ngày khám' });
    const row = screen.getByRole('row', { name: /APT-261104-00000001/ });
    expect(within(row).getAllByRole('cell')[dateHeader.cellIndex]).toHaveTextContent(/^04\/11\/2026$/);
});
