import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';

type Classify = (role: string, text: string, menuCodes?: readonly string[]) => string | null;
// Glob lets the table run (and fail) before the module exists on main.
const modules = import.meta.glob<{ classifyStaffTypedIntent: Classify }>('../components/copilot/staffTypedIntent.ts', { eager: true });
const classify = modules['../components/copilot/staffTypedIntent.ts']?.classifyStaffTypedIntent;

// [role, typed text, expected code]. Each role covers accented, unaccented,
// one-character typos and at least three sentences that must stay on free text.
const cases: [string, string, string | null][] = [
    ['Receptionist', 'lịch hẹn sắp tới', 'receptionist.upcoming_appointments'],
    ['Receptionist', 'lich hen tuan nay', 'receptionist.upcoming_appointments'],
    ['Receptionist', 'xem lịch hẹn ngày mai', 'receptionist.upcoming_appointments'],
    ['Receptionist', 'lịch hẹn tuần tới có những ai', 'receptionist.upcoming_appointments'],
    ['Receptionist', 'lịch hẹn ngày maii', 'receptionist.upcoming_appointments'],
    ['Receptionist', 'lịch hẹn hôm nay', 'receptionist.today_appointments'],
    ['Receptionist', 'lich hen hom nay', 'receptionist.today_appointments'],
    ['Receptionist', 'hàng đợi', 'receptionist.queue'],
    ['Receptionist', 'bệnh nhân đang chờ', 'receptionist.queue'],
    ['Receptionist', 'ai dang cho tiep nhan', 'receptionist.queue'],
    ['Receptionist', 'xem hàngg đợi', 'receptionist.queue'],
    ['Receptionist', 'hóa đơn chưa thanh toán', 'receptionist.pending_payments'],
    ['Receptionist', 'hoa don cho thanh toan', 'receptionist.pending_payments'],
    ['Receptionist', 'bệnh nhân còn nợ', 'receptionist.pending_payments'],
    ['Receptionist', 'hóa đơn chưa thanh toám', 'receptionist.pending_payments'],
    ['Receptionist', 'tra lịch APT-261004-511E130', null],
    ['Receptionist', 'cách tiếp nhận bệnh nhân', null],
    ['Receptionist', 'Hôm nay quầy tiếp đón có những lượt hẹn nào?', null],
    ['Receptionist', 'hướng dẫn xác nhận lịch hẹn hôm nay', null],
    ['Receptionist', 'số 0912345678 có lịch hẹn hôm nay không', null],

    ['Doctor', 'hôm nay tôi khám ai', 'doctor.my_queue'],
    ['Doctor', 'hom nay toi kham ai', 'doctor.my_queue'],
    ['Doctor', 'hàng đợi của tôi', 'doctor.my_queue'],
    ['Doctor', 'bệnh nhân chờ khám', 'doctor.my_queue'],
    ['Doctor', 'hàngg đợi bệnh nhân', 'doctor.my_queue'],
    ['Doctor', 'lịch hẹn hôm nay của tôi', 'doctor.today_appointments'],
    ['Doctor', 'lich kham hom nay', 'doctor.today_appointments'],
    ['Doctor', 'xem lịch hẹn', 'doctor.today_appointments'],
    ['Doctor', 'lịch hẹnn hôm nay', 'doctor.today_appointments'],
    ['Doctor', 'lịch hẹn ngày mai', null],
    ['Doctor', 'Hôm nay tôi có những lượt đang chờ được phân công nào?', null],
    ['Doctor', 'cách kê đơn thuốc', null],
    ['Doctor', 'xem VIS-20261004-0001', null],

    ['DiagnosticTechnician', 'đã xong hôm nay', 'technician.completed_today'],
    ['DiagnosticTechnician', 'phiếu đã hoàn tất hôm nay', 'technician.completed_today'],
    ['DiagnosticTechnician', 'hom nay hoan thanh bao nhieu chi dinh', 'technician.completed_today'],
    ['DiagnosticTechnician', 'hoàn tấtt hôm nay', 'technician.completed_today'],
    ['DiagnosticTechnician', 'chỉ định cần thực hiện', 'technician.worklist'],
    ['DiagnosticTechnician', 'chi dinh dang cho', 'technician.worklist'],
    ['DiagnosticTechnician', 'danh sách phiếu', 'technician.worklist'],
    ['DiagnosticTechnician', 'worklist', 'technician.worklist'],
    ['DiagnosticTechnician', 'việc cần làm', 'technician.worklist'],
    ['DiagnosticTechnician', 'chỉ địnhh cần làm', 'technician.worklist'],
    ['DiagnosticTechnician', 'đã hoàn tất', null],
    ['DiagnosticTechnician', 'Các yêu cầu xét nghiệm đang nằm trong hàng đợi của tôi là gì?', null],
    ['DiagnosticTechnician', 'cách nhập kết quả', null],
    ['DiagnosticTechnician', 'tra INV-123 chỉ định', null],

    ['Pharmacist', 'thuốc sắp hết', 'pharmacist.low_stock'],
    ['Pharmacist', 'thuoc het hang', 'pharmacist.low_stock'],
    ['Pharmacist', 'thuốc tồn thấp', 'pharmacist.low_stock'],
    ['Pharmacist', 'thuốc cần nhập thêm', 'pharmacist.low_stock'],
    ['Pharmacist', 'thuốc sắp hếtt', 'pharmacist.low_stock'],
    ['Pharmacist', 'tồn kho', 'pharmacist.inventory'],
    ['Pharmacist', 'ton kho hien tai', 'pharmacist.inventory'],
    ['Pharmacist', 'tồnn kho', 'pharmacist.inventory'],
    ['Pharmacist', 'đơn chờ cấp', 'pharmacist.prescription_queue'],
    ['Pharmacist', 'don thuoc cho xu ly', 'pharmacist.prescription_queue'],
    ['Pharmacist', 'toa chờ cấp phát', 'pharmacist.prescription_queue'],
    ['Pharmacist', 'Những toa đang chờ xử lý ở quầy thuốc gồm những toa nào?', null],
    ['Pharmacist', 'cách cấp phát thuốc', null],
    ['Pharmacist', 'giá thuốc paracetamol', null],

    ['Admin', 'doanh thu hôm nay', 'admin.revenue_today'],
    ['Admin', 'doanh thu', 'admin.revenue_today'],
    ['Admin', 'doanhh thu', 'admin.revenue_today'],
    ['Admin', 'doanh thu tháng này', 'admin.revenue_this_month'],
    ['Admin', 'doanh thu thang nay', 'admin.revenue_this_month'],
    ['Admin', 'chỉ số hôm nay', 'admin.dashboard_metrics'],
    ['Admin', 'thong ke', 'admin.dashboard_metrics'],
    ['Admin', 'tổng quan hệ thống', 'admin.dashboard_metrics'],
    ['Admin', 'thốngg kê', 'admin.dashboard_metrics'],
    ['Admin', 'trợ lý có hoạt động không', 'admin.ai_health'],
    ['Admin', 'ai suc khoe', 'admin.ai_health'],
    ['Admin', 'doanh thu tháng trước', null],
    ['Admin', 'Tình hình vận hành hôm nay của hệ thống thế nào?', null],
    ['Admin', 'hướng dẫn xem báo cáo', null],
];

