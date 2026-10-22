const actionLabels: Readonly<Record<string, string>> = {
    Created: 'Tạo mới', StatusChanged: 'Cập nhật trạng thái', Cancelled: 'Hủy lịch',
    Rescheduled: 'Đổi lịch', Confirmed: 'Xác nhận', Completed: 'Hoàn thành',
    NoShow: 'Đánh dấu vắng', RescheduleRequested: 'Yêu cầu đổi lịch', CancelRequested: 'Yêu cầu hủy',
    CheckedIn: 'Tiếp nhận', InConsultation: 'Bắt đầu khám', RevisitCreated: 'Tạo lịch tái khám'
};

export const getAppointmentHistoryActionLabel = (action: string): string =>
    Object.hasOwn(actionLabels, action) ? actionLabels[action] : action;
