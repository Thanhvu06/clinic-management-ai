import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { Button, Input } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PatientChatBoundary } from '../layouts/PatientChatBoundary';
import { VerificationQueueRoute } from '../layouts/VerificationQueueRoute';
import { RoleRoute } from '../routes/guards';
import { ClinicalEditor } from '../pages/doctor/ClinicalEditor';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { AdminWorkSchedules } from '../pages/admin/AdminWorkSchedules';
import { formatAuditTime } from '../utils/auditTime';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';

const auth = vi.hoisted(() => ({ user: { role: 'LabSupervisor' } as { role: string } | null, loading: false, isAuthenticated: true }));
const selection = vi.hoisted(() => vi.fn());
vi.mock('../auth/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../components/MedicalChatWidget', () => ({ MedicalChatWidget: () => <div>Patient chat</div> }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: selection }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn(), showConfirm: vi.fn() }) }));
vi.mock('../hooks/usePrescriptionSafety', () => ({ usePrescriptionSafety: () => ({ result: null, loading: false, error: '', retry: vi.fn() }) }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getVisitPatientClinicalContext: vi.fn(), getActiveMedicines: vi.fn(), saveVisitEncounter: vi.fn(), saveVisitPrescriptionDraft: vi.fn() } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getDoctorOrdersByVisit: vi.fn(async () => ({ success: true, data: [] })), getCatalog: vi.fn(async () => ({ success: true, data: [] })),
    getVerificationQueue: vi.fn(async () => ({ success: true, data: { items: [{ id: 7, orderCode: 'CLS-7', patientName: 'Người bệnh', items: [] }], totalItems: 1 } })),
} }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));

afterEach(cleanup);
beforeEach(() => { auth.user = { role: 'LabSupervisor' }; auth.loading = false; vi.clearAllMocks(); });

describe('demo round three regressions', () => {
    it.each(['LabSupervisor', 'Doctor', 'Receptionist', 'DiagnosticTechnician', 'Pharmacist', 'Admin'])('hides patient chat for %s', role => {
        auth.user = { role }; render(<PatientChatBoundary />); expect(screen.queryByText('Patient chat')).not.toBeInTheDocument();
    });
    it.each(['Patient', null])('shows chat for patient or guest: %s', role => {
        auth.user = role ? { role } : null; render(<PatientChatBoundary />); expect(screen.getByText('Patient chat')).toBeInTheDocument();
    });
    it('keeps editable clinical fields enabled and disables them in archived records', () => {
        const view = render(<ClinicalEditor readOnly={false}><Input aria-label="Chẩn đoán" /><Button>Lưu</Button></ClinicalEditor>);
        expect(screen.getByRole('textbox')).toBeEnabled();
        view.rerender(<ClinicalEditor readOnly><Input aria-label="Chẩn đoán" /><Button>Lưu</Button></ClinicalEditor>);
        expect(screen.getByRole('textbox')).toBeDisabled(); expect(screen.getByRole('button')).toBeDisabled();
    });
    it('locks encounter, vitals and prescription fields in the completed examination desk', async () => {
        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue({ success: true, message: '', data: {
            patientId: 1, patientName: 'Hồ sơ đã chốt', patientPhone: '0901234567', pastVisits: [], totalPastVisits: 0,
            currentAppointment: { id: 1, appointmentCode: 'VIS-1', patientId: 1, doctorId: 1, doctorName: 'Nguyễn Minh Khải', specialtyId: 1, specialtyName: 'Nội tổng quát', appointmentDate: '2026-10-24', startTime: '08:00', endTime: '08:30', status: 'Completed' },
        } });
        vi.mocked(doctorApi.getActiveMedicines).mockResolvedValue({ success: true, message: '', data: [] });
        render(<MemoryRouter initialEntries={['/doctor/visits/1/examination']}><Routes><Route path="/doctor/visits/:visitId/examination" element={<DoctorExaminationWorkspace />} /></Routes></MemoryRouter>);
        await screen.findByText('Hồ sơ đã chốt');
        for (const tab of [/Diễn tiến/, /Sinh hiệu/, /Kê đơn/]) {
            fireEvent.click(screen.getByRole('button', { name: tab }));
            const editor = screen.getByRole('group', { name: 'Nội dung hồ sơ khám' });
            expect(editor).toBeDisabled();
            for (const input of editor.querySelectorAll('input, textarea')) expect(input).toBeDisabled();
        }
        expect(doctorApi.saveVisitEncounter).not.toHaveBeenCalled(); expect(doctorApi.saveVisitPrescriptionDraft).not.toHaveBeenCalled();
    });
    it('lets a lab supervisor open the queue and links to the supervisor review page', async () => {
        render(<MemoryRouter initialEntries={['/diagnostics/verification']}><Routes>
            <Route element={<RoleRoute roles={['DiagnosticTechnician', 'LabSupervisor']} />}><Route path="/diagnostics/verification" element={<VerificationQueueRoute />} /></Route>
            <Route path="/forbidden" element={<div>Forbidden</div>} />
        </Routes></MemoryRouter>);
        expect(await screen.findByRole('link', { name: 'Mở phiếu' })).toHaveAttribute('href', '/lab/orders/7');
    });
    it.each(['Receptionist', 'Patient', 'Pharmacist', 'Admin'])('keeps %s out of the lab queue', async role => {
        auth.user = { role };
        render(<MemoryRouter initialEntries={['/diagnostics/verification']}><Routes>
            <Route element={<RoleRoute roles={['DiagnosticTechnician', 'LabSupervisor']} />}><Route path="/diagnostics/verification" element={<VerificationQueueRoute />} /></Route>
            <Route path="/forbidden" element={<div>Forbidden</div>} />
        </Routes></MemoryRouter>);
        expect(await screen.findByText('Forbidden')).toBeInTheDocument();
    });
    it.each([0, 7])('offers slot generation only when a shift has no slots: %i', async slotCount => {
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => ({ success: true, data: url.includes('work-schedules')
            ? [{ id: 1, doctorId: 1, workDate: '2026-10-26', startTime: '08:00', endTime: '11:30', isActive: true, slotCount }]
            : { items: [{ id: 1, fullName: 'Bác sĩ Minh' }], totalItems: 1 } }) as never);
        render(<AdminWorkSchedules />);
        await waitFor(() => expect(screen.getByRole('combobox')).toBeEnabled());
        fireEvent.mouseDown(screen.getByRole('combobox'));
        fireEvent.click(await screen.findByText('Bác sĩ Minh', { selector: '.ant-select-item-option-content' }));
        await screen.findByRole('table');
        const rows = within(screen.getByRole('table'));
        expect(rows.queryByRole('button', { name: 'Tạo khung giờ' }) !== null).toBe(slotCount === 0);
        expect(screen.queryByText(/Cần tạo khung giờ/) !== null).toBe(slotCount === 0);
    });
    it('formats audit timestamps in Vietnam with minutes and a four digit year', () => {
        expect(formatAuditTime('2026-10-24T01:23:45Z')).toBe('08:23 24/10/2026');
        expect(formatAuditTime('2026-10-24T08:23:45')).toBe('08:23 24/10/2026');
    });
});
