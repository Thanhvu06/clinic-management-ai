import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { ChatProvider } from '../contexts/ChatContext';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ isAuthenticated: true, user: { role: 'Patient', userId: 'cancel-form-patient' } })
}));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../components/copilot/useSuggestionMenu', async importOriginal => ({
    ...await importOriginal<object>(), useSuggestionMenu: () => []
}));

it('renders the cancellation form from a chat result and waits for confirmation before executing', async () => {
    const sessionId = 'sess_cancel_form';
    const appointmentCode = 'APT-261127-12ABC345';
    post.mockResolvedValueOnce({
        success: true,
        data: {
            sessionId,
            message: 'Bạn có chắc muốn hủy lịch này không?',
            actions: [{
                id: 'patient-change-cancel-12', label: 'Hủy lịch này',
                type: 'RequestCancellation', style: 'secondary',
                payload: { appointmentId: 12, appointmentCode, targetUrl: '/patient/appointments' }
            }]
        }
    }).mockResolvedValueOnce({
        status: 'pending_confirmation',
        actionId: 'cancel-action',
        data: { concurrencyToken: 'cancel-token', immediate: true }
    }).mockResolvedValueOnce({
        status: 'completed',
        resultType: 'change_request',
        data: { cancelled: true, appointmentCode }
    });

    HTMLElement.prototype.scrollIntoView = vi.fn();
    render(<MemoryRouter><ChatProvider><MedicalChatWidget /></ChatProvider></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung tin nhắn gửi tới ClinicCare AI' }),
        { target: { value: `hủy lịch hẹn mã ${appointmentCode}` } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi tin nhắn' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Hủy lịch này' }));

    fireEvent.change(screen.getByRole('textbox', { name: 'Lý do yêu cầu' }), { target: { value: 'Bận việc gia đình' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xem trước yêu cầu' }));
    const confirm = await screen.findByRole('button', { name: 'Xác nhận thực hiện' });
    expect(post).toHaveBeenCalledTimes(2);
    expect(post).toHaveBeenLastCalledWith('/ai/copilot/actions/prepare', expect.objectContaining({ sessionId }), expect.anything());

    fireEvent.click(confirm);
    expect(await screen.findByText(`• Lịch hẹn ${appointmentCode} đã được hủy`)).toBeVisible();
    expect(post).toHaveBeenLastCalledWith('/ai/tool-actions/cancel-action/confirm',
        { sessionId, concurrencyToken: 'cancel-token' }, expect.anything());
});
