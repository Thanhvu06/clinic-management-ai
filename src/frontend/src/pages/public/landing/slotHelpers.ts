import { formatDateOnly } from '../../../utils/formatters';
import type { LandingSlot } from './landingTypes';

export function vietnamDate(now = new Date()): string {
    return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}

export function slotRange(now = new Date()) {
    const fromDate = vietnamDate(now);
    const end = new Date(`${fromDate}T00:00:00+07:00`);
    end.setUTCDate(end.getUTCDate() + 7);
    return { fromDate, toDate: vietnamDate(end) };
}

export function slotTimestamp(slot: LandingSlot): number {
    return Date.parse(`${slot.slotDate.slice(0, 10)}T${slot.startTime}+07:00`);
}

export function slotLabel(slot: LandingSlot): string {
    const weekday = new Intl.DateTimeFormat('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh', weekday: 'long' }).format(new Date(slotTimestamp(slot)));
    const title = weekday.charAt(0).toLocaleUpperCase('vi-VN') + weekday.slice(1);
    return `${slot.startTime.slice(0, 5)} · ${title}, ${formatDateOnly(slot.slotDate).slice(0, 5)}`;
}
