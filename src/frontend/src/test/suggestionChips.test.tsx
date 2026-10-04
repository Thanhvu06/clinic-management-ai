import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { PatientAiConsultation } from '../pages/patient/PatientAiConsultation';
import { SuggestionChips } from '../components/copilot/SuggestionChips';
import { sanitizeSuggestions } from '../components/copilot/useSuggestionMenu';
import { ChatProvider } from '../contexts/ChatContext';
import type { AiCopilotResponse } from '../api/aiCopilotApi';
import type { AiSuggestionItem, AiSuggestionMenu } from '../types/ai';

let mockUser: { userId: string; fullName: string; role: string } | null = null;
const sendMock = vi.fn<(request: unknown, signal?: AbortSignal) => Promise<AiCopilotResponse>>();
const menuMock = vi.fn<(request: unknown, signal?: AbortSignal) => Promise<AiSuggestionMenu>>();

vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ user: mockUser, identityVersion: 1, isAuthenticated: Boolean(mockUser), logout: vi.fn() })
}));

vi.mock('../api/aiCopilotApi', () => ({
    sendRoleCopilotMessage: (request: unknown, signal?: AbortSignal) => sendMock(request, signal),
    getRoleCopilotCatalog: async () => ({ tools: [], actionTools: [] }),
    prepareRoleAction: vi.fn(),
    confirmRoleAction: vi.fn(),
    cancelRoleAction: vi.fn()
}));

vi.mock('../api/aiSuggestionApi', () => ({
    getCopilotSuggestions: (request: unknown, signal?: AbortSignal) => menuMock(request, signal)
}));

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

const DOCTOR_MENU: AiSuggestionItem[] = [{ code: 'doctor.my_queue', label: 'Hôm nay tôi khám ai?' }];
const DOCTOR_CASE_MENU: AiSuggestionItem[] = [
    { code: 'doctor.my_queue', label: 'Hôm nay tôi khám ai?' },
    { code: 'doctor.patient_summary', label: 'Tóm tắt bệnh nhân đang mở' },
    { code: 'doctor.diagnostic_orders', label: 'Chỉ định cận lâm sàng của ca này' },
    { code: 'doctor.prescription_status', label: 'Trạng thái đơn thuốc của ca này' }
];
const PATIENT_MENU: AiSuggestionItem[] = [
    { code: 'patient.my_appointments', label: 'Lịch hẹn của tôi' },
    { code: 'patient.my_visits', label: 'Lượt khám của tôi' },
    { code: 'patient.my_bills', label: 'Hóa đơn của tôi' }
];

const copilotResponse = (overrides: Partial<AiCopilotResponse> = {}): AiCopilotResponse => ({
    role: 'Doctor', assistantStatus: 'Ready', providerStatus: 'NotCalled', providerState: 'NotCalled', assistantMode: 'Ready',
    plannerMode: 'Deterministic', executionMode: 'DeterministicFallback', providerWasCalled: false, conversationId: 'conv-s', turnId: `turn-${Math.random()}`,
    intent: 'QueueLookup', message: 'Dữ liệu hàng đợi đã kiểm chứng.', suggestedPrompts: [], availableTools: [],
    cards: [{ type: 'doctor_queue', title: 'Dữ liệu đã kiểm chứng', description: 'Hàng đợi hiện tại', data: [], sources: [] }],
    suggestions: [{ code: 'doctor.patient_summary', label: 'Tóm tắt bệnh nhân đang mở' }],
    ...overrides
});

