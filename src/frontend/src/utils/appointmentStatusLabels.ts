export const appointmentStatusLabels: Readonly<Record<string, string>> = {
    Pending: 'Chờ xác nhận',
    Confirmed: 'Chờ tiếp nhận',
    PendingReschedule: 'Chờ duyệt đổi lịch',
    PendingCancellation: 'Chờ duyệt hủy',
    Cancelled: 'Đã hủy',
    Completed: 'Đã khám xong',
    NoShow: 'Vắng mặt',
    CheckedIn: 'Đã tiếp nhận',
    InConsultation: 'Đang khám'
};

export const getAppointmentStatusLabel = (status: string): string =>
    Object.hasOwn(appointmentStatusLabels, status) ? appointmentStatusLabels[status] : 'Không xác định';
