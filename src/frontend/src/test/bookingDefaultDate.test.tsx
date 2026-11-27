import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { BookAppointment } from '../pages/patient/BookAppointment';
import { ChatProvider } from '../contexts/ChatContext';
import { getNextWorkingDateString } from '../utils/doctorNameHelper';

const mocks = vi.hoisted(() => ({ get: vi.fn(), showAlert: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'date-patient', role: 'Patient' }, isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.showAlert }) }));

const today = '2026-11-27';
const available = (slotDate: string, startTime: string) => ({ slotId: Number(slotDate.slice(-2)), doctorId: 2, slotDate, startTime, endTime: '22:30' });
type Slot = ReturnType<typeof available>;
const success = (data: unknown) => ({ success: true, data });

function mockSlots(slots: Slot[], holdRange?: Promise<Slot[]>) {
    mocks.get.mockImplementation(async (url: string) => {
        if (url === '/appointments/online-eligibility') return success({ isBlocked: false });
        if (url === '/specialties') return success([{ id: 1, specialtyName: 'Nội tổng quát' }]);
        if (url === '/specialties/1/doctors') return success([{ id: 2, fullName: 'Nguyễn Minh Khải', specialtyId: 1 }]);
        const params = new URL(url, 'http://localhost').searchParams;
        const from = params.get('fromDate')!;
        const to = params.get('toDate')!;
        if (from !== to && holdRange) return success(await holdRange);
        return success(slots.filter(slot => slot.slotDate >= from && slot.slotDate <= to));
    });
}

async function chooseDoctor() {
    render(<MemoryRouter initialEntries={['/patient/book']}><ChatProvider><BookAppointment /></ChatProvider></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Nội tổng quát' }));
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn bác sĩ/ }));
    fireEvent.click(await screen.findByRole('button', { name: /Nguyễn Minh Khải/ }));
    fireEvent.click(screen.getByRole('button', { name: /Tiếp tục: Chọn giờ khám/ }));
    return screen.getByLabelText('Chọn ngày khám');
}

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    mocks.get.mockReset();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-26T18:30:00Z'));
});
afterEach(() => {
    cleanup();
    vi.useRealTimers();
});

it('uses today in Vietnam when the server returns a bookable slot for today', async () => {
    mockSlots([available('2026-11-29', '08:00'), available(today, '16:30')]);
    const date = await chooseDoctor();
    expect(await screen.findByRole('button', { name: '16:30' })).toHaveAttribute('aria-pressed', 'false');
    expect(date).toHaveValue(today);
    expect(screen.queryByRole('button', { name: '08:00' })).not.toBeInTheDocument();
    expect(mocks.get).toHaveBeenCalledWith('/doctors/2/available-slots?fromDate=2026-11-27&toDate=2026-12-28&specialtyId=1');
});

it('skips multiple empty days and shows the earliest returned day', async () => {
    mockSlots([available('2026-12-03', '08:00'), available('2026-12-01', '17:00')]);
    const date = await chooseDoctor();
    expect(await screen.findByRole('button', { name: '17:00' })).toBeInTheDocument();
    expect(date).toHaveValue('2026-12-01');
});

it('keeps the previous default and the date picker when the whole range is empty', async () => {
    mockSlots([]);
    const date = await chooseDoctor();
    expect(await screen.findByText(/Bác sĩ chưa có lịch trống/)).toBeInTheDocument();
    expect(date).toHaveValue(getNextWorkingDateString());
    fireEvent.change(date, { target: { value: '2026-12-05' } });
    expect(await screen.findByText(/Bác sĩ chưa có lịch trống ngày 05\/12\/2026/)).toBeInTheDocument();
});

it('keeps a manual date when the earlier availability request finishes afterwards', async () => {
    let finishRange!: (slots: Slot[]) => void;
    const range = new Promise<Slot[]>(resolve => { finishRange = resolve; });
    mockSlots([available('2026-12-05', '17:00')], range);
    const date = await chooseDoctor();
    fireEvent.change(date, { target: { value: '2026-12-05' } });
    expect(await screen.findByRole('button', { name: '17:00' })).toBeInTheDocument();
    await act(async () => { finishRange([available(today, '16:30')]); });
    expect(date).toHaveValue('2026-12-05');
    expect(screen.queryByRole('button', { name: '16:30' })).not.toBeInTheDocument();
});
