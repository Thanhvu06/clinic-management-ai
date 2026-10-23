export const appointmentStatusLabels: Readonly<Record<string, string>> = {
    Pending: 'Chờ xác nhận',
    Confirmed: 'Chờ tiếp nhận',
    PendingReschedule: 'Chờ duyệt đổi lịch',
    PendingCancellation: 'Chờ duyệt hủy',
    Cancelled: 'Đã hủy',
    Completed: 'Đã khám xong',
    NoShow: 'Vắng mặt',
    CheckedIn: 'Đã tiếp nhận',
    InConsultation: 'Đang khám',
    InProgress: 'Đang khám'
};

export const getAppointmentStatusLabel = (status: string, audience: 'staff' | 'patient' = 'staff'): string => {
    if (status === 'Confirmed' && audience === 'patient') return 'Đã xác nhận';
    return Object.hasOwn(appointmentStatusLabels, status) ? appointmentStatusLabels[status] : 'Không xác định';
};
