import { act, cleanup, fireEvent, render, renderHook, screen, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { AiBookingWizardResponse } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ isAuthenticated: true, user: { userId: 'safety-patient', role: 'Patient' } })
}));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../api/aiSuggestionApi', () => ({
    getCopilotSuggestions: async () => ({ role: 'Patient', suggestions: [{ code: 'patient.start_booking', label: 'Đặt lịch khám' }] })
}));

const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const wizard: AiBookingWizardResponse = {
    step: 'specialty', title: 'Chọn chuyên khoa', message: 'Chọn chuyên khoa để tiếp tục.',
    textToken: 'current-step', options: [{ token: 'specialty-choice', label: 'Nhi khoa' }],
    canGoBack: false, actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false
};
const poisonCases = [
    'con tôi 3 tuổi lỡ uống nước tẩy bồn cầu', 'con toi 3 tuoi lo uong nuoc tay bon cau',
    'bé nhà tôi vừa nuốt viên pin cúc áo', 'be nha toi vua nuot vien pin cuc ao',
    'chồng tôi uống thuốc trừ sâu, đang nôn mửa', 'chong toi uong thuoc tru sau, dang non mua'
];
const selfHarmCases = ['dạo này tôi thấy không muốn sống nữa', 'dao nay toi thay khong muon song nua'];
const emergencyReply = 'Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 ngay.';
const selfHarmReply = 'Hãy liên hệ ngay với người thân hoặc người bạn tin cậy. Nếu đang gặp nguy hiểm tức thời, hãy gọi 115.';
const response = (data: object) => ({ success: true, data });

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
});
afterEach(cleanup);

function openChat() {
    render(<MedicalChatWidget />, { wrapper });
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
}

function send(message: string) {
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung tin nhắn gửi tới ClinicCare AI' }), { target: { value: message } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi tin nhắn' }));
}

it.each(poisonCases)('shows 115 without a booking chip for %s', async message => {
    post.mockResolvedValueOnce(response({ message: emergencyReply, urgency: 'EMERGENCY', dialogueOutcome: 'SafetyEmergency', providerState: 'SafetyBlocked' }));
    openChat();
    send(message);
    expect(await screen.findByRole('link', { name: 'Gọi cấp cứu 115' })).toHaveAttribute('href', 'tel:115');
    expect(screen.queryByRole('button', { name: 'Đặt lịch khám' })).not.toBeInTheDocument();
    expect(screen.queryByText('Chọn chuyên khoa')).not.toBeInTheDocument();
});

