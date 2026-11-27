import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';

export interface BookingSlot {
    slotId: number;
    doctorId: number;
    slotDate: string;
    startTime: string;
    endTime: string;
}

const maxSlotRangeDays = 31;

export async function loadBookingSlots(doctorId: number, specialtyId: number | '', date: string, findDefaultDate: boolean) {
    const specialty = specialtyId ? `&specialtyId=${specialtyId}` : '';
    const load = async (fromDate: string, toDate: string) => {
        const response = await axiosClient.get<unknown, ApiResponse<BookingSlot[]>>(
            `/doctors/${doctorId}/available-slots?fromDate=${fromDate}&toDate=${toDate}${specialty}`
        );
        if (!response.success || !response.data) throw new Error(response.message);
        return response.data;
    };
    if (findDefaultDate) {
        const today = new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' });
        const end = new Date(`${today}T00:00:00Z`);
        end.setUTCDate(end.getUTCDate() + maxSlotRangeDays);
        const lastDate = end.toISOString().slice(0, 10);
        const slots = await load(today, lastDate);
        const first = slots.reduce<string | undefined>((earliest, slot) => {
            const day = slot.slotDate;
            return day >= today && day <= lastDate && (!earliest || day < earliest) ? day : earliest;
        }, undefined);
        if (first) return { date: first, slots: slots.filter(slot => slot.slotDate === first) };
    }
    return { date, slots: await load(date, date) };
}
