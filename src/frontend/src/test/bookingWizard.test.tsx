import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import type { AiBookingWizardResponse, ReviewBookingAction } from '../types/ai';

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
const mount = (_widget = true) => render(<MemoryRouter initialEntries={['/patient/ai-consultation']}><ChatProvider>
    <MedicalChatWidget />
</ChatProvider></MemoryRouter>);
const wizard = () => screen.getByRole('region', { name: 'Đặt lịch khám từng bước' });
const choose = async (name: string) => {
    await waitFor(() => expect(within(wizard()).getByRole('button', { name })).toBeEnabled());
    fireEvent.click(within(wizard()).getByRole('button', { name }));
};

describe('patient button booking wizard', () => {
    beforeEach(() => {
        post.mockReset(); localStorage.clear(); sessionStorage.clear();
        HTMLElement.prototype.scrollIntoView = vi.fn();
    });

    it.each([true])('carries the chosen facility through review and booking (widget=%j)', async widget => {
        const boundReview = { ...review, payload: { ...review.payload, facilityId: 991007, facilityName: 'Cơ sở B' } };
        const boundFlow = [...flow.slice(0, 4), response('facility', 'Cơ sở B'), response('reason'),
            { ...response('review'), reviewAction: boundReview, summary: { facilityName: 'Cơ sở B', requiresFacilitySelection: true, reasonProvided: true } }];
        boundFlow.forEach(data => post.mockResolvedValueOnce({ success: true, data }));
        post.mockResolvedValueOnce({ success: true, data: { id: 9911, appointmentCode: 'TEST-BOOKING', ...boundReview.payload } });
        mount(widget);
        if (widget) fireEvent.click(screen.getByRole('button', { name: /Mở.*trợ lý/i }));
        fireEvent.click(await screen.findByRole('button', { name: 'Gợi ý: Đặt lịch khám' }));
        for (const label of ['Khoa tổng hợp', 'Bác sĩ kiểm thử', '02/10/2026', '08:00 – 08:30', 'Cơ sở B']) await choose(label);
        fireEvent.change(await screen.findByRole('textbox', { name: 'Lý do khám từ 10 đến 500 ký tự' }), { target: { value: 'Khám tổng quát' } });
        fireEvent.click(within(wizard()).getByRole('button', { name: 'Xem lại thông tin khám' }));
        await screen.findByText('Cơ sở: Cơ sở B');
        fireEvent.click(screen.getByRole('button', { name: 'Xem tóm tắt thông tin khám' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận đặt lịch' }));
        await waitFor(() => expect(post).toHaveBeenCalledWith('/appointments', expect.objectContaining({ facilityId: 991007, confirmationId: 'conf-opaque' }), expect.anything()));
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
