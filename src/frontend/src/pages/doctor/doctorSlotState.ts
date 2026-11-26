import type { DoctorScheduleSlotDto } from '../../types';

export type DoctorSlotState = 'Booked' | 'NoShow' | 'Past' | 'Free';

export const SLOT_STATE_LABEL: Record<DoctorSlotState, string> = {
    Booked: 'Đã đặt',
    NoShow: 'Vắng mặt',
    Past: 'Đã qua',
    Free: 'Trống',
};

export const slotStateOf = (slot: DoctorScheduleSlotDto): DoctorSlotState => {
    if (slot.slotState === 'Booked' || slot.slotState === 'NoShow' || slot.slotState === 'Past' || slot.slotState === 'Free') {
        return slot.slotState;
    }
    if (slot.status === 'NoShow') return 'NoShow';
    if (slot.isBooked) return 'Booked';
    return slot.isPast ? 'Past' : 'Free';
};

export const isSlotTaken = (slot: DoctorScheduleSlotDto): boolean => {
    const state = slotStateOf(slot);
    return state === 'Booked' || state === 'NoShow';
};
