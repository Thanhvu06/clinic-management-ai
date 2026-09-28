import type React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';
import { COPILOT_ROLE_CONFIG, providerStateLabel } from '../components/copilot/copilotConfig';
import { CopilotResourceProvider, useCopilotResource } from '../components/copilot/copilotResourceContext';
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
    suggestedPrompts: [], cards: [{ type: 'doctor_summary', title: 'Ca được phân công', description: 'Dữ liệu hiện tại', retrievedAtUtc: '2030-01-01T00:00:00Z', data: [{ doctorName: 'Doctor 1', status: 'scheduled', patientId: 'must-not-render', appointmentId: 42 }], sources: [{ name: 'appointments', kind: 'database' }] }],
    availableTools: [], sources: [{ name: 'appointments', kind: 'database' }], ...overrides
});

const SelectedResourceButton: React.FC<{ context: Record<string, unknown>; source?: string; actionArguments?: Record<string, unknown> }> = ({ context, source = 'test-domain-row', actionArguments }) => {
    const { setSelection } = useCopilotResource();
    return <button type="button" onClick={() => setSelection({ context, source, actionArguments })}>Chọn resource từ dòng nghiệp vụ</button>;
};

const DoctorActionArgumentsButtons: React.FC = () => {
    const { setSelection } = useCopilotResource();
    const setIndication = (clinicalIndication: string) => setSelection({
        context: { visitId: 42, serviceIds: [7] },
        source: 'doctor-visit-form',
        actionArguments: { clinicalIndication }
    });
    return <div>
        <button type="button" onClick={() => setIndication('Đau ngực khi gắng sức')}>Chọn chỉ định A</button>
        <button type="button" onClick={() => setIndication('Ho kéo dài')}>Chọn chỉ định B</button>
    </div>;
};

