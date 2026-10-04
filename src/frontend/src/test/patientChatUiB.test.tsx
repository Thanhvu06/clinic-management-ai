import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, afterEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import type { AiAction } from '../types/ai';

const mocks = vi.hoisted(() => ({ flow: {} as Record<string, any>, send: vi.fn(), action: vi.fn(), retry: vi.fn() }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true, user: { role: 'Patient', fullName: 'An', userId: 'ui-b' } }) }));
vi.mock('../contexts/ChatContext', async importOriginal => ({ ...await importOriginal<object>(), useChatContext: () => ({ setPendingSpecialtyId: vi.fn() }) }));
vi.mock('../hooks/useAiBookingFlow', () => ({ useAiBookingFlow: () => mocks.flow, formatVietnameseDate: (s: string) => s.split('-').reverse().join('/') }));
vi.mock('../components/copilot/useSuggestionMenu', async importOriginal => ({ ...await importOriginal<object>(), useSuggestionMenu: () => [] }));
const action = (type: AiAction['type'], version = 1): AiAction => ({ id: type, type, label: type, style: 'primary', requiresAuthentication: false, requiresConfirmation: false, draftVersion: version, payload: {} } as AiAction);
const mount = () => {
    const view = render(<MemoryRouter><MedicalChatWidget /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
    return view;
};
beforeEach(() => {
    mocks.send.mockReset(); mocks.action.mockReset(); mocks.retry.mockReset();
    mocks.flow = { input: 'Khám tổng quát', setInput: vi.fn(), loading: false, submittingBooking: false, errorMsg: '', messages: [], activeDraft: null,
        clearChat: vi.fn(), handleSendMessage: mocks.send, handleSuggestion: vi.fn(), wizard: null, handleWizardStep: vi.fn(),
        handleActionClick: mocks.action, confirmToolAction: vi.fn(), cancelToolAction: vi.fn(), formatVietnameseDate: (s: string) => s,
        retryLastRequest: mocks.retry, canRetry: false, retryAfterSeconds: 0 };
    HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(() => vi.useRealTimers());

it('B1 shows compact grounded data and one emergency notice without repeating its copy', () => {
    mocks.flow.messages = [{ role: 'model', content: 'Gọi ngay 115.', urgency: 'EMERGENCY', safetyNotice: 'Gọi ngay 115.',
        toolResults: [{ status: 'completed', resultType: 'specialties', data: [{ id: 1, name: 'Nội khoa' }] }, { status: 'failed', error: { message: 'Không thể tải dữ liệu.' } }] }];
    mount();
    expect(within(document.querySelector('[data-chat-bubble]') as HTMLElement).getAllByText('Gọi ngay 115.')).toHaveLength(1);
    expect(screen.queryByText('Dữ liệu từ hệ thống ClinicCare')).not.toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Dữ liệu đã kiểm chứng' })).toHaveTextContent('Nội khoa');
    expect(screen.getByText('Không thể tải dữ liệu.')).toBeInTheDocument();
});
it('B2 has one whole specialty button with the existing send payload', () => {
    mocks.flow.messages = [{ role: 'model', content: 'Chọn khoa', suggestions: [{ specialtyId: 7, specialtyName: 'Nội khoa', reason: 'Khám tổng quát' }] }];
    mount();
    fireEvent.click(screen.getByRole('button', { name: 'Xem lịch khám khoa Nội khoa' }));
    expect(mocks.send).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ specialtyId: 7 }));
    expect(screen.queryByText('Chi tiết khoa')).not.toBeInTheDocument();
    expect(screen.queryByText('Phù hợp tham khảo')).not.toBeInTheDocument();
    expect(screen.getByText('Chạm vào một khoa để xem lịch khám.')).toBeInTheDocument();
});
it('B3 hides all stale actions once while retaining old navigation', () => {
    mocks.flow.activeDraft = { version: 2, isComplete: false };
    mocks.flow.messages = [{ role: 'model', content: 'Cũ', actions: [action('SelectDoctor'), action('ReviewBooking'), action('ViewMyAppointments')] }, { role: 'model', content: 'Mới' }];
    mount();
    expect(screen.queryByRole('button', { name: /ReviewBooking/ })).not.toBeInTheDocument();
    expect(screen.getAllByText('Lựa chọn ở bước trước ✓')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'ViewMyAppointments' })).toBeInTheDocument();
});
it('B4 collapses older history and toggles it', () => {
    mocks.flow.messages = Array.from({ length: 8 }, (_, i) => ({ role: i % 2 ? 'model' : 'user', content: `Tin ${i}` }));
    mount();
    expect(screen.queryByText('Tin 0')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Xem 4 tin nhắn trước' }));
    expect(screen.getByText('Tin 0')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Thu gọn' }));
    expect(screen.queryByText('Tin 0')).not.toBeInTheDocument();
});
it('B4 isolates the active wizard and puts back before its progress', () => {
    mocks.flow.messages = [{ role: 'user', content: 'Tin trước wizard' }];
    mocks.flow.wizard = { step: 'slot', title: 'Chọn giờ', message: 'Chọn giờ khám', options: [], canGoBack: true, backToken: 'opaque', summary: { specialtyName: 'Nội khoa' } };
    mount();
    expect(screen.queryByText('Tin trước wizard')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Xem 1 tin nhắn trước' })).toBeInTheDocument();
    const region = screen.getByRole('region', { name: 'Đặt lịch khám từng bước' });
    expect(region.firstElementChild).toBe(screen.getByRole('button', { name: 'Quay lại bước trước' }));
    expect(region.textContent).not.toContain('✓');
});
it('1 welcomes the patient without the old quick prompt heading', () => {
    mount();
    expect(screen.getByText(/Xin chào, An! Mình có thể giúp bạn đặt lịch khám/)).toBeInTheDocument();
    expect(screen.queryByText('Gợi ý câu hỏi nhanh:')).not.toBeInTheDocument();
});
it('2 announces loading and prevents Send and Enter during booking submission', () => {
    mocks.flow.loading = true; mocks.flow.submittingBooking = true;
    mount();
    expect(screen.getByRole('status', { name: 'Trợ lý đang trả lời' })).toHaveTextContent('Trợ lý đang trả lời…');
    expect(screen.getByRole('button', { name: 'Gửi tin nhắn' })).toBeDisabled();
    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' });
    expect(mocks.send).not.toHaveBeenCalled();
});
it('3 preserves reading position, reveals new-message control, and keeps input focus unchanged', () => {
    mocks.flow.messages = [{ role: 'model', content: 'Đầu tiên' }];
    const view = mount();
    const area = document.querySelector('[data-chat-messages]')!;
    Object.defineProperties(area, { scrollHeight: { configurable: true, value: 1000 }, clientHeight: { configurable: true, value: 200 }, scrollTop: { configurable: true, writable: true, value: 100 } });
    fireEvent.scroll(area);
    const other = screen.getByRole('button', { name: 'Làm mới cuộc trò chuyện' }); other.focus();
    vi.mocked(HTMLElement.prototype.scrollIntoView).mockClear();
    mocks.flow.messages = [...mocks.flow.messages, { role: 'model', content: 'Mới đến' }];
    view.rerender(<MemoryRouter><MedicalChatWidget /></MemoryRouter>);
    expect(HTMLElement.prototype.scrollIntoView).not.toHaveBeenCalled();
    expect(other).toHaveFocus();
    fireEvent.click(screen.getByRole('button', { name: 'Có tin nhắn mới' }));
    expect(HTMLElement.prototype.scrollIntoView).toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Có tin nhắn mới' })).not.toBeInTheDocument();
});
it('4 displays guarded retry on the latest error and error line', () => {
    mocks.flow.messages = [{ role: 'model', content: 'Lỗi đọc dữ liệu', isError: true }];
    mocks.flow.errorMsg = 'Lỗi đọc dữ liệu'; mocks.flow.canRetry = true; mocks.flow.retryAfterSeconds = 2;
    const view = mount();
    expect(screen.getAllByRole('button', { name: 'Thử lại sau 2 giây' })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: 'Thử lại sau 2 giây' }).every(b => (b as HTMLButtonElement).disabled)).toBe(true);
    mocks.flow.retryAfterSeconds = 0;
    view.rerender(<MemoryRouter><MedicalChatWidget /></MemoryRouter>);
    fireEvent.click(screen.getAllByRole('button', { name: 'Thử lại' })[0]);
    expect(mocks.retry).toHaveBeenCalledOnce();
});
it('5 hides technical detail and explains fallback outside the status element', () => {
    mocks.flow.messages = [{ role: 'model', content: 'Hỗ trợ nội bộ', providerState: 'Disabled', executionMode: 'Deterministic', fallbackActive: true }];
    mount();
    expect(screen.queryByText('Deterministic')).not.toBeInTheDocument();
    expect(screen.queryByText(/Trợ lý đang ở chế độ nội bộ nên chỉ hiểu một số câu/)).not.toBeInTheDocument();
    expect(document.querySelector('[data-provider-status]')).toHaveAttribute('data-tone', 'local');
});
it('6 shows a single success card action with the upcoming highlight URL', () => {
    mocks.flow.messages = [{ role: 'model', content: 'Đặt lịch khám thành công.', bookingResult: { appointmentId: 21, appointmentCode: 'APT21', doctorName: 'Bác sĩ An', specialtyName: 'Nội khoa', facilityName: 'Cơ sở A', slotDate: '2027-02-03', startTime: '09:00', reason: 'Khám tổng quát' } }];
    mount();
    const button = screen.getByRole('button', { name: 'Xem lịch sắp khám' });
    fireEvent.click(button);
    expect(mocks.action).toHaveBeenCalledWith(expect.objectContaining({ type: 'ViewMyAppointments', payload: { targetUrl: '/patient/appointments?tab=upcoming&appointmentId=21' } }));
    expect(screen.getByText(/03\/02\/2027/)).toBeInTheDocument();
    expect(screen.queryByText('Xem danh sách lịch hẹn của tôi')).not.toBeInTheDocument();
});
it('7 presents the new history and specialty controls as buttons without nested controls', () => {
    mocks.flow.messages = Array.from({ length: 7 }, () => ({ role: 'model', content: 'Khám', suggestions: [{ specialtyId: 1, specialtyName: 'Nội khoa', reason: 'Tham khảo' }] }));
    mount();
    expect(screen.getByRole('button', { name: 'Xem 3 tin nhắn trước' })).toBeInTheDocument();
    screen.getAllByRole('button', { name: 'Xem lịch khám khoa Nội khoa' }).forEach(b => expect(b.querySelector('button')).toBeNull());
});
it('8 announces only 200 plain characters, not the whole message history', () => {
    mocks.flow.messages = [{ role: 'model', content: '**' + 'a'.repeat(220) + '**' }];
    mount();
    expect(document.querySelector('[data-chat-messages]')).not.toHaveAttribute('aria-live');
    const live = document.querySelector('[data-chat-announcement]')!;
    expect(live).toHaveAttribute('aria-live', 'polite');
    expect(live).toHaveTextContent('a'.repeat(200));
});
