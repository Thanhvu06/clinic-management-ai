import { act, cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { PatientAppointments } from '../pages/patient/PatientAppointments';

const get = vi.hoisted(() => vi.fn());
vi.mock('../api/axiosClient', () => ({ default: { get } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));

const items = [
    { id: 1, appointmentCode: 'LATE', status: 'Confirmed', slotDate: '2027-01-02', startTime: '10:00', endTime: '10:30' }
];

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    get.mockImplementation(async (url: string) => ({ success: true, data: url.startsWith('/appointments/my') ? { items } : url === '/specialties' ? [] : { items: [] } }));
    vi.spyOn(HTMLElement.prototype, 'scrollIntoView').mockImplementation(() => {});
});

afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
});

it('cancels the deep-link scroll timer when unmounted before 150ms', async () => {
    const view = render(<MemoryRouter initialEntries={['/patient/appointments?tab=upcoming&appointmentId=1']}><PatientAppointments /></MemoryRouter>);
    await act(async () => {});
    expect(screen.getByText('LATE')).toBeInTheDocument();
    const getElementById = vi.spyOn(document, 'getElementById');
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();

    view.unmount();
    await act(async () => { vi.advanceTimersByTime(200); });

    expect(getElementById).not.toHaveBeenCalledWith('appointment-card-1');
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
});

it('scrolls to the deep-linked appointment after the delay while mounted', async () => {
    render(<MemoryRouter initialEntries={['/patient/appointments?tab=upcoming&appointmentId=1']}><PatientAppointments /></MemoryRouter>);
    await act(async () => {});
    expect(screen.getByText('LATE')).toBeInTheDocument();
    const getElementById = vi.spyOn(document, 'getElementById');
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();

    await act(async () => { vi.advanceTimersByTime(200); });

    expect(getElementById).toHaveBeenCalledWith('appointment-card-1');
    expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'center' });
});
