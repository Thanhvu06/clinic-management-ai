export interface Specialty {
    id: number;
    specialtyCode: string;
    specialtyName: string;
    description: string;
    aiEnabled: boolean;
    consultationFee?: number;
}
export interface Doctor {
    id: number;
    fullName: string;
    academicTitle?: string;
    experienceYears: number;
}
export interface RecommendedDoctor {
    doctorId: number;
    fullName: string;
    experienceYears: number;
    experienceScore: number;
    availabilityScore: number;
    totalScore: number;
    earliestAvailableSlot: string | null;
}
export interface ApiResponse<T> { success: boolean; data: T; message?: string; }

const slotFormatter = new Intl.DateTimeFormat('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh', weekday: 'long', day: '2-digit', month: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
});
const bookingDateFormatter = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit',
});
export function bookingDate(slot: string): string {
    const parts = bookingDateFormatter.formatToParts(new Date(slot));
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value;
    return `${part('year')}-${part('month')}-${part('day')}`;
}
export function slotLabel(slot: string): string {
    const parts = slotFormatter.formatToParts(new Date(slot));
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value;
    return `${part('weekday')}, ${part('day')}/${part('month')}, ${part('hour')}:${part('minute')}`;
}
