type ErrorRecord = Record<string, unknown>;

export const isAiToolArgumentError = (code?: string): boolean => Boolean(code && (
    ['UNKNOWN_TOOL_ARGUMENT', 'FORBIDDEN_TOOL_ARGUMENT', 'MISSING_TOOL_ARGUMENT'].includes(code) || code.startsWith('INVALID_')
));

export const aiToolErrorMessage = (code?: string, message?: string): string | undefined => isAiToolArgumentError(code)
    ? 'ClinicCare chưa xử lý được yêu cầu này. Bạn thử diễn đạt lại hoặc chọn một gợi ý bên dưới.'
    : message;

const asRecord = (value: unknown): ErrorRecord =>
    value && typeof value === 'object' ? value as ErrorRecord : {};

const readString = (value: unknown): string => typeof value === 'string' ? value : '';

const readNestedPayload = (error: unknown): ErrorRecord => {
    const record = asRecord(error);
    const response = asRecord(record.response);
    const responseData = asRecord(response.data);
    const data = asRecord(record.data);
    return { ...response, ...responseData, ...data, ...record };
};

const readStatus = (error: unknown): number | undefined => {
    const record = asRecord(error);
    const response = asRecord(record.response);
    const status = record.status ?? record.httpStatus ?? response.status;
    return typeof status === 'number' && Number.isInteger(status) ? status : undefined;
};

const readCode = (error: unknown): string => {
    const record = readNestedPayload(error);
    return readString(record.errorCode || record.code).toUpperCase();
};

const readProviderCode = (error: unknown): string => {
    const record = readNestedPayload(error);
    return readString(record.providerFailureCode || record.failureCode || record.providerStatus).toUpperCase();
};

const readRetryAfterSeconds = (error: unknown): number | undefined => {
    const record = readNestedPayload(error);
    const value = record.retryAfterSeconds;
    return typeof value === 'number' && Number.isFinite(value) && value > 0 ? Math.ceil(value) : undefined;
};

const readMessage = (error: unknown): string => {
    const record = readNestedPayload(error);
    return readString(record.message);
};

export const aiChatFailureMessage = (error: unknown): string => {
    const status = readStatus(error);
    const code = readCode(error);
    const providerCode = readProviderCode(error);
    const message = readMessage(error).toLowerCase();
    const retryAfter = readRetryAfterSeconds(error);

    if (providerCode === 'CLIENTCANCELLED' || code === 'ERR_CANCELED' || code === 'ECONNABORTED' && message.includes('cancel'))
        return 'Yêu cầu đã được dừng theo thao tác của bạn. Bản nháp đặt lịch vẫn được giữ nguyên.';
    if (providerCode === 'CIRCUITOPEN' || providerCode === 'PROVIDERCIRCUITOPEN')
        return 'Dịch vụ AI đang tạm ngưng để tự hồi phục. ClinicCare vẫn giữ phiên và bản nháp; bạn có thể tiếp tục tra cứu bằng chế độ hỗ trợ cơ bản.';
    if (providerCode === 'CONFIGURATIONDISABLED' || providerCode === 'DISABLED')
        return 'AI đang tắt theo cấu hình. ClinicCare vẫn giữ phiên và bản nháp để bạn tiếp tục bằng thao tác hỗ trợ cơ bản.';
    if (providerCode === 'AUTHENTICATIONFAILED' || providerCode === 'AUTHFAILURE' || providerCode === 'MODELUNAVAILABLE' || providerCode === 'INVALIDMODELORENDPOINT')
        return 'Dịch vụ AI chưa sẵn sàng do cấu hình model hoặc xác thực. ClinicCare vẫn giữ phiên và bản nháp; bạn có thể tiếp tục bằng chế độ hỗ trợ cơ bản.';
    if (providerCode === 'INVALIDRESPONSE' || providerCode === 'INVALID_PROVIDER_SCHEMA')
        return 'Dịch vụ AI trả về dữ liệu không hợp lệ nên ClinicCare đã bỏ qua dữ liệu đó. Bản nháp đặt lịch vẫn được giữ nguyên.';
    if (providerCode === 'RATELIMITED' || status === 429 || code === 'TOO_MANY_REQUESTS' || message.includes('quá nhiều'))
        return retryAfter
            ? `Dịch vụ AI đang giới hạn lưu lượng. Bạn có thể thử lại sau khoảng ${retryAfter} giây; bản nháp đặt lịch vẫn được giữ nguyên.`
            : 'Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau; bản nháp đặt lịch vẫn được giữ nguyên.';
    if (providerCode === 'TIMEOUT')
        return 'Dịch vụ AI phản hồi quá lâu nên ClinicCare đã chuyển sang chế độ hỗ trợ cơ bản. Bản nháp đặt lịch vẫn được giữ nguyên.';
    if (providerCode === 'SERVERERROR' || providerCode === 'PROVIDERSERVERERROR')
        return 'Dịch vụ AI đang gặp lỗi tạm thời. ClinicCare vẫn giữ phiên và bản nháp; bạn có thể thử lại sau.';
    if (providerCode === 'NETWORKERROR')
        return 'Dịch vụ AI đang gặp lỗi mạng tạm thời. ClinicCare vẫn giữ phiên và bản nháp; bạn có thể thử lại sau.';

    if (status === 429 || code === 'TOO_MANY_REQUESTS' || message.includes('quá nhiều'))
        return 'Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau; bản nháp đặt lịch vẫn được giữ nguyên.';
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
