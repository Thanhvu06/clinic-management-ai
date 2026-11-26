import { act, render, renderHook, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import { BookingWizard } from '../components/BookingWizard';
import type { AiBookingWizardResponse } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Patient', userId: 'lock-chip' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));

const base = (over: Partial<AiBookingWizardResponse>): AiBookingWizardResponse => ({
    step: 'slot', title: 'Chọn giờ khám', message: 'Chọn khung giờ.', options: [], canGoBack: false, actions: [], suggestions: [],
    assistantMode: 'Ready', providerWasCalled: false, ...over,
});

describe('booking wizard summary date (J2)', () => {
    it('shows the chosen day as dd/MM/yyyy, never the raw ISO date', () => {
        render(<BookingWizard busy={false} onStep={async () => {}} state={base({
            summary: { specialtyName: 'Nội tổng quát', doctorName: 'BS. Kiểm thử', slotDate: '2026-11-27', reasonProvided: false },
        })} />);
        expect(screen.getByText('Ngày: 27/11/2026')).toBeInTheDocument();
        expect(screen.queryByText(/2026-11-27/)).not.toBeInTheDocument();
    });
});

const blocked = base({
    step: 'stopped', title: 'Tạm khóa đặt lịch online', errorCode: 'ONLINE_BOOKING_BLOCKED',
    message: 'Bạn đã vắng mặt ít nhất 3 lần trong 90 ngày. Vui lòng liên hệ lễ tân để đặt lịch.',
});
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const chip = { code: 'patient.start_booking', label: 'Đặt lịch khám' };

describe('booking chip for a no-show locked patient (J3)', () => {
    beforeEach(() => {
        localStorage.clear(); sessionStorage.clear(); post.mockReset();
    });

    it('answers every chip click with the user turn and the lock card, like a typed request', async () => {
        post.mockResolvedValue({ success: true, data: blocked });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });

        await act(async () => result.current.handleSuggestion(chip));
        expect(result.current.wizard?.errorCode).toBe('ONLINE_BOOKING_BLOCKED');
        expect(result.current.messages.filter(m => m.role === 'user' && m.content === 'Đặt lịch khám')).toHaveLength(1);

        await act(async () => result.current.handleSuggestion(chip));
        expect(result.current.wizard?.title).toBe('Tạm khóa đặt lịch online');
        expect(result.current.messages.filter(m => m.role === 'user' && m.content === 'Đặt lịch khám')).toHaveLength(2);
        expect(post.mock.calls.filter(([url]) => url === '/ai/booking-wizard')).toHaveLength(2);
    });

    it('keeps the unlocked chip start unchanged (no extra user turn)', async () => {
        post.mockResolvedValue({ success: true, data: base({ step: 'specialty', title: 'Chọn chuyên khoa' }) });
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        const before = result.current.messages.length;
        await act(async () => result.current.handleSuggestion(chip));
        expect(result.current.wizard?.step).toBe('specialty');
        expect(result.current.messages).toHaveLength(before);
    });

    it('renders the lock card with the reception contact', () => {
        render(<MemoryRouter><BookingWizard busy={false} onStep={async () => {}} state={blocked} /></MemoryRouter>);
        expect(screen.getByRole('heading', { name: 'Tạm khóa đặt lịch online' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Liên hệ lễ tân' })).toHaveAttribute('href', '/locations');
    });
});
