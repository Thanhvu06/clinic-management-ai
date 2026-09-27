import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';
import { COPILOT_ROLE_CONFIG, providerStateLabel } from '../components/copilot/copilotConfig';
import { isValidBookingReason, normalizeBookingReason } from '../hooks/useAiBookingFlow';

let mockUser: { userId: string; fullName: string; role: string } | null = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
let mockIdentityVersion = 1;
const sendMock = vi.fn();
const catalogMock = vi.fn();
const prepareActionMock = vi.fn();
const confirmActionMock = vi.fn();

vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({ user: mockUser, identityVersion: mockIdentityVersion })
}));

vi.mock('../api/aiCopilotApi', () => ({
    sendRoleCopilotMessage: (...args: unknown[]) => sendMock(...args),
    getRoleCopilotCatalog: (...args: unknown[]) => catalogMock(...args),
    prepareRoleAction: (...args: unknown[]) => prepareActionMock(...args),
    confirmRoleAction: (...args: unknown[]) => confirmActionMock(...args)
}));

const response = (overrides: Record<string, unknown> = {}) => ({
    role: 'Doctor', assistantStatus: 'Ready', providerStatus: 'Online', assistantMode: 'Ready', plannerMode: 'ProviderStructured',
    conversationId: 'conv-1', turnId: 'turn-1', intent: 'PatientSummary', message: 'Đây là dữ liệu đã kiểm chứng.',
    suggestedPrompts: [], cards: [{ type: 'doctor_summary', title: 'Ca được phân công', description: 'Dữ liệu hiện tại', data: [{ doctorName: 'Doctor 1', status: 'scheduled', patientId: 'must-not-render' }], sources: [{ name: 'appointments', kind: 'database' }] }],
    availableTools: [], sources: [{ name: 'appointments', kind: 'database' }], ...overrides
});

describe('UnifiedCopilotPanel', () => {
    beforeEach(() => {
        sendMock.mockReset();
        catalogMock.mockReset().mockResolvedValue({ tools: [], actionTools: [] });
        prepareActionMock.mockReset();
        confirmActionMock.mockReset();
        mockUser = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
        mockIdentityVersion = 1;
    });

    it('renders role-specific prompts, sends only route/resource context, and displays grounded response metadata', async () => {
        sendMock.mockResolvedValueOnce(response());
        render(<MemoryRouter initialEntries={['/doctor/appointments/42']}><UnifiedCopilotPanel /></MemoryRouter>);

        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        expect(screen.getByText('Tóm tắt bệnh nhân hiện tại')).toBeInTheDocument();
        expect(screen.getByText('Xử lý nội bộ')).toBeInTheDocument();
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Tóm tắt ca này' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));

        await waitFor(() => expect(screen.getByText('Gemini đã phản hồi')).toBeInTheDocument());
        expect(screen.getByText('Doctor 1')).toBeInTheDocument();
        expect(screen.getAllByText(/appointments · database/)).toHaveLength(1);
        expect(screen.queryByText('must-not-render')).not.toBeInTheDocument();
        const request = sendMock.mock.calls[0][0] as Record<string, unknown>;
        expect(request).toMatchObject({ message: 'Tóm tắt ca này', currentRoute: '/doctor/appointments/42' });
        expect(request.resourceContext).toEqual({ appointmentId: 42 });
        expect(request).not.toHaveProperty('userId');
        expect(request).not.toHaveProperty('role');
        expect(request).not.toHaveProperty('facilityId');
    });

    it('resets on identity switch and ignores a late response from the previous identity', async () => {
        let resolveOld!: (value: unknown) => void;
        sendMock.mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve; }));
        const view = render(<MemoryRouter initialEntries={['/doctor']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Xem hàng đợi' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
        mockUser = { userId: 'reception-1', fullName: 'Reception', role: 'Receptionist' };
        mockIdentityVersion = 2;
        view.rerender(<MemoryRouter initialEntries={['/reception']}><UnifiedCopilotPanel /></MemoryRouter>);
        resolveOld(response({ message: 'Late response from old identity' }));

        await waitFor(() => expect(screen.getByText('Copilot Lễ tân')).toBeInTheDocument());
        expect(screen.queryByText('Late response from old identity')).not.toBeInTheDocument();
        expect(screen.getByText('Xem lịch hẹn hôm nay')).toBeInTheDocument();
    });

    it('keeps provider mapping separate from assistant mode for every supported role', () => {
        expect(Object.keys(COPILOT_ROLE_CONFIG)).toHaveLength(6);
        expect(providerStateLabel('NotCalled')).toBe('Xử lý nội bộ');
        expect(providerStateLabel('Online')).toBe('Gemini đã phản hồi');
        expect(providerStateLabel('Degraded')).toBe('Đang dùng chế độ dự phòng');
        expect(providerStateLabel('Unavailable')).toBe('Dịch vụ AI chưa cấu hình/không khả dụng');
        expect(providerStateLabel('SafetyBlocked')).toBe('Đã chặn vì an toàn');
    });

    it('keeps role action tokens in memory and requires an explicit confirm click', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        catalogMock.mockResolvedValue({
            tools: [],
            actionTools: [{ name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' }]
        });
        prepareActionMock.mockResolvedValue({ status: 'pending_confirmation', actionId: 'action-1', data: { confirmationToken: 'secret-token', expiresAtUtc: '2030-01-01T00:00:00Z' } });
        confirmActionMock.mockResolvedValue({ status: 'completed', displayText: 'Đã tiếp nhận phiếu.' });

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());
        expect(screen.queryByText('secret-token')).not.toBeInTheDocument();
        expect(confirmActionMock).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: /Xác nhận thao tác/i }));
        await waitFor(() => expect(screen.getByText('Đã tiếp nhận phiếu.')).toBeInTheDocument());
        expect(confirmActionMock).toHaveBeenCalledWith('action-1', expect.objectContaining({ sessionId: expect.any(String), concurrencyToken: 'secret-token' }), expect.anything());
        expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument();
    });

    it('rejects gibberish/question booking reasons and normalizes a trailing escape', () => {
        expect(normalizeBookingReason('Đau đầu kéo dài\\')).toBe('Đau đầu kéo dài');
        expect(isValidBookingReason('kkkkkkkkkkkk')).toBe(false);
        expect(isValidBookingReason('Tôi nên chọn khoa nào?')).toBe(false);
        expect(isValidBookingReason('Đau đầu kéo dài ba ngày')).toBe(true);
    });
});
