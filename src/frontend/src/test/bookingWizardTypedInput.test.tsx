import { act, cleanup, renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { AiBookingWizardResponse } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'typed-wizard-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const state = (step: AiBookingWizardResponse['step'], textToken: string | undefined = `opaque-${step}`): AiBookingWizardResponse => ({
    step, title: `Bước ${step}`, message: 'Chọn để tiếp tục.', textToken,
    options: [{ token: 'opaque-option', label: 'Lựa chọn hiện có' }], canGoBack: true, backToken: 'opaque-back',
    actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false
});
beforeEach(() => {
    localStorage.clear(); sessionStorage.clear(); post.mockReset();
    post.mockResolvedValue({ success: true, data: state('slot') });
});
afterEach(cleanup);

describe('Typed input stays inside the current booking wizard', () => {
    it.each(['specialty', 'doctor', 'day', 'slot', 'facility'] as const)('routes text in %s through its state token without chat or restart', async step => {
        post.mockResolvedValueOnce({ success: true, data: state(step) });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        const session = post.mock.calls[0][1].sessionId;
        post.mockClear();
        await act(async () => result.current.handleSendMessage('khám ngày 22/10/2026'));
        expect(post).toHaveBeenCalledExactlyOnceWith('/ai/booking-wizard', expect.objectContaining({
            sessionId: session, step: 'text', optionToken: `opaque-${step}`, text: 'khám ngày 22/10/2026'
        }), expect.anything());
        expect(result.current.messages.filter(message => message.role === 'user').map(message => message.content)).toEqual(['khám ngày 22/10/2026']);
        expect(result.current.wizard?.step).toBe('slot');
        expect(result.current.input).toBe('');
    });

    it('does not restart even when text contains a booking intent', async () => {
        post.mockResolvedValueOnce({ success: true, data: state('day') });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        post.mockClear();
        await act(async () => result.current.handleSendMessage('đặt lịch khám ngày 22/10/2026'));
        expect(post).toHaveBeenCalledExactlyOnceWith('/ai/booking-wizard', expect.objectContaining({
            step: 'text', optionToken: 'opaque-day', text: 'đặt lịch khám ngày 22/10/2026'
        }), expect.anything());
    });

    it.each(['lịch hẹn của tôi', 'tôi có quyền hạn gì'])('keeps help/read alias %s ahead of typed wizard choices', async text => {
        post.mockResolvedValueOnce({ success: true, data: state('day') });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        post.mockClear();
        post.mockResolvedValueOnce({ success: true, data: { message: 'Dữ liệu tra cứu', cards: [], suggestions: [], actions: [], providerWasCalled: false } });
        await act(async () => result.current.handleSendMessage(text));
        expect(post).toHaveBeenCalledExactlyOnceWith('/ai/copilot/chat', expect.objectContaining({ message: text, suggestionCode: undefined }), expect.anything());
        expect(result.current.wizard?.step).toBe('day');
    });

    it('retries the same typed text, token and session without adding another user message', async () => {
        post.mockResolvedValueOnce({ success: true, data: state('day') });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        post.mockClear();
        post.mockRejectedValueOnce({ status: 503, message: 'Tạm thời chưa tải được lịch.' });
        await act(async () => result.current.handleSendMessage('ngày mai'));
        const first = post.mock.calls[0][1];
        expect(result.current.canRetry).toBe(true);
        await act(async () => result.current.retryLastRequest());
        expect(post).toHaveBeenCalledTimes(2);
        expect(post.mock.calls[1][0]).toBe('/ai/booking-wizard');
        expect(post.mock.calls[1][1]).toEqual(first);
        expect(first).toMatchObject({ step: 'text', text: 'ngày mai', optionToken: 'opaque-day' });
        expect(result.current.messages.filter(message => message.role === 'user')).toHaveLength(1);
    });

    it('keeps the legacy chat path when the server has supplied no text token', async () => {
        post.mockResolvedValueOnce({ success: true, data: { ...state('day'), textToken: undefined } });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('start'));
        post.mockClear();
        post.mockResolvedValueOnce({ success: true, data: { message: 'Legacy', actions: [] } });
        await act(async () => result.current.handleSendMessage('ngày 22'));
        expect(post).toHaveBeenCalledExactlyOnceWith('/ai/chat', expect.objectContaining({ message: 'ngày 22' }), expect.anything());
    });
});
