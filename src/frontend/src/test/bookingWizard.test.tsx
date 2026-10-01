import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { PatientAiConsultation } from '../pages/patient/PatientAiConsultation';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import type { AiBookingWizardRequest, AiBookingWizardResponse, ReviewBookingAction } from '../types/ai';

const post = vi.fn<(url: string, body: unknown, config?: unknown) => Promise<unknown>>();
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { userId: 'synthetic-patient', role: 'Patient' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post: (url: string, body: unknown, config?: unknown) => post(url, body, config), get: vi.fn() } }));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({ role: 'Patient', suggestions: [{ code: 'patient.start_booking', label: 'Đặt lịch khám' }] }) }));

const review: ReviewBookingAction = {
    id: 'opaque-review', type: 'ReviewBooking', label: 'Xem tóm tắt thông tin khám', style: 'secondary',
    requiresAuthentication: true, requiresConfirmation: false, draftVersion: 1,
    payload: { specialtyId: 981001, doctorId: 981002, slotId: 981003, specialtyName: 'Khoa tổng hợp', doctorName: 'Bác sĩ kiểm thử',
        slotDate: '2026-10-02', startTime: '08:00', endTime: '08:30', reason: 'Khám tổng quát', draftVersion: 1,
        draftId: 'draft-opaque', sessionId: 'sess-opaque', contextSnapshotId: 'snap-opaque', confirmationId: 'conf-opaque' }
};
const response = (step: AiBookingWizardResponse['step'], label?: string): AiBookingWizardResponse => ({
    step, title: `Bước ${step}`, message: 'Chọn để tiếp tục.', options: label ? [{ token: `encrypted-${step}`, label }] : [],
    canGoBack: step !== 'specialty', backToken: `encrypted-back-${step}`, reasonToken: step === 'reason' ? 'encrypted-reason' : undefined,
    actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false,
    ...(step === 'review' ? { reviewAction: review } : {})
});
const flow = [response('specialty', 'Khoa tổng hợp'), response('doctor', 'Bác sĩ kiểm thử'), response('day', '02/10/2026'),
    response('slot', '08:00 – 08:30'), response('reason', 'Khám tổng quát'), response('review')];
const mount = (widget = false) => render(<MemoryRouter initialEntries={['/patient/ai-consultation']}><ChatProvider>
    {widget ? <MedicalChatWidget /> : <PatientAiConsultation />}
</ChatProvider></MemoryRouter>);
const wizard = () => screen.getByRole('region', { name: 'Đặt lịch khám từng bước' });
const mockFlow = () => flow.forEach(data => post.mockResolvedValueOnce({ success: true, data }));
const choose = async (name: string) => {
    await waitFor(() => expect(within(wizard()).getByRole('button', { name })).toBeEnabled());
    fireEvent.click(within(wizard()).getByRole('button', { name }));
};

