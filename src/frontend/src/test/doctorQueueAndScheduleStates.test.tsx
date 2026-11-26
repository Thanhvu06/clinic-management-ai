import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { DoctorWeekSchedule } from '../pages/doctor/DoctorWeekSchedule';
import { DialogProvider } from '../contexts/DialogContext';
import { doctorApi } from '../api/doctorApi';
import { isSlotTaken, slotStateOf } from '../pages/doctor/doctorSlotState';
import type { DoctorScheduleDayDto, DoctorScheduleSlotDto } from '../types';

vi.mock('../api/doctorApi', () => ({
    doctorApi: { getDashboard: vi.fn(), checkInAppointment: vi.fn(), startConsultation: vi.fn() }
}));

const queueRow = (id: number, status: string) => ({
    appointmentId: id, appointmentCode: `APT-Q-${id}`, patientId: id, patientName: `Người bệnh ${id}`, patientPhone: '0900000000',
    patientGender: 'Female', patientDob: '1990-02-03', patientAge: 36, reason: 'Khám', appointmentDate: '2026-11-26',
    startTime: '08:00:00', endTime: '08:30:00', status, queueOrder: id,
});

describe('doctor queue (H2)', () => {
    it('counts exactly the rows the backend queue returns and keeps no-show rows visible', async () => {
        vi.mocked(doctorApi.getDashboard).mockResolvedValue({ success: true, message: '', data: {
            totalToday: 2, todayShifts: [], currentShift: null, nextPatient: null,
            queue: [queueRow(1, 'CheckedIn'), queueRow(2, 'NoShow')], upcomingAppointments: [],
        } } as never);
        render(<MemoryRouter><DialogProvider><DoctorDashboard /></DialogProvider></MemoryRouter>);
        expect(await screen.findByText('Hàng đợi khám hôm nay (2 bệnh nhân)')).toBeInTheDocument();
        expect(screen.getByText('APT-Q-2')).toBeInTheDocument();
        expect(screen.queryByText('Đã hủy')).not.toBeInTheDocument();
    });
});

const slot = (over: Partial<DoctorScheduleSlotDto>): DoctorScheduleSlotDto => ({
    slotId: 1, slotDate: '2026-11-26', startTime: '00:00:00', endTime: '00:30:00', isBooked: false, ...over,
});

describe('doctor schedule slot states (H3)', () => {
    it('derives states from the backend slotState and falls back for older payloads', () => {
        expect(slotStateOf(slot({ slotState: 'Past', isPast: true }))).toBe('Past');
        expect(slotStateOf(slot({ slotState: 'NoShow', isBooked: true, status: 'NoShow' }))).toBe('NoShow');
        expect(slotStateOf(slot({ status: 'NoShow' }))).toBe('NoShow');
        expect(slotStateOf(slot({ isPast: true }))).toBe('Past');
        expect(slotStateOf(slot({ isBooked: true }))).toBe('Booked');
        expect(slotStateOf(slot({}))).toBe('Free');
        expect(isSlotTaken(slot({ slotState: 'NoShow' }))).toBe(true);
        expect(isSlotTaken(slot({ slotState: 'Past', isPast: true }))).toBe(false);
    });

    it('labels past, no-show, booked and free slots and counts no-shows as booked', () => {
        const days: DoctorScheduleDayDto[] = [{
            date: '2026-11-26', dayOfWeekName: 'Thứ Năm',
            shifts: [{
                scheduleId: 9, shiftName: 'Ca đêm', startTime: '00:00:00', endTime: '06:00:00', isActive: true,
                totalSlots: 4, bookedSlots: 2, availableSlots: 2,
                slots: [
                    slot({ slotId: 1, startTime: '00:00:00', endTime: '00:30:00', isBooked: true, isPast: true, slotState: 'NoShow',
                        status: 'NoShow', appointmentCode: 'APT-NOSHOW', patientName: 'Người vắng mặt' }),
                    slot({ slotId: 2, startTime: '00:30:00', endTime: '01:00:00', isPast: true, slotState: 'Past' }),
                    slot({ slotId: 3, startTime: '05:00:00', endTime: '05:30:00', isBooked: true, slotState: 'Booked',
                        status: 'Confirmed', appointmentCode: 'APT-BOOKED', patientName: 'Người đã đặt' }),
                    slot({ slotId: 4, startTime: '05:30:00', endTime: '06:00:00', slotState: 'Free' }),
                ],
            }],
        }];
        render(<DoctorWeekSchedule days={days} />);
        const day = screen.getByTestId('schedule-day-2026-11-26');
        expect(within(day).getByText('2/4 khung giờ đã được đặt')).toBeInTheDocument();
        const slotOf = (time: string) => within(day).getByText(time).closest('[data-slot-state]') as HTMLElement;
        expect(within(slotOf('00:00 - 00:30')).getByText('Vắng mặt')).toBeInTheDocument();
        expect(within(slotOf('00:00 - 00:30')).getByText('APT-NOSHOW')).toBeInTheDocument();
        expect(within(slotOf('00:30 - 01:00')).getByText('Đã qua')).toBeInTheDocument();
        expect(within(slotOf('05:00 - 05:30')).getByText('Đã đặt')).toBeInTheDocument();
        expect(within(slotOf('05:30 - 06:00')).getByText('Trống')).toBeInTheDocument();
        expect(within(day).getAllByText('Trống')).toHaveLength(1);
    });
});
