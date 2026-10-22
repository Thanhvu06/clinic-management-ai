import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import type { DoctorScheduleDayDto } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn() })
}));
afterEach(() => vi.useRealTimers());

it('requests the weekly schedule using controller query names and renders backend DTO fields', async () => {
    vi.useFakeTimers({ toFake: ['Date'], shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-10-21T03:00:00Z'));
    const days: DoctorScheduleDayDto[] = [{
        date: '2026-10-19', dayOfWeekName: 'Thứ Hai',
        shifts: [{
            scheduleId: 71, shiftName: 'Ca sáng', startTime: '09:00:00', endTime: '12:00:00',
            isActive: true, totalSlots: 2, bookedSlots: 1, availableSlots: 1,
            slots: [
                { slotId: 81, slotDate: '2026-10-19', startTime: '09:00:00', endTime: '09:30:00',
                    isBooked: true, appointmentId: 91, appointmentCode: 'APT-SCHEDULE-91', patientName: 'Người bệnh lịch trực', status: 'Confirmed' },
                { slotId: 82, slotDate: '2026-10-19', startTime: '09:30:00', endTime: '10:00:00',
                    isBooked: false, appointmentId: null, appointmentCode: null, patientName: null, status: null }
            ]
        }]
    }];
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, message: '', data: days });

    render(<MemoryRouter><DoctorSchedule /></MemoryRouter>);

    expect(await screen.findByText('Ca sáng: 09:00 - 12:00')).toBeInTheDocument();
    expect(axiosClient.get).toHaveBeenCalledWith('/doctor/schedule?fromDate=2026-10-19&toDate=2026-10-25');
    for (const [url] of vi.mocked(axiosClient.get).mock.calls) {
        expect(url).not.toContain('startDate=');
        expect(url).not.toContain('endDate=');
    }
    expect(screen.getByText('19/10')).toBeInTheDocument();
    expect(screen.getByText('09:00 - 09:30')).toBeInTheDocument();
    expect(screen.getByText('Đã đặt')).toBeInTheDocument();
    expect(screen.getByText('APT-SCHEDULE-91')).toBeInTheDocument();
    expect(screen.getByText('Người bệnh lịch trực')).toBeInTheDocument();
    expect(screen.getByText('Trống')).toBeInTheDocument();
    expect(screen.queryByText(/01\/01\/0001/)).not.toBeInTheDocument();
});
