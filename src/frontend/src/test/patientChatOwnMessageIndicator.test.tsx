import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider } from '../contexts/ChatContext';
import type { ApiResponse } from '../types';
import type { AiBookingWizardResponse } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'own-message-indicator-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({ role: 'Patient', suggestions: [] }) }));

const deferred = <T,>() => {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>(settle => { resolve = settle; });
    return { promise, resolve };
};
const wizardState = (step: AiBookingWizardResponse['step'], title: string): AiBookingWizardResponse => ({
    step, title, message: 'Chọn để tiếp tục', textToken: `opaque-${step}`, options: [], canGoBack: false,
    actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false,
});
const openChat = () => {
    render(<MemoryRouter initialEntries={['/patient']}><ChatProvider><MedicalChatWidget /></ChatProvider></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Mở.*trợ lý/i }));
};
const send = (text: string) => {
    fireEvent.change(screen.getByPlaceholderText('Mô tả triệu chứng hoặc đặt câu hỏi...'), { target: { value: text } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi tin nhắn' }));
};
const scrollAboveLatest = () => {
    const area = document.querySelector<HTMLElement>('[data-chat-messages]')!;
    Object.defineProperties(area, {
        scrollHeight: { configurable: true, value: 1000 },
        clientHeight: { configurable: true, value: 200 },
        scrollTop: { configurable: true, writable: true, value: 100 },
    });
    fireEvent.scroll(area);
    vi.mocked(HTMLElement.prototype.scrollIntoView).mockClear();
    return area;
};

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

it('does not announce an own wizard message while the response and next step are still pending', async () => {
    post.mockResolvedValueOnce({ success: true, data: wizardState('specialty', 'Chọn chuyên khoa') });
    openChat();
    send('đặt lịch');
    await screen.findByRole('heading', { name: 'Chọn chuyên khoa' });
    const area = scrollAboveLatest();
    const pending = deferred<ApiResponse<AiBookingWizardResponse>>();
    post.mockReturnValueOnce(pending.promise);

    send('bác sĩ khải á');

    try {
        await within(area).findByText('bác sĩ khải á');
        expect(post.mock.lastCall?.slice(0, 2)).toEqual(['/ai/booking-wizard', expect.objectContaining({ step: 'text', text: 'bác sĩ khải á' })]);
        expect(screen.queryByRole('heading', { name: 'Chọn ngày khám' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Có tin nhắn mới' })).not.toBeInTheDocument();
        expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
    } finally {
        await act(async () => { pending.resolve({ success: true, message: '', data: wizardState('day', 'Chọn ngày khám') }); });
    }

    await screen.findByRole('heading', { name: 'Chọn ngày khám' });
    expect(screen.queryByRole('button', { name: 'Có tin nhắn mới' })).not.toBeInTheDocument();
});

it('announces a model reply while the reader remains more than 80 pixels above the bottom', async () => {
    post.mockResolvedValueOnce({ success: true, data: { message: 'Phản hồi trước' } });
    openChat();
    send('Câu hỏi trước');
    await within(document.querySelector<HTMLElement>('[data-chat-messages]')!).findByText('Phản hồi trước');
    const area = scrollAboveLatest();
    const pending = deferred<ApiResponse<{ message: string }>>();
    post.mockReturnValueOnce(pending.promise);

    send('Câu hỏi tiếp theo');
    expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalledWith({ behavior: 'instant', block: 'end' });
    expect(screen.queryByRole('button', { name: 'Có tin nhắn mới' })).not.toBeInTheDocument();
    scrollAboveLatest();
    await act(async () => { pending.resolve({ success: true, message: '', data: { message: 'Tin mới từ trợ lý' } }); });

    await within(area).findByText('Tin mới từ trợ lý');
    expect(screen.getByRole('button', { name: 'Có tin nhắn mới' })).toBeInTheDocument();
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
    expect(area.scrollTop).toBe(100);
});
