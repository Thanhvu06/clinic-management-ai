import { describe, expect, it } from 'vitest';
import { aiChatFailureMessage } from '../api/aiErrorMessages';

describe('aiChatFailureMessage', () => {
    it('distinguishes rate limiting, timeout, backend failure and network failure without exposing provider details', () => {
        expect(aiChatFailureMessage({ status: 429 })).toMatch(/quá nhiều yêu cầu/i);
        expect(aiChatFailureMessage({ code: 'ECONNABORTED', message: 'timeout' })).toMatch(/quá lâu/i);
        expect(aiChatFailureMessage({ status: 503 })).toMatch(/máy chủ ClinicCare/i);
        expect(aiChatFailureMessage({ message: 'socket closed' })).toMatch(/kết nối máy chủ ClinicCare/i);
    });

    it('keeps the draft-preservation promise in every transport message', () => {
        for (const error of [{ status: 429 }, { status: 500 }, { code: 'ETIMEDOUT' }, { message: 'offline' }]) {
            expect(aiChatFailureMessage(error)).toMatch(/bản nháp đặt lịch vẫn được giữ nguyên/i);
        }
    });

    it('explains provider failures without presenting them as a broken chat connection', () => {
        expect(aiChatFailureMessage({ providerFailureCode: 'CircuitOpen' })).toMatch(/tạm ngưng/i);
        expect(aiChatFailureMessage({ providerFailureCode: 'Timeout' })).toMatch(/phản hồi quá lâu/i);
        expect(aiChatFailureMessage({ providerFailureCode: 'AuthenticationFailed' })).toMatch(/cấu hình model hoặc xác thực/i);
        expect(aiChatFailureMessage({ providerFailureCode: 'ClientCancelled' })).not.toMatch(/kết nối/i);
        expect(aiChatFailureMessage({ providerFailureCode: 'RateLimited', retryAfterSeconds: 7 })).toMatch(/7 giây/i);
    });
});
