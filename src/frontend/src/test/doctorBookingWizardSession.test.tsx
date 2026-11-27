import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';
import type { StartBookingAction } from '../types/ai';

const post = vi.hoisted(() => vi.fn());
vi.mock('../api/axiosClient', () => ({ default: { post } }));
vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ isAuthenticated: true, user: { userId: 'doctor-wizard-session-patient', role: 'Patient' } })
}));

beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    post.mockReset();
});

it('keeps the new wizard session when the doctor action contains null session metadata', async () => {
    post.mockResolvedValueOnce({
        success: true,
        data: { step: 'day', options: [], summary: { doctorName: 'Lý Kim Dung', specialtyName: 'Nội tiết' } }
    });
    const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    const payload = { doctorId: 7, specialtyId: 9, sessionId: null, reason: 'metadata from the action', draftVersion: 1 };
    const action: StartBookingAction = {
        id: 'act-book-this-doctor', type: 'StartBooking', label: 'Đặt lịch với bác sĩ này', style: 'primary',
        requiresAuthentication: true, requiresConfirmation: false, payload
    };
    await act(() => result.current.handleActionClick(action));

    expect(post).toHaveBeenCalledExactlyOnceWith('/ai/booking-wizard', expect.objectContaining({
        sessionId: expect.stringMatching(/^sess_/), step: 'start', doctorId: 7, specialtyId: 9
    }), expect.anything());
    expect(post.mock.calls[0][1].reason).toBeUndefined();
    expect(post.mock.calls[0][1]).not.toHaveProperty('draftVersion');
    expect(result.current.wizard?.summary?.doctorName).toBe('Lý Kim Dung');
});