const ReceptionDepartmentButtons: React.FC = () => {
    const { setSelection } = useCopilotResource();
    return <div>
        <button type="button" onClick={() => setSelection({ context: { appointmentId: 77, departmentId: 19 }, source: 'reception-appointment-row-A' })}>Chọn khoa A</button>
        <button type="button" onClick={() => setSelection({ context: { appointmentId: 77 }, source: 'reception-appointment-row-A' })}>Bỏ chọn khoa</button>
        <button type="button" onClick={() => setSelection({ context: { appointmentId: 77, departmentId: 20 }, source: 'reception-appointment-row-B' })}>Chọn khoa B</button>
    </div>;
};

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
        expect(screen.queryByText('42')).not.toBeInTheDocument();
        expect(screen.getByText(/Dữ liệu đọc lúc:/)).toBeInTheDocument();
        const request = sendMock.mock.calls[0][0] as Record<string, unknown>;
        expect(request).toMatchObject({ message: 'Tóm tắt ca này', currentRoute: '/doctor/appointments/42' });
        expect(request.resourceContext).toEqual({ appointmentId: 42 });
        expect(request).not.toHaveProperty('userId');
        expect(request).not.toHaveProperty('role');
        expect(request).not.toHaveProperty('facilityId');
    });

    it('renders the versioned public catalog envelope without treating metadata as a result row', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Patient', role: 'Patient' };
        sendMock.mockResolvedValueOnce(response({
            cards: [{
                type: 'clinic_knowledge',
                title: 'Dữ liệu đã kiểm chứng',
                description: 'Đã tìm thấy dữ liệu công khai.',
                data: {
                    status: 'matched',
                    mode: 'search',
                    sourceType: 'specialty',
                    items: [{
                        sourceType: 'specialty',
                        sourceId: 'specialty:42',
                        title: 'Tim mạch',
                        publishedPrice: 250000,
                        details: { specialtyCode: 'SP06' }
                    }],
                    retrievedAtUtc: '2030-01-01T00:00:00Z'
                },
                sources: [{ name: 'clinic_public_catalog', kind: 'approved_database' }]
            }]
        }));

        render(<MemoryRouter initialEntries={['/patient']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở (?:Copilot|Trợ lý) Bệnh nhân/i }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Có khám tim mạch không?' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));

        await waitFor(() => expect(screen.getByText('Tim mạch')).toBeInTheDocument());
        expect(screen.getByText('250.000 ₫')).toBeInTheDocument();
        expect(screen.getByText('SP06')).toBeInTheDocument();
        expect(screen.getByText('clinic_public_catalog · approved_database')).toBeInTheDocument();
        expect(screen.queryByText(/items:/i)).not.toBeInTheDocument();
    });

    it('renders a public catalog not_found response without inventing a result row', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Patient', role: 'Patient' };
        sendMock.mockResolvedValueOnce(response({
            message: 'Tra cứu đã hoàn tất.',
            cards: [{
                type: 'clinic_knowledge',
                title: 'Dữ liệu công khai',
                description: 'Không có cơ sở phù hợp với mã TEST-NOT-FOUND.',
                data: {
                    status: 'not_found',
                    mode: 'search',
                    sourceType: 'facility',
                    items: [],
                    retrievedAtUtc: '2030-01-01T00:00:00Z'
                },
                sources: [{ name: 'clinic_public_catalog', kind: 'approved_database' }]
            }]
        }));

        render(<MemoryRouter initialEntries={['/patient']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở (?:Copilot|Trợ lý) Bệnh nhân/i }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Tìm cơ sở TEST-NOT-FOUND' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));

        await waitFor(() => expect(screen.getByText('Không có cơ sở phù hợp với mã TEST-NOT-FOUND.')).toBeInTheDocument());
        expect(screen.queryByText('Cơ sở không xác định')).not.toBeInTheDocument();
    });

    it('renders a catalog clarification without showing catalog rows', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Patient', role: 'Patient' };
        sendMock.mockResolvedValueOnce(response({
            message: 'Bạn muốn xem danh sách hay tra cứu tên cụ thể?',
            clarification: 'Bạn muốn xem danh sách hay tra cứu tên cụ thể?',
            cards: []
        }));

        render(<MemoryRouter initialEntries={['/patient']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở (?:Copilot|Trợ lý) Bệnh nhân/i }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Có bác sĩ nào?' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));

        await waitFor(() => expect(screen.getAllByText('Bạn muốn xem danh sách hay tra cứu tên cụ thể?')).toHaveLength(2));
        expect(screen.queryByText('Bác sĩ Nhiễu')).not.toBeInTheDocument();
    });

    it('renders unpublished patient results as unavailable and never leaks the result text', async () => {
        mockUser = { userId: 'patient-1', fullName: 'Patient', role: 'Patient' };
        sendMock.mockResolvedValueOnce(response({
            cards: [{
                type: 'patient_diagnostic_results', title: 'Kết quả cận lâm sàng', description: 'Kết quả theo quyền hiện tại.',
                retrievedAtUtc: '2030-01-01T00:00:00Z',
                data: [{ publishedToPatient: false, reviewedAtUtc: null, items: [{ service: 'Siêu âm', status: 'Completed', result: { resultText: 'KẾT QUẢ CHƯA CÔNG BỐ' } }] }],
                sources: [{ name: 'patient_results', kind: 'approved_database' }]
            }]
        }));
        render(<MemoryRouter initialEntries={['/patient']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở (?:Copilot|Trợ lý) Bệnh nhân/i }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Xem kết quả' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
        await waitFor(() => expect(screen.getByText('Chưa công bố')).toBeInTheDocument());
        expect(screen.getByText('Chưa được công bố')).toBeInTheDocument();
        expect(screen.queryByText('KẾT QUẢ CHƯA CÔNG BỐ')).not.toBeInTheDocument();
    });

    it('renders pharmacy payment status and each payment item from the typed contract', async () => {
        mockUser = { userId: 'pharmacist-1', fullName: 'Pharmacist', role: 'Pharmacist' };
        sendMock.mockResolvedValueOnce(response({
            role: 'Pharmacist',
            cards: [{
                type: 'pharmacist_prescription_queue', title: 'Đơn thuốc', description: 'Dữ liệu đơn thuốc.',
                retrievedAtUtc: '2030-01-01T00:00:00Z',
                data: [{ status: 'Issued', paymentStatus: 'PartiallyPaid', paymentItems: [{ medicine: 'Paracetamol', requiredQuantity: 2, paidQuantity: 1, itemPaymentStatus: 'PartiallyPaid' }] }],
                sources: [{ name: 'pharmacy', kind: 'approved_database' }]
            }]
        }));
        render(<MemoryRouter initialEntries={['/pharmacy/prescriptions']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Dược sĩ/i }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Xem đơn thuốc' } });
        fireEvent.click(screen.getByRole('button', { name: 'Gửi yêu cầu Copilot' }));
        await waitFor(() => expect(screen.getAllByText('PartiallyPaid')).toHaveLength(2));
        expect(screen.getByText('Paracetamol')).toBeInTheDocument();
        expect(screen.getByText('2')).toBeInTheDocument();
        expect(screen.getByText('1')).toBeInTheDocument();
    });

    it('uses the selected real domain row instead of a typed route query for action arguments', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        const tool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockResolvedValue({
            status: 'pending_confirmation', actionId: 'selected-action', data: { confirmationToken: 'selected-token' },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu đã chọn', facility: 'Cơ sở', department: 'Khoa', subject: 'Bệnh nhân', encounter: 'Phiếu', currentStatus: 'Ordered' }, changes: [{ kind: 'start', summary: 'Tiếp nhận', items: [{ label: 'Phiếu', value: 'Đã chọn' }] }], consequence: 'Sẽ tiếp nhận phiếu.', confirmationSummary: 'Đã kiểm tra.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:10Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] }
        });
        render(<MemoryRouter initialEntries={['/diagnostics/orders/999?diagnosticOrderId=999']}><CopilotResourceProvider><SelectedResourceButton context={{ diagnosticOrderId: 42 }} /><UnifiedCopilotPanel /></CopilotResourceProvider></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Chọn resource từ dòng nghiệp vụ' }));
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());
        expect(prepareActionMock).toHaveBeenCalledWith(expect.objectContaining({ argumentsJson: JSON.stringify({ orderId: 42 }) }), expect.anything());
    });

    it('keeps reception Copilot check-in locked when the selected appointment lacks a real department selector', async () => {
        mockUser = { userId: 'reception-1', fullName: 'Reception', role: 'Receptionist' };
        const tool = { name: 'reception.prepare_check_in_appointment', version: '1.0', description: 'Tiếp nhận lịch hẹn', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        render(<MemoryRouter initialEntries={['/reception/appointments']}><CopilotResourceProvider><SelectedResourceButton context={{ appointmentId: 77 }} /><UnifiedCopilotPanel /></CopilotResourceProvider></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Chọn resource từ dòng nghiệp vụ' }));
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Lễ tân/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeDisabled());
        expect(screen.getByText('Cần appointment và department đang được chọn từ dữ liệu thật.')).toBeInTheDocument();
    });

    it('keeps the doctor conversation while form arguments change and retires the old preview/key', async () => {
        mockUser = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
        const tool = { name: 'doctor.prepare_diagnostic_order', version: '1.0', description: 'Tạo chỉ định', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        const pending = (actionId: string, token: string) => ({
            status: 'pending_confirmation', actionId, data: { confirmationToken: token },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'PatientVisit', resourceId: '42', resource: { identity: 'Ca khám đang mở', facility: 'Cơ sở', department: 'Khoa', subject: 'Người bệnh', encounter: 'V-42', currentStatus: 'InProgress' }, changes: [{ kind: 'diagnostic_order', summary: 'Tạo chỉ định', items: [{ label: 'Chỉ định', value: 'Đã kiểm tra' }] }], consequence: 'Tạo phiếu cận lâm sàng.', confirmationSummary: 'Đã kiểm tra quyền và dữ liệu.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'visit', kind: 'database' }] }
        });
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockResolvedValueOnce(pending('action-a', 'token-a')).mockResolvedValueOnce(pending('action-b', 'token-b'));

        render(<MemoryRouter initialEntries={['/doctor/visits/42']}><CopilotResourceProvider><DoctorActionArgumentsButtons /><UnifiedCopilotPanel /></CopilotResourceProvider></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Chọn chỉ định A' }));
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());
        fireEvent.change(screen.getByRole('textbox', { name: 'Nội dung Copilot' }), { target: { value: 'Tin nhắn đang gõ dở' } });

        fireEvent.click(screen.getByRole('button', { name: 'Chọn chỉ định B' }));
        await waitFor(() => expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument());
        expect(screen.getByRole('textbox', { name: 'Nội dung Copilot' })).toHaveValue('Tin nhắn đang gõ dở');

        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(prepareActionMock).toHaveBeenCalledTimes(2));
        const firstRequest = prepareActionMock.mock.calls[0][0] as { argumentsJson: string; idempotencyKey: string };
        const secondRequest = prepareActionMock.mock.calls[1][0] as { argumentsJson: string; idempotencyKey: string };
        expect(JSON.parse(firstRequest.argumentsJson)).toMatchObject({ clinicalIndication: 'Đau ngực khi gắng sức' });
        expect(JSON.parse(secondRequest.argumentsJson)).toMatchObject({ clinicalIndication: 'Ho kéo dài' });
        expect(secondRequest.idempotencyKey).not.toBe(firstRequest.idempotencyKey);
    });

    it('ignores a late prepare response after the doctor changes the action input', async () => {
        const tool = { name: 'doctor.prepare_diagnostic_order', version: '1.0', description: 'Tạo chỉ định', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        let resolvePrepare!: (value: unknown) => void;
        mockUser = { userId: 'doctor-1', fullName: 'Doctor', role: 'Doctor' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockReturnValueOnce(new Promise(resolve => { resolvePrepare = resolve; }));
        render(<MemoryRouter initialEntries={['/doctor/visits/42']}><CopilotResourceProvider><DoctorActionArgumentsButtons /><UnifiedCopilotPanel /></CopilotResourceProvider></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Chọn chỉ định A' }));
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Bác sĩ/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        fireEvent.click(screen.getByRole('button', { name: 'Chọn chỉ định B' }));
        resolvePrepare({ status: 'pending_confirmation', actionId: 'late-action', data: { confirmationToken: 'late-token' }, preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'PatientVisit', resourceId: '42', resource: { identity: 'Ca A' }, changes: [{ kind: 'write', summary: 'A', items: [{ label: 'x', value: 'y' }] }], consequence: 'A', confirmationSummary: 'A', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', sources: [{ name: 'db', kind: 'database' }] } });
        await act(async () => { await Promise.resolve(); await Promise.resolve(); });
        expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument();
        expect(screen.queryByText('late-token')).not.toBeInTheDocument();
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

    it('clears a pending preview after backend 409 so a stale token cannot be retried', async () => {
        mockUser = { userId: 'tech-1', fullName: 'Technician', role: 'DiagnosticTechnician' };
        const tool = { name: 'technician.prepare_start_diagnostic_order', version: '1.0', description: 'Tiếp nhận phiếu', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockResolvedValue({
            status: 'pending_confirmation', actionId: 'conflict-action', data: { confirmationToken: 'conflict-token' },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'DiagnosticOrder', resourceId: '42', resource: { identity: 'Phiếu đã chọn', facility: 'Cơ sở', department: 'Khoa', subject: 'Bệnh nhân', encounter: 'Phiếu', currentStatus: 'Ordered' }, changes: [{ kind: 'start', summary: 'Tiếp nhận', items: [{ label: 'Phiếu', value: 'Đã chọn' }] }], consequence: 'Sẽ tiếp nhận phiếu.', confirmationSummary: 'Đã kiểm tra.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:10Z', sources: [{ name: 'diagnostic_orders', kind: 'database' }] }
        });
        confirmActionMock.mockRejectedValue({ status: 409, message: 'RESOURCE_VERSION_CHANGED' });

        render(<MemoryRouter initialEntries={['/diagnostics/orders/42']}><UnifiedCopilotPanel /></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Kỹ thuật viên/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: /Xác nhận thao tác/i }));
        await waitFor(() => expect(screen.getByText(/Dữ liệu hoặc mã xác nhận đã thay đổi/)).toBeInTheDocument());
        expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument();
        expect(screen.queryByText('conflict-token')).not.toBeInTheDocument();
    });

    it('locks and clears a receptionist preview when the department is deselected, then uses a new key for another department', async () => {
        mockUser = { userId: 'reception-1', fullName: 'Reception', role: 'Receptionist' };
        const tool = { name: 'reception.prepare_check_in_appointment', version: '1.0', description: 'Tiếp nhận lịch hẹn', accessMode: 'RoleRestricted', riskLevel: 'High', confirmation: 'ExplicitUserConfirmation' };
        const pending = (actionId: string, token: string, department: string) => ({
            status: 'pending_confirmation', actionId, data: { confirmationToken: token },
            preview: { toolName: tool.name, status: 'pending_confirmation', resourceType: 'Appointment', resourceId: '77', resource: { identity: `Lịch hẹn AP-77 · ${department}`, facility: 'Cơ sở test', department, subject: 'Bệnh nhân mã hóa', encounter: 'Appointment #77', currentStatus: 'Confirmed' }, changes: [{ kind: 'check_in', summary: 'Tiếp nhận bệnh nhân', items: [{ label: 'Khoa', value: department }] }], consequence: `Hậu quả ${department}`, confirmationSummary: 'Backend đã kiểm tra.', validatedAtUtc: '2030-01-01T00:00:00Z', expiresAtUtc: '2030-01-02T00:00:00Z', sources: [{ name: 'appointments', kind: 'database' }] }
        });
        catalogMock.mockResolvedValue({ tools: [], actionTools: [tool] });
        prepareActionMock.mockResolvedValueOnce(pending('action-a', 'token-a', 'Khoa A')).mockResolvedValueOnce(pending('action-b', 'token-b', 'Khoa B'));

        render(<MemoryRouter initialEntries={['/reception/appointments/77']}><CopilotResourceProvider><ReceptionDepartmentButtons /><UnifiedCopilotPanel /></CopilotResourceProvider></MemoryRouter>);
        fireEvent.click(screen.getByRole('button', { name: 'Chọn khoa A' }));
        fireEvent.click(screen.getByRole('button', { name: /Mở Copilot Lễ tân/i }));
        const previewButton = await screen.findByRole('button', { name: 'Xem trước' });
        expect(previewButton).not.toBeDisabled();
        fireEvent.click(previewButton);
        await waitFor(() => expect(screen.getByRole('button', { name: /Xác nhận thao tác/i })).toBeInTheDocument());
        expect(screen.getByText('Hậu quả Khoa A')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Bỏ chọn khoa' }));
        await waitFor(() => expect(screen.queryByRole('button', { name: /Xác nhận thao tác/i })).not.toBeInTheDocument());
        expect(screen.queryByText('Hậu quả Khoa A')).not.toBeInTheDocument();
        expect(screen.queryByText('token-a')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Xem trước' })).toBeDisabled();
        expect(screen.getByText('Cần appointment và department đang được chọn từ dữ liệu thật.')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Chọn khoa B' }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Xem trước' })).not.toBeDisabled());
        fireEvent.click(screen.getByRole('button', { name: 'Xem trước' }));
        await waitFor(() => expect(screen.getByText('Hậu quả Khoa B')).toBeInTheDocument());
        expect(prepareActionMock).toHaveBeenCalledTimes(2);
        const firstKey = (prepareActionMock.mock.calls[0][0] as { idempotencyKey: string }).idempotencyKey;
        const secondKey = (prepareActionMock.mock.calls[1][0] as { idempotencyKey: string }).idempotencyKey;
        expect(secondKey).not.toBe(firstKey);
    });

    it('rejects gibberish/question booking reasons and normalizes a trailing escape', () => {
        expect(normalizeBookingReason('Đau đầu kéo dài\\')).toBe('Đau đầu kéo dài');
        expect(isValidBookingReason('kkkkkkkkkkkk')).toBe(false);
        expect(isValidBookingReason('Tôi nên chọn khoa nào?')).toBe(false);
        expect(isValidBookingReason('Đau đầu kéo dài ba ngày')).toBe(true);
    });
});
