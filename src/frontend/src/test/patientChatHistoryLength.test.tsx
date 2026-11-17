import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import type { AiChatRequestPayload } from '../hooks/useAiBookingFlow';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ isAuthenticated: true, identityVersion: 1, user: { role: 'Patient', userId: 'history-patient' } }),
}));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({ suggestions: [] }) }));

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
});

it.each([
    { variant: 'plain text', reply: 'a'.repeat(1200), expectedPrefix: 'a'.repeat(500) },
    { variant: 'a surrogate pair across the limit', reply: 'a'.repeat(499) + '😊' + 'b'.repeat(699), expectedPrefix: 'a'.repeat(499) },
    { variant: 'a complete surrogate pair at the limit', reply: 'a'.repeat(498) + '😊' + 'b'.repeat(700), expectedPrefix: 'a'.repeat(498) + '😊' },
])('limits the history payload for $variant and keeps the full reply visible', async ({ reply, expectedPrefix }) => {
    const packageQuestion = 'Gói khám tổng quát có mấy gói, nên chọn gói nào?';
    const followUp = 'Tôi bị ho, sổ mũi thì nên khám khoa nào?';
    const followUpReply = 'Bạn có thể khám chuyên khoa Tai Mũi Họng.';
    post
        .mockResolvedValueOnce({ success: true, data: { message: reply, actions: [] } })
        .mockResolvedValueOnce({ success: true, data: { message: followUpReply, actions: [] } });

    render(<MemoryRouter><MedicalChatWidget /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
    const input = screen.getByPlaceholderText('Mô tả triệu chứng hoặc đặt câu hỏi...');
    const send = screen.getByRole('button', { name: 'Gửi tin nhắn' });
    const bubble = { selector: '[data-chat-bubble] p' };

    fireEvent.change(input, { target: { value: packageQuestion } });
    fireEvent.click(send);
    expect(await screen.findByText(reply, bubble)).toBeVisible();

    fireEvent.change(input, { target: { value: followUp } });
    fireEvent.click(send);
    expect(await screen.findByText(followUpReply, bubble)).toBeVisible();
    expect(screen.getByText(reply, bubble)).toBeVisible();

    expect(post).toHaveBeenCalledTimes(2);
    const [endpoint, request] = post.mock.calls[1] as [string, AiChatRequestPayload];
    expect(endpoint).toBe('/ai/chat');
    expect(request.message).toBe(followUp);
    expect(request.context.every(({ content }) => content.length <= 500)).toBe(true);
    expect(request.context.slice(-2)).toEqual([
        { role: 'user', content: packageQuestion },
        { role: 'model', content: expectedPrefix },
    ]);
});
