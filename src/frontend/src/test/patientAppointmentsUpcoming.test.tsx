import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
const get = vi.hoisted(() => vi.fn());
vi.mock('../api/axiosClient', () => ({ default: { get } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));
const items = [
    { id: 1, appointmentCode: 'LATE', status: 'Confirmed', slotDate: '2027-01-02', startTime: '10:00', endTime: '10:30' },
    { id: 2, appointmentCode: 'EARLY', status: 'Confirmed', slotDate: '2027-01-01', startTime: '08:00', endTime: '08:30' },
    { id: 3, appointmentCode: 'PAST', status: 'Completed', slotDate: '2026-01-01', startTime: '08:00' }
];
beforeEach(() => { get.mockImplementation(async (url: string) => ({ success: true, data: url.startsWith('/appointments/my') ? { items } : url === '/specialties' ? [] : { items: [] } })); HTMLElement.prototype.scrollIntoView = vi.fn(); });
it('opens upcoming, sorts chronologically and retains the requested highlight', async () => {
    render(<MemoryRouter initialEntries={['/patient/appointments?tab=upcoming&appointmentId=1']}><PatientAppointments /></MemoryRouter>);
    await screen.findByText('#LATE');
    expect(screen.getByRole('button', { name: 'Sắp tới' })).toHaveClass('active');
    expect(screen.queryByText('#PAST')).not.toBeInTheDocument();
    const cards = [...document.querySelectorAll('[id^="appointment-card-"]')];
    expect(cards.map(c => c.id)).toEqual(['appointment-card-2', 'appointment-card-1']);
    await waitFor(() => expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalled());
});
it('opens the past query tab', async () => {
    render(<MemoryRouter initialEntries={['/patient/appointments?tab=past']}><PatientAppointments /></MemoryRouter>);
    await screen.findByText('#PAST');
    expect(screen.queryByText('#LATE')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Đã qua' })).toHaveClass('active');
});
it('refreshes an already open appointment page after a success-card deep link', async () => {
    const Link = () => { const navigate = useNavigate(); return <button onClick={() => navigate('/patient/appointments?tab=upcoming&appointmentId=4')}>Xem lịch vừa đặt</button>; };
    render(<MemoryRouter initialEntries={['/patient/appointments']}><Link /><PatientAppointments /></MemoryRouter>);
    await screen.findByText('#PAST');
    get.mockImplementation(async (url: string) => ({ success: true, data: url.startsWith('/appointments/my') ? { items: [...items, { id: 4, appointmentCode: 'NEW', status: 'Confirmed', slotDate: '2027-01-03', startTime: '09:00', endTime: '09:30' }] } : url === '/specialties' ? [] : { items: [] } }));
    fireEvent.click(screen.getByRole('button', { name: 'Xem lịch vừa đặt' }));
    await screen.findByText('#NEW');
    expect(screen.getByRole('button', { name: 'Sắp tới' })).toHaveClass('active');
    expect(screen.queryByText('#PAST')).not.toBeInTheDocument();
});
