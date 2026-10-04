import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import * as formatters from '../utils/formatters';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { PatientRevisit } from '../pages/patient/PatientRevisit';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { ChatProvider } from '../contexts/ChatContext';

const mocks = vi.hoisted(() => ({ get: vi.fn(), schedule: vi.fn(), dialog: { showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() } }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getSchedule: mocks.schedule } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => mocks.dialog }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'local-date', role: 'Patient' }, isAuthenticated: true }) }));
const oldTZ = process.env.TZ;
beforeEach(() => {
    process.env.TZ = 'Asia/Ho_Chi_Minh';
    vi.useFakeTimers({ toFake: ['Date'] }); vi.setSystemTime(new Date('2026-10-04T18:30:00Z'));
    mocks.get.mockReset(); mocks.schedule.mockReset();
    mocks.schedule.mockResolvedValue({ success: true, data: [] });
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data:
        url.includes('available-slots') ? [] :
        url === '/specialties' ? [{ id: 1, specialtyName: 'Nội khoa' }] :
        url.includes('/doctors') && !url.includes('available-slots') ? [{ id: 2, fullName: 'Lan', specialtyId: 1 }] :
        url.startsWith('/appointments/my') ? { items: [{ id: 1, appointmentCode: 'LOCAL', status: 'Confirmed', slotDate: '2026-10-05', doctorId: 2, specialtyId: 1, startTime: '08:00', endTime: '08:30' }] } : { items: [] } }));
});
afterEach(() => { vi.useRealTimers(); if (oldTZ === undefined) delete process.env.TZ; else process.env.TZ = oldTZ; });
const mount = (node: React.ReactNode) => render(<MemoryRouter><ChatProvider>{node}</ChatProvider></MemoryRouter>);
it('D1 helper uses the local day at Vietnam 01:30', () => {
    const helper = (formatters as unknown as { toLocalDateString: (date?: Date) => string }).toLocalDateString;
    expect(helper).toBeTypeOf('function');
    expect(helper()).toBe('2026-10-05');
    expect(helper(new Date('2026-12-31T18:30:00Z'))).toBe('2027-01-01');
});
it('D1 schedule requests Monday to Sunday of the local week', async () => {
    await act(async () => mount(<DoctorSchedule />));
    expect(mocks.schedule).toHaveBeenCalledWith('2026-10-05', '2026-10-11');
});
it('D1 booking date minimum is the local day', async () => {
    await act(async () => mount(<BookAppointment />));
    fireEvent.click(screen.getByText('Nội khoa'));
    await act(async () => {});
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
    await act(async () => {});
    fireEvent.click(screen.getByText('Lan'));
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
    await act(async () => {});
    expect(document.querySelector('#slotDate')).toHaveAttribute('min', '2026-10-05');
});
it('D1 reschedule minimum is the next local day', async () => {
    await act(async () => mount(<PatientAppointments />));
    fireEvent.click(screen.getByRole('button', { name: /Đổi lịch/ }));
    await act(async () => {});
    expect(document.querySelector('input[type="date"]')).toHaveAttribute('min', '2026-10-06');
});
it('D2 appointment card displays dd/MM/yyyy', async () => {
    await act(async () => mount(<PatientAppointments />));
    expect(screen.getByText('05/10/2026')).toBeInTheDocument();
});
it('D2 reschedule summary displays dd/MM/yyyy', async () => {
    await act(async () => mount(<PatientAppointments />));
    fireEvent.click(screen.getByRole('button', { name: /Đổi lịch/ }));
    await act(async () => {});
    expect(screen.getByText(/05\/10\/2026 \(08:00 - 08:30\)/)).toBeInTheDocument();
});
it.each(['PendingPatientResponse', 'Accepted'])('D2 revisit %s displays dd/MM/yyyy', async status => {
    mocks.get.mockResolvedValue({ success: true, data: [{ id: 1, status, suggestedDate: '2026-10-05', doctorName: 'Lan' }] });
    await act(async () => mount(<PatientRevisit />));
    expect(screen.getByText(status === 'PendingPatientResponse' ? '05/10/2026' : 'Ngày hẹn: 05/10/2026')).toBeInTheDocument();
});
