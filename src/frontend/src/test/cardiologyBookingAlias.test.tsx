import { act, renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { classifyPatientTypedIntent, extractBookingHint } from '../components/copilot/patientTypedIntent';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Patient', userId: 'cardiology' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;

beforeEach(() => {
    localStorage.clear(); sessionStorage.clear(); post.mockReset();
    post.mockResolvedValue({ success: true, data: { step: 'doctor', message: 'Chọn bác sĩ', summary: { specialtyName: 'Tim mạch' }, options: [] } });
});

it.each(['tôi muốn đặt lịch khám tim', 'dat lich kham tim', 'đặt lịch khoa tim', 'đặt lịch bác sĩ tim', 'đặt lịch bệnh tim'])(
    'preserves the cardiology specialty when starting from %s', async message => {
        expect(extractBookingHint(message)).toBe('Tim mạch');
        const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
        await act(async () => result.current.handleSendMessage(message));
        expect(post).toHaveBeenCalledWith('/ai/booking-wizard', expect.objectContaining({ step: 'start', hint: 'Tim mạch' }), expect.anything());
    });

it.each(['tìm bác sĩ giỏi', 'tim', 'tìm', 'đau tim', 'đau ngực khó thở'])(
    'does not prefill cardiology or bypass chat safety for %s', message => {
        expect(extractBookingHint(message)).not.toBe('Tim mạch');
        expect(classifyPatientTypedIntent(message)).not.toBe('booking');
    });
