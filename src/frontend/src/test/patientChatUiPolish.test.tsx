/// <reference types="node" />
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import * as renderers from '../components/copilot/copilotDataRenderers';
import { readFileSync } from 'node:fs';
import type { AiCopilotCard } from '../api/aiCopilotApi';
import type { ChatMessage } from '../types/ai';

const mocks = vi.hoisted(() => ({ flow: {} as Record<string, unknown> }));
const chipsCss = readFileSync('src/components/copilot/SuggestionChips.module.css', 'utf8');
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true, user: { role: 'Patient', fullName: 'An', userId: 'polish' } }) }));
vi.mock('../hooks/useAiBookingFlow', () => ({ useAiBookingFlow: () => mocks.flow }));
vi.mock('../components/copilot/useSuggestionMenu', async importOriginal => ({ ...await importOriginal<object>(), useSuggestionMenu: () => [] }));
const card = (type: string, data: unknown = []): AiCopilotCard => ({ type, title: 'Dữ liệu đã kiểm chứng', data });
const message = (overrides: Partial<ChatMessage> = {}): ChatMessage => ({ role: 'model', content: 'Đã kiểm tra.', ...overrides });
const failedTool = (code = 'INVALID_QUERY') => ({ toolName: 'patient.get_my_appointments', status: 'failed' as const, error: { code, message: 'Lỗi tra cứu' } });
const mount = (...messages: ChatMessage[]) => {
    mocks.flow.messages = messages;
    render(<MemoryRouter><MedicalChatWidget /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
    return document.querySelector('[data-chat-bubble]') as HTMLElement;
};
beforeEach(() => {
    mocks.flow = { input: '', setInput: vi.fn(), loading: false, submittingBooking: false, errorMsg: '', messages: [], activeDraft: null,
        clearChat: vi.fn(), handleSendMessage: vi.fn(), handleSuggestion: vi.fn(), wizard: null, handleWizardStep: vi.fn(),
        handleActionClick: vi.fn(), confirmToolAction: vi.fn(), cancelToolAction: vi.fn(), formatVietnameseDate: (s: string) => s,
        retryLastRequest: vi.fn(), canRetry: false, retryAfterSeconds: 0 };
});

it('P1 suppresses a duplicate empty invoice card including its title and check icon', () => {
    const bubble = mount(message({ content: 'Bạn chưa có hóa đơn nào.', copilotCards: [card('patient_bills')] }));
    expect(within(bubble).getAllByText('Bạn chưa có hóa đơn nào.')).toHaveLength(1);
    expect(within(bubble).queryByRole('heading')).toBeNull();
    expect(bubble.querySelector('svg')).toBeNull();
});
it('P1 renders a distinct empty notice as secondary text without a card title or icon', () => {
    const bubble = mount(message({ copilotCards: [card('patient_visits')] }));
    expect(within(bubble).getByText('Bạn chưa có lượt khám nào.')).toBeInTheDocument();
    expect(within(bubble).queryByRole('heading')).toBeNull();
    expect(bubble.querySelector('svg')).toBeNull();
});
const emptyCases: Array<[string, unknown]> = [
    ['clinic_knowledge', null], ['clinic_knowledge', { status: 'not_found', items: [{ title: 'A' }] }], ['clinic_knowledge', { items: [] }],
    ['specialties', []], ['doctors', [null, 'invalid']], ['available_slots', []], ['facilities', []],
    ['pricing_catalog', null], ['pricing_catalog', { consultation: [], diagnostics: [] }], ['appointments', { items: [] }],
    ['appointment_detail', null], ['patient_visits', []], ['patient_diagnostic_results', []], ['patient_prescriptions', []],
    ['patient_bills', []], ['reception_appointments', []], ['reception_queue', []], ['appointment_lookup', null],
    ['appointment_lookup', { status: 'not_found', appointmentCode: 'APT' }], ['appointment_lookup', {}], ['doctor_patient_summary', null],
    ['doctor_summary', []], ['doctor_queue', []], ['doctor_diagnostic_orders', []], ['doctor_prescription_status', []],
    ['technician_worklist', []], ['pharmacist_prescription_queue', []], ['pharmacist_prescription_payment', null],
    ['pharmacy_inventory', []], ['admin_dashboard_metrics', null], ['admin_ai_health', null], ['unsupported', {}]
];
it.each(emptyCases)('P1 returns the exact renderer EmptyData copy for %s (%j)', (type, data) => {
    const value = card(type, data);
    const helper = (renderers as unknown as { copilotCardEmptyMessage: (card: AiCopilotCard) => string | null }).copilotCardEmptyMessage;
    render(<>{renderers.renderCopilotCardData(value)}</>);
    expect(helper(value)).toBe(screen.getByRole('status').textContent);
});
it('P1 retains a populated invoice card and returns null for nonempty data', () => {
    const value = card('patient_bills', [{ invoiceCode: 'INV1', totalAmount: 100 }]);
    const helper = (renderers as unknown as { copilotCardEmptyMessage: (card: AiCopilotCard) => string | null }).copilotCardEmptyMessage;
    expect(helper(value)).toBeNull();
    const bubble = mount(message({ copilotCards: [value] }));
    expect(within(bubble).getByRole('heading', { name: value.title })).toBeInTheDocument();
    expect(within(bubble).getByText('INV1')).toBeInTheDocument();
});
it('P2 uses one neutral not-understood reply, retains navigation, and excludes retry', () => {
    mocks.flow.canRetry = true;
    const bubble = mount(message({ content: 'ClinicCare chưa thể kiểm tra dữ liệu cho yêu cầu này. Vui lòng thử lại sau.', isError: true,
        toolResults: [failedTool(), failedTool('MISSING_TOOL_ARGUMENT')], actions: [{ id: 'appointments', type: 'ViewMyAppointments', label: 'Xem lịch hẹn của tôi', style: 'primary', payload: { targetUrl: '/patient/appointments' }, requiresAuthentication: false, requiresConfirmation: false }] }));
    const reply = within(bubble).getByText('ClinicCare chưa hiểu câu này. Bạn thử diễn đạt lại hoặc chọn một gợi ý bên dưới.');
    expect(reply.className).not.toMatch(/toolError/);
    expect(within(bubble).queryByText(/chưa thể kiểm tra/)).toBeNull();
    expect(within(bubble).queryByText(/chưa xử lý được/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Thử lại' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Xem lịch hẹn của tôi' })).toBeInTheDocument();
});
it('P2 keeps the original content and tool error when a failure is not an argument error', () => {
    const bubble = mount(message({ toolResults: [failedTool(), failedTool('NETWORK_ERROR')] }));
    expect(within(bubble).getByText('Đã kiểm tra.')).toBeInTheDocument();
    expect(within(bubble).getByText('Lỗi tra cứu')).toBeInTheDocument();
});
it('P3 hides the fallback explanation after a successful local reply and retains the status', () => {
    mount(message({ providerState: 'Disabled', fallbackActive: true }));
    expect(screen.queryByText(/Trợ lý đang ở chế độ nội bộ nên chỉ hiểu/)).toBeNull();
    expect(document.querySelector('[data-provider-status]')).toHaveAttribute('data-tone', 'local');
    expect(document.querySelector('[data-provider-status]')).toHaveTextContent('Chế độ nội bộ');
});
it.each([
    { isError: true }, { toolResults: [failedTool('NETWORK_ERROR')] }, { toolResults: [failedTool()] }, { providerState: 'Degraded' as const }
])('P3 shows the explanation for an unsuccessful latest reply %j', overrides => {
    mount(message(overrides));
    expect(screen.getByText(/Trợ lý đang ở chế độ nội bộ nên chỉ hiểu/)).toBeInTheDocument();
});
it('P3 explains rate limiting from the wizard error line', () => {
    mocks.flow.errorMsg = 'Dịch vụ AI đang giới hạn lưu lượng.';
    mount(message());
    expect(screen.getByText(/Trợ lý đang ở chế độ nội bộ nên chỉ hiểu/)).toBeInTheDocument();
});
it('P3 removes the explanation after a later successful reply', () => {
    mount(message({ isError: true }), message({ providerState: 'Disabled', fallbackActive: true }));
    expect(screen.queryByText(/Trợ lý đang ở chế độ nội bộ nên chỉ hiểu/)).toBeNull();
});
it('P4 declares a 6px transparent scrollbar with a border-colored rounded thumb and no arrows', () => {
    expect(chipsCss).not.toMatch(/scrollbar-width|scrollbar-color/);
    expect(chipsCss).toMatch(/::-webkit-scrollbar\s*\{\s*height:\s*6px/);
    expect(chipsCss).toMatch(/::-webkit-scrollbar-track\s*\{\s*background:\s*transparent/);
    expect(chipsCss).toMatch(/::-webkit-scrollbar-thumb\s*\{\s*background:\s*var\(--chat-border\);\s*border-radius:/);
    expect(chipsCss).toMatch(/::-webkit-scrollbar-button\s*\{\s*display:\s*none/);
    expect(chipsCss).toMatch(/max-height:\s*96px/);
    expect(chipsCss).toContain('mask-image: linear-gradient');
});
it('P6 exposes the latest announcement as 200 plain text characters in a polite live region', () => {
    mount(message({ content: '**' + 'a'.repeat(220) + '**' }));
    const live = document.querySelector('[data-chat-announcement]')!;
    expect(live).toHaveAttribute('aria-live', 'polite');
    expect(live.textContent).toBe('a'.repeat(200));
    expect(live.querySelector('[aria-label], [role="note"]')).toBeNull();
    expect(document.querySelector('[data-chat-messages]')).not.toHaveAttribute('aria-live');
});
