import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { LabSupervisorDashboard } from '../pages/lab/LabSupervisorDashboard';
import { DiagnosticVerificationReview } from '../pages/common/DiagnosticVerificationReview';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto, PatientClinicalContextDto } from '../types';
import type { DiagnosticResultValueDto } from '../types/diagnostic';
import { formatDelta } from '../utils/diagnosticLabels';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({
    technicianDetail: vi.fn(), doctorOrders: vi.fn(), context: vi.fn(), queue: vi.fn(), stats: vi.fn(), review: vi.fn(), verify: vi.fn(),
    alert: vi.fn(), toast: vi.fn(), confirm: vi.fn(),
}));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getTechnicianOrderById: mocks.technicianDetail, getDoctorOrdersByAppointment: mocks.doctorOrders,
    getSharedVerificationQueue: mocks.queue, getVerificationStats: mocks.stats, getVerificationOrder: mocks.review, verifySharedOrder: mocks.verify,
    getPatientVitals: async () => ({ success: true, data: [] }), getCatalog: async () => ({ success: true, data: [] }),
} }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getPatientClinicalContext: mocks.context, getActiveMedicines: async () => ({ success: true, data: [] }) } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { id: 'reviewer', fullName: 'Trưởng khoa', role: 'LabSupervisor' }, logout: vi.fn(), isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast, showConfirm: mocks.confirm }) }));

const failing: DiagnosticResultValueDto = {
    parameterId: 11, parameterCode: 'WBC', parameterName: 'Bạch cầu', unit: '10^9/L', value: 12, refLow: 4, refHigh: 10, referenceRangeText: '4.0 – 10.0',
    flag: 'High', sortOrder: 0, previousValue: 6, previousResultedAtUtc: '2026-10-02T18:00:00Z', previousOrderCode: 'DX-OLD-001',
    deltaChange: 6, deltaPercent: 100, isDeltaFailure: true,
};
const stable: DiagnosticResultValueDto = {
    parameterId: 12, parameterCode: 'HGB', parameterName: 'Huyết sắc tố', unit: 'g/L', value: 125, refLow: 120, refHigh: 150, referenceRangeText: '120.0 – 150.0',
    flag: 'Normal', sortOrder: 1, previousValue: 130, previousResultedAtUtc: '2026-10-03T02:00:00Z', previousOrderCode: 'DX-OLD-001',
    deltaChange: -5, deltaPercent: -3.85, isDeltaFailure: false,
};
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-DLT-001', orderCode: 'DX-DLT-001', patientId: 2,
    patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female', patientAge: 35,
    orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa', clinicalIndication: 'Theo dõi công thức máu',
    status: 'InProgress', verificationStatus: 'Pending', orderedAtUtc: '2026-10-13T01:15:00Z', rowVersion: 'order-v1',
    submittedForVerificationAtUtc: '2026-10-13T01:35:00Z', submittedForVerificationByUserName: 'KTV 1', canCurrentUserVerify: true, hasDeltaFailure: true,
    items: [{ id: 101, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', status: 'Completed', rowVersion: 'item-v1',
        result: { id: 501, diagnosticOrderItemId: 101, resultText: 'WBC 12', resultedAtUtc: '2026-10-13T01:30:00Z', resultedByUserId: 'tech-1', resultedByUserName: 'KTV 1',
            hasCriticalValue: false, values: [failing, stable] } }],
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });

function mount(path: string) {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}><Routes>
        <Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} />
        <Route path="/doctor/appointments/:id/examination" element={<DoctorExaminationWorkspace />} />
        <Route path="/lab" element={<LabSupervisorDashboard />} />
        <Route path="/lab/orders/:id" element={<DiagnosticVerificationReview />} />
    </Routes></MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}

beforeEach(() => {
    vi.clearAllMocks();
    mocks.technicianDetail.mockResolvedValue(ok(order));
    mocks.doctorOrders.mockResolvedValue(ok([{ ...order, status: 'Completed', verificationStatus: 'Verified', canCurrentUserVerify: false }]));
    mocks.queue.mockResolvedValue(ok({ items: [order], totalItems: 1, page: 1, pageSize: 15 }));
    mocks.stats.mockResolvedValue(ok({ pendingCount: 1, criticalPendingCount: 0, oldestPendingMinutes: 5, returnedTodayCount: 0 }));
    mocks.review.mockResolvedValue(ok(order));
    mocks.verify.mockResolvedValue(ok({ ...order, status: 'Completed', verificationStatus: 'Verified' }));
    const context: PatientClinicalContextDto = {
        patientId: 2, patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female',
        patientDob: '1991-01-01', totalPastVisits: 0, pastVisits: [],
        currentAppointment: { id: 10, appointmentCode: 'APT-DLT-001', patientId: 2, doctorId: 5, doctorName: 'BS. Lê Minh', specialtyId: 1, specialtyName: 'Nội khoa', appointmentDate: '2026-10-13', status: 'InConsultation', reason: 'Theo dõi', startTime: '08:00:00', endTime: '08:30:00' },
    };
    mocks.context.mockResolvedValue(ok(context));
});
afterEach(cleanup);

