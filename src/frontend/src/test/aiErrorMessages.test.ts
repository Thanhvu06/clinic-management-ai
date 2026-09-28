import { describe, expect, it } from 'vitest';
import { aiChatFailureMessage } from '../api/aiErrorMessages';

describe('aiChatFailureMessage', () => {
    it('distinguishes rate limiting, timeout, backend failure and network failure without exposing provider details', () => {
        expect(aiChatFailureMessage({ status: 429 }, 'booking')).toMatch(/quá nhiều yêu cầu/i);
        expect(aiChatFailureMessage({ code: 'ECONNABORTED', message: 'timeout' }, 'booking')).toMatch(/quá lâu/i);
        expect(aiChatFailureMessage({ status: 503 }, 'booking')).toMatch(/máy chủ ClinicCare/i);
        expect(aiChatFailureMessage({ message: 'socket closed' }, 'booking')).toMatch(/kết nối máy chủ ClinicCare/i);
    });

    it('keeps the draft-preservation promise in every transport message', () => {
        for (const error of [{ status: 429 }, { status: 500 }, { code: 'ETIMEDOUT' }, { message: 'offline' }]) {
            expect(aiChatFailureMessage(error, 'booking')).toMatch(/bản nháp đặt lịch vẫn được giữ nguyên/i);
        }
    });

    it('does not mention a patient booking draft in role Copilot errors', () => {
        expect(aiChatFailureMessage({ status: 503 })).toMatch(/không có thao tác ghi nào được thực hiện/i);
        expect(aiChatFailureMessage({ status: 503 })).not.toMatch(/bản nháp đặt lịch/i);
    });
});
