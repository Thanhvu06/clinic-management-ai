import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
    afterEach(() => {
        vi.useRealTimers();
    });

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
        prepareActionMock.mockResolvedValue({ status: 'pending_confirmation', actionId: 'action-1', data: { confirmationToken: 'secret-token', expiresAtUtc: '2030-01-01T00:00:00Z' }, preview: { toolName: 'technician.prepare_start_diagnostic_order', status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu chỉ định LAB-42', facility: 'FAC-TEST — Cơ sở test', department: 'DEP-TEST — Khoa test', subject: 'Bệnh nhân mã #42', encounter: 'DiagnosticOrder #42', currentStatus: 'Ordered' }, changes: [{ kind: 'diagnostic_start', summary: 'Tiếp nhận phiếu vào worklist', items: [{ label: 'Phiếu', value: 'LAB-42' }] }], consequence: 'Backend sẽ tiếp nhận phiếu.', confirmationSummary: 'Đã kiểm tra quyền và resource.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] } });
        confirmActionMock.mockResolvedValue({ status: 'completed', displayText: 'Đã tiếp nhận phiếu.' });

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument();
        expect(screen.queryByText('secret-token')).not.toBeInTheDocument();
        expect(confirmActionMock).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: /Xác nhận thao tác/i }));
        await waitFor(() => expect(screen.getByText('Đã tiếp nhận phiếu.')).toBeInTheDocument());
        expect(confirmActionMock).toHaveBeenCalledWith('action-1', expect.objectContaining({ sessionId: expect.any(String), concurrencyToken: 'secret-token' }), expect.anything());
        expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument();
    });

    it('retires an idempotency key after completion before preparing the same resource again', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        const tool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        const pending = (actionId: string, token: string) => ({
            status: 'pending_confirmation', actionId, data: { confirmationToken: token, expiresAtUtc: '2030-01-01T00:00:00Z' },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu chỉ định LAB-42', facility: 'FAC-TEST — Cơ sở test', department: 'DEP-TEST — Khoa test', subject: 'Bệnh nhân mã #42', encounter: 'DiagnosticOrder #42', currentStatus: 'Ordered' }, changes: [{ kind: 'diagnostic_start', summary: 'Tiếp nhận phiếu vào worklist', items: [{ label: 'Phiếu', value: 'LAB-42' }] }], consequence: 'Backend sẽ tiếp nhận phiếu.', confirmationSummary: 'Đã kiểm tra quyền và resource.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] }
        });
        prepareActionMock.mockResolvedValueOnce(pending('action-1', 'token-1')).mockResolvedValueOnce(pending('action-2', 'token-2'));
        confirmActionMock.mockResolvedValueOnce({ status: 'completed', displayText: 'Đã tiếp nhận phiếu.' });

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /Xác nhận thao tác/i }));
        await waitFor(() => expect(screen.getByText('Đã tiếp nhận phiếu.')).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(prepareActionMock).toHaveBeenCalledTimes(2));

        const firstRequest = prepareActionMock.mock.calls[0][0] as { idempotencyKey: string };
        const secondRequest = prepareActionMock.mock.calls[1][0] as { idempotencyKey: string };
        expect(secondRequest.idempotencyKey).not.toBe(firstRequest.idempotencyKey);
    });

    it('reuses the same key only when a prepare response is lost', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        const tool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockRejectedValueOnce(new Error('network interrupted')).mockResolvedValueOnce({
            status: 'pending_confirmation', actionId: 'action-retry', data: { confirmationToken: 'retry-token', expiresAtUtc: '2030-01-01T00:00:00Z' },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu chỉ định LAB-42', facility: 'FAC-TEST — Cơ sở test', department: 'DEP-TEST — Khoa test', subject: 'Bệnh nhân mã #42', encounter: 'DiagnosticOrder #42', currentStatus: 'Ordered' }, changes: [{ kind: 'diagnostic_start', summary: 'Tiếp nhận phiếu vào worklist', items: [{ label: 'Phiếu', value: 'LAB-42' }] }], consequence: 'Backend sẽ tiếp nhận phiếu.', confirmationSummary: 'Đã kiểm tra quyền và resource.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] }
        });

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByText('network interrupted')).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());

        expect(prepareActionMock.mock.calls[1][0]).toMatchObject({ idempotencyKey: prepareActionMock.mock.calls[0][0].idempotencyKey });
    });

    it('clears action A before preparing action B so a failed B cannot show A confirmation', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        const startTool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        const completeTool = { name: 'technician.prepare_complete_diagnostic_order', version: '1.0', description: 'Hoàn tất phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [startTool, completeTool] });
        prepareActionMock.mockResolvedValueOnce({
            status: 'pending_confirmation', actionId: 'action-a', data: { confirmationToken: 'token-a', expiresAtUtc: '2030-01-01T00:00:00Z' },
            preview: { toolName: startTool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu chỉ định LAB-42', facility: 'FAC-TEST — Cơ sở test', department: 'DEP-TEST — Khoa test', subject: 'Bệnh nhân mã #42', encounter: 'DiagnosticOrder #42', currentStatus: 'Ordered' }, changes: [{ kind: 'diagnostic_start', summary: 'Tiếp nhận phiếu vào worklist', items: [{ label: 'Phiếu', value: 'LAB-42' }] }], consequence: 'Hậu quả A', confirmationSummary: 'Đã kiểm tra A', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] }
        }).mockRejectedValueOnce(new Error('prepare B failed'));

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await waitFor(() => expect(screen.getAllByRole('button', { name: 'Xem trước' })).toHaveLength(2));
        fireEvent.click(screen.getAllByRole('button', { name: 'Xem trước' })[0]);
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());
        fireEvent.click(screen.getAllByRole('button', { name: 'Xem trước' })[1]);
        await waitFor(() => expect(screen.getByText('prepare B failed')).toBeInTheDocument());
        expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument();
        expect(screen.queryByText('Hậu quả A')).not.toBeInTheDocument();
    });

    it('ignores a late prepare response after account and route change', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        let resolvePrepare!: (value: unknown) => void;
        const tool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockReturnValueOnce(new Promise(resolve => { resolvePrepare = resolve; }));
        const view = render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));

        mockUser = { userId: 'reception-1', fullName: 'Reception', role: 'Receptionist' };
        mockIdentityVersion = 2;
        view.rerender(<MemoryRouter initialEntries={['/reception']}><UnifiedCopilotPanel /></MemoryRouter>);
        resolvePrepare({
            status: 'pending_confirmation', actionId: 'late-action', data: { confirmationToken: 'late-token' },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu chỉ định LAB-42', facility: 'FAC-TEST — Cơ sở test', department: 'DEP-TEST — Khoa test', subject: 'Bệnh nhân mã #42', encounter: 'DiagnosticOrder #42', currentStatus: 'Ordered' }, changes: [{ kind: 'diagnostic_start', summary: 'Tiếp nhận phiếu vào worklist', items: [{ label: 'Phiếu', value: 'LAB-42' }] }], consequence: 'Late consequence', confirmationSummary: 'Late preview', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] }
        });

        await waitFor(() => expect(screen.getByText('Copilot Lễ tân')).toBeInTheDocument());
        expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument();
        expect(screen.queryByText('Late consequence')).not.toBeInTheDocument();
        expect(screen.queryByText('late-token')).not.toBeInTheDocument();
    });

    it('disables confirmation at preview expiry, never sends the old token, and allows a fresh prepare', async () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2030-01-01T00:00:00.000Z'));
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        const tool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        const preview = (expiresAtUtc: string, token: string) => ({
            status: 'pending_confirmation',
            actionId: token === 'token-1' ? 'action-1' : 'action-2',
            data: { confirmationToken: token, expiresAtUtc },
            preview: {
                toolName: tool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42',
                resource: { identity: 'Phiếu chỉ định LAB-42', facility: 'FAC-TEST — Cơ sở test', department: 'DEP-TEST — Khoa test', subject: 'Bệnh nhân mã #42', encounter: 'DiagnosticOrder #42', currentStatus: 'Ordered' },
                changes: [{ kind: 'diagnostic_start', summary: 'Tiếp nhận phiếu vào worklist', items: [{ label: 'Phiếu', value: 'LAB-42' }] }],
                consequence: 'Backend sẽ tiếp nhận phiếu.', confirmationSummary: 'Đã kiểm tra quyền và resource.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc,
                sources: [{ name: 'diagnostic_orders', kind: 'database' }]
            }
        });
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockResolvedValueOnce(preview('2030-01-01T00:00:10Z', 'token-1')).mockResolvedValueOnce(preview('2030-01-01T00:00:20Z', 'token-2'));
        confirmActionMock.mockResolvedValue({ status: 'completed', displayText: 'Đã tiếp nhận phiếu.' });

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument();
        const confirm = screen.getByRole('button', { name: /Xác nhận thao tác/i });
        expect(confirm).not.toBeDisabled();

        await act(async () => { vi.advanceTimersByTime(10001); });
        expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeDisabled();
        expect(screen.getByRole('alert')).toHaveTextContent('Preview đã hết hạn');
        fireEvent.click(screen.getByRole('button', { name: /Xác nhận thao tác/i }));
        expect(confirmActionMock).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: 'Chuẩn bị lại xem trước' }));
        await act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
        expect(prepareActionMock).toHaveBeenCalledTimes(2);
        expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).not.toBeDisabled();
    });

    it('rejects gibberish/question booking reasons and normalizes a trailing escape', () => {
        expect(normalizeBookingReason('Đau đầu kéo dài\\')).toBe('Đau đầu kéo dài');
        expect(isValidBookingReason('kkkkkkkkkkkk')).toBe(false);
        expect(isValidBookingReason('Tôi nên chọn khoa nào?')).toBe(false);
        expect(isValidBookingReason('Đau đầu kéo dài ba ngày')).toBe(true);
    });
});