describe('patient button booking wizard', () => {
    beforeEach(() => {
        post.mockReset(); localStorage.clear(); sessionStorage.clear();
        HTMLElement.prototype.scrollIntoView = vi.fn();
    });

    it('completes all steps with opaque tokens, shows the shared summary and uses existing review/confirm handlers', async () => {
        mockFlow();
        post.mockResolvedValueOnce({ success: true, data: { id: 9911, appointmentCode: 'TEST-BOOKING', ...review.payload } });
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        for (const label of ['Khoa tổng hợp', 'Bác sĩ kiểm thử', '02/10/2026', '08:00 – 08:30']) await choose(label);
        const reason = await screen.findByRole('textbox', { name: 'Lý do khám từ 10 đến 500 ký tự' });
        fireEvent.change(reason, { target: { value: 'Khám tổng quát' } });
        fireEvent.click(within(wizard()).getByRole('button', { name: 'Xem lại thông tin khám' }));
        await screen.findByText('Tóm tắt thông tin đặt lịch');
        expect(document.body.innerHTML).not.toMatch(/981001|981002|981003|encrypted-|conf-opaque|snap-opaque/);
        const calls = post.mock.calls.slice(0, 6).map(call => call[1] as AiBookingWizardRequest);
        expect(calls.map(call => call.step)).toEqual(['start', 'pick', 'pick', 'pick', 'pick', 'reason']);
        expect(calls.slice(1, 5).map(call => call.optionToken)).toEqual(['encrypted-specialty', 'encrypted-doctor', 'encrypted-day', 'encrypted-slot']);
        expect(calls[5]).toMatchObject({ optionToken: 'encrypted-reason', reason: 'Khám tổng quát' });
        fireEvent.click(screen.getByRole('button', { name: 'Xem tóm tắt thông tin khám' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận đặt lịch' }));
        await waitFor(() => expect(post).toHaveBeenCalledWith('/appointments', expect.objectContaining({
            doctorId: 981002, appointmentSlotId: 981003, confirmationId: 'conf-opaque', contextSnapshotId: 'snap-opaque', draftVersion: 1
        }), expect.objectContaining({ headers: { 'Idempotency-Key': expect.any(String) } })));
    });

    it('locks buttons and rejects double clicks until the pending request completes', async () => {
        let resolve: ((value: unknown) => void) | undefined;
        post.mockReturnValueOnce(new Promise(done => { resolve = done; }));
        mount();
        const start = await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' });
        fireEvent.click(start); fireEvent.click(start);
        expect(post).toHaveBeenCalledTimes(1);
        expect(start).toBeDisabled();
        await act(async () => resolve?.({ success: true, data: flow[0] }));
        expect(within(wizard()).getByRole('button', { name: 'Khoa tổng hợp' })).toBeEnabled();
    });

    it('supports back and restart without sending raw IDs', async () => {
        post.mockResolvedValueOnce({ success: true, data: flow[0] }).mockResolvedValueOnce({ success: true, data: flow[1] })
            .mockResolvedValueOnce({ success: true, data: flow[0] }).mockResolvedValueOnce({ success: true, data: flow[0] });
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        await choose('Khoa tổng hợp');
        await choose('Quay lại bước trước');
        await choose('Bắt đầu lại đặt lịch');
        await waitFor(() => expect(post).toHaveBeenCalledTimes(4));
        expect(post.mock.calls[2][1]).toMatchObject({ step: 'back', optionToken: 'encrypted-back-doctor' });
        expect(post.mock.calls[3][1]).toMatchObject({ step: 'start' });
        expect(JSON.stringify(post.mock.calls)).not.toContain('specialtyId');
    });

    it('enforces the visible reason minimum and submits by keyboard', async () => {
        post.mockResolvedValueOnce({ success: true, data: flow[4] }).mockResolvedValueOnce({ success: true, data: flow[5] });
        const user = userEvent.setup();
        mount();
        fireEvent.click(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        const input = await screen.findByRole('textbox', { name: 'Lý do khám từ 10 đến 500 ký tự' });
        await user.click(input); await user.type(input, 'Khám');
        const button = within(wizard()).getByRole('button', { name: 'Xem lại thông tin khám' });
        expect(button).toBeDisabled();
        await user.type(input, ' tổng quát'); await user.tab();
        expect(button).toHaveFocus();
        await user.keyboard('{Enter}');
        await waitFor(() => expect(post.mock.calls[1][1]).toMatchObject({ step: 'reason', reason: 'Khám tổng quát' }));
    });

    it('displays 429 errors and enables retry', async () => {
        post.mockRejectedValueOnce({ status: 429 });
        mount(); fireEvent.click(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        await screen.findByText(/Bạn đã gửi quá nhiều yêu cầu/);
        expect(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' })).toBeEnabled();
    });

    it('starts from the patient widget suggestion and presents emergency call 115', async () => {
        post.mockResolvedValueOnce({ success: true, data: { ...response('stopped'), title: 'Cấp cứu', errorCode: 'EMERGENCY', message: 'Gọi ngay 115.' } });
        mount(true);
        fireEvent.click(screen.getByRole('button', { name: /Mở.*trợ lý/i }));
        fireEvent.click(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        expect(await screen.findByRole('link', { name: 'Gọi cấp cứu 115' })).toHaveAttribute('href', 'tel:115');
        expect(post.mock.calls[0][1]).toMatchObject({ step: 'start' });
    });
});