it.each(selfHarmCases)('shows the dedicated support message for %s', async message => {
    post.mockResolvedValueOnce(response({ message: selfHarmReply, dialogueOutcome: 'SafetySelfHarm', providerState: 'SafetyBlocked' }));
    openChat();
    send(message);
    expect(await screen.findByText(selfHarmReply, { selector: ':not([data-chat-announcement])' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Đặt lịch khám' })).not.toBeInTheDocument();
    expect(screen.queryByText('Chọn chuyên khoa')).not.toBeInTheDocument();
});

it.each([
    'thuốc tẩy giun cho bé uống lúc nào', 'thuoc tay giun cho be uong luc nao',
    'pin điện thoại bao nhiêu tiền', 'pin dien thoai bao nhieu tien',
    'sống ở đâu gần phòng khám', 'song o dau gan phong kham'
])('keeps %s on the ordinary chat path', async message => {
    post.mockResolvedValueOnce(response({ message: 'Mình có thể giúp bạn tra cứu thông tin phòng khám.' }));
    openChat();
    send(message);
    expect(await screen.findByText('Mình có thể giúp bạn tra cứu thông tin phòng khám.', { selector: ':not([data-chat-announcement])' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Gọi cấp cứu 115' })).not.toBeInTheDocument();
    expect(post).toHaveBeenCalledExactlyOnceWith('/ai/chat', expect.objectContaining({ message }), expect.anything());
});

it.each([
    'tra cứu lịch hẹn mã LH0056',
    'lịch lh 56 của tôi ngày mấy, bác sĩ nào',
    'tra cứu lịch hẹn mã APT-261128-ABCD1234',
])('keeps the full appointment code question on the scoped chat path for %s', async message => {
    const copy = 'Lịch hẹn LH0056 của bạn.';
    post.mockResolvedValueOnce(response({ message: copy }));
    openChat();
    send(message);
    expect(await screen.findByText(copy, { selector: ':not([data-chat-announcement])' })).toBeVisible();
    expect(post).toHaveBeenCalledExactlyOnceWith('/ai/chat', expect.objectContaining({ message }), expect.anything());
});

it.each([
    [poisonCases[3], 'EMERGENCY', emergencyReply],
    [selfHarmCases[1], 'SELF_HARM_RISK', selfHarmReply]
])('keeps an open wizard and shows safety for %s', async (message, errorCode, copy) => {
    post.mockResolvedValueOnce(response(wizard)).mockResolvedValueOnce(response({ errorCode, message: copy }));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => result.current.handleWizardStep('start'));
    await act(() => result.current.handleSendMessage(message));
    expect(result.current.wizard?.textToken).toBe('current-step');
    expect(result.current.messages.at(-1)).toMatchObject({ content: copy, providerState: 'SafetyBlocked' });
});

it.each([
    [poisonCases[0], 'SafetyEmergency', emergencyReply],
    [selfHarmCases[0], 'SafetySelfHarm', selfHarmReply]
])('still shows %s safety after an online booking lock', async (message, dialogueOutcome, copy) => {
    post.mockResolvedValueOnce(response({ ...wizard, step: 'stopped', errorCode: 'ONLINE_BOOKING_BLOCKED' }))
        .mockResolvedValueOnce(response({ message: copy, dialogueOutcome, providerState: 'SafetyBlocked' }));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => result.current.handleWizardStep('start'));
    await act(() => result.current.handleSendMessage(message));
    expect(result.current.wizard?.errorCode).toBe('ONLINE_BOOKING_BLOCKED');
    expect(result.current.messages.at(-1)).toMatchObject({ content: copy, dialogueOutcome });
});

it.each(['bác sĩ Lý Kim Dung khám khoa gì, lịch trống khi nào', 'bac si Ly Kim Dung kham khoa gi, lich trong khi nao'])(
    'answers %s before opening the prefilled doctor wizard',
    async message => {
        post.mockResolvedValueOnce(response({
            message: 'Bác sĩ Lý Kim Dung, chuyên khoa Nội tiết.',
            actions: [{ id: 'doctor-wizard', type: 'StartBooking', label: 'Đặt lịch với bác sĩ này', payload: { specialtyId: 9, doctorId: 12 } }]
        })).mockResolvedValueOnce(response({ ...wizard, step: 'day', title: 'Chọn ngày khám' }));
        openChat();
        send(message);
        expect(screen.queryByText('Chọn chuyên khoa')).not.toBeInTheDocument();
        fireEvent.click(await screen.findByRole('button', { name: 'Đặt lịch với bác sĩ này' }));
        expect(await screen.findByText('Chọn ngày khám')).toBeVisible();
        expect(post.mock.calls[0][0]).toBe('/ai/chat');
        expect(post.mock.calls[1][1]).toMatchObject({ step: 'start', specialtyId: 9, doctorId: 12 });
    }
);

it.each([
    'phòng khám ở đâu, có chỗ gửi xe không',
    'phong kham o dau, co cho gui xe khong',
    'tôi bị đau đầu nên làm gì'
])('answers %s without consuming the wizard option', async message => {
    post.mockResolvedValueOnce(response(wizard)).mockResolvedValueOnce(response({ ...wizard, routeToChat: true }))
        .mockResolvedValueOnce(response({ message: 'Thông tin từ phòng khám.' }));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => result.current.handleWizardStep('start'));
    await act(() => result.current.handleSendMessage(message));
    expect(post.mock.calls[1][1]).toMatchObject({ step: 'text', optionToken: 'current-step' });
    expect(post.mock.calls[2][0]).toBe('/ai/chat');
    expect(post.mock.calls[2][1]).toMatchObject({ message, hasOpenBookingWizard: true });
    expect(result.current.wizard?.textToken).toBe('current-step');
    expect(result.current.messages.filter(turn => turn.role === 'user')).toHaveLength(1);
    expect(result.current.messages.at(-1)?.content).toBe('Thông tin từ phòng khám.');
});

it('accepts a typed reason that matches a visible wizard option', async () => {
    post.mockResolvedValueOnce(response({
        ...wizard, step: 'reason', options: [{ token: 'reason-choice', label: 'Khám tổng quát' }]
    })).mockResolvedValueOnce(response({ ...wizard, step: 'review' }));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => result.current.handleWizardStep('start'));
    await act(() => result.current.handleSendMessage('kham tong quat'));
    expect(post.mock.calls[1][0]).toBe('/ai/booking-wizard');
    expect(post.mock.calls[1][1]).toMatchObject({ step: 'pick', optionToken: 'reason-choice' });
});

it.each([
    [{ status: 'completed', displayText: 'Lịch hẹn APT-261127-87AECB2B đã được hủy.' }, 'Đã thực hiện'],
    [{ status: 'completed', resultType: 'idempotent_replay', isIdempotentReplay: true, displayText: 'Đã thực hiện trước đó' }, 'Đã thực hiện'],
    [{ status: 'failed', error: { code: 'ACTION_EXPIRED' } }, 'Đã hết hạn'],
    [{ status: 'failed', error: { code: 'ACTION_CANCELLED' } }, 'Đã hủy thao tác'],
    [{ status: 'failed', error: { code: 'ACTION_EXECUTION_FAILED', retryable: false } }, 'Đã từ chối']
] as const)('retires the original confirmation card as %s', async (outcome, label) => {
    post.mockResolvedValueOnce(response({
        message: 'Xem trước thao tác với APT-261127-87AECB2B.',
        toolResults: [{ status: 'pending_confirmation', actionId: 'action-1', data: { concurrencyToken: 'token' } }]
    })).mockResolvedValueOnce(outcome);
    openChat();
    send('hủy APT-261127-87AECB2B');
    const card = await screen.findByRole('status', { name: 'Trạng thái thao tác AI' });
    fireEvent.click(within(card).getByRole('button', { name: 'Xác nhận thực hiện' }));
    expect(await within(card).findByText(label)).toBeVisible();
    expect(within(card).queryByRole('button', { name: 'Xác nhận thực hiện' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Hủy thao tác' })).not.toBeInTheDocument();
    expect(screen.queryByText('Đã thực hiện trước đó')).not.toBeInTheDocument();
    expect(post).toHaveBeenCalledTimes(2);
});

it('shows the status supplied by the expiry worker on the requested appointment', async () => {
    post.mockResolvedValueOnce(response({
        message: 'Chi tiết lịch hẹn',
        toolResults: [{ status: 'completed', resultType: 'appointment_detail', data: { appointmentCode: 'APT-261126-7D95807B', status: 'Cancelled' } }]
    }));
    openChat();
    send('xem APT-261126-7D95807B');
    expect(await screen.findByText(/APT-261126-7D95807B · Đã hủy/)).toBeVisible();
});
