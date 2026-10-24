import { cleanup, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EarliestSlots } from '../pages/public/landing/EarliestSlots';
import type { LandingDoctor } from '../pages/public/landing/landingTypes';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
const doctors: LandingDoctor[] = [
    { id: 1, fullName: 'Nguyễn Văn An', specialtyId: 2, specialtyName: 'Tim mạch' },
    { id: 2, fullName: 'Trần Thị Bình', specialtyName: 'Nội khoa' },
    { id: 3, fullName: 'Lê Văn Cường', specialtyName: 'Nhi khoa' },
];
const slot = (slotId: number, slotDate: string, startTime: string) => ({ slotId, slotDate, startTime, endTime: '10:30:00' });
const mount = (items = doctors, loading = false) => render(<MemoryRouter><EarliestSlots doctors={items} loading={loading} /></MemoryRouter>);

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-24T01:30:00Z'));
    mocks.get.mockReset();
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('Earliest real available slots', () => {
    it('sorts each doctor’s next future slot, ignores a failed doctor, and links the supported booking parameters', async () => {
        mocks.get.mockImplementation(async (url: string) => {
            if (url.includes('/3/')) throw new Error('Unavailable');
            return { success: true, data: url.includes('/1/')
                ? [slot(13, '2026-10-26', '10:00:00'), slot(11, '2026-10-24', '07:00:00'), slot(12, '2026-10-26', '08:00:00')]
                : [slot(21, '2026-10-24', '09:00:00')] };
        });
        mount();
        await screen.findByText('08:00 · Thứ Hai, 26/10');
        const items = within(screen.getByRole('list')).getAllByRole('listitem');
        expect(items).toHaveLength(2);
        expect(items[0]).toHaveTextContent('Trần Thị Bình');
        expect(items[1]).toHaveTextContent('08:00 · Thứ Hai, 26/10');
        expect(within(items[1]).getByRole('link', { name: 'Đặt giờ này' })).toHaveAttribute('href', '/patient/book?doctorId=1&date=2026-10-26&specialtyId=2');
        expect(mocks.get).toHaveBeenCalledWith('/doctors/1/available-slots', { params: { fromDate: '2026-10-24', toDate: '2026-10-31' } });
    });
    it('shows the required empty message and booking link for empty responses', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [] });
        mount();
        expect(await screen.findByText('Hiện chưa có lịch trống trong 7 ngày tới')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Đặt lịch khám' })).toHaveAttribute('href', '/patient/book');
    });
    it('shows a skeleton before doctor data resolves and requests at most six doctors', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [] });
        const items = Array.from({ length: 8 }, (_, index) => ({ id: index + 1, fullName: `Bác sĩ ${index + 1}` }));
        const view = mount(items, true);
        expect(screen.getByRole('status', { name: 'Đang tải lịch trống' })).toBeInTheDocument();
        expect(mocks.get).not.toHaveBeenCalled();
        view.rerender(<MemoryRouter><EarliestSlots doctors={items} loading={false} /></MemoryRouter>);
        await screen.findByText('Hiện chưa có lịch trống trong 7 ngày tới');
        expect(mocks.get).toHaveBeenCalledTimes(6);
    });
});
