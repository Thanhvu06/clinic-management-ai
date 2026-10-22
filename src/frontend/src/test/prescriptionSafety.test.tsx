import { act, cleanup, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PrescriptionSafetyPanel } from '../components/prescriptions/PrescriptionSafetyPanel';
import { usePrescriptionSafety } from '../hooks/usePrescriptionSafety';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { PatientClinicalContextDto } from '../types';
import type { PrescriptionSafetyResultDto, PrescriptionSafetyWarningDto } from '../types/prescriptionSafety';

const mocks = vi.hoisted(() => ({ check: vi.fn(), context: vi.fn(), complete: vi.fn(), completeVisit: vi.fn(), save: vi.fn(),
    get: vi.fn(), post: vi.fn(), toast: vi.fn(), alert: vi.fn() }));
vi.mock('../api/doctorApi', () => ({ doctorApi: {
    checkPrescriptionSafety: mocks.check, getPatientClinicalContext: mocks.context, getVisitPatientClinicalContext: mocks.context,
    completeConsultation: mocks.complete, completeVisitConsultation: mocks.completeVisit, savePrescriptionDraft: mocks.save, saveVisitPrescriptionDraft: mocks.save,
    getActiveMedicines: async () => ({ success: true, data: [{ id: 2, code: 'PS-2', name: 'Thuốc thứ hai', unit: 'Viên', stockQuantity: 100 }] }),
} }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getDoctorOrdersByAppointment: async () => ({ success: true, data: [] }), getDoctorOrdersByVisit: async () => ({ success: true, data: [] }),
    getCatalog: async () => ({ success: true, data: [] }),
} }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showToast: mocks.toast, showAlert: mocks.alert, showConfirm: vi.fn() }) }));

const danger: PrescriptionSafetyWarningDto = { level: 'Danger', type: 'Allergy', medicineIds: [1], title: 'Dị ứng thuốc đã ghi nhận', detail: 'Amoxicillin: dị ứng Penicillin, mức độ nặng, nổi mề đay.' };
const warning: PrescriptionSafetyWarningDto = { level: 'Warning', type: 'Interaction', medicineIds: [1, 2], title: 'Tương tác thuốc', detail: 'Cần đánh giá tương tác.' };
const info: PrescriptionSafetyWarningDto = { level: 'Info', type: 'MissingIngredient', medicineIds: [2], title: 'Không có hoạt chất để kiểm tra', detail: 'Thuốc chưa có hoạt chất.' };
const empty: PrescriptionSafetyResultDto = { allergies: [], warnings: [] };
const dangerous: PrescriptionSafetyResultDto = { allergies: [{ id: 1, patientId: 2, allergenType: 1, allergenTypeName: 'Drug', allergenName: 'Penicillin', severity: 3, severityName: 'Severe', reactionDescription: 'Nổi mề đay', recordedAtUtc: '2026-10-16T01:00:00Z' }], warnings: [danger] };
const context: PatientClinicalContextDto = {
    patientId: 2, patientName: 'Bệnh nhân an toàn', patientPhone: '0901234567', totalPastVisits: 0, pastVisits: [],
    currentAppointment: { id: 10, appointmentCode: 'APT-PS', patientId: 2, doctorId: 5, doctorName: 'BS. An', specialtyId: 1, specialtyName: 'Nội khoa', appointmentDate: '2026-10-16', status: 'InConsultation', reason: 'Kiểm tra', startTime: '08:00:00', endTime: '08:30:00' },
    encounter: { id: 7, appointmentId: 10, doctorId: 5, doctorName: 'BS. An', diagnosis: 'Chẩn đoán kiểm thử', summary: 'Kết luận kiểm thử', createdAtUtc: '2026-10-16T01:00:00Z', rowVersion: 'enc-v1' },
    prescription: { id: 9, appointmentId: 10, doctorId: 5, doctorName: 'BS. An', patientId: 2, patientName: 'Bệnh nhân an toàn', status: 'Draft', createdAt: '2026-10-16T01:00:00Z', rowVersion: 'rx-v1', items: [{ medicineId: 1, medicineCode: 'PS-1', medicineName: 'Amoxicillin', unit: 'Viên', quantity: 2, availableStock: 100, dosage: '1 viên', frequency: '2 lần', durationDays: 2 }] },
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function mount(path = '/doctor/appointments/10/examination') {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}><Routes>
        <Route path="/doctor/appointments/:id/examination" element={<DoctorExaminationWorkspace />} />
        <Route path="/doctor/visits/:visitId/examination" element={<DoctorExaminationWorkspace />} />
        <Route path="/pharmacy" element={<PharmacyPrescriptions />} />
        <Route path="/doctor/appointments/:id" element={<p>Đã hoàn tất</p>} />
        <Route path="/doctor/queue" element={<p>Đã hoàn tất</p>} />
    </Routes></MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}
