export const appointmentStatusLabels = {
    Pending: 'Chờ xác nhận',
    Confirmed: 'Đã xác nhận',
    PendingReschedule: 'Chờ duyệt đổi lịch',
    PendingCancellation: 'Chờ duyệt hủy',
    Cancelled: 'Đã hủy',
    Completed: 'Đã khám xong',
    NoShow: 'Vắng mặt',
    CheckedIn: 'Đã tiếp nhận',
    InConsultation: 'Đang khám',
    InProgress: 'Đang khám'
} as const;

export const getAppointmentStatusLabel = (status: string): string =>
    Object.hasOwn(appointmentStatusLabels, status) ? appointmentStatusLabels[status as keyof typeof appointmentStatusLabels] : 'Không xác định';