const deferred = <T,>() => {
    let resolve!: (value: T) => void;
    let reject!: (reason: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
};

const menuGroup = (name: string) => screen.findByRole('group', { name });

describe('Role suggestion buttons', () => {
    it.each([[false, 'TÔI CÓ QUYỀN HẠN GÌ?', 'Help'], [true, 'TÔI CÓ QUYỀN HẠN GÌ?', 'Help'], [false, 'Lịch hẹn của mình', 'ViewAppointments'], [true, 'Lịch hẹn của mình', 'ViewAppointments'],
        [true, 'lịch hẹn của tôi', 'ViewAppointments'], [true, 'lượt khám của tôi', 'VisitLookup'],
        [true, 'đơn thuốc của tôi', 'PrescriptionLookup'], [true, 'kết quả xét nghiệm của tôi', 'DiagnosticLookup'], [true, 'hóa đơn của tôi', 'BillingLookup']
    ] as const)('routes patient help/read aliases through the existing read-only endpoint (widget=%s, text=%s)', async (widget, text, intent) => {
        mockUser = { userId: 'synthetic-patient', fullName: 'Synthetic', role: 'Patient' };
        menuMock.mockResolvedValue({ role: 'Patient', suggestions: PATIENT_MENU });
        sendMock.mockResolvedValueOnce(copilotResponse({ role: 'Patient', intent, message: 'Bạn chọn một gợi ý bên dưới nhé.', suggestions: PATIENT_MENU, cards: [] }));
        render(<MemoryRouter><ChatProvider>{widget ? <MedicalChatWidget /> : <PatientAiConsultation />}</ChatProvider></MemoryRouter>);
        if (widget) fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
        await menuGroup('Tra cứu nhanh dữ liệu của bạn');
        fireEvent.change(screen.getByRole('textbox'), { target: { value: text } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi tin nhắn' }));
        await screen.findByText('Bạn chọn một gợi ý bên dưới nhé.');
        expect(sendMock).toHaveBeenCalledTimes(1);
        expect(sendMock.mock.calls[0][0]).toMatchObject({ message: text });
        expect(sendMock.mock.calls[0][0]).toHaveProperty('suggestionCode', undefined);
        const group = await menuGroup('Gợi ý tiếp theo');
        expect(group.closest('[data-chat-composer]')).not.toBeNull();
        expect(group.closest('[data-chat-bubble]')).toBeNull();
        expect(screen.getAllByRole('group')).toHaveLength(1);
    });

    beforeEach(() => {
        vi.unstubAllEnvs();
        sendMock.mockReset();
        menuMock.mockReset();
        sessionStorage.clear();
        mockUser = null;
    });

    it('shows grouped two-column empty buttons with icons and preserves sanitized groups', async () => {
        const grouped = [
            { code: 'patient.start_booking', label: 'Đặt lịch khám', group: ' Đặt lịch ' },
            { code: 'patient.my_bills', label: 'Hóa đơn của tôi', group: 'Dữ liệu của tôi' }
        ];
        expect(sanitizeSuggestions(grouped)[0].group).toBe('Đặt lịch');
        const { container } = render(<SuggestionChips suggestions={grouped} variant="grid" onSelect={vi.fn()} />);
        expect(screen.getAllByRole('heading').map(element => element.textContent)).toEqual(['Đặt lịch', 'Dữ liệu của tôi']);
        expect(container.querySelectorAll('button svg')).toHaveLength(2);
        expect(screen.getByRole('group').className).toMatch(/grid/);
    });

    it('keeps exactly one strip under only the latest assistant and removes legacy and raw-tool trays', async () => {
        mockUser = { userId: 'doctor-1', fullName: 'Synthetic', role: 'Doctor' };
        menuMock.mockResolvedValue({ role: 'Doctor', suggestions: DOCTOR_MENU });
        sendMock.mockResolvedValueOnce(copilotResponse({ message: 'Câu trả lời thứ nhất', suggestions: DOCTOR_MENU }))
            .mockResolvedValueOnce(copilotResponse({ message: 'Câu trả lời thứ hai', suggestions: DOCTOR_MENU }));
        render(<MemoryRouter><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Mở Copilot Bác sĩ' }));
        fireEvent.click(within(await menuGroup('Gợi ý theo vai trò')).getByRole('button'));
        await screen.findByText('Câu trả lời thứ nhất');
        fireEvent.click(within(await menuGroup('Gợi ý tiếp theo')).getByRole('button'));
        await screen.findByText('Câu trả lời thứ hai');
        expect(screen.getAllByRole('group')).toHaveLength(1);
        expect(screen.getByRole('group').parentElement).toHaveAttribute('aria-describedby', 'copilot-latest-reply');
        expect(screen.getByRole('group').closest('[data-chat-composer]')).not.toBeNull();
        expect(document.getElementById('copilot-latest-reply')).toContainElement(screen.getByText('Câu trả lời thứ hai'));
        expect(screen.getByText('Câu trả lời thứ nhất').parentElement).not.toContainElement(screen.getByRole('group'));
        expect(screen.queryByLabelText('Công cụ được phép')).not.toBeInTheDocument();
        expect(screen.queryByText(/get my queue|doctor\.get_my_queue/)).not.toBeInTheDocument();
        expect(screen.queryByRole('group', { name: 'Gợi ý dự phòng' })).not.toBeInTheDocument();
    });

    it('deduplicates clarification, hides codes in production and offers the menu on ManualHandoff', async () => {
        vi.stubEnv('DEV', false);
        mockUser = { userId: 'doctor-1', fullName: 'Synthetic', role: 'Doctor' };
        menuMock.mockResolvedValue({ role: 'Doctor', suggestions: DOCTOR_MENU });
        sendMock.mockResolvedValueOnce(copilotResponse({ message: 'Bạn muốn tra cứu gì?', clarification: '  BẠN   MUỐN TRA CỨU GÌ?  ', assistantMode: 'Clarifying', executionMode: 'ManualHandoff', errorCode: 'LOCAL_CLARIFICATION', cards: [], suggestions: [] }));
        render(<MemoryRouter><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Mở Copilot Bác sĩ' }));
        await menuGroup('Gợi ý theo vai trò');
        fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Câu hỏi chưa rõ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
        expect(await screen.findByText('Bạn muốn tra cứu gì?')).toBeInTheDocument();
        expect(screen.getAllByText(/bạn muốn tra cứu gì\?/i)).toHaveLength(1);
        expect(screen.queryByText(/Mã xử lý|LOCAL_CLARIFICATION/)).not.toBeInTheDocument();
        expect(screen.getByText('Chế độ nội bộ').closest('[data-provider-status]')).toHaveAttribute('title', 'Clarifying · Deterministic');
        expect(screen.queryByText('Clarifying · Deterministic')).not.toBeInTheDocument();
        expect(screen.getByText('Bạn có thể chọn một trong các gợi ý bên dưới.')).toBeInTheDocument();
        expect(within(await menuGroup('Gợi ý tiếp theo')).getAllByRole('button')).toHaveLength(1);
    });

    it('uses legacy prompts only when the server menu is empty and hides missing-case prompts', async () => {
        mockUser = { userId: 'doctor-1', fullName: 'Synthetic', role: 'Doctor' };
        menuMock.mockResolvedValue({ role: 'Doctor', suggestions: [] });
        render(<MemoryRouter initialEntries={['/doctor']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Mở Copilot Bác sĩ' }));
        expect(screen.getByRole('group', { name: 'Gợi ý dự phòng' })).toBeInTheDocument();
        expect(screen.queryByText('Tóm tắt bệnh nhân hiện tại')).not.toBeInTheDocument();
        expect(screen.queryByText('Xem chỉ định cận lâm sàng')).not.toBeInTheDocument();
    });

    it('patient consultation also uses one server strip and renders suggestion read cards', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Synthetic', role: 'Patient' };
        menuMock.mockResolvedValue({ role: 'Patient', suggestions: PATIENT_MENU });
        sendMock.mockResolvedValueOnce(copilotResponse({ role: 'Patient', message: 'Dữ liệu của bạn', suggestions: PATIENT_MENU, cards: [{ type: 'patient_bills', title: 'Hóa đơn kiểm thử', data: [], sources: [] }] }));
        render(<MemoryRouter><ChatProvider><PatientAiConsultation /></ChatProvider></MemoryRouter>);
        const menu = await menuGroup('Tra cứu nhanh dữ liệu của bạn');
        expect(screen.queryByText('Tôi nên khám chuyên khoa nào?')).not.toBeInTheDocument();
        fireEvent.click(within(menu).getByRole('button', { name: 'Gợi ý: Hóa đơn của tôi' }));
        expect(await screen.findByText('Hóa đơn kiểm thử')).toBeInTheDocument();
        expect(screen.getAllByRole('group')).toHaveLength(1);
    });

    it('sanitizes server chips: valid codes only, no duplicates, at most six', () => {
        const items = sanitizeSuggestions([
            { code: 'doctor.my_queue', label: ' Hôm nay tôi khám ai? ' },
            { code: 'doctor.my_queue', label: 'Trùng mã' },
            { code: 'DOCTOR.X', label: 'Sai định dạng' },
            { code: 'doctor.get_my_queue; drop', label: 'Chèn ký tự' },
            { code: 'doctor.empty', label: '   ' },
            ...Array.from({ length: 8 }, (_, index) => ({ code: `patient.item_${'abcdefgh'[index]}`, label: `Mục ${index}` }))
        ]);
        expect(items[0]).toEqual({ code: 'doctor.my_queue', label: 'Hôm nay tôi khám ai?' });
        expect(items).toHaveLength(6);
        expect(new Set(items.map(item => item.code)).size).toBe(6);
        expect(items.map(item => item.code)).not.toContain('DOCTOR.X');
        expect(sanitizeSuggestions(null)).toEqual([]);
    });

    it('is keyboard operable and exposes an accessible name per chip', async () => {
        const onSelect = vi.fn();
        const user = userEvent.setup();
        render(<SuggestionChips suggestions={PATIENT_MENU} onSelect={onSelect} ariaLabel="Gợi ý thử" />);
        const group = screen.getByRole('group', { name: 'Gợi ý thử' });
        expect(within(group).getAllByRole('button')).toHaveLength(3);
        await user.tab();
        expect(screen.getByRole('button', { name: 'Gợi ý: Lịch hẹn của tôi' })).toHaveFocus();
        await user.keyboard('{Enter}');
        await user.tab();
        await user.keyboard(' ');
        expect(onSelect.mock.calls.map(call => (call[0] as AiSuggestionItem).code)).toEqual(['patient.my_appointments', 'patient.my_visits']);
    });

    it('doctor panel loads the role menu, sends only the suggestion code, locks while sending and shows reply suggestions', async () => {
        mockUser = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
        menuMock.mockResolvedValue({ role: 'Doctor', suggestions: DOCTOR_MENU });
        const pending = deferred<AiCopilotResponse>();
        sendMock.mockReturnValueOnce(pending.promise);
        render(<MemoryRouter initialEntries={['/doctor/queue']}><UnifiedCopilotPanel /></MemoryRouter>);

        expect(menuMock).not.toHaveBeenCalled();
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        const menu = await menuGroup('Gợi ý theo vai trò');
        expect(menuMock).toHaveBeenCalledTimes(1);
        expect(menuMock.mock.calls[0][0]).toEqual({ currentRoute: '/doctor/queue' });
        expect(within(menu).queryByText(/Lịch hẹn của tôi/)).not.toBeInTheDocument();

        fireEvent.click(within(menu).getByRole('button', { name: 'Gợi ý: Hôm nay tôi khám ai?' }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Gợi ý: Hôm nay tôi khám ai?' })).toBeDisabled());
        fireEvent.click(screen.getByRole('button', { name: 'Gợi ý: Hôm nay tôi khám ai?' }));
        expect(sendMock).toHaveBeenCalledTimes(1);

        const request = sendMock.mock.calls[0][0] as Record<string, unknown>;
        expect(request).toMatchObject({ message: 'Hôm nay tôi khám ai?', suggestionCode: 'doctor.my_queue', currentRoute: '/doctor/queue' });
        for (const forbidden of ['toolName', 'toolCalls', 'arguments', 'role', 'userId', 'facilityId', 'allowedTools'])
            expect(request).not.toHaveProperty(forbidden);
        expect(screen.getAllByText('Hôm nay tôi khám ai?').length).toBeGreaterThanOrEqual(2);

        await act(async () => { pending.resolve(copilotResponse()); await pending.promise; });
        const next = await menuGroup('Gợi ý tiếp theo');
        expect(within(next).getByRole('button', { name: 'Gợi ý: Tóm tắt bệnh nhân đang mở' })).toBeEnabled();
        expect(screen.queryByRole('group', { name: 'Gợi ý theo vai trò' })).not.toBeInTheDocument();
        expect(screen.getByText('Dữ liệu hàng đợi đã kiểm chứng.')).toBeInTheDocument();
        // Free-text input stays available and untouched by chip clicks.
        expect(screen.getByRole('textbox', { name: 'Nội dung Copilot' })).toHaveValue('');
    });

    it('doctor panel sends the verified route resource to the menu and keeps a typed draft when a chip is used', async () => {
        mockUser = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
        menuMock.mockResolvedValue({ role: 'Doctor', suggestions: DOCTOR_CASE_MENU });
        sendMock.mockResolvedValueOnce(copilotResponse({ intent: 'PatientSummary', suggestions: DOCTOR_MENU }));
        render(<MemoryRouter initialEntries={['/doctor/appointments/42']}><UnifiedCopilotPanel /></MemoryRouter>);

        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        const menu = await menuGroup('Gợi ý theo vai trò');
        expect(menuMock.mock.calls[0][0]).toEqual({ currentRoute: '/doctor/appointments/42', resourceContext: { appointmentId: 42 } });
        expect(within(menu).getAllByRole('button')).toHaveLength(4);

        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'bản nháp' } });
        fireEvent.click(within(menu).getByRole('button', { name: 'Gợi ý: Tóm tắt bệnh nhân đang mở' }));
        await waitFor(() => expect(sendMock).toHaveBeenCalledTimes(1));
        expect(sendMock.mock.calls[0][0]).toMatchObject({ suggestionCode: 'doctor.patient_summary', resourceContext: { appointmentId: 42 } });
        expect(screen.getByRole('textbox', { name: 'Nội dung Copilot' })).toHaveValue('bản nháp');
        expect(await menuGroup('Gợi ý tiếp theo')).toBeInTheDocument();
    });

    it('rate-limited suggestion shows the existing 429 message and can be retried with the same code', async () => {
        mockUser = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
        menuMock.mockResolvedValue({ role: 'Doctor', suggestions: DOCTOR_MENU });
        sendMock.mockRejectedValueOnce({ status: 429, errorCode: 'TOO_MANY_REQUESTS', message: 'Bạn gửi quá nhiều yêu cầu.' })
            .mockResolvedValueOnce(copilotResponse());
        render(<MemoryRouter initialEntries={['/doctor/queue']}><UnifiedCopilotPanel /></MemoryRouter>);

        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        fireEvent.click(within(await menuGroup('Gợi ý theo vai trò')).getByRole('button', { name: 'Gợi ý: Hôm nay tôi khám ai?' }));
        const retry = await screen.findByRole('button', { name: /Thử lại/ });
        expect(within(await menuGroup('Gợi ý theo vai trò')).getByRole('button', { name: 'Gợi ý: Hôm nay tôi khám ai?' })).toBeEnabled();
        fireEvent.click(retry);
        await waitFor(() => expect(sendMock).toHaveBeenCalledTimes(2));
        expect(sendMock.mock.calls[1][0]).toMatchObject({ message: 'Hôm nay tôi khám ai?', suggestionCode: 'doctor.my_queue' });
    });

    it.each([
        ['Patient', '/patient', 'Mở Trợ lý bệnh nhân', 'patient.my_appointments', 'Lịch hẹn của tôi'],
        ['Doctor', '/doctor', 'Mở Copilot Bác sĩ', 'doctor.my_queue', 'Hôm nay tôi khám ai?'],
        ['Receptionist', '/reception', 'Mở Copilot Lễ tân', 'receptionist.queue', 'Hàng đợi tiếp nhận'],
        ['DiagnosticTechnician', '/diagnostics', 'Mở Copilot Kỹ thuật viên', 'technician.worklist', 'Chỉ định cần thực hiện'],
        ['Pharmacist', '/pharmacy', 'Mở Copilot Dược sĩ', 'pharmacist.inventory', 'Tồn kho thuốc'],
        ['Admin', '/admin', 'Mở Copilot Quản trị viên', 'admin.ai_health', 'Hoạt động của trợ lý AI']
    ])('%s loads its own server menu', async (role, route, launcher, code, label) => {
        mockUser = { userId: 'synthetic-actor', fullName: 'Synthetic', role };
        menuMock.mockResolvedValue({ role, suggestions: [{ code, label, group: 'Việc hôm nay' }] });
        render(<MemoryRouter initialEntries={[route]}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: launcher }));
        const group = await menuGroup('Gợi ý theo vai trò');
        expect(menuMock.mock.calls[0][0]).toEqual({ currentRoute: route });
        expect(within(group).getAllByRole('button')).toHaveLength(1);
        expect(within(group).getByRole('button', { name: 'Gợi ý: ' + label })).toBeInTheDocument();
        expect(screen.queryByRole('group', { name: 'Gợi ý dự phòng' })).not.toBeInTheDocument();
    });

    it('patient widget shows the patient menu, sends the code, renders the grounded card and next suggestions', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Bệnh nhân tổng hợp', role: 'Patient' };
        menuMock.mockResolvedValue({ role: 'Patient', suggestions: PATIENT_MENU });
        const pending = deferred<AiCopilotResponse>();
        sendMock.mockReturnValueOnce(pending.promise);
        render(<MemoryRouter initialEntries={['/patient']}><ChatProvider><MedicalChatWidget /></ChatProvider></MemoryRouter>);

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        const menu = await menuGroup('Tra cứu nhanh dữ liệu của bạn');
        expect(menuMock.mock.calls[0][0]).toEqual({ currentRoute: '/patient' });
        expect(within(menu).queryByText(/Hôm nay tôi khám ai/)).not.toBeInTheDocument();
        // Legacy prompts yield to the single server menu.
        expect(screen.queryByText('Tôi nên khám chuyên khoa nào?')).not.toBeInTheDocument();

        fireEvent.click(within(menu).getByRole('button', { name: 'Gợi ý: Lịch hẹn của tôi' }));
        await waitFor(() => expect(sendMock).toHaveBeenCalledTimes(1));
        const request = sendMock.mock.calls[0][0] as Record<string, unknown>;
        expect(request).toMatchObject({ message: 'Lịch hẹn của tôi', suggestionCode: 'patient.my_appointments', currentRoute: '/patient' });
        for (const forbidden of ['toolName', 'arguments', 'role', 'userId', 'patientId'])
            expect(request).not.toHaveProperty(forbidden);
        // While sending, no suggestion button is actionable.
        expect(screen.queryAllByRole('button', { name: /^Gợi ý:/ }).filter(button => !(button as HTMLButtonElement).disabled)).toHaveLength(0);
        expect(screen.getAllByText('Lịch hẹn của tôi').length).toBeGreaterThanOrEqual(1);

        await act(async () => {
            pending.resolve(copilotResponse({
                role: 'Patient', intent: 'ViewAppointments', message: 'Đây là lịch hẹn của bạn.',
                cards: [{ type: 'appointments', title: 'Dữ liệu đã kiểm chứng', description: 'Lịch hẹn của chính bạn', data: { items: [] }, sources: [] }],
                suggestions: [{ code: 'patient.my_bills', label: 'Hóa đơn của tôi' }, { code: 'patient.my_visits', label: 'Lượt khám của tôi' }]
            }));
            await pending.promise;
        });
        expect(await screen.findByText('Đây là lịch hẹn của bạn.')).toBeInTheDocument();
        expect(screen.getByText('Lịch hẹn của chính bạn')).toBeInTheDocument();
        const next = await menuGroup('Gợi ý tiếp theo');
        expect(within(next).getByRole('button', { name: 'Gợi ý: Hóa đơn của tôi' })).toBeEnabled();
        expect(within(next).queryByRole('button', { name: /Lịch hẹn của tôi/ })).not.toBeInTheDocument();
        expect(screen.getByRole('textbox', { name: 'Nội dung tin nhắn gửi tới ClinicCare AI' })).toBeInTheDocument();

        // Second round: reply chips lock while their request is in flight.
        const second = deferred<AiCopilotResponse>();
        sendMock.mockReturnValueOnce(second.promise);
        fireEvent.click(within(next).getByRole('button', { name: 'Gợi ý: Hóa đơn của tôi' }));
        await waitFor(() => expect(within(next).getByRole('button', { name: 'Gợi ý: Lượt khám của tôi' })).toBeDisabled());
        fireEvent.click(within(next).getByRole('button', { name: 'Gợi ý: Lượt khám của tôi' }));
        expect(sendMock).toHaveBeenCalledTimes(2);
        expect(sendMock.mock.calls[1][0]).toMatchObject({ message: 'Hóa đơn của tôi', suggestionCode: 'patient.my_bills' });
        await act(async () => { second.resolve(copilotResponse({ role: 'Patient', message: 'Hóa đơn của bạn.', cards: [], suggestions: [] })); await second.promise; });
        expect(await screen.findByText('Hóa đơn của bạn.')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Gợi ý: Lượt khám của tôi' })).not.toBeInTheDocument();
    });

    it('patient widget reports a 429 on a suggestion with the existing rate-limit message', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Bệnh nhân tổng hợp', role: 'Patient' };
        menuMock.mockResolvedValue({ role: 'Patient', suggestions: PATIENT_MENU });
        sendMock.mockRejectedValueOnce({ status: 429, errorCode: 'TOO_MANY_REQUESTS', message: 'Bạn gửi quá nhiều yêu cầu.' });
        render(<MemoryRouter initialEntries={['/patient']}><ChatProvider><MedicalChatWidget /></ChatProvider></MemoryRouter>);

        fireEvent.click(screen.getByLabelText('Mở Trợ lý ClinicCare AI'));
        fireEvent.click(within(await menuGroup('Tra cứu nhanh dữ liệu của bạn')).getByRole('button', { name: 'Gợi ý: Lượt khám của tôi' }));
        expect(await screen.findByText(/quá nhiều|giới hạn/i)).toBeInTheDocument();
        expect(screen.getAllByText('Lượt khám của tôi').length).toBeGreaterThanOrEqual(1);
    });
});
