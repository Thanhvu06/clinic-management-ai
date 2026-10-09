import type { VisitStatus } from '../types/visit';

export const statusLabels: Record<string, string> & Record<VisitStatus, string> = {
    Pending: 'Chờ xử lý', Confirmed: 'Đã xác nhận', PendingReschedule: 'Chờ đổi lịch', PendingCancellation: 'Chờ hủy',
    Cancelled: 'Đã hủy', Completed: 'Đã hoàn tất', NoShow: 'Không đến', CheckedIn: 'Đã tiếp nhận', Registered: 'Đã đăng ký',
    WaitingForDoctor: 'Chờ bác sĩ', WaitingDoctor: 'Chờ bác sĩ', InConsultation: 'Đang khám',
    WaitingForDiagnostics: 'Chờ cận lâm sàng', InDiagnostics: 'Đang làm cận lâm sàng', ResultsReady: 'Đã có kết quả',
    Transferred: 'Đã chuyển', ConsultationCompleted: 'Đã kết thúc khám', InPharmacy: 'Đang tại quầy thuốc', InBilling: 'Đang thanh toán',
    Draft: 'Bản nháp', Issued: 'Đã phát hành', Dispensed: 'Đã cấp phát', ReservedForPurchase: 'Đã giữ thuốc',
    Ordered: 'Đã chỉ định', InProgress: 'Đang thực hiện', Unpaid: 'Chưa thanh toán', Paid: 'Đã thanh toán',
    PartiallyPaid: 'Thanh toán một phần', Succeeded: 'Thành công', Voided: 'Đã hủy giao dịch'
};

export const getVisitStatusLabel = (value: string): string =>
    Object.hasOwn(statusLabels, value) ? statusLabels[value] : 'Không xác định';
