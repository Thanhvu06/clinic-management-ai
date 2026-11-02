import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { PatientAppointments } from '../pages/patient/PatientAppointments';
import { ChatProvider } from '../contexts/ChatContext';

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), showAlert: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'hours-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.showAlert }) }));
vi.mock('../hooks/useAiBookingFlow', async importOriginal => ({ ...await importOriginal<typeof import('../hooks/useAiBookingFlow')>(),
    useAiBookingFlow: () => ({ input: '', setInput: vi.fn(), loading: false, submittingBooking: false, errorMsg: '', messages: [], activeDraft: null,
        clearChat: vi.fn(), handleSendMessage: vi.fn(), handleSuggestion: vi.fn(), wizard: null, handleWizardStep: vi.fn(), handleActionClick: vi.fn() }) }));
const sunday = '2030-01-06';
const slot = { slotId: 10, doctorId: 2, slotDate: sunday, startTime: '23:00', endTime: '23:30' };
const ok = (data: unknown) => ({ success: true, data });
const mount = (node: React.ReactNode, route = '/') => render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><ChatProvider>{node}</ChatProvider></MemoryRouter></ConfigProvider>);
beforeEach(() => {
    vi.clearAllMocks(); localStorage.clear(); sessionStorage.clear();
    mocks.get.mockImplementation(async (url: string) => url === '/specialties' ? ok([{ id: 1, specialtyName: 'Nội khoa', aiEnabled: true }])
        : url.includes('available-slots') ? ok([slot]) : url.startsWith('/appointments/my') ? ok({ items: [{ id: 1, appointmentCode: 'HOURS', doctorId: 2, specialtyId: 1,
            appointmentDate: '2030-01-05', startTime: '10:00', endTime: '10:30', status: 'Confirmed' }], totalItems: 1 })
            : ok([{ id: 2, fullName: 'Nguyễn Minh', specialtyId: 1 }]));
});
afterEach(cleanup);

it('keeps a Sunday URL and shows the late slot returned by the operating policy', async () => {
    mount(<BookAppointment />, `/?specialtyId=1&doctorId=2&date=${sunday}`);
    fireEvent.click(await screen.findByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
    fireEvent.click(await screen.findByRole('button', { name: /Nguyễn Minh/ }));
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
    expect(await screen.findByRole('button', { name: '23:00' })).toBeInTheDocument();
    expect(screen.getByLabelText('Chọn ngày khám')).toHaveValue(sunday);
    expect(mocks.showAlert).not.toHaveBeenCalled();
});

it('requests Sunday reschedule slots from the server', async () => {
    mount(<PatientAppointments />);
    fireEvent.click(await screen.findByRole('button', { name: /Đổi lịch/ }));
    const date = document.querySelector('input[type="date"]');
    expect(date).not.toBeNull();
    fireEvent.change(date!, { target: { value: sunday } });
    await waitFor(() => expect(mocks.get.mock.calls.some(([url]) => String(url).includes(`fromDate=${sunday}`))).toBe(true));
    expect(await screen.findByText(/23:00/)).toBeInTheDocument();
});
