import { StrictMode } from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { ClinicalPageErrorBoundary } from '../components/diagnostics/ClinicalPageErrorBoundary';
import { DialogProvider } from '../contexts/DialogContext';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto } from '../types';

const mocks = vi.hoisted(() => ({ orders: vi.fn(), context: vi.fn(), detail: vi.fn(), save: vi.fn(), prescription: vi.fn() }));
vi.mock('../api/doctorApi', () => ({ doctorApi: {
    getVisitPatientClinicalContext: mocks.context,
    getActiveMedicines: async () => ({ success: true, data: [{ id: 1, code: 'PARA', name: 'Paracetamol', unit: 'viên', stockQuantity: 100 }] }),
    checkPrescriptionSafety: async () => ({ success: true, data: { warnings: [] } }),
    saveVisitPrescriptionDraft: mocks.prescription,
} }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getDoctorOrdersByVisit: mocks.orders, getTechnicianOrderById: mocks.detail, recordTechnicianItemResult: mocks.save,
    getCatalog: async () => ({ success: true, data: [{ id: 1, code: 'IMG-CXR', name: 'Chụp X-quang ngực thẳng', category: 'Imaging' }] }),
} }));
const ok = <T,>(data: T) => ({ success: true, data });
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 1, appointmentCode: 'APT-1', orderCode: 'DX-1', patientId: 1,
    patientName: 'Bệnh nhân thử nghiệm', patientPhone: '', patientGender: 'Male', orderingDoctorId: 1,
    orderingDoctorName: 'Bác sĩ', specialtyName: 'Nội khoa', clinicalIndication: 'Kiểm tra',
    status: 'InProgress', orderedAtUtc: '2026-10-26T01:00:00Z',
    items: [{ id: 11, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'IMG-CXR', serviceName: 'Chụp X-quang ngực thẳng', category: 'Imaging', status: 'InProgress' }],
};
beforeEach(() => {
    vi.clearAllMocks();
    mocks.context.mockResolvedValue(ok({ patientId: 1, patientName: 'Bệnh nhân thử nghiệm', patientPhone: '', patientGender: 'Male', totalPastVisits: 0, pastVisits: [],
        currentAppointment: { id: 1, appointmentCode: 'APT-1', status: 'InConsultation', startTime: '08:00:00', endTime: '08:30:00' } }));
    mocks.orders.mockResolvedValue(ok([])); mocks.detail.mockResolvedValue(ok(order)); mocks.save.mockResolvedValue(ok(order));
});
afterEach(cleanup);
function mount(technician = false) {
    return render(<StrictMode><DialogProvider><CopilotResourceProvider><MemoryRouter initialEntries={[technician ? '/diagnostics/orders/1' : '/doctor/visits/1']}>
        <Routes><Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} /><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /></Routes>
    </MemoryRouter></CopilotResourceProvider></DialogProvider></StrictMode>);
}
describe('Clinical pages demo regressions', () => {
    it.each(['Ordered', 'InProgress', 'Completed'] as const)('disables an already ordered diagnostic service for an active %s order', async status => {
        mocks.orders.mockResolvedValue(ok([{ ...order, status }]));
        mount(); fireEvent.click(await screen.findByRole('button', { name: /^Cận lâm sàng/ }));
        expect(screen.getByRole('checkbox', { name: /Chụp X-quang ngực thẳng/ })).toBeDisabled();
        expect(screen.getByText('Đã chỉ định')).toBeInTheDocument();
    });
    it.each(['order', 'item'])('allows ordering again after cancellation of the %s', async target => {
        mocks.orders.mockResolvedValue(ok([{ ...order, status: target === 'order' ? 'Cancelled' : 'InProgress', items: [{ ...order.items[0], status: target === 'item' ? 'Cancelled' : 'InProgress' }] }]));
        mount(); fireEvent.click(await screen.findByRole('button', { name: /^Cận lâm sàng/ }));
        const checkbox = screen.getByRole('checkbox', { name: /Chụp X-quang ngực thẳng/ });
        expect(checkbox).toBeEnabled(); fireEvent.click(checkbox); expect(checkbox).toBeChecked();
    });
    it('keeps the doctor page mounted through five repeated selection and typing sequences', async () => {
        mount(); fireEvent.click(await screen.findByRole('button', { name: /^Cận lâm sàng/ }));
        for (let i = 0; i < 5; i++) {
            fireEvent.click(screen.getByRole('checkbox', { name: /Chụp X-quang ngực thẳng/ }));
            fireEvent.change(screen.getByPlaceholderText('Ví dụ: Kiểm tra men gan / Nghi ngờ sỏi thận...'), { target: { value: `Kiểm tra ${i}` } });
        }
        expect(screen.getByDisplayValue('Kiểm tra 4')).toBeInTheDocument();
        expect(screen.queryByText('Có lỗi hiển thị, vui lòng tải lại')).not.toBeInTheDocument();
    }, 15000);
    it('sends each result PUT through five repeated technician input, conclusion and save sequences', async () => {
        mount(true);
        const text = await screen.findByLabelText(/Kết quả chi tiết/);
        for (let i = 0; i < 5; i++) {
            fireEvent.change(text, { target: { value: `Không thấy tổn thương ${i}` } });
            fireEvent.click(screen.getByRole('button', { name: 'Bình thường' }));
            fireEvent.click(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' }));
            await waitFor(() => expect(mocks.save).toHaveBeenCalledTimes(i + 1));
            await waitFor(() => expect(screen.getByRole('button', { name: 'Lưu kết quả dịch vụ' })).toBeEnabled());
        }
        expect(mocks.save).toHaveBeenLastCalledWith(1, 11, expect.objectContaining({ resultText: 'Không thấy tổn thương 4', conclusion: 'Bình thường' }));
        expect(screen.queryByText('Có lỗi hiển thị, vui lòng tải lại')).not.toBeInTheDocument();
    }, 15000);
    it('shows the reload fallback for a render failure', () => {
        const log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
        function BrokenContent(): never { throw new Error('render failed'); }
        try {
            render(<ClinicalPageErrorBoundary><BrokenContent /></ClinicalPageErrorBoundary>);
            expect(screen.getByText('Có lỗi hiển thị, vui lòng tải lại')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Tải lại' })).toBeInTheDocument();
        } finally { log.mockRestore(); }
    });
});
