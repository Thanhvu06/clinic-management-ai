const shortWeekdays = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'];
const longWeekdays = ['Chủ nhật', 'Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];

const parts = (date: string) => {
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(date);
    if (!match) return null;
    const [, year, month, day] = match;
    // Lấy thứ theo ngày lịch trong chuỗi, không theo múi giờ trình duyệt; API trả dayOfWeek bằng tiếng Anh.
    return { day, month, weekday: new Date(Date.UTC(Number(year), Number(month) - 1, Number(day))).getUTCDay() };
};

export const formatScheduleDayShort = (date: string): string => {
    const value = parts(date);
    return value ? `${shortWeekdays[value.weekday]} ${value.day}/${value.month}` : date;
};

export const formatScheduleWeekday = (date: string): string => {
    const value = parts(date);
    return value ? longWeekdays[value.weekday] : '';
};

export const formatFreeSlotCount = (count: number): string => count > 0 ? `${count} giờ trống` : 'Hết chỗ';

export const formatTimeRange = (start: string, end: string): string => `${start.substring(0, 5)}–${end.substring(0, 5)}`;
