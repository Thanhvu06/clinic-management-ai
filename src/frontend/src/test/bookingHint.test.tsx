import { act, renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { useAiBookingFlow } from '../hooks/useAiBookingFlow';

type Extract = (text: string) => string | null;
// Glob keeps the table runnable (and failing) before the helper exists on main.
const modules = import.meta.glob<{ extractBookingHint?: Extract }>('../components/copilot/patientTypedIntent.ts', { eager: true });
const extract = modules['../components/copilot/patientTypedIntent.ts']?.extractBookingHint;

const hintCases: [string, string | null][] = [
    ['đặt khám với bác sĩ Lan', 'Lan'],
    ['dat kham voi bac si Lan', 'Lan'],
    ['Tôi muốn đặt khám với bác sĩ Lan', 'Lan'],
    ['hẹn khám tim mạch', 'tim mạch'],
    ['hen kham tim mach', 'tim mach'],
    ['đặt lịch khám khoa Da liễu ngày mai', 'Da liễu'],
    ['tôi muốn đặt lịch bs Nguyễn Văn An', 'Nguyễn Văn An'],
    ['đặt lịch chuyên khoa tai mũi họng cho tôi', 'tai mũi họng'],
    ['hẹn khám với bác sĩ Minh', 'Minh'],
    ['book lich kham bac si Hoa', 'Hoa'],
    ['đặt khám bác sĩ Lan sđt 0912345678', 'Lan'],
    ['đặt lịch APT-261004-511E130 bác sĩ Lan', 'Lan'],
    ['Đặt lịch khám', null],
    ['đặt lịch cho tôi ngày mai', null],
    ['đặt khám bác sĩ', null],
    ['đặt lịch khám bác sĩ 0912345678', null],
    ['dat lich kham', null],
    ['đặt khám bác sĩ ' + 'a'.repeat(120), null],
];

it.each(hintCases)('extractBookingHint(%s) -> %s', (text, expected) => {
    expect(extract, 'extractBookingHint must exist').toBeTypeOf('function');
    expect(extract!(text)).toBe(expected);
});

it('never returns digits, record codes or more than six words', () => {
    expect(extract, 'extractBookingHint must exist').toBeTypeOf('function');
    for (const text of ['đặt khám bác sĩ Lan 0987654321', 'hẹn khám khoa nhi REG-2026-77 ngày mai', 'đặt lịch bác sĩ An 123456']) {
        expect(extract!(text) ?? '').not.toMatch(/\d|-/);
    }
    expect(extract!('đặt khám bác sĩ một hai ba bốn năm sáu bảy')).toBeNull();
});

const post = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { role: 'Patient', userId: 'hint' }, identityVersion: 1, isAuthenticated: true }) }));
vi.mock('../api/axiosClient', () => ({ default: { post } }));
const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter><ChatProvider>{children}</ChatProvider></MemoryRouter>;
const wizardBodies = () => post.mock.calls.filter(([url]) => url === '/ai/booking-wizard').map(([, body]) => body as Record<string, unknown>);

beforeEach(() => {
    localStorage.clear(); sessionStorage.clear(); post.mockReset();
    post.mockResolvedValue({ success: true, data: { step: 'day', title: 'Chọn ngày khám', message: 'Chọn ngày', options: [], actions: [], suggestions: [] } });
});

it('sends the hint once with the typed booking start only', async () => {
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage('Tôi muốn đặt khám với bác sĩ Lan'));
    expect(wizardBodies()).toEqual([expect.objectContaining({ step: 'start', hint: 'Lan' })]);

    await act(async () => result.current.handleWizardStep('back', 'opaque-token'));
    expect('hint' in wizardBodies()[1]).toBe(false);
    await act(async () => result.current.handleSendMessage('đặt lịch khám'));
    expect(wizardBodies()[2]).toEqual(expect.objectContaining({ step: 'start' }));
    expect('hint' in wizardBodies()[2]).toBe(false);
    expect(wizardBodies().filter(body => 'hint' in body)).toHaveLength(1);
});

it('starts from the booking chip without a hint, unlike a typed start', async () => {
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSuggestion({ code: 'patient.start_booking', label: 'Đặt lịch khám' }));
    await act(async () => result.current.handleSendMessage('đặt khám với bác sĩ Lan'));
    await act(async () => result.current.handleSuggestion({ code: 'patient.start_booking', label: 'Đặt lịch khám' }));
    expect(wizardBodies().map(body => body.step)).toEqual(['start', 'start', 'start']);
    expect(wizardBodies().map(body => body.hint)).toEqual([undefined, 'Lan', undefined]);
    expect('hint' in wizardBodies()[0]).toBe(false);
    expect('hint' in wizardBodies()[2]).toBe(false);
});

it('retries a failed typed start without the hint', async () => {
    post.mockRejectedValueOnce(Object.assign(new Error('Network Error'), { code: 'ERR_NETWORK' }));
    const { result } = renderHook(() => useAiBookingFlow(), { wrapper });
    await act(async () => result.current.handleSendMessage('hẹn khám tim mạch'));
    expect(wizardBodies()[0]).toEqual(expect.objectContaining({ step: 'start', hint: 'tim mạch' }));
    await act(async () => result.current.retryLastRequest());
    expect(wizardBodies()).toHaveLength(2);
    expect(wizardBodies()[1]).toEqual(expect.objectContaining({ step: 'start' }));
    expect('hint' in wizardBodies()[1]).toBe(false);
});
