import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AiCopilotCard } from '../api/aiCopilotApi';
import { copilotCardEmptyMessage, renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';

const UNSUPPORTED = 'chưa hỗ trợ trình bày';
const card = (type: string, data: unknown, description?: string): AiCopilotCard =>
    ({ type, title: 'Dữ liệu đã kiểm chứng', data, ...(description ? { description } : {}) });
const renderCard = (value: AiCopilotCard) => render(<>{renderCopilotCardData(value)}</>).container;

describe('typed-suggestion staff cards', () => {
    it('renders pending payments with VND amount, localized status and Vietnam creation time', () => {
        const value = card('reception_pending_payments', [{ invoiceCode: 'INV-77', patientName: 'Nguyễn Văn B', totalAmount: 150000, status: 'Unpaid', createdAtUtc: '2026-10-04T01:30:00' }]);
        expect(copilotCardEmptyMessage(value)).toBeNull();
        const view = renderCard(value);
        expect(view.textContent).toContain('INV-77');
        expect(screen.getByText('Nguyễn Văn B')).toBeInTheDocument();
        expect(view.textContent).toContain('150.000 ₫');
        expect(view.textContent).toContain('Chưa thanh toán');
        expect(view.textContent).toContain('08:30');
        expect(view.textContent).toContain('04/10/2026');
        expect(view.textContent).not.toContain(UNSUPPORTED);
        expect(view.textContent).not.toContain('2026-10-04T01:30');
    });

    it('renders doctor appointments today with time range, status and optional reason', () => {
        const view = renderCard(card('doctor_appointments_today', [
            { appointmentCode: 'APT-9', startTime: '08:00:00', endTime: '08:30:00', status: 'Confirmed', patientName: 'Trần C', reason: 'Đau lưng kéo dài' },
            { appointmentCode: 'APT-10', startTime: '09:00:00', endTime: '09:30:00', status: 'CheckedIn', patientName: 'Lê D' }
        ]));
        expect(view.textContent).toContain('APT-9');
        expect(view.textContent).toContain('08:00 – 08:30');
        expect(view.textContent).toContain('Trần C');
        expect(view.textContent).toContain('Đã xác nhận');
        expect(view.textContent).toContain('Đau lưng kéo dài');
        expect(view.textContent).toContain('Đã tiếp nhận');
        expect(screen.getAllByText('Lý do khám:')).toHaveLength(1);
        expect(view.textContent).not.toContain(UNSUPPORTED);
    });

    it('renders technician completions with Vietnam completion time and joined services', () => {
        const view = renderCard(card('technician_completed_today', [{ orderCode: 'ORD-5', status: 'Completed', completedAtUtc: '2026-10-04T02:15:00Z', services: ['Xét nghiệm máu', 'Siêu âm bụng'] }]));
        expect(view.textContent).toContain('ORD-5');
        expect(view.textContent).toContain('09:15');
        expect(view.textContent).toContain('Xét nghiệm máu, Siêu âm bụng');
        expect(view.textContent).not.toContain(UNSUPPORTED);
    });

    it('renders low stock as name (code) with current / reorder level and unit', () => {
        const view = renderCard(card('pharmacy_low_stock', [{ code: 'PARA500', name: 'Paracetamol 500mg', unit: 'Viên', stockQuantity: 5, reorderLevel: 10 }]));
        expect(screen.getByText('Paracetamol 500mg (PARA500)')).toBeInTheDocument();
        expect(view.textContent).toContain('5 / 10 Viên');
        expect(view.textContent).not.toContain(UNSUPPORTED);
    });

    it('renders revenue with period label, VND total, invoice count and facilities only when there are two or more', () => {
        const view = renderCard(card('admin_revenue_summary', { period: 'this_month', totalRevenue: 1250000, invoiceCount: 3, byFacility: [
            { facilityId: 1, totalRevenue: 1000000, invoiceCount: 2 }, { facilityId: 2, totalRevenue: 250000, invoiceCount: 1 }] }));
        expect(view.textContent).toContain('Tháng này');
        expect(view.textContent).toContain('1.250.000 ₫');
        expect(view.textContent).toContain('Số hóa đơn: 3');
        expect(view.textContent).toContain('Cơ sở #1: 1.000.000 ₫ · 2 hóa đơn');
        expect(view.textContent).toContain('Cơ sở #2: 250.000 ₫ · 1 hóa đơn');
        expect(view.textContent).not.toContain(UNSUPPORTED);
        const single = renderCard(card('admin_revenue_summary', { period: 'today', totalRevenue: 50000, invoiceCount: 1, byFacility: [{ facilityId: 1, totalRevenue: 50000, invoiceCount: 1 }] }));
        expect(single.textContent).toContain('Hôm nay');
        expect(single.textContent).not.toContain('Cơ sở #');
        expect(renderCard(card('admin_revenue_summary', { period: 'last_month', totalRevenue: 1, invoiceCount: 1 })).textContent).toContain('last_month');
    });

    it('drops missing fields without breaking the card', () => {
        const view = renderCard(card('reception_pending_payments', [{ invoiceCode: 'INV-1' }]));
        expect(view.textContent).toContain('INV-1');
        expect(view.textContent).not.toContain('Số tiền');
        expect(view.textContent).not.toContain('Tạo lúc');
        expect(view.textContent).not.toContain(UNSUPPORTED);
    });

    it.each([
        ['reception_pending_payments', [], 'Không có hóa đơn chờ thanh toán.'],
        ['doctor_appointments_today', [], 'Hôm nay bạn không có lịch hẹn phù hợp.'],
        ['technician_completed_today', [], 'Hôm nay chưa có chỉ định hoàn tất.'],
        ['pharmacy_low_stock', [], 'Không có thuốc dưới mức đặt lại.'],
        ['admin_revenue_summary', { period: 'today', totalRevenue: 0, invoiceCount: 0, byFacility: [] }, 'Chưa có doanh thu trong kỳ này.'],
    ])('empty %s shows its own empty sentence', (type, data, message) => {
        const value = card(type, data);
        expect(copilotCardEmptyMessage(value)).toBe(message);
        const view = renderCard(value);
        expect(screen.getByRole('status')).toHaveTextContent(message);
        expect(view.textContent).not.toContain(UNSUPPORTED);
    });
});

let mockUser = { userId: 'pharm-1', fullName: 'Pharmacist', role: 'Pharmacist' };
let menu: { role: string; suggestions: { code: string; label: string }[]; typedSuggestions?: { code: string; label: string }[] };
const sendMock = vi.fn();
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mockUser, identityVersion: 1 }) }));
vi.mock('../api/aiCopilotApi', () => ({
    sendRoleCopilotMessage: (...args: unknown[]) => sendMock(...args),
    getRoleCopilotCatalog: async () => ({ tools: [], actionTools: [] }),
    prepareRoleAction: vi.fn(), confirmRoleAction: vi.fn(), cancelRoleAction: vi.fn()
}));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => menu }));

