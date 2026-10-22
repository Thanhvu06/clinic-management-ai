import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { DoctorAppointmentDetail } from '../pages/doctor/DoctorAppointmentDetail';
import { DoctorExaminationPage } from '../pages/doctor/DoctorExaminationPage';
import { CheckInTicketModal } from '../components/CheckInTicketModal';
import { formatDecimalVi, formatVitalSummaryVi } from '../utils/formatDecimalVi';
import { getPrescriptionStatusLabel } from '../utils/prescriptionStatusLabels';
import type { CheckInTicketDto } from '../types';

const mocks = vi.hoisted(() => ({ context: vi.fn(), history: vi.fn(), medicines: vi.fn(), orders: vi.fn(), catalog: vi.fn() }));
vi.mock('../api/doctorApi', () => ({ doctorApi: {
    getPatientClinicalContext: mocks.context, getVisitPatientClinicalContext: mocks.context,
    getAppointmentHistory: mocks.history, getActiveMedicines: mocks.medicines
} }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getDoctorOrdersByVisit: mocks.orders, getCatalog: mocks.catalog } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn() }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: vi.fn() }) }));

const context = {
    patientId: 1, patientName: 'Người bệnh kiểm tra', patientPhone: '0901234567', patientGender: 'Male',
    totalPastVisits: 0, pastVisits: [], currentAppointment: { id: 1, appointmentCode: 'APT-20261022-0001',
        appointmentDate: '2026-10-22', startTime: '08:00:00', endTime: '08:30:00', status: 'InConsultation' },
    vitalSigns: { temperature: 37.2, height: 170, weight: 65, bmi: 22.5, heartRate: 75, spO2: 98 },
    prescription: { status: 'Issued', items: [{ medicineId: 1, medicineName: 'Thuốc kiểm tra', quantity: 2, unit: 'Viên' }] },
    vitalHistory: [{ recordedAtUtc: '2026-10-22T01:00:00Z', temperature: 37.2, weight: 65, height: 170, bmi: 22.5 }]
};
const ok = (data: unknown) => ({ success: true, data });

beforeEach(() => {
    vi.resetAllMocks();
    mocks.context.mockResolvedValue(ok(structuredClone(context)));
    mocks.history.mockResolvedValue(ok([]));
    mocks.medicines.mockResolvedValue(ok([]));
    mocks.orders.mockResolvedValue(ok([]));
    mocks.catalog.mockResolvedValue(ok([]));
});
afterEach(cleanup);

const detail = () => render(<MemoryRouter initialEntries={['/doctor/appointments/1']}>
    <Routes><Route path="/doctor/appointments/:id" element={<DoctorAppointmentDetail />} /></Routes>
</MemoryRouter>);

it.each([
    ['Draft', 'Nháp'], ['Issued', 'Đã phát hành'], ['Dispensed', 'Đã cấp phát'],
    ['Cancelled', 'Đã hủy'], ['ReservedForPurchase', 'Giữ thuốc chờ mua']
])('shows prescription %s without an unknown badge', async (status, label) => {
    mocks.context.mockResolvedValue(ok({ ...context, prescription: { ...context.prescription, status } }));
    detail();
    await screen.findByRole('heading', { name: 'Đơn thuốc' });
    expect(getPrescriptionStatusLabel(status)).toBe(label);
    expect(screen.getByText(label)).toBeInTheDocument();
    expect(screen.queryByText('Không xác định')).not.toBeInTheDocument();
});

it.each([[22.5, '22,5'], [37.2, '37,2'], [22, '22'], [0, '0'], [null, '--']])('formats %s as %s', (value, expected) => {
    expect(formatDecimalVi(value as number | null)).toBe(expected);
});

it('formats decimal vital summaries without changing blood-pressure separators', () => {
    expect(formatVitalSummaryVi('37.2°C · BMI 22.5 · 120/80')).toBe('37,2°C · BMI 22,5 · 120/80');
});

it('separates height and weight and uses Vietnamese decimals in the vitals strip', async () => {
    detail();
    const strip = await screen.findByRole('group', { name: 'Sinh hiệu' });
    for (const label of ['Chiều cao', 'Cân nặng', '170', '65', '37,2', '22,5']) expect(within(strip).getByText(label)).toBeInTheDocument();
    expect(within(strip).queryByText('Chiều cao / cân nặng')).not.toBeInTheDocument();
});

it('keeps input decimals intact while rendering BMI and history with commas', async () => {
    render(<MemoryRouter initialEntries={['/doctor/visits/1/examination']}>
        <Routes><Route path="/doctor/visits/:visitId/examination" element={<DoctorExaminationPage />} /></Routes>
    </MemoryRouter>);
    const title = await screen.findByRole('heading', { name: 'Bàn khám lâm sàng' });
    await screen.findByRole('button', { name: 'Đồng bộ dữ liệu' });
    expect(within(title.closest('[class*="pageHeader"]') as HTMLElement).getByRole('button', { name: 'Đồng bộ dữ liệu' })).toBeInTheDocument();
    expect(screen.queryByText('Phiên khám lâm sàng')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Sinh hiệu' }));
    expect(screen.getByRole('button', { name: 'Lưu sinh hiệu' })).toBeInTheDocument();
    expect(screen.getByDisplayValue('37.2')).toHaveAttribute('type', 'number');
    expect(screen.getByText('Chỉ số BMI')).toBeInTheDocument();
    expect(screen.getAllByText('22,5').length).toBeGreaterThan(0);
    expect(screen.getByText('37,2°C')).toBeInTheDocument();
});

it('shows the queue number separately from its department code', () => {
    const ticket = { visitCode: 'VIS-002', patientName: 'Người bệnh', queueNumber: 2, queueDisplay: 'CANARY-DEPT-002' } as CheckInTicketDto;
    render(<CheckInTicketModal isOpen ticket={ticket} onClose={vi.fn()} />);
    expect(screen.getByText('002')).toBeInTheDocument();
    expect(screen.getByText('CANARY-DEPT')).toBeInTheDocument();
    expect(screen.queryByText('CANARY-DEPT-002')).not.toBeInTheDocument();
    expect(screen.getByText('Mã bệnh án:')).toBeInTheDocument();
});