const MENUS: Record<string, string[]> = {
    Receptionist: ['receptionist.today_appointments', 'receptionist.queue', 'receptionist.upcoming_appointments', 'receptionist.pending_payments'],
    Doctor: ['doctor.my_queue', 'doctor.today_appointments'],
    DiagnosticTechnician: ['technician.worklist', 'technician.completed_today'],
    Pharmacist: ['pharmacist.prescription_queue', 'pharmacist.inventory', 'pharmacist.low_stock'],
    Admin: ['admin.dashboard_metrics', 'admin.ai_health', 'admin.revenue_today', 'admin.revenue_this_month'],
};

describe('classifyStaffTypedIntent', () => {
    it('covers at least 60 sentences with at least 10 per role and 3 null cases per role', () => {
        expect(classify, 'staff classifier must exist').toBeTypeOf('function');
        expect(cases.length).toBeGreaterThanOrEqual(60);
        for (const role of Object.keys(MENUS)) {
            expect(cases.filter(([r]) => r === role).length).toBeGreaterThanOrEqual(10);
            expect(cases.filter(([r, , code]) => r === role && code === null).length).toBeGreaterThanOrEqual(3);
        }
    });

    it.each(cases)('%s: %s -> %s', (role, text, expected) => {
        expect(classify, 'staff classifier must exist').toBeTypeOf('function');
        expect(classify!(role, text, MENUS[role])).toBe(expected);
        expect(classify!(role, text)).toBe(expected);
    });

    it('returns only codes from the current menu, and nothing for other roles or long text', () => {
        expect(classify, 'staff classifier must exist').toBeTypeOf('function');
        expect(classify!('Receptionist', 'lịch hẹn sắp tới', ['receptionist.today_appointments', 'receptionist.queue'])).toBeNull();
        expect(classify!('Receptionist', 'lịch hẹn sắp tới', [])).toBeNull();
        expect(classify!('Pharmacist', 'lịch hẹn sắp tới', MENUS.Pharmacist)).toBeNull();
        expect(classify!('Patient', 'lịch hẹn hôm nay')).toBeNull();
        expect(classify!('Receptionist', 'lịch hẹn hôm nay ' + 'x'.repeat(120))).toBeNull();
    });
});

