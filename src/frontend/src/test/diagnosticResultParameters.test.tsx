import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { PatientDiagnosticResults } from '../pages/patient/PatientDiagnosticResults';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto, PatientClinicalContextDto } from '../types';
import type { DiagnosticResultFlag, DiagnosticServiceParameterDto } from '../types/diagnostic';
import { previewDiagnosticFlag } from '../utils/diagnosticLabels';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ detail: vi.fn(), save: vi.fn(), doctorOrders: vi.fn(), patientOrders: vi.fn(), context: vi.fn(), alert: vi.fn(), toast: vi.fn(), confirm: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getTechnicianOrderById: mocks.detail, recordTechnicianItemResult: mocks.save,
    getDoctorOrdersByAppointment: mocks.doctorOrders, getPatientOrders: mocks.patientOrders,
    getPatientVitals: async () => ({ success: true, data: [] }), getCatalog: async () => ({ success: true, data: [] }),
} }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getPatientClinicalContext: mocks.context, getActiveMedicines: async () => ({ success: true, data: [] }) } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast, showConfirm: mocks.confirm }) }));

const parameters: DiagnosticServiceParameterDto[] = [
    { id: 11, code: 'WBC', name: 'Bạch cầu', unit: '10^9/L', refLow: 4, refHigh: 10, criticalLow: 2, criticalHigh: 30, referenceRangeText: '4.0 – 10.0' },
    { id: 12, code: 'HGB', name: 'Huyết sắc tố', unit: 'g/L', refLow: 120, refHigh: 150, criticalLow: 70, criticalHigh: 200, referenceRangeText: '120.0 – 150.0' },
];
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-LIS-001', orderCode: 'DX-LIS-001', patientId: 2,
    patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female', patientAge: 35,
    orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa', clinicalIndication: 'Kiểm tra sức khỏe',
    status: 'InProgress', orderedAtUtc: '2026-10-13T01:15:00Z', rowVersion: 'order-v1',
    items: [{ id: 101, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích tế bào máu', category: 'Laboratory', status: 'InProgress', rowVersion: 'item-v1', parameters }],
};
const completedOrder: DiagnosticOrderDto = {
    ...order, status: 'Completed', reviewedAtUtc: '2026-10-13T02:00:00Z', reviewedByDoctorName: 'BS. Lê Minh',
    items: [{ ...order.items[0], status: 'Completed', result: {
        id: 501, diagnosticOrderItemId: 101, resultText: 'Bạch cầu (WBC): 2 10^9/L [Thấp – NGUY HIỂM]',
        conclusion: 'Đề nghị bác sĩ đối chiếu', resultedAtUtc: '2026-10-13T01:40:00Z', resultedByUserId: 'tech-1', resultedByUserName: 'KTV Trần Khoa',
        hasCriticalValue: true, criticalNotifiedAtUtc: '2026-10-13T01:40:00Z', values: [
            { parameterId: 11, parameterCode: 'WBC', parameterName: 'Bạch cầu', unit: '10^9/L', value: 2, refLow: 4, refHigh: 10, referenceRangeText: '4.0 – 10.0', flag: 'CriticalLow', sortOrder: 0 },
            { parameterId: 12, parameterCode: 'HGB', parameterName: 'Huyết sắc tố', unit: 'g/L', value: 125, refLow: 120, refHigh: 150, referenceRangeText: '120.0 – 150.0', flag: 'Normal', sortOrder: 1 },
        ],
    } }],
};
const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
function mount(role: 'technician' | 'doctor' | 'patient' = 'technician') {
    const path = role === 'technician' ? '/diagnostics/orders/1' : role === 'doctor' ? '/doctor/appointments/10/examination' : '/patient/diagnostic-results';
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} />
            <Route path="/doctor/appointments/:id/examination" element={<DoctorExaminationWorkspace />} />
            <Route path="/patient/diagnostic-results" element={<PatientDiagnosticResults />} />
        </Routes>
    </MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}
beforeEach(() => {
    vi.clearAllMocks();
    mocks.detail.mockResolvedValue(ok(order));
    mocks.save.mockResolvedValue(ok(completedOrder));
    mocks.doctorOrders.mockResolvedValue(ok([completedOrder]));
    mocks.patientOrders.mockResolvedValue(ok({ items: [completedOrder], totalItems: 1, page: 1, pageSize: 50 }));
    const context: PatientClinicalContextDto = {
        patientId: 2, patientName: 'Trần Thị An', patientPhone: '0901234567', patientGender: 'Female',
        patientDob: '1991-01-01', totalPastVisits: 0, pastVisits: [],
        currentAppointment: { id: 10, appointmentCode: 'APT-LIS-001', patientId: 2, doctorId: 5, doctorName: 'BS. Lê Minh', specialtyId: 1, specialtyName: 'Nội khoa', appointmentDate: '2026-10-13', status: 'InConsultation', reason: 'Kiểm tra sức khỏe', startTime: '08:00:00', endTime: '08:30:00' },
    };
    mocks.context.mockResolvedValue(ok(context));
});
afterEach(cleanup);

