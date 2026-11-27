// The server re-checks the stored status; this only picks the endpoint and the wording.
export const cancelsImmediately = (appointment: { status: string } | null | undefined): boolean =>
    appointment?.status === 'Pending';

export const cancelDialogCopy = (appointment: { status: string } | null | undefined) => cancelsImmediately(appointment)
    ? {
        notice: 'Lịch hẹn đang chờ xác nhận nên sẽ được hủy ngay và giải phóng khung giờ, không cần chờ lễ tân duyệt. Bạn có chắc chắn muốn hủy lịch khám này?',
        confirmLabel: 'Xác nhận hủy lịch'
    }
    : {
        notice: 'Yêu cầu hủy sẽ được gửi đến lễ tân duyệt. Bạn có chắc chắn muốn yêu cầu hủy lịch khám này?',
        confirmLabel: 'Xác nhận gửi yêu cầu'
    };