const panel = (props: Parameters<typeof PrescriptionSafetyPanel>[0] = {}) => <ConfigProvider theme={{ token: { motion: false } }}><PrescriptionSafetyPanel {...props} /></ConfigProvider>;
async function confirmCompletion() {
    fireEvent.click(screen.getByRole('button', { name: 'HOÀN TẤT KHÁM BỆNH' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận hoàn tất' }));
}

beforeEach(() => {
    vi.resetAllMocks();
    mocks.check.mockResolvedValue(ok(empty)); mocks.context.mockResolvedValue(ok(context));
    mocks.complete.mockResolvedValue({ success: true }); mocks.completeVisit.mockResolvedValue({ success: true });
    mocks.save.mockResolvedValue(ok(context.prescription));
});
afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); });

describe('Prescription safety panel and API', () => {
    it('renders recorded allergy severity and error, warning and info alerts', () => {
        render(panel({ result: { ...dangerous, warnings: [danger, warning, info] } }));
        expect(screen.getByText('Penicillin • Nặng')).toBeInTheDocument();
        const alerts = screen.getAllByRole('alert');
        expect(alerts[0]).toHaveClass('ant-alert-error'); expect(alerts[1]).toHaveClass('ant-alert-warning'); expect(alerts[2]).toHaveClass('ant-alert-info');
        expect(screen.getByText(danger.detail)).toBeInTheDocument();
        expect(screen.getByText('Dữ liệu tương tác mang tính tham khảo, cần dược sĩ/bác sĩ xác nhận.')).toBeInTheDocument();
    });

    it('separates loading, retryable errors and no-warning states', () => {
        const retry = vi.fn(); const view = render(panel({ loading: true, result: dangerous }));
        expect(screen.getByText('Đang kiểm tra an toàn đơn thuốc...')).toBeInTheDocument();
        expect(screen.queryByText(danger.title)).not.toBeInTheDocument();
        view.rerender(panel({ error: 'Mất kết nối', onRetry: retry, result: dangerous }));
        expect(screen.getByText('Mất kết nối')).toBeInTheDocument(); fireEvent.click(screen.getByRole('button', { name: 'Thử lại' })); expect(retry).toHaveBeenCalledOnce();
        expect(screen.queryByText('Chưa phát hiện cảnh báo')).not.toBeInTheDocument();
        view.rerender(panel({ result: empty })); expect(screen.getByText('Chưa phát hiện cảnh báo')).toBeInTheDocument();
    });

    it('posts medicine IDs to the appointment and visit safety endpoints', async () => {
        const { doctorApi } = await vi.importActual<typeof import('../api/doctorApi')>('../api/doctorApi');
        await doctorApi.checkPrescriptionSafety(10, [1, 2]); await doctorApi.checkPrescriptionSafety(20, [3], true);
        expect(mocks.post).toHaveBeenNthCalledWith(1, '/doctor/appointments/10/prescription/safety-check', { medicineIds: [1, 2] });
        expect(mocks.post).toHaveBeenNthCalledWith(2, '/doctor/visits/20/prescription/safety-check', { medicineIds: [3] });
    });
});