const reply = (message: string, cards: AiCopilotCard[]) => ({ role: mockUser.role, assistantMode: 'Ready', plannerMode: 'Deterministic',
    conversationId: 'conv', turnId: 'turn', intent: 'PharmacyInventory', message, suggestedPrompts: [], cards, availableTools: [], sources: [] });

const openAndSend = async (label: RegExp, route: string, text: string, waitChip?: string) => {
    render(<MemoryRouter initialEntries={[route]}><UnifiedCopilotPanel /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: label }));
    if (waitChip) await screen.findByRole('button', { name: `Gợi ý: ${waitChip}` });
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: text } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
    await waitFor(() => expect(sendMock).toHaveBeenCalledTimes(1));
};
const lastBubble = () => { const bubbles = document.querySelectorAll('[data-chat-bubble]'); return bubbles[bubbles.length - 1] as HTMLElement; };

describe('staff panel with typed cards', () => {
    beforeEach(() => {
        sendMock.mockReset();
        mockUser = { userId: 'pharm-1', fullName: 'Pharmacist', role: 'Pharmacist' };
        menu = { role: 'Pharmacist', suggestions: [{ code: 'pharmacist.prescription_queue', label: 'Đơn thuốc chờ xử lý' }, { code: 'pharmacist.inventory', label: 'Tồn kho thuốc' }],
            typedSuggestions: [{ code: 'pharmacist.low_stock', label: 'Thuốc sắp hết' }] };
    });

    it('routes "thuốc sắp hết" to pharmacist.low_stock and renders the medicine', async () => {
        const message = 'Tồn kho toàn hệ thống được lấy từ danh mục thuốc hiện tại.';
        sendMock.mockResolvedValueOnce(reply(message, [{ ...card('pharmacy_low_stock', [{ code: 'PARA500', name: 'Paracetamol 500mg', unit: 'Viên', stockQuantity: 5, reorderLevel: 10 }], message), sources: [{ name: 'ClinicCare domain database', kind: 'database' }] }]));
        await openAndSend(/Mở Copilot Dược sĩ/i, '/pharmacy', 'thuốc sắp hết', 'Tồn kho thuốc');
        expect(sendMock.mock.calls[0][0]).toEqual(expect.objectContaining({ message: 'thuốc sắp hết', suggestionCode: 'pharmacist.low_stock' }));
        expect(await screen.findByText('Paracetamol 500mg (PARA500)')).toBeInTheDocument();
        expect(lastBubble().textContent).toContain('5 / 10 Viên');
        expect(lastBubble().textContent).not.toContain(UNSUPPORTED);
    });

    it('shows a specific public catalog description that differs from the reply instead of the generic line', async () => {
        sendMock.mockResolvedValueOnce(reply('Tra cứu đã hoàn tất.', [
            card('clinic_knowledge', { status: 'not_found', mode: 'search', items: [] }, 'Không có thuốc PARA-X trong danh mục công khai.'),
            card('pharmacy_low_stock', [], 'Có 0 thuốc dưới mức đặt lại.')
        ]));
        await openAndSend(/Mở Copilot Dược sĩ/i, '/pharmacy', 'xin chào');
        await waitFor(() => expect(within(lastBubble()).getByText('Không có thuốc PARA-X trong danh mục công khai.')).toBeInTheDocument());
        expect(within(lastBubble()).queryByText('Không có bản ghi công khai phù hợp.')).toBeNull();
        // A staff card keeps its own empty sentence, not its count description.
        expect(within(lastBubble()).getByText('Không có thuốc dưới mức đặt lại.')).toBeInTheDocument();
        expect(within(lastBubble()).queryByText('Có 0 thuốc dưới mức đặt lại.')).toBeNull();
        expect(within(lastBubble()).queryByText('Dữ liệu đã kiểm chứng')).toBeNull();
        expect(lastBubble().querySelector('article')).toBeNull();
    });

    it('shows the default empty sentence when an empty card has no description', async () => {
        sendMock.mockResolvedValueOnce(reply('Tra cứu đã hoàn tất.', [card('pharmacy_low_stock', [])]));
        await openAndSend(/Mở Copilot Dược sĩ/i, '/pharmacy', 'xin chào');
        await waitFor(() => expect(within(lastBubble()).getByText('Không có thuốc dưới mức đặt lại.')).toBeInTheDocument());
        expect(within(lastBubble()).queryByText('Dữ liệu đã kiểm chứng')).toBeNull();
        expect(lastBubble().textContent).not.toContain(UNSUPPORTED);
    });
});
