import { act, cleanup, fireEvent, render, renderHook, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { AiAction, AiBookingWizardResponse } from '../types/ai';

const mocks = vi.hoisted(() => ({ post: vi.fn(), user: { userId: 'coverage-patient', role: 'Patient' } }));
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ isAuthenticated: true, user: mocks.user })
}));
vi.mock('../api/axiosClient', () => ({ default: { post: mocks.post } }));
vi.mock('../api/aiSuggestionApi', () => ({
    getCopilotSuggestions: async () => ({
        role: 'Patient', suggestions: [{ code: 'patient.start_booking', label: 'Đặt lịch khám' }]
    })
}));

const wrapper = ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={['/patient']}><ChatProvider>{children}</ChatProvider></MemoryRouter>
);
const wizard: AiBookingWizardResponse = {
    step: 'specialty', title: 'Chọn chuyên khoa', message: 'Chọn chuyên khoa để tiếp tục.',
    textToken: 'coverage-step', options: [{ token: 'specialty-choice', label: 'Nhi khoa' }],
    canGoBack: false, actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false
};
const overdose = 'tôi lỡ uống 20 viên paracetamol cùng lúc';
const fumes = 'mẹ tôi hít phải khí gas, đang chóng mặt buồn nôn';
const idiom = 'đau lưng muốn chết luôn, nên khám khoa nào';
const intent = 'đau khớp suốt đêm, bây giờ tôi muốn chết';
const emergency = 'Dấu hiệu có thể là tình huống cấp cứu. Hãy gọi 115 ngay.';
const support = 'Hãy liên hệ ngay với người thân hoặc người bạn tin cậy.';
const specialtyReply = 'Bạn có thể khám chuyên khoa Cơ xương khớp.';
const emergencyAction: AiAction = {
    id: 'emergency', type: 'CallEmergency', label: 'Gọi cấp cứu 115', style: 'danger',
    requiresAuthentication: false, requiresConfirmation: false, payload: { targetUrl: 'tel:115' }
};
const api = (data: object) => ({ success: true, data });

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    mocks.post.mockReset();
    mocks.user.userId = 'coverage-patient';
});
afterEach(cleanup);

function CurrentRoute() {
    return <output>{useLocation().pathname}</output>;
}

function openChat() {
    render(<><MedicalChatWidget /><CurrentRoute /></>, { wrapper });
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
}

function send(message: string) {
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung tin nhắn gửi tới ClinicCare AI' }), { target: { value: message } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi tin nhắn' }));
}

it.each([overdose, fumes, 'xem lịch hẹn, ba vua hit phai khi CO trong gara'])('sends a possible exposure to chat and shows 115 for %s', async message => {
    mocks.post.mockResolvedValueOnce(api({ message: emergency, urgency: 'EMERGENCY', dialogueOutcome: 'SafetyEmergency', actions: [emergencyAction] }));
    openChat();
    send(message);
    expect(await screen.findByRole('link', { name: 'Gọi cấp cứu 115' })).toHaveAttribute('href', 'tel:115');
    expect(screen.queryByRole('button', { name: 'Đặt lịch khám' })).not.toBeInTheDocument();
    expect(mocks.post).toHaveBeenCalledExactlyOnceWith('/ai/chat', expect.objectContaining({ message }), expect.anything());
});

it.each([
    [overdose, 'EMERGENCY', 'SafetyEmergency', emergency],
    [intent, 'SELF_HARM_RISK', 'SafetySelfHarm', support]
])('shows safety for %s when the wizard is open or booking is locked', async (message, errorCode, dialogueOutcome, copy) => {
    mocks.post.mockResolvedValueOnce(api(wizard)).mockResolvedValueOnce(api({ message: copy, errorCode, actions: errorCode === 'EMERGENCY' ? [emergencyAction] : [] }));
    const open = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => open.result.current.handleWizardStep('start'));
    await act(() => open.result.current.handleSendMessage(message));
    expect(open.result.current.wizard?.textToken).toBe('coverage-step');
    expect(mocks.post.mock.calls[1][1]).toMatchObject({ step: 'text', optionToken: 'coverage-step', text: message });
    expect(open.result.current.messages.at(-1)).toMatchObject({ content: copy, dialogueOutcome });
    open.unmount();

    mocks.user.userId = 'patient.07';
    mocks.post.mockResolvedValueOnce(api({ ...wizard, step: 'stopped', errorCode: 'ONLINE_BOOKING_BLOCKED' }))
        .mockResolvedValueOnce(api({ message: copy, dialogueOutcome, urgency: errorCode === 'EMERGENCY' ? 'EMERGENCY' : 'ROUTINE' }));
    const locked = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => locked.result.current.handleWizardStep('start'));
    await act(() => locked.result.current.handleSendMessage(message));
    expect(locked.result.current.wizard?.errorCode).toBe('ONLINE_BOOKING_BLOCKED');
    expect(locked.result.current.messages.at(-1)).toMatchObject({ content: copy, dialogueOutcome });
});

