import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ user: { role: 'Patient', userId: 'filtered-patient' }, identityVersion: 1, isAuthenticated: true })
}));
vi.mock('../api/axiosClient', () => ({ default: { post } }));

const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
    post.mockResolvedValue({ success: true, data: { message: 'Đã tải', actions: [], cards: [], suggestions: [], step: 'day' } });
});

it.each([
    'xem hóa đơn ngày 17/10/2026',
    'hóa đơn chưa thanh toán của tôi',
    'đơn thuốc gần nhất của tôi',
    'kết quả xét nghiệm công thức máu của tôi',
    'kết quả x-quang',
    'lượt khám từ ngày 1/10/2026 đến ngày 31/10/2026',
    'lịch hẹn sắp tới của tôi'
])('sends the complete filtered request to the patient chat endpoint for %s', async message => {
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage(message));

    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/ai/chat', expect.objectContaining({ message }), expect.anything());
    expect(post.mock.calls[0][1]).not.toHaveProperty('suggestionCode');
});

it('uses the typed schedule question to start the booking wizard with the named doctor', async () => {
    post.mockResolvedValueOnce({ success: true, data: { step: 'day', textToken: 'day-token' } });
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage('bác sĩ Khải khám ngày nào'));

    expect(post).toHaveBeenCalledWith('/ai/booking-wizard',
        expect.objectContaining({ step: 'start', hint: 'bác sĩ Khải khám ngày nào' }), expect.anything());

    const price = 'khám nội tổng quát giá bao nhiêu';
    await act(async () => result.current.handleSendMessage(price));
    expect(post).toHaveBeenLastCalledWith('/ai/chat', expect.objectContaining({ message: price }), expect.anything());
});

it('renders a prescription code and Vietnam prescribing date with the legacy fallback', () => {
    render(<>{renderCopilotCardData({
        type: 'patient_prescriptions', title: 'Đơn thuốc', data: [
            { prescriptionCode: 'RX-261102-0042', prescribedDate: '2026-11-02' },
            { createdAt: '2026-10-15T23:00:00Z' }
        ]
    })}</>);
    expect(screen.getByText('RX-261102-0042 — 02/11/2026')).toBeInTheDocument();
    expect(screen.getByText('Đơn thuốc của bạn')).toBeInTheDocument();
});

it('prepares cancellation from the selected appointment and leaves it awaiting confirmation', async () => {
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleActionClick({
        id: 'patient-change-cancel-42', type: 'RequestCancellation', label: 'Yêu cầu hủy',
        style: 'secondary', requiresAuthentication: true, requiresConfirmation: false,
        payload: { appointmentId: 42, appointmentCode: 'APT-42', targetUrl: '/patient/appointments' }
    }));
    const card = result.current.messages.at(-1)?.copilotCards?.[0];
    expect(card).toBeDefined();
    render(<>{renderCopilotCardData(card!)}</>);
    post.mockResolvedValueOnce({ status: 'pending_confirmation', actionId: 'preview-42', data: { concurrencyToken: 'token' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Lý do yêu cầu' }), { target: { value: 'Có lịch công tác' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xem trước yêu cầu' }));

    await waitFor(() => expect(result.current.messages.at(-1)?.toolResults?.[0]).toMatchObject({
        status: 'pending_confirmation', actionId: 'preview-42', data: { concurrencyToken: 'token' }
    }));
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/ai/copilot/actions/prepare', expect.objectContaining({
        toolName: 'patient.prepare_cancel_appointment',
        argumentsJson: JSON.stringify({ appointmentId: 42, reason: 'Có lịch công tác' })
    }), expect.anything());
});