describe('Prescription safety debounce', () => {
    it('debounces medicine changes for 400ms and sends the newest distinct IDs', async () => {
        vi.useFakeTimers();
        const hook = renderHook(({ ids }) => usePrescriptionSafety(10, false, ids), { initialProps: { ids: [1] } });
        await act(async () => { await vi.advanceTimersByTimeAsync(399); }); expect(mocks.check).not.toHaveBeenCalled();
        hook.rerender({ ids: [2, 1, 2] });
        await act(async () => { await vi.advanceTimersByTimeAsync(399); }); expect(mocks.check).not.toHaveBeenCalled();
        await act(async () => { await vi.advanceTimersByTimeAsync(1); });
        expect(mocks.check).toHaveBeenCalledExactlyOnceWith(10, [1, 2], false); expect(hook.result.current.loading).toBe(false);
    });

    it('ignores an older response after a newer medicine check has completed', async () => {
        vi.useFakeTimers(); let finishA!: (value: ReturnType<typeof ok<PrescriptionSafetyResultDto>>) => void; let finishB!: typeof finishA;
        mocks.check.mockImplementationOnce(() => new Promise(resolve => { finishA = resolve; })).mockImplementationOnce(() => new Promise(resolve => { finishB = resolve; }));
        const hook = renderHook(({ ids }) => usePrescriptionSafety(10, false, ids), { initialProps: { ids: [1] } });
        await act(async () => { await vi.advanceTimersByTimeAsync(400); }); hook.rerender({ ids: [2] }); expect(hook.result.current.result).toBeNull();
        await act(async () => { await vi.advanceTimersByTimeAsync(400); finishB(ok(dangerous)); });
        expect(hook.result.current.result).toEqual(dangerous);
        await act(async () => { finishA(ok(empty)); }); expect(hook.result.current.result).toEqual(dangerous);
    });

    it('retries failed checks and uses visit identity when the route changes', async () => {
        vi.useFakeTimers(); mocks.check.mockRejectedValueOnce({ message: 'Thử lại kiểm tra' }).mockResolvedValue(ok(empty));
        const hook = renderHook(({ visit }) => usePrescriptionSafety(10, visit, [1]), { initialProps: { visit: false } });
        await act(async () => { await vi.advanceTimersByTimeAsync(400); }); expect(hook.result.current.error).toBe('Thử lại kiểm tra');
        act(() => hook.result.current.retry()); await act(async () => { await vi.advanceTimersByTimeAsync(400); }); expect(hook.result.current.error).toBe('');
        hook.rerender({ visit: true }); await act(async () => { await vi.advanceTimersByTimeAsync(400); }); expect(mocks.check).toHaveBeenLastCalledWith(10, [1], true);
    });
});

