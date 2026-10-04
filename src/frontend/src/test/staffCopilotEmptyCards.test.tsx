import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';

let mockUser = { userId: 'staff-1', fullName: 'Staff', role: 'Receptionist' };
const sendMock = vi.fn();

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mockUser, identityVersion: 1 }) }));
vi.mock('../api/aiCopilotApi', () => ({
    sendRoleCopilotMessage: (...args: unknown[]) => sendMock(...args),
    getRoleCopilotCatalog: async () => ({ tools: [], actionTools: [] }),
    prepareRoleAction: vi.fn(), confirmRoleAction: vi.fn(), cancelRoleAction: vi.fn()
}));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({ role: 'Receptionist', suggestions: [] }) }));

const card = (type: string, description: string, data: unknown = []) =>
    ({ type, title: 'Dữ liệu đã kiểm chứng', description, data, sources: [{ name: 'ClinicCare domain database', kind: 'database' }] });

const send = async (role: string, route: string, label: RegExp, message: string, cards: unknown[]) => {
    mockUser = { userId: 'staff-1', fullName: 'Staff', role };
    sendMock.mockResolvedValueOnce({ role, assistantMode: 'Ready', plannerMode: 'Deterministic', conversationId: 'conv', turnId: 'turn',
        intent: 'ViewAppointments', message, suggestedPrompts: [], cards, availableTools: [], sources: [] });
    render(<MemoryRouter initialEntries={[route]}><UnifiedCopilotPanel /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: label }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'xin chào' } });
    fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
    await waitFor(() => expect(screen.getAllByText(message).length).toBeGreaterThan(0));
    const bubbles = document.querySelectorAll('[data-chat-bubble]');
    return bubbles[bubbles.length - 1] as HTMLElement;
};

beforeEach(() => sendMock.mockReset());

it.each([
    ['Receptionist', '/reception', /Mở Copilot Lễ tân/i, 'reception_appointments', 'Có 0 lịch hẹn trong ngày hôm nay.', 'Không có lịch hẹn phù hợp.'],
    ['Receptionist', '/reception', /Mở Copilot Lễ tân/i, 'reception_queue', 'Hàng đợi hiện có 0 lượt.', 'Hàng đợi hiện không có lượt phù hợp.'],
    ['Doctor', '/doctor', /Mở Copilot Bác sĩ/i, 'doctor_queue', 'Hàng đợi của bạn có 0 lượt.', 'Hàng đợi bác sĩ hiện không có dữ liệu phù hợp.'],
    ['DiagnosticTechnician', '/diagnostics', /Mở Copilot Kỹ thuật viên/i, 'technician_worklist', 'Có 0 chỉ định.', 'Worklist cận lâm sàng hiện không có phiếu phù hợp.'],
    ['Pharmacist', '/pharmacy', /Mở Copilot Dược sĩ/i, 'pharmacy_inventory', 'Tồn kho toàn hệ thống được lấy từ danh mục thuốc hiện tại.', 'Kho thuốc hiện không có dữ liệu phù hợp.'],
])('%s empty %s shows one secondary line without frame, title, icon or sources', async (role, route, label, type, message, empty) => {
    const bubble = await send(role, route, label, message, [card(type, message)]);
    expect(within(bubble).getAllByText(message)).toHaveLength(1);
    expect(within(bubble).getAllByText(empty)).toHaveLength(1);
    expect(within(bubble).queryByText('Dữ liệu đã kiểm chứng')).toBeNull();
    expect(within(bubble).queryByLabelText('Nguồn dữ liệu')).toBeNull();
    expect(bubble.querySelector('article')).toBeNull();
    expect(bubble.querySelector('svg')).toBeNull();
    expect(within(bubble).queryByRole('status')).toBeNull();
});

it('does not repeat the empty line when the reply already says it', async () => {
    const bubble = await send('Receptionist', '/reception', /Mở Copilot Lễ tân/i, 'Không có lịch hẹn phù hợp.', [card('reception_appointments', 'Có 0 lịch hẹn.')]);
    expect(within(bubble).getAllByText('Không có lịch hẹn phù hợp.')).toHaveLength(1);
    expect(within(bubble).queryByText('Dữ liệu đã kiểm chứng')).toBeNull();
    expect(bubble.querySelector('article')).toBeNull();
});

it('keeps a populated staff card unchanged next to an empty one', async () => {
    const bubble = await send('Receptionist', '/reception', /Mở Copilot Lễ tân/i, 'Có 1 lịch hẹn trong ngày hôm nay.', [
        card('reception_appointments', 'Có 1 lịch hẹn trong ngày hôm nay.', [{ id: 1, appointmentCode: 'APT-1', patientName: 'Người bệnh', status: 'Confirmed', appointmentDate: '2030-01-02' }]),
        card('reception_queue', 'Hàng đợi hiện có 0 lượt.')
    ]);
    expect(within(bubble).getAllByText('Dữ liệu đã kiểm chứng')).toHaveLength(1);
    expect(bubble.querySelector('article')).not.toBeNull();
    expect(within(bubble).getAllByLabelText('Nguồn dữ liệu')).toHaveLength(1);
    expect(within(bubble).getByText('Hàng đợi hiện không có lượt phù hợp.')).toBeInTheDocument();
    expect(within(bubble).getByText('APT-1', { exact: false })).toBeInTheDocument();
});
