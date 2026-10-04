import { act, renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';

type Intent = 'booking' | 'appointments' | 'visits' | 'prescriptions' | 'results' | 'bills' | null;
// Glob allows the regression table to run before the new module exists on main.
const modules = import.meta.glob<{ classifyPatientTypedIntent: (text: string) => Intent }>('../components/copilot/patientTypedIntent.ts', { eager: true });
const classify = modules['../components/copilot/patientTypedIntent.ts']?.classifyPatientTypedIntent;
export const typedCases: [string, Intent][] = [
    ['hôm nay tôi có lịch khám khồn', 'appointments'], ['lich hen cua toi', 'appointments'],
    ['toa thuốc của mình đâu', 'prescriptions'], ['xem kết quả xét nghiệm', 'results'],
    ['hóa đơn tháng này', 'bills'], ['lịch sử khám', 'visits'],
    ['Tôi muốn đặt khám với bác sĩ Lan', 'booking'], ['đặt lịch khám', 'booking'],
    ['tôi bị đau đầu muốn đặt lịch', null], ['đau ngực dữ dội khó thở', null],
    ['lịch khám của bác sĩ Lan', null], ['xin chào', null], ['a'.repeat(150), null],
    ['ĐẶT, LỊCH!', 'booking'], ['đăng ký khám', 'booking'], ['muốn khám', 'booking'],
    ['hẹn khám', 'booking'], ['khám bệnh', 'booking'], ['book lịch', 'booking'],
    ['lượt khám gần nhất', 'visits'], ['đơn thuốc hôm qua', 'prescriptions'],
    ['kết quả cận lâm sàng', 'results'], ['kết quả siêu âm', 'results'], ['kết quả chụp', 'results'],
    ['kết quả của tôi', 'results'], ['kết quả bóng đá', null], ['biên lai mới', 'bills'],
    ['viện phí của mình', 'bills'], ['lịch của tôi tuần này', 'appointments'],
    ['lịcch hẹn tuần này', 'appointments'], ['xem đon thuốcc', 'prescriptions'],
    ['lịch trình', null], ['lịch tuần này khám', null], ['lịch khám khoa nội', null],
    ['đặt lịch vì sốt', null], ['ho muốn khám', null], ['mệt muốn khám', null],
    ['chóng mặt đặt lịch', null], ['buồn nôn đặt lịch', null], ['ngứa đặt lịch', null],
    ['sưng đặt lịch', null], ['tiêu chảy đặt lịch', null], ['tức ngực đặt lịch', null],
    ['nhức đầu đặt lịch', null], ['ngất đặt lịch', null], ['bất tỉnh đặt lịch', null],
    ['co giật đặt lịch', null], ['chảy máu đặt lịch', null], ['đột quỵ đặt lịch', null],
    ['liệt đặt lịch', null], ['115 đặt lịch', null], ['lịchh hẹn đặt lịch', 'booking'],
    ['xem hoa doon', 'bills'], ['lịch hẹn'.padEnd(120, ' '), 'appointments'],
    ['lịch hẹn'.padEnd(121, ' '), null], ['thuốc', null], ['nôn muốn khám', null],
    ['hôm nay muốn khám vì đau', null], ['lichx hen', 'appointments'], ['lichxx hen', null],
];
it.each(typedCases)('T classify %s -> %s', (text, intent) => {
    expect(classify, 'typed classifier must exist').toBeTypeOf('function');
    expect(classify!(text)).toBe(intent);
});

// Preserve legacy contract tests through null inputs, and separately prove
// that every original input now reaches its specified typed-intent endpoint.
export const migratedLegacyInputs = [
    { line: 625, original: 'Tôi muốn xem hóa đơn', replacement: 'Cho tôi xem thông tin thanh toán', intent: 'bills' },
    { line: 1283, original: 'Đặt lịch tim mạch', replacement: 'Tôi đau đầu, Đặt lịch tim mạch', intent: 'booking' },
    { line: 1352, original: 'Đặt lịch khám tim', replacement: 'Tôi đau đầu, Đặt lịch khám tim', intent: 'booking' },
    { line: 1360, original: 'Hủy đặt lịch', replacement: 'Hủy bản nháp hiện tại', intent: 'booking' },
    { line: 1542, original: 'Đặt lịch khám tim mạch', replacement: 'Tôi đau đầu, Đặt lịch khám tim mạch', intent: 'booking' },
    { line: 1700, original: 'Đặt lịch A', replacement: 'Tôi đau đầu, Đặt lịch A', intent: 'booking' },
    { line: 1709, original: 'Hủy đặt lịch', replacement: 'Hủy bản nháp hiện tại', intent: 'booking' },
    { line: 2222, original: 'Tôi muốn đặt lịch khám tim mạch', replacement: 'Tôi đau đầu, Tôi muốn đặt lịch khám tim mạch', intent: 'booking' },
    { line: 2416, original: 'Đặt lịch khám', replacement: 'Tôi đau đầu, Đặt lịch khám', intent: 'booking' },
    { line: 2426, original: 'Hủy đặt lịch', replacement: 'Hủy bản nháp hiện tại', intent: 'booking' },
    { line: 2435, original: 'Đặt lịch khám lại', replacement: 'Tôi đau đầu, Đặt lịch khám lại', intent: 'booking' },
    { line: 2517, original: 'Đặt lịch', replacement: 'Tôi đau đầu, Đặt lịch', intent: 'booking' },
    { line: 2683, original: 'Đặt lịch khám', replacement: 'Tôi đau đầu, Đặt lịch khám', intent: 'booking' },
    { line: 2839, original: 'Đặt lịch 9h', replacement: 'Tôi đau đầu, Đặt lịch 9h', intent: 'booking' },
] as const;
it.each(migratedLegacyInputs)('T legacy line $line: null replacement, original $original -> $intent', async ({ original, replacement, intent }) => {
    expect(classify, 'typed classifier must exist').toBeTypeOf('function');
    expect(classify!(replacement)).toBeNull();
    expect(classify!(original)).toBe(intent);
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage(replacement));
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/ai/chat', expect.objectContaining({ message: replacement }), expect.anything());
    post.mockClear();
    await act(async () => result.current.handleSendMessage(original));
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(intent === 'booking' ? '/ai/booking-wizard' : '/ai/copilot/chat',
        expect.objectContaining(intent === 'booking' ? { step: 'start' } : { message: original, suggestionCode: 'patient.my_bills' }), expect.anything());
});

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Patient', userId: 'typed-intent' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
beforeEach(() => {
    localStorage.clear(); sessionStorage.clear(); post.mockReset();
    post.mockResolvedValue({ success: true, data: { message: 'Đã tải', actions: [], cards: [], suggestions: [], step: 'specialty' } });
});
it.each([
    ['Tôi muốn đặt khám với bác sĩ Lan', 'booking', undefined],
    ['hôm nay tôi có lịch khám khồn', 'appointments', 'patient.my_appointments'],
    ['lịch sử khám', 'visits', 'patient.my_visits'],
    ['toa thuốc của mình đâu', 'prescriptions', 'patient.my_prescriptions'],
    ['xem kết quả xét nghiệm', 'results', 'patient.my_diagnostic_results'],
    ['hóa đơn tháng này', 'bills', 'patient.my_bills'],
] as const)('T routes typed %s to %s', async (text, intent, code) => {
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.setInput(text));
    await act(async () => result.current.handleSendMessage(text));
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith(intent === 'booking' ? '/ai/booking-wizard' : '/ai/copilot/chat',
        expect.objectContaining(intent === 'booking' ? { step: 'start' } : { message: text, suggestionCode: code }), expect.anything());
    expect(result.current.input).toBe('');
    expect(result.current.messages.filter(m => m.role === 'user').map(m => m.content)).toEqual([text]);
    // In the same conversation, a null intent still uses the legacy symptom path.
    await act(async () => result.current.handleSendMessage('tôi bị đau đầu muốn đặt lịch'));
    expect(post.mock.calls[1][0]).toBe('/ai/chat');
    if (intent === 'booking') {
        for (const fallback of ['đau ngực dữ dội khó thở', 'lịch khám của bác sĩ Lan', 'xin chào', 'a'.repeat(150)]) {
            await act(async () => result.current.handleSendMessage(fallback));
            expect(post.mock.calls.at(-1)?.[0]).toBe('/ai/chat');
        }
        await act(async () => result.current.handleSendMessage('đặt lịch khám'));
        expect(post.mock.calls.at(-1)).toEqual(['/ai/booking-wizard', expect.objectContaining({ step: 'start' }), expect.anything()]);
        for (const alias of ['lich hen cua toi', 'Lịch hẹn của mình', 'tôi có quyền hạn gì']) {
            await act(async () => result.current.handleSendMessage(alias));
            expect(post.mock.calls.at(-1)).toEqual(['/ai/copilot/chat', expect.objectContaining({ message: alias, suggestionCode: undefined }), expect.anything()]);
        }
    }
});