it.each([false, true])('routes a pain idiom to specialty guidance with booking lock set to %s', async locked => {
    if (locked) mocks.user.userId = 'patient.07';
    mocks.post.mockResolvedValueOnce(api(locked ? { ...wizard, step: 'stopped', errorCode: 'ONLINE_BOOKING_BLOCKED' } : wizard));
    if (!locked) mocks.post.mockResolvedValueOnce(api({ ...wizard, routeToChat: true }));
    mocks.post.mockResolvedValueOnce(api({ message: specialtyReply, urgency: 'ROUTINE' }));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(() => result.current.handleWizardStep('start'));
    await act(() => result.current.handleSendMessage(idiom));
    expect(mocks.post.mock.calls.at(-1)).toEqual(['/ai/chat', expect.objectContaining({ message: idiom }), expect.anything()]);
    expect(result.current.messages.at(-1)).toMatchObject({ content: specialtyReply, urgency: 'ROUTINE' });
});

it('asks chat for the doctor’s day and starts the prefilled wizard on click', async () => {
    const message = 'bs Đinh Thị Yến có lịch khám thứ hai không';
    mocks.post.mockResolvedValueOnce(api({
        message: 'Có lịch trống ngày 30/11/2026:\n- 08:00–08:30\n- 09:00–09:30',
        actions: [{ id: 'book', type: 'StartBooking', label: 'Đặt lịch với bác sĩ này', payload: { doctorId: 12, specialtyId: 9 } }]
    })).mockResolvedValueOnce(api({ ...wizard, step: 'day', title: 'Chọn ngày khám' }));
    openChat();
    send(message);
    expect(await screen.findByText('08:00–08:30')).toBeVisible();
    expect(mocks.post).toHaveBeenCalledExactlyOnceWith('/ai/chat', expect.objectContaining({ message }), expect.anything());
    fireEvent.click(screen.getByRole('button', { name: 'Đặt lịch với bác sĩ này' }));
    expect(await screen.findByText('Chọn ngày khám')).toBeVisible();
    expect(mocks.post.mock.calls[1][1]).toMatchObject({ step: 'start', doctorId: 12, specialtyId: 9 });
});

it('offers both price actions and displays every fee returned for a general request', async () => {
    const scopeReply = 'ClinicCare chỉ có thông tin về phí khám và giá thuốc của phòng khám.';
    const specialties = ['Nội tổng quát', 'Nhi', 'Sản phụ khoa', 'Da liễu', 'Tai mũi họng', 'Tim mạch',
        'Cơ xương khớp', 'Thần kinh', 'Nội tiết', 'Nhãn khoa', 'Tiêu hóa'];
    mocks.post.mockResolvedValueOnce(api({
        message: scopeReply, dialogueOutcome: 'OutsideClinicPricing',
        actions: [{ id: 'medicines', type: 'ViewSpecialty', label: 'Mở Bảng giá thuốc', payload: { targetUrl: '/medicines' } }]
    })).mockResolvedValueOnce(api({
        message: 'Bảng giá chi phí khám tham khảo:\n' + specialties.map(name => `- **Khoa ${name}**: 250.000 ₫ / lượt khám`).join('\n')
    }));
    openChat();
    send('pin điện thoại bao nhiêu tiền');
    expect(await screen.findByText(scopeReply, { selector: ':not([data-chat-announcement])' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Hỏi phí khám' }));
    expect(await screen.findByText('Khoa Tiêu hóa')).toBeVisible();
    expect(mocks.post.mock.calls[1][1]).toMatchObject({ intent: 'PricingInquiry' });
    const rows = screen.getByText('Khoa Tiêu hóa').closest('ul')?.querySelectorAll('strong');
    expect(Array.from(rows ?? [], row => row.textContent)).toEqual(specialties.map(name => `Khoa ${name}`));
    fireEvent.click(screen.getByRole('button', { name: 'Mở Bảng giá thuốc' }));
    expect(await screen.findByText('/medicines')).toBeVisible();
});