describe('formatDelta', () => {
    it('formats previous value, Vietnam date, signed change and percent', () => {
        expect(formatDelta({ previousValue: 120, previousResultedAtUtc: '2026-10-03T02:00:00Z', deltaChange: 45, deltaPercent: 37.5 }))
            .toBe('Trước: 120 (03/10) → Δ +45 (+37,5%)');
    });
    it('uses Vietnam time for the date and treats a zone-less timestamp as UTC', () => {
        expect(formatDelta({ previousValue: 150, previousResultedAtUtc: '2026-10-02T18:00:00', deltaChange: -40, deltaPercent: -26.67 }))
            .toBe('Trước: 150 (03/10) → Δ -40 (-26,67%)');
    });
    it('omits the percent when the previous value was zero', () => {
        expect(formatDelta({ previousValue: 0, previousResultedAtUtc: '2026-10-03T02:00:00Z', deltaChange: 25, deltaPercent: null }))
            .toBe('Trước: 0 (03/10) → Δ +25');
    });
    it('formats decimals with the Vietnamese separator', () => {
        expect(formatDelta({ previousValue: 1200.5, previousResultedAtUtc: '2026-10-03T02:00:00Z', deltaChange: 0, deltaPercent: 0 }))
            .toBe('Trước: 1.200,5 (03/10) → Δ 0 (0%)');
    });
    it('returns a dash without a previous value', () => {
        expect(formatDelta({ previousValue: null, previousResultedAtUtc: null, deltaChange: null, deltaPercent: null })).toBe('—');
        expect(formatDelta({})).toBe('—');
    });
});

describe('Delta check UI', () => {
    it('shows the comparison column, delta tag and warning alert on technician entry', async () => {
        mount('/diagnostics/orders/1');
        const table = await screen.findByRole('table', { name: 'Chỉ số đã lưu: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('So với lần trước')).toBeInTheDocument();
        expect(within(table).getByText(/Trước: 6 \(03\/10\) → Δ \+6 \(\+100%\)/)).toBeInTheDocument();
        expect(within(table).getByText(/Trước: 130 \(03\/10\) → Δ -5 \(-3,85%\)/)).toBeInTheDocument();
        expect(within(table).getAllByText('Lệch delta')).toHaveLength(1);
        expect(screen.getByText('Có chỉ số lệch delta so với lần trước, kiểm tra mẫu/nhãn trước khi hoàn tất phiếu.')).toBeInTheDocument();
    }, 15000);

    it('hides the technician alert when no value fails', async () => {
        mocks.technicianDetail.mockResolvedValue(ok({ ...order, hasDeltaFailure: false, items: [{ ...order.items[0], result: { ...order.items[0].result!, values: [stable] } }] }));
        mount('/diagnostics/orders/1');
        const table = await screen.findByRole('table', { name: 'Chỉ số đã lưu: Tổng phân tích tế bào máu' });
        expect(within(table).queryByText('Lệch delta')).not.toBeInTheDocument();
        expect(screen.queryByText('Có chỉ số lệch delta so với lần trước, kiểm tra mẫu/nhãn trước khi hoàn tất phiếu.')).not.toBeInTheDocument();
    }, 15000);

    it('keeps verification locked until the delta note has at least 5 characters', async () => {
        mount('/lab/orders/1');
        const table = await screen.findByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('So với lần trước')).toBeInTheDocument();
        expect(within(table).getByText('Lệch delta')).toBeInTheDocument();
        expect(screen.getByText(/Có chỉ số lệch delta so với lần trước/)).toBeInTheDocument();
        const note = screen.getByPlaceholderText('Bắt buộc: ghi chú đã kiểm tra lệch delta');
        const approve = screen.getByRole('button', { name: 'Duyệt kết quả' });
        expect(approve).toBeDisabled();
        fireEvent.change(note, { target: { value: '  abcd  ' } });
        expect(approve).toBeDisabled();
        fireEvent.change(note, { target: { value: 'Đã gọi lại khoa' } });
        expect(approve).toBeEnabled();
        fireEvent.click(approve);
        await waitFor(() => expect(mocks.verify).toHaveBeenCalledWith(1, { note: 'Đã gọi lại khoa', rowVersion: 'order-v1' }));
    }, 15000);

    it('shows the DELTA_NOTE_REQUIRED server error next to the note', async () => {
        const message = 'Kết quả lệch delta so với lần trước, vui lòng ghi chú đã kiểm tra (nhầm mẫu/nhầm bệnh nhân/diễn tiến lâm sàng).';
        mocks.verify.mockRejectedValue({ success: false, errorCode: 'DELTA_NOTE_REQUIRED', message, status: 400 });
        mount('/lab/orders/1');
        const note = await screen.findByPlaceholderText('Bắt buộc: ghi chú đã kiểm tra lệch delta');
        fireEvent.change(note, { target: { value: 'Đã kiểm tra mẫu' } });
        fireEvent.click(screen.getByRole('button', { name: 'Duyệt kết quả' }));
        expect(await screen.findByText(message)).toBeInTheDocument();
        expect(mocks.toast).not.toHaveBeenCalled();
    }, 15000);

    it('does not require a note when the order has no delta failure', async () => {
        mocks.review.mockResolvedValue(ok({ ...order, hasDeltaFailure: false }));
        mount('/lab/orders/1');
        await screen.findByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' });
        expect(screen.queryByPlaceholderText('Bắt buộc: ghi chú đã kiểm tra lệch delta')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Duyệt kết quả' })).toBeEnabled();
    }, 15000);

    it('tags delta failures in the supervisor queue', async () => {
        mount('/lab');
        const table = await screen.findByRole('table', { name: 'Phiếu chờ duyệt' });
        expect(await within(table).findByText('Lệch delta')).toBeInTheDocument();
    }, 15000);

    it('shows the previous-result column in the doctor workspace', async () => {
        mount('/doctor/appointments/10/examination');
        await screen.findByText('Trần Thị An');
        fireEvent.click(screen.getByRole('button', { name: /Cận lâm sàng/ }));
        const table = await screen.findByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('Lần trước')).toBeInTheDocument();
        expect(within(table).getByText(/Trước: 6 \(03\/10\) → Δ \+6 \(\+100%\)/)).toBeInTheDocument();
        expect(within(table).getByText('Lệch delta')).toBeInTheDocument();
    }, 15000);
});