describe('LIS diagnostic result parameters', () => {
    it('shows the entry table, patient-specific reference and live low/critical flags', async () => {
        mount();
        const table = await screen.findByRole('table', { name: 'Nhập chỉ số: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('120.0 – 150.0')).toBeInTheDocument();
        expect(screen.getByLabelText('Nhận xét (không bắt buộc)')).toBeInTheDocument();
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' }), { target: { value: '3' } });
        expect(within(table).getByText('Thấp')).toBeInTheDocument();
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' }), { target: { value: '2' } });
        expect(within(table).getByText('Thấp – NGUY HIỂM')).toBeInTheDocument();
        expect(screen.getByRole('alert')).toHaveTextContent('Có giá trị nguy hiểm — hệ thống sẽ báo ngay cho bác sĩ chỉ định khi lưu.');
    }, 15000);

    it('blocks saving with missing values and reports them inline', async () => {
        mount();
        await screen.findByRole('table', { name: /Nhập chỉ số:/ });
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' }), { target: { value: '7' } });
        fireEvent.click(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' }));
        expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng nhập đủ chỉ số: Huyết sắc tố.');
        expect(mocks.save).not.toHaveBeenCalled();
        expect(mocks.alert).not.toHaveBeenCalled();
    }, 15000);

    it('saves the exact numeric values and row version without requiring a comment', async () => {
        mount();
        await screen.findByRole('table', { name: /Nhập chỉ số:/ });
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' }), { target: { value: '2' } });
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Huyết sắc tố (HGB)' }), { target: { value: '125' } });
        fireEvent.click(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' }));
        await waitFor(() => expect(mocks.save).toHaveBeenCalledWith(1, 101, {
            resultText: '', conclusion: undefined, referenceRange: undefined, unit: undefined, rowVersion: 'item-v1',
            values: [{ parameterId: 11, value: 2 }, { parameterId: 12, value: 125 }],
        }));
        expect(await screen.findByRole('table', { name: 'Chỉ số đã lưu: Tổng phân tích tế bào máu' })).toBeInTheDocument();
        expect(screen.getByText('Đã báo bác sĩ lúc 08:40 13/10')).toBeInTheDocument();
    }, 15000);

    it('disables submit and changes its text while saving', async () => {
        let resolveSave!: (value: unknown) => void;
        mocks.save.mockImplementation(() => new Promise(resolve => { resolveSave = resolve; }));
        mount();
        await screen.findByRole('table', { name: /Nhập chỉ số:/ });
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' }), { target: { value: '7' } });
        fireEvent.change(screen.getByRole('spinbutton', { name: 'Kết quả Huyết sắc tố (HGB)' }), { target: { value: '125' } });
        fireEvent.click(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' }));
        expect(screen.getByRole('button', { name: 'Đang lưu...' })).toBeDisabled();
        resolveSave(ok(completedOrder));
        await screen.findByText('Đã báo bác sĩ lúc 08:40 13/10');
    }, 15000);

    it('keeps the text form and legacy payload for ultrasound', async () => {
        mocks.detail.mockResolvedValue(ok({ ...order, items: [{ ...order.items[0], parameters: [], category: 'Ultrasound', serviceName: 'Siêu âm ổ bụng', serviceCode: 'US-ABD' }] }));
        mount();
        const text = await screen.findByLabelText('Kết quả chi tiết / Mô tả tổn thương *');
        expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
        fireEvent.change(text, { target: { value: 'Nhu mô gan đồng nhất' } });
        fireEvent.click(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' }));
        await waitFor(() => expect(mocks.save).toHaveBeenCalledWith(1, 101, expect.objectContaining({ resultText: 'Nhu mô gan đồng nhất' })));
        expect(mocks.save.mock.calls[0][2]).not.toHaveProperty('values');
    }, 15000);

    it('shows saved values and the notification time even when the result is collapsed', async () => {
        mocks.detail.mockResolvedValue(ok({ ...completedOrder, status: 'InProgress' }));
        mount();
        const table = await screen.findByRole('table', { name: /Chỉ số đã lưu:/ });
        expect(within(table).getByText('Thấp – NGUY HIỂM')).toBeInTheDocument();
        expect(screen.getByText('Đã báo bác sĩ lúc 08:40 13/10')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Xem/Sửa kết quả' })).toHaveAttribute('aria-expanded', 'false');
    }, 15000);

    it('shows doctors the nested numeric table and critical service badge', async () => {
        mount('doctor');
        await screen.findByText('Trần Thị An');
        fireEvent.click(screen.getByRole('button', { name: /Cận lâm sàng/ }));
        const table = await screen.findByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('2 10^9/L')).toBeInTheDocument();
        expect(within(table).getByText('Thấp – NGUY HIỂM')).toBeInTheDocument();
        expect(screen.getByText('Giá trị nguy hiểm')).toBeInTheDocument();
    }, 15000);

    it('shows patients a neutral critical label without exposing the generated critical wording', async () => {
        const { container } = mount('patient');
        const table = await screen.findByRole('table', { name: 'Chỉ số: Tổng phân tích tế bào máu' });
        expect(within(table).getByText('Cần trao đổi với bác sĩ')).toBeInTheDocument();
        expect(within(table).getByText('Bình thường')).toBeInTheDocument();
        expect(container.textContent?.toLowerCase()).not.toContain('nguy hiểm');
    }, 15000);

    it.each<[number, DiagnosticResultFlag]>([[2, 'CriticalLow'], [30, 'CriticalHigh'], [4, 'Normal'], [10, 'Normal'], [3, 'Low'], [11, 'High']])('matches backend flag boundaries for WBC %s', (value, flag) => {
        expect(previewDiagnosticFlag(value, parameters[0])).toBe(flag);
    });
});
