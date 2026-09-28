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

export const aiChatFailureMessage = (error: unknown): string => {
    const status = readStatus(error);
    const code = readCode(error);
    const message = readMessage(error).toLowerCase();

    if (status === 429 || code === 'TOO_MANY_REQUESTS' || message.includes('quá nhiều'))
        return 'Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút. Bản nháp đặt lịch vẫn được giữ nguyên.';
    if (status === 401 || status === 403)
        return 'Phiên đăng nhập không còn hợp lệ hoặc không có quyền dùng trợ lý. Bản nháp đặt lịch vẫn được giữ nguyên.';
    if (code === 'ECONNABORTED' || code === 'ETIMEDOUT' || message.includes('timeout') || message.includes('timed out'))
        return 'Máy chủ phản hồi quá lâu. Bản nháp đặt lịch vẫn được giữ nguyên; bạn có thể thử lại hoặc đặt lịch trực tiếp.';
    if (status !== undefined && status >= 500)
        return 'Máy chủ ClinicCare đang gặp sự cố tạm thời. Bản nháp đặt lịch vẫn được giữ nguyên; bạn có thể thử lại sau.';
    if (status === 400)
        return 'Yêu cầu đặt lịch chưa hợp lệ. Bản nháp đặt lịch vẫn được giữ nguyên; hãy kiểm tra lại thông tin.';
    return 'Không thể kết nối máy chủ ClinicCare lúc này. Bản nháp đặt lịch vẫn được giữ nguyên; bạn có thể thử lại hoặc đặt lịch trực tiếp.';
};