let mockUser = { userId: 'rec-1', fullName: 'Receptionist', role: 'Receptionist' };
let menu: { role: string; suggestions: { code: string; label: string }[]; typedSuggestions?: { code: string; label: string }[] };
const sendMock = vi.fn();

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mockUser, identityVersion: 1 }) }));
vi.mock('../api/aiCopilotApi', () => ({
    sendRoleCopilotMessage: (...args: unknown[]) => sendMock(...args),
    getRoleCopilotCatalog: async () => ({ tools: [], actionTools: [] }),
    prepareRoleAction: vi.fn(), confirmRoleAction: vi.fn(), cancelRoleAction: vi.fn()
}));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => menu }));

const reply = { role: 'Receptionist', assistantMode: 'Ready', plannerMode: 'Deterministic', conversationId: 'conv-1', turnId: 'turn-1',
    intent: 'ViewAppointments', message: 'Đã kiểm tra.', suggestedPrompts: [], cards: [], availableTools: [], sources: [] };

const typeAndSend = async (text: string) => {
    render(<MemoryRouter initialEntries={['/reception']}><UnifiedCopilotPanel /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Lễ tân/i }));
    await screen.findByRole('button', { name: 'Gợi ý: Lịch hẹn hôm nay' });
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: text } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
    await waitFor(() => expect(sendMock).toHaveBeenCalledTimes(1));
    return sendMock.mock.calls[0][0] as Record<string, unknown>;
};

const sendAgain = async (text: string) => {
    await waitFor(() => expect(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' })).toBeInTheDocument());
    await waitFor(() => expect(screen.queryByText('Đang kiểm tra dữ liệu…')).toBeNull());
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: text } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
    await waitFor(() => expect(sendMock).toHaveBeenCalledTimes(2));
    return sendMock.mock.calls[1][0] as Record<string, unknown>;
};

describe('useUnifiedCopilot typed routing', () => {
    beforeEach(() => {
        mockUser = { userId: 'rec-1', fullName: 'Receptionist', role: 'Receptionist' };
        menu = {
            role: 'Receptionist',
            suggestions: [{ code: 'receptionist.today_appointments', label: 'Lịch hẹn hôm nay' }, { code: 'receptionist.queue', label: 'Hàng đợi tiếp nhận' }],
            typedSuggestions: [{ code: 'receptionist.upcoming_appointments', label: 'Lịch hẹn sắp tới' }, { code: 'receptionist.pending_payments', label: 'Hóa đơn chờ thanh toán' }]
        };
        sendMock.mockReset();
        sendMock.mockResolvedValue(reply);
    });

    it('sends a recognised typed question with its menu code and the typed text as label', async () => {
        const request = await typeAndSend('lịch hẹn sắp tới');
        expect(request).toEqual(expect.objectContaining({ message: 'lịch hẹn sắp tới', suggestionCode: 'receptionist.upcoming_appointments' }));
        expect(await screen.findByText('lịch hẹn sắp tới')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Gợi ý: Lịch hẹn sắp tới' })).toBeNull();
        expect((screen.getByRole('textbox', { name: 'Nội dung Copilot' }) as HTMLTextAreaElement).value).toBe('');
    });

    it('keeps an unrecognised question on the existing free-text path', async () => {
        const request = await typeAndSend('xin chào trợ lý');
        expect(request.message).toBe('xin chào trợ lý');
        expect('suggestionCode' in request).toBe(false);
        expect(await sendAgain('hóa đơn chưa thanh toán')).toEqual(expect.objectContaining({ suggestionCode: 'receptionist.pending_payments' }));
    });

    it('does not send a code that is missing from the current menu', async () => {
        menu = { ...menu, typedSuggestions: undefined };
        const request = await typeAndSend('lịch hẹn sắp tới');
        expect(request.message).toBe('lịch hẹn sắp tới');
        expect('suggestionCode' in request).toBe(false);
        expect(await sendAgain('lịch hẹn hôm nay')).toEqual(expect.objectContaining({ suggestionCode: 'receptionist.today_appointments' }));
    });
});