describe('Doctor and pharmacist prescription safety', () => {
    it.each([false, true])('requires a trimmed override reason for danger and sends it on completion (visit=%s)', async isVisit => {
        mocks.check.mockResolvedValue(ok(dangerous)); mount(isVisit ? '/doctor/visits/10/examination' : undefined);
        await screen.findByText('Bệnh nhân an toàn'); await waitFor(() => expect(mocks.check).toHaveBeenCalledWith(10, [1], isVisit));
        fireEvent.click(screen.getByRole('button', { name: /Kê đơn thuốc/ }));
        expect(await screen.findByText('Nguy hiểm')).toBeInTheDocument();
        await confirmCompletion();
        const dialog = await screen.findByRole('dialog', { name: 'Xác nhận kê đơn có cảnh báo nguy hiểm' });
        const complete = within(dialog).getByRole('button', { name: 'Vẫn kê và hoàn tất' }); const reason = within(dialog).getByLabelText('Lý do vẫn kê');
        expect(complete).toBeDisabled(); fireEvent.change(reason, { target: { value: ' 123456789 ' } }); expect(complete).toBeDisabled();
        fireEvent.change(reason, { target: { value: '  Đã đánh giá nguy cơ và xác nhận  ' } }); expect(complete).toBeEnabled(); fireEvent.click(complete);
        await waitFor(() => expect(isVisit ? mocks.completeVisit : mocks.complete).toHaveBeenCalledWith(10, expect.objectContaining({ issuePrescription: true, prescriptionSafetyOverrideReason: 'Đã đánh giá nguy cơ và xác nhận' })));
        expect(await screen.findByText('Đã hoàn tất')).toBeInTheDocument();
    }, 15000);

    it('reopens the override modal with the normalized server error when live checks missed danger', async () => {
        mocks.complete.mockRejectedValueOnce({ errorCode: 'PRESCRIPTION_SAFETY_OVERRIDE_REQUIRED', message: 'Server phát hiện cảnh báo mới.' }); mount();
        await screen.findByText('Bệnh nhân an toàn'); await confirmCompletion();
        const dialog = await screen.findByRole('dialog', { name: 'Xác nhận kê đơn có cảnh báo nguy hiểm' });
        expect(within(dialog).getByText('Server phát hiện cảnh báo mới.')).toBeInTheDocument();
        fireEvent.change(within(dialog).getByLabelText('Lý do vẫn kê'), { target: { value: 'Đã kiểm tra cảnh báo mới' } });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Vẫn kê và hoàn tất' }));
        await waitFor(() => expect(mocks.complete).toHaveBeenLastCalledWith(10, expect.objectContaining({ prescriptionSafetyOverrideReason: 'Đã kiểm tra cảnh báo mới' })));
    }, 15000);

    it.each([{ warnings: [] }, { warnings: [warning] }, { warnings: [info] }])('keeps normal completion without an override when there is no Danger ($warnings)', async ({ warnings }) => {
        mocks.check.mockResolvedValue(ok({ allergies: [], warnings })); mount();
        await screen.findByText('Bệnh nhân an toàn'); await waitFor(() => expect(mocks.check).toHaveBeenCalled()); await confirmCompletion();
        await waitFor(() => expect(mocks.complete).toHaveBeenCalledOnce());
        expect(mocks.complete.mock.calls[0][1]).not.toHaveProperty('prescriptionSafetyOverrideReason');
        expect(screen.queryByRole('dialog', { name: 'Xác nhận kê đơn có cảnh báo nguy hiểm' })).not.toBeInTheDocument();
    }, 15000);

    it('checks changed medicines after adding a catalog medicine', async () => {
        mount(); await screen.findByText('Bệnh nhân an toàn'); fireEvent.click(screen.getByRole('button', { name: /Kê đơn thuốc/ }));
        fireEvent.change(screen.getByPlaceholderText('Gõ tên thuốc hoặc mã thuốc để tìm kiếm và thêm vào đơn...'), { target: { value: 'Thuốc thứ hai' } });
        fireEvent.click(await screen.findByText('Thuốc thứ hai'));
        await waitFor(() => expect(mocks.check).toHaveBeenLastCalledWith(10, [1, 2], false));
    }, 15000);

    it('shows the immutable warning snapshot and doctor reason read-only at the pharmacy', async () => {
        const detail = { id: 9, appointmentCode: 'APT-PS', patientName: 'Bệnh nhân an toàn', doctorName: 'BS. An', status: 'Issued', createdAt: '2026-10-16T01:00:00Z',
            safetyWarnings: [danger], safetyOverrideReason: 'Đã đánh giá và xác nhận chuyên môn', items: [{ medicineId: 1, medicineCode: 'PS-1', medicineName: 'Amoxicillin', unit: 'Viên', quantity: 2, availableStock: 100, isActive: true, dosage: '1 viên', frequency: '2 lần' }] };
        mocks.get.mockImplementation((url: string) => Promise.resolve(ok(url.includes('/prescriptions/9') ? detail : { items: [{ ...detail, itemCount: 1 }], totalItems: 1 })));
        mount('/pharmacy'); fireEvent.click(await screen.findByRole('button', { name: /Xem & cấp phát/ }));
        const safety = await screen.findByRole('region', { name: 'An toàn kê đơn' });
        expect(within(safety).getByText(danger.title)).toBeInTheDocument(); expect(within(safety).getByText(/Đã đánh giá và xác nhận chuyên môn/)).toBeInTheDocument();
        expect(within(safety).queryByRole('textbox')).not.toBeInTheDocument(); expect(mocks.check).not.toHaveBeenCalled();
    }, 15000);
});
