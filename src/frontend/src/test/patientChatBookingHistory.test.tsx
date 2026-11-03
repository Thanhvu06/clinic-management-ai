import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider } from '../contexts/ChatContext';
import type { AiBookingWizardResponse, AiSpecialtySuggestion } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'booking-history-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({ role: 'Patient', suggestions: [] }) }));

const recommendation = (specialtyId: number, specialtyName: string): AiSpecialtySuggestion => ({
    specialtyId, specialtyCode: `SP${specialtyId}`, specialtyName, rank: specialtyId, reason: 'Khoa phù hợp',
});
const specialty = recommendation(1, 'Tiêu hóa');
const cardiology = recommendation(2, 'Tim mạch');
const wizardState = (step: AiBookingWizardResponse['step'], title: string): AiBookingWizardResponse => ({
    step, title, message: 'Chọn để tiếp tục', textToken: `opaque-${step}`, options: [], canGoBack: false,
    actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false,
});
const mockReply = (message: string, specialtySuggestions: AiSpecialtySuggestion[] = []) =>
    post.mockResolvedValueOnce({ success: true, data: { message, specialtySuggestions } });
const mockWizard = (step: AiBookingWizardResponse['step'], title: string) =>
    post.mockResolvedValueOnce({ success: true, data: wizardState(step, title) });

const openChat = () => {
    render(<MemoryRouter initialEntries={['/patient']}><ChatProvider><MedicalChatWidget /></ChatProvider></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Mở.*trợ lý/i }));
};
const send = (text: string) => {
    fireEvent.change(screen.getByPlaceholderText('Mô tả triệu chứng hoặc đặt câu hỏi...'), { target: { value: text } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi tin nhắn' }));
};
const messageArea = () => document.querySelector<HTMLElement>('[data-chat-messages]')!;

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

it.each([
    { suggestions: [specialty], request: 'đặt lịch giúp tôi', hint: 'Tiêu hóa' },
    { suggestions: [specialty, cardiology], request: 'đặt lịch giúp tôi', hint: undefined },
    { suggestions: [], request: 'đặt lịch giúp tôi', hint: undefined },
    { suggestions: [specialty], request: 'đặt lịch khám khoa Da liễu', hint: 'Da liễu' },
])('uses hint $hint for typed booking after the latest reply', async ({ suggestions, request, hint }) => {
    mockReply('Gợi ý từ trợ lý', suggestions);
    mockWizard('specialty', 'Chọn chuyên khoa');
    openChat();
    send('ở đây có khám đau bụng k');
    await within(messageArea()).findByText('Gợi ý từ trợ lý');
    send(request);
    await screen.findByRole('heading', { name: 'Chọn chuyên khoa' });
    const [url, body] = post.mock.lastCall!;
    expect(url).toBe('/ai/booking-wizard');
    expect(body.step).toBe('start');
    if (hint) expect(body.hint).toBe(hint);
    else expect(body).not.toHaveProperty('hint');
});

it('renders ten messages in order and keeps typed wizard messages above the final card', async () => {
    openChat();
    const conversation: string[] = [];
    for (let index = 1; index <= 5; index++) {
        const question = `Câu hỏi ${index}`;
        const answer = `Phản hồi ${index}`;
        conversation.push(question, answer);
        mockReply(answer);
        send(question);
        await within(messageArea()).findByText(answer);
    }
    const area = messageArea();
    const bubbles = () => Array.from(area.querySelectorAll('[data-chat-bubble]'));
    expect(bubbles().map(bubble => bubble.textContent)).toEqual(conversation);
    expect(screen.queryByRole('button', { name: /Xem \d+ tin nhắn trước|Thu gọn/ })).not.toBeInTheDocument();

    mockWizard('specialty', 'Chọn chuyên khoa');
    send('đặt lịch');
    await screen.findByRole('heading', { name: 'Chọn chuyên khoa' });
    mockWizard('day', 'Chọn ngày khám');
    send('bác sĩ khải á');
    const card = screen.getByRole('region', { name: 'Đặt lịch khám từng bước' });
    await within(card).findByRole('heading', { name: 'Chọn ngày khám' });

    expect(bubbles().map(bubble => bubble.textContent)).toEqual([...conversation, 'đặt lịch', 'bác sĩ khải á']);
    for (const bubble of bubbles()) expect(bubble.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(card.nextElementSibling).toBeEmptyDOMElement();
    expect(post.mock.lastCall?.[1]).toMatchObject({ step: 'text', text: 'bác sĩ khải á' });
});

it('keeps a reader above the bottom in place when the wizard changes steps', async () => {
    mockWizard('specialty', 'Chọn chuyên khoa');
    mockWizard('day', 'Chọn ngày khám');
    openChat();
    send('đặt lịch');
    await screen.findByRole('heading', { name: 'Chọn chuyên khoa' });
    const area = messageArea();
    Object.defineProperties(area, { scrollHeight: { value: 1000 }, clientHeight: { value: 200 }, scrollTop: { value: 100 } });
    fireEvent.scroll(area);
    vi.mocked(HTMLElement.prototype.scrollIntoView).mockClear();
    send('bác sĩ khải á');
    await screen.findByRole('heading', { name: 'Chọn ngày khám' });
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Có tin nhắn mới' }));
    expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalledWith({ behavior: 'instant', block: 'end' });
});
