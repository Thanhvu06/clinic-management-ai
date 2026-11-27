import { act, cleanup, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { AiBookingWizardResponse } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ user: { userId: 'cancel-wizard-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true })
}));
vi.mock('../api/axiosClient', () => ({ default: { post } }));

const wrapper = ({ children }: { children: ReactNode }) =>
    <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;

const wizard: AiBookingWizardResponse = {
    step: 'day', title: 'Chọn ngày khám', message: 'Bạn muốn khám ngày nào?',
    textToken: 'day-text', options: [], actions: [], suggestions: [],
    canGoBack: false, assistantMode: 'Ready', providerWasCalled: false
};

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    post.mockResolvedValueOnce({ success: true, data: wizard });
});
afterEach(cleanup);

describe('Hủy lịch bằng chat khi đang mở luồng đặt lịch', () => {
    it.each(['hủy', 'HỦY!', 'thôi', 'không đặt nữa'])('đóng luồng đặt lịch sau khi máy chủ xử lý "%s"', async message => {
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        post.mockResolvedValueOnce({ success: true, data: { message: 'Đã hủy bản nháp.', dialogueOutcome: 'DraftCancelled' } });

        await act(async () => result.current.handleSendMessage(message));

        expect(post).toHaveBeenLastCalledWith('/ai/chat', expect.objectContaining({ message, hasOpenBookingWizard: true }), expect.anything());
        expect(result.current.wizard).toBeNull();
    });

    it('giữ luồng đặt lịch đang mở khi bot hỏi người dùng muốn hủy lịch nào', async () => {
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        const message = 'hủy lịch khám';
        const reply = 'Bạn muốn hủy bản nháp đang đặt dở hay một lịch hẹn đã đặt?';
        post.mockResolvedValueOnce({ success: true, data: { message: reply, dialogueOutcome: 'ClarificationRequired' } });

        await act(async () => result.current.handleSendMessage(message));

        expect(post).toHaveBeenLastCalledWith('/ai/chat', expect.objectContaining({ message, hasOpenBookingWizard: true }), expect.anything());
        expect(result.current.messages.at(-1)?.content).toBe(reply);
        expect(result.current.wizard?.step).toBe('day');
    });
});
