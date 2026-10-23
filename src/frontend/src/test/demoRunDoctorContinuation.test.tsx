import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { DoctorQueue } from '../pages/doctor/DoctorQueue';
import { doctorApi } from '../api/doctorApi';
import { getAppointmentStatusLabel } from '../utils/appointmentStatusLabels';
import { getVisitStatusLabel } from '../utils/visitStatusLabels';

const navigate = vi.hoisted(() => vi.fn());
vi.mock('react-router-dom', async importOriginal => ({ ...await importOriginal<typeof import('react-router-dom')>(), useNavigate: () => navigate }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getDashboard: vi.fn(), getQueue: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }) }));

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

describe.each([DoctorDashboard, DoctorQueue])('Continue consultation on $name', Component => {
    it.each(['InConsultation', 'WaitingForDiagnostics', 'InDiagnostics', 'ResultsReady'])('resumes a visit with status %s', async status => {
        const item = { patientVisitId: 8, appointmentId: null, appointmentCode: 'VIS-8', patientName: 'Lê Thị Lan', patientPhone: '0901234567',
            patientGender: 'Female', status, startTime: '08:00', endTime: '08:30', appointmentDate: '2026-10-23' };
        vi.mocked(doctorApi.getQueue).mockResolvedValue({ success: true, data: [item], message: '' } as never);
        vi.mocked(doctorApi.getDashboard).mockResolvedValue({ success: true, data: { queue: [item], inConsultationCount: 1 }, message: '' } as never);
        render(<MemoryRouter><Component /></MemoryRouter>);
        const button = await screen.findByRole('button', { name: 'Tiếp tục khám' });
        fireEvent.click(button);
        expect(navigate).toHaveBeenCalledWith('/doctor/visits/8/examination');
    });

    it('labels completed appointments consistently', async () => {
        const item = { appointmentId: 9, appointmentCode: 'APT-9', patientName: 'Lê Thị Lan', patientPhone: '0901234567',
            patientGender: 'Female', status: 'Completed', startTime: '08:00', endTime: '08:30', appointmentDate: '2026-10-23' };
        vi.mocked(doctorApi.getQueue).mockResolvedValue({ success: true, data: [item], message: '' } as never);
        vi.mocked(doctorApi.getDashboard).mockResolvedValue({ success: true, data: { queue: [item] }, message: '' } as never);
        render(<MemoryRouter><Component /></MemoryRouter>);
        expect((await screen.findAllByText('Đã khám xong')).length).toBeGreaterThan(0);
        expect(getVisitStatusLabel('Completed')).toBe(getAppointmentStatusLabel('Completed'));
        expect(screen.queryByText('Hoàn thành')).not.toBeInTheDocument();
    });
});
