import { act, renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { ChatProvider, useChatContext } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { ChatMessage } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
const copilot = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Patient', userId: 'polish-retry' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../api/aiCopilotApi', () => ({ sendRoleCopilotMessage: copilot }));
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
beforeEach(() => { localStorage.clear(); sessionStorage.clear(); post.mockReset(); copilot.mockReset(); });

it.each(['message', 'suggestion', 'alias', 'envelope'] as const)('P5 retries %s without duplicating the question and removes only its own failed bubble', async kind => {
    const api = kind === 'suggestion' || kind === 'alias' ? copilot : post;
    if (kind === 'envelope') api.mockResolvedValueOnce({ success: true, data: { message: 'Tạm lỗi', retryable: true, actions: [] } });
    else api.mockRejectedValueOnce({ message: 'NetworkError' });
    api.mockResolvedValueOnce(api === copilot ? { message: 'Đã tải', cards: [], suggestions: [] } : { success: true, data: { message: 'Đã tải', actions: [] } });
    const { result } = renderHook(() => ({ flow: useAiBookingFlow(), chat: useChatContext() }), { wrapper });
    const oldError: ChatMessage = { role: 'model', content: 'Lỗi cũ vẫn cần giữ', isError: true };
    await act(async () => result.current.chat.setMessages([oldError]));
    await act(async () => {
        if (kind === 'suggestion') await result.current.flow.handleSuggestion({ code: 'patient.get_my_bills', label: 'Hóa đơn' });
        else if (kind === 'alias') await result.current.flow.handleSendMessage('Hóa đơn của tôi');
        else await result.current.flow.handleSendMessage('Khám nội khoa', { specialtyId: 7 });
    });
    const failed = result.current.flow.messages.at(-1)!;
    expect(failed.isError).toBe(true);
    const other: ChatMessage = { role: 'model', content: 'Thông báo đến sau lỗi' };
    await act(async () => result.current.chat.setMessages(previous => [...previous, other]));
    await act(async () => result.current.flow.retryLastRequest());
    expect(result.current.flow.messages.filter(m => m.role === 'user')).toHaveLength(1);
    expect(result.current.flow.messages).not.toContain(failed);
    expect(result.current.flow.messages).toContain(oldError);
    expect(result.current.flow.messages).toContain(other);
    expect(result.current.flow.messages.at(-1)?.content).toBe('Đã tải');
    expect(result.current.flow.canRetry).toBe(false);
    if (api === post) expect(post.mock.calls[1][1]).toEqual(post.mock.calls[0][1]);
    else expect(copilot.mock.calls[1][0]).toMatchObject({ message: copilot.mock.calls[0][0].message, sessionId: copilot.mock.calls[0][0].sessionId, suggestionCode: copilot.mock.calls[0][0].suggestionCode });
});
it('P5 repeated failed retries leave one question and only the current failed bubble; new sends still add questions', async () => {
    copilot.mockRejectedValue({ message: 'NetworkError' });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSuggestion({ code: 'patient.get_my_bills', label: 'Hóa đơn' }));
    await act(async () => result.current.retryLastRequest());
    await act(async () => result.current.retryLastRequest());
    expect(result.current.messages.filter(m => m.role === 'user')).toHaveLength(1);
    expect(result.current.messages.filter(m => m.isError)).toHaveLength(1);
    await act(async () => result.current.handleSuggestion({ code: 'patient.get_my_prescriptions', label: 'Đơn thuốc' }));
    expect(result.current.messages.filter(m => m.role === 'user')).toHaveLength(2);
});
