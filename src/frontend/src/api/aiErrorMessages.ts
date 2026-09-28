type ErrorRecord = Record<string, unknown>;

const asRecord = (value: unknown): ErrorRecord =>
    value && typeof value === 'object' ? value as ErrorRecord : {};

const readString = (value: unknown): string => typeof value === 'string' ? value : '';

const readStatus = (error: unknown): number | undefined => {
    const record = asRecord(error);
    const response = asRecord(record.response);
    const status = record.status ?? record.httpStatus ?? response.status;
    return typeof status === 'number' && Number.isInteger(status) ? status : undefined;
};

const readCode = (error: unknown): string => {
    const record = asRecord(error);
    return readString(record.errorCode || record.code || asRecord(record.response).errorCode).toUpperCase();
};

const readMessage = (error: unknown): string => {
    const record = asRecord(error);
    return readString(record.message || asRecord(record.response).message);
};

export const aiChatFailureMessage = (error: unknown, surface: 'booking' | 'copilot' = 'copilot'): string => {
    const status = readStatus(error);
    const code = readCode(error);
    const message = readMessage(error).toLowerCase();
    const preservedState = surface === 'booking'
        ? 'Bản nháp đặt lịch vẫn được giữ nguyên.'
        : 'Không có thao tác ghi nào được thực hiện.';

    if (status === 429 || code === 'TOO_MANY_REQUESTS' || message.includes('quá nhiều'))
        return `Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút. ${preservedState}`;
    if (status === 401 || status === 403)
        return `Phiên đăng nhập không còn hợp lệ hoặc không có quyền dùng trợ lý. ${preservedState}`;
    if (code === 'ECONNABORTED' || code === 'ETIMEDOUT' || message.includes('timeout') || message.includes('timed out'))
        return `Máy chủ phản hồi quá lâu. ${preservedState} Bạn có thể thử lại.`;
    if (status !== undefined && status >= 500)
        return `Máy chủ ClinicCare đang gặp sự cố tạm thời. ${preservedState} Bạn có thể thử lại sau.`;
    if (status === 400)
        return `Yêu cầu chưa hợp lệ. ${preservedState} Hãy kiểm tra lại thông tin.`;
    return `Không thể kết nối máy chủ ClinicCare lúc này. ${preservedState} Bạn có thể thử lại.`;
};
