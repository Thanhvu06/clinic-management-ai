export const auditActionLabels: Readonly<Record<string, string>> = {
    CREATE_INVOICE: 'Lập hóa đơn',
    PROCESS_PAYMENT: 'Thu tiền',
    CANCEL_INVOICE: 'Hủy hóa đơn',
    UPDATE_SPECIALTY_FEE: 'Cập nhật phí khám',
    REGISTER_HEALTH_PACKAGE: 'Đăng ký gói khám',
    CANCEL_PACKAGE_REGISTRATION: 'Hủy đăng ký gói khám',
    CONFIRM_PACKAGE_REGISTRATION: 'Xác nhận đăng ký gói khám',
    RECEPTION_CANCEL_PACKAGE_REGISTRATION: 'Lễ tân hủy đăng ký gói khám',
    PrescriptionSafetyOverridden: 'Xác nhận ngoại lệ an toàn đơn thuốc',
    DiagnosticOrderCreated: 'Tạo phiếu chỉ định',
    DiagnosticOrderReviewed: 'Bác sĩ đã xem kết quả',
    DiagnosticOrderCancelled: 'Hủy phiếu chỉ định',
    DiagnosticOrderStarted: 'Bắt đầu thực hiện CLS',
    DiagnosticCriticalValueNotified: 'Thông báo giá trị nguy cấp',
    DiagnosticResultRecorded: 'Ghi kết quả CLS',
    DiagnosticResultsSubmittedForVerification: 'Gửi duyệt kết quả CLS',
    DiagnosticResultsVerified: 'Duyệt kết quả CLS',
    DiagnosticResultsReturned: 'Trả lại kết quả CLS',
    DiagnosticOrderCompleted: 'Hoàn tất phiếu chỉ định',
    DiagnosticAttachmentUploaded: 'Tải lên tệp kết quả CLS',
    DiagnosticAttachmentDeleted: 'Xóa tệp kết quả CLS',
    SpecimenCollected: 'Lấy mẫu',
    SpecimenReceived: 'Nhận mẫu',
    SpecimenRejected: 'Từ chối mẫu',
    InitializeAppointments: 'Khởi tạo dữ liệu lịch hẹn',
};

export const auditEntityLabels: Readonly<Record<string, string>> = {
    Payment: 'Thanh toán',
    Invoice: 'Hóa đơn',
    Specialty: 'Chuyên khoa',
    HealthPackageRegistration: 'Đăng ký gói khám',
    Prescription: 'Đơn thuốc',
    DiagnosticOrder: 'Phiếu chỉ định',
    DiagnosticOrderItem: 'Dịch vụ CLS',
    DevelopmentData: 'Dữ liệu hệ thống',
};

export const getAuditActionLabel = (code: string): string =>
    Object.hasOwn(auditActionLabels, code) ? auditActionLabels[code] : code;

export const getAuditEntityLabel = (code: string): string =>
    Object.hasOwn(auditEntityLabels, code) ? auditEntityLabels[code] : code;
