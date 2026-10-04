import { act, renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { ReactNode } from 'react';
import type { ReviewBookingAction } from '../types/ai';
const post = vi.hoisted(() => vi.fn());
const copilot = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Patient', userId: 'retry-patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../api/aiCopilotApi', () => ({ sendRoleCopilotMessage: copilot }));
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
beforeEach(() => { localStorage.clear(); sessionStorage.clear(); post.mockReset(); copilot.mockReset(); });
it.each(['message', 'suggestion', 'wizard'])('4 retries the failed %s request, preserves its selection, and clears failure on success', async kind => {
    const api = kind === 'suggestion' ? copilot : post;
    api.mockRejectedValueOnce({ message: 'Mạng không kết nối', retryAfterSeconds: 0 });
    api.mockResolvedValueOnce(kind === 'suggestion' ? { message: 'Đã tải', cards: [], suggestions: [] } : { success: true, data: kind === 'wizard' ? { step: 'slot', title: 'Chọn giờ', message: 'Tiếp tục', options: [] } : { message: 'Đã tải', actions: [] } });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => {
        if (kind === 'wizard') await result.current.handleWizardStep('pick', 'opaque-failed-token');
        else if (kind === 'suggestion') await result.current.handleSuggestion({ code: 'patient.get_my_bills', label: 'Hóa đơn' });
        else await result.current.handleSendMessage('Khám nội khoa', { specialtyId: 7 });
    });
    expect((result.current as any).canRetry).toBe(true);
    await act(async () => (result.current as any).retryLastRequest());
    expect(api).toHaveBeenCalledTimes(2);
    if (kind === 'wizard') expect(post.mock.calls[1][1]).toMatchObject({ step: 'pick', optionToken: 'opaque-failed-token', sessionId: post.mock.calls[0][1].sessionId });
    if (kind === 'message') expect(post.mock.calls[1][1]).toMatchObject({ message: 'Khám nội khoa', pendingSpecialtyId: 7 });
    if (kind === 'suggestion') expect(copilot.mock.calls[1][0]).toMatchObject({ suggestionCode: 'patient.get_my_bills' });
    expect((result.current as any).canRetry).toBe(false);
    expect(result.current.errorMsg).toBe('');
});
it('4 counts down retryAfterSeconds and does not retry while locked', async () => {
    vi.useFakeTimers();
    try {
        post.mockRejectedValueOnce({ message: 'Quá nhiều yêu cầu', retryAfterSeconds: 2 });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleWizardStep('pick', 'opaque'));
        expect((result.current as any).retryAfterSeconds).toBe(2);
        await act(async () => (result.current as any).retryLastRequest());
        expect(post).toHaveBeenCalledTimes(1);
        await act(async () => vi.advanceTimersByTime(2000));
        expect((result.current as any).retryAfterSeconds).toBe(0);
    } finally { vi.useRealTimers(); }
});
it('4 honors a retryable assistant failure returned in an HTTP 200 envelope', async () => {
    post.mockResolvedValueOnce({ success: true, data: { message: 'Trợ lý phản hồi quá lâu.', providerState: 'Degraded', retryable: true, retryAfterSeconds: 2, actions: [] } });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage('Khám tổng quát'));
    expect((result.current as any).canRetry).toBe(true);
    expect((result.current as any).retryAfterSeconds).toBe(2);
    expect(result.current.messages.at(-1)).toMatchObject({ isError: true });
});
it.each([true, false])('6 creates the structured success result and excludes failed appointment confirmations from retry (success=%j)', async success => {
    const review: ReviewBookingAction = { id: 'review', type: 'ReviewBooking', label: 'Review', style: 'secondary', requiresAuthentication: true, requiresConfirmation: false, draftVersion: 1,
        payload: { specialtyId: 7, doctorId: 8, slotId: 9, slotDate: '2027-02-03', startTime: '09:00', endTime: '09:30', reason: 'Khám tổng quát', specialtyName: 'Nội khoa', doctorName: 'Bác sĩ An', facilityId: 10, facilityName: 'Cơ sở A', draftVersion: 1, draftId: 'draft', confirmationId: 'confirmation', contextSnapshotId: 'snapshot', sessionId: 'sess-draft' } };
    post.mockResolvedValueOnce({ success: true, data: { step: 'review', title: 'Xem lại', message: 'Xem lại thông tin', options: [], reviewAction: review } });
    if (success) post.mockResolvedValueOnce({ success: true, data: { id: 21, appointmentCode: 'APT21', doctorName: 'Bác sĩ đã lưu', specialtyName: 'Khoa đã lưu', appointmentDate: '2027-02-03', startTime: '09:00', endTime: '09:30', reason: 'Khám tổng quát' } });
    else post.mockRejectedValueOnce({ message: 'Timeout' });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleWizardStep('reason', 'opaque-reason', 'Khám tổng quát'));
    await act(async () => result.current.handleActionClick(review));
    const confirm = result.current.messages.at(-1)!.actions![0];
    await act(async () => result.current.handleActionClick(confirm));
    expect((result.current as any).canRetry).toBe(false);
    if (success) {
        expect(result.current.messages.at(-1)).toMatchObject({ content: 'Đặt lịch khám thành công.', bookingResult: { appointmentId: 21, appointmentCode: 'APT21', doctorName: 'Bác sĩ đã lưu', specialtyName: 'Khoa đã lưu', facilityName: 'Cơ sở A', slotDate: '2027-02-03', startTime: '09:00', reason: 'Khám tổng quát' } });
        expect(result.current.wizard).toBeNull();
    }
    await act(async () => (result.current as any).retryLastRequest());
    expect(post).toHaveBeenCalledTimes(2);
});
it('4 excludes confirm and cancel tool mutations from retry', async () => {
    post.mockRejectedValue({ message: 'Timeout' });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.confirmToolAction('tool-action', 'concurrency-token'));
    expect((result.current as any).canRetry).toBe(false);
    await act(async () => result.current.cancelToolAction('tool-action'));
    expect((result.current as any).canRetry).toBe(false);
    await act(async () => (result.current as any).retryLastRequest());
    expect(post).toHaveBeenCalledTimes(2);
});
