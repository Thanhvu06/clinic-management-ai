import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import type { ChatMessage } from '../types/ai';

const mocks = vi.hoisted(() => ({ flow: {} as Record<string, unknown> }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true, user: { role: 'Patient', userId: 'manual-round-one' } }) }));
vi.mock('../contexts/ChatContext', async original => ({ ...await original<object>(), useChatContext: () => ({ setPendingSpecialtyId: vi.fn() }) }));
vi.mock('../hooks/useAiBookingFlow', () => ({ useAiBookingFlow: () => mocks.flow }));
vi.mock('../components/copilot/useSuggestionMenu', async original => ({ ...await original<object>(), useSuggestionMenu: () => [] }));

beforeEach(() => {
    mocks.flow = {
        input: '', setInput: vi.fn(), loading: false, submittingBooking: false, errorMsg: '',
        messages: [{ role: 'model', content: 'Phản hồi trước' }], activeDraft: null,
        clearChat: vi.fn(), handleSendMessage: vi.fn(), handleSuggestion: vi.fn(), wizard: null,
        handleWizardStep: vi.fn(), handleActionClick: vi.fn(), confirmToolAction: vi.fn(), cancelToolAction: vi.fn(),
        formatVietnameseDate: (value: string) => value, retryLastRequest: vi.fn(), canRetry: false, retryAfterSeconds: 0,
    };
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

const tree = () => <MemoryRouter><MedicalChatWidget /></MemoryRouter>;
const open = () => {
    const view = render(tree());
    fireEvent.click(screen.getByRole('button', { name: /Mở.*trợ lý/i }));
    const area = document.querySelector<HTMLElement>('[data-chat-messages]')!;
    Object.defineProperties(area, {
        scrollHeight: { configurable: true, value: 1000 }, clientHeight: { configurable: true, value: 200 },
        scrollTop: { configurable: true, writable: true, value: 100 },
    });
    fireEvent.scroll(area);
    vi.mocked(HTMLElement.prototype.scrollIntoView).mockClear();
    return view;
};

it.each([false, true])('scrolls an own message only outside the wizard (wizard=%s)', wizard => {
    if (wizard) mocks.flow.wizard = { step: 'doctor', title: 'Chọn bác sĩ', options: [], canGoBack: false };
    const view = open();
    mocks.flow.messages = [...mocks.flow.messages as ChatMessage[], { role: 'user', content: 'Tin vừa gửi' }];
    view.rerender(tree());
    expect(screen.getByText('Tin vừa gửi')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Có tin nhắn mới' })).not.toBeInTheDocument();
    if (wizard) expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
    else expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalledWith({ behavior: 'instant', block: 'end' });
});

it('keeps the new-message control for a model reply while scrolled up', () => {
    const view = open();
    mocks.flow.messages = [...mocks.flow.messages as ChatMessage[], { role: 'model', content: 'Tin mới từ trợ lý' }];
    view.rerender(tree());
    expect(screen.getByRole('button', { name: 'Có tin nhắn mới' })).toBeInTheDocument();
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
});

it.each([
    { providerState: 'SafetyBlocked', urgency: undefined, hidden: true },
    { providerState: 'NotCalled', urgency: 'EMERGENCY', hidden: true },
    { providerState: 'NotCalled', urgency: undefined, hidden: false },
])('shows the internal hint only outside safety replies: %j', ({ providerState, urgency, hidden }) => {
    mocks.flow.messages = [{ role: 'model', content: 'Thông báo mới nhất', isError: true, providerState, urgency }];
    open();
    const hint = screen.queryByText(/chỉ hiểu một số câu/);
    if (hidden) expect(hint).not.toBeInTheDocument();
    else expect(hint).toBeInTheDocument();
});
