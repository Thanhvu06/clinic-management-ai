import { readFileSync } from 'node:fs';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AdminUsers } from '../pages/admin/AdminUsers';
import { AdminLeaves } from '../pages/admin/AdminLeaves';
import { AdminDashboard } from '../pages/admin/AdminDashboard';
import { DoctorLeaveRequests } from '../pages/doctor/DoctorLeaveRequests';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { DiagnosticOrderPrint } from '../pages/doctor/DiagnosticOrderPrint';
import { PatientDiagnosticResults } from '../pages/patient/PatientDiagnosticResults';
import { MedicineCard } from '../components/medicines/MedicineCard';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { diagnosticApi } from '../api/diagnosticApi';
import axiosClient from '../api/axiosClient';
import '../theme/browserCompat';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrderById: vi.fn(), getDoctorOrderById: vi.fn(), getPatientOrders: vi.fn(), getPatientVitals: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn(), showConfirm: vi.fn() }) }));

const order = { id: 1, orderCode: 'DX-1', appointmentCode: 'APT-1', patientId: 1, patientName: 'Lê Thị Lan', patientPhone: '0901234567',
    patientGender: 'Female', orderingDoctorId: 1, orderingDoctorName: 'Bác sĩ', specialtyName: 'Nội', clinicalIndication: '',
    status: 'InProgress', verificationStatus: 'NotRequired', orderedAtUtc: '2026-10-23T06:35:00Z', items: [{ id: 1, diagnosticOrderId: 1,
        diagnosticServiceId: 1, serviceName: 'Xét nghiệm', serviceCode: 'CBC', category: 'Laboratory', specimenType: 'Máu', status: 'Completed',
        result: { id: 1, diagnosticOrderItemId: 1, resultText: 'Bình thường', resultedAtUtc: '2026-10-23T06:35:00Z', resultedByUserId: 'tech' } }] };

beforeEach(() => {
    vi.clearAllMocks();
    const leave = { id: 1, doctorId: 1, doctorName: 'Bác sĩ Minh', startDateTime: '2026-10-25T13:35:00', endDateTime: '2026-10-26T13:35:00', reason: 'Nghỉ', status: 'Pending' };
    vi.mocked(axiosClient.get).mockImplementation(async (url: string) => ({ success: true,
        data: url.includes('stats') ? {} : { items: url.includes('leave-requests') ? [leave] : [], totalItems: 1 } }));
    vi.mocked(diagnosticApi.getTechnicianOrderById).mockResolvedValue({ success: true, data: order } as never);
    vi.mocked(diagnosticApi.getDoctorOrderById).mockResolvedValue({ success: true, data: order } as never);
    vi.mocked(diagnosticApi.getPatientOrders).mockResolvedValue({ success: true, data: { items: [{ ...order, status: 'Completed' }] } } as never);
    vi.mocked(diagnosticApi.getPatientVitals).mockResolvedValue({ success: true, data: [] } as never);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it.each([AdminDashboard, AdminLeaves, DoctorLeaveRequests])('keeps Vietnam leave wall time on $name', async Component => {
    render(<MemoryRouter><Component /></MemoryRouter>);
    expect(await screen.findByText(/25\/10\/2026[,\s]+13:35|13:35 25\/10\/2026/)).toBeInTheDocument();
    expect(screen.getByText(/26\/10\/2026[,\s]+13:35|13:35 26\/10\/2026/)).toBeInTheDocument();
    expect(screen.queryByText(/06:35/)).not.toBeInTheDocument();
});

it.each([
    { path: '/diagnostics/orders/1', route: '/diagnostics/orders/:id', Component: TechnicianOrderDetail },
    { path: '/doctor/diagnostic-orders/1/print', route: '/doctor/diagnostic-orders/:id/print', Component: DiagnosticOrderPrint },
    { path: '/patient/diagnostic-results', route: '/patient/diagnostic-results', Component: PatientDiagnosticResults },
])('hides an empty clinical indication on $path', async ({ path, route, Component }) => {
    render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}>
        <Routes><Route path={route} element={<Component />} /></Routes>
    </MemoryRouter></CopilotResourceProvider></ConfigProvider>);
    await screen.findByText('DX-1', { exact: false });
    expect(screen.queryByText(/Chỉ định lâm sàng:/)).not.toBeInTheDocument();
    expect(screen.queryByText('Chỉ định:')).not.toBeInTheDocument();
    expect(screen.queryByText('Chỉ định lâm sàng / Lý do:')).not.toBeInTheDocument();
    expect(screen.queryByText('Chỉ định cận lâm sàng', { exact: true })).not.toBeInTheDocument();
});

it('tells the technician that completing a specimen order sends results to the doctor', async () => {
    render(<CopilotResourceProvider><MemoryRouter initialEntries={['/diagnostics/orders/1']}><Routes>
        <Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} />
    </Routes></MemoryRouter></CopilotResourceProvider>);
    expect(await screen.findByText('Tất cả các dịch vụ đã có kết quả. Bạn có thể chốt phiếu.')).toBeInTheDocument();
});

it('shows usable stock separately from total stock on pharmacy cards', () => {
    render(<MedicineCard medicine={{ code: 'MED01', name: 'Thuốc', unit: 'Viên', isPrescriptionRequired: true, stockQuantity: 50, usableStock: 10 }} />);
    expect(screen.getByText('Còn dùng được: 10 Viên')).toBeInTheDocument();
    expect(screen.getByText('Tổng tồn (gồm lô hết hạn): 50 Viên')).toBeInTheDocument();
});

it('identifies the historical inventory balance as total stock including expired batches', async () => {
    vi.mocked(axiosClient.get).mockImplementation(async (url: string) => ({ success: true, data: url === '/medicines/active' ? [] :
        { items: [{ id: 1, medicineId: 1, medicineCode: 'MED01', medicineName: 'Thuốc', type: 'Dispense', unit: 'Viên',
            quantityChange: -10, balanceAfter: 195, createdAt: '2026-10-23T09:00:00Z', actorName: 'Dược sĩ' }], totalItems: 1 } }));
    render(<PharmacyInventory />);
    expect(await screen.findByRole('columnheader', { name: 'Tổng tồn sau giao dịch (gồm lô hết hạn)' })).toBeInTheDocument();
    expect(screen.getByText('195 Viên')).toBeInTheDocument();
});

it('uses the current mask API in all twelve files and closes the modal without a deprecation error', async () => {
    const pages = ['admin/AdminBilling', 'admin/AdminDoctors', 'admin/AdminFacilities', 'admin/AdminHealthPackages', 'admin/AdminLeaves',
        'admin/AdminMedicines', 'admin/AdminSpecialties', 'admin/AdminUsers', 'admin/AdminWorkSchedules',
        'patient/PatientPackageRegistrations', 'patient/PatientRevisit', 'patient/PatientAppointments'];
    for (const page of pages) expect(readFileSync(`src/pages/${page}.tsx`, 'utf8')).not.toContain('maskClosable');
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    render(<ConfigProvider theme={{ token: { motion: false } }}><AdminUsers /></ConfigProvider>);
    await screen.findByText('Không tìm thấy tài khoản nào phù hợp.');
    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài khoản nhân sự' }));
    await screen.findByRole('dialog');
    const mask = document.querySelector('.ant-modal-mask');
    expect(mask).not.toBeNull();
    fireEvent.mouseDown(document.querySelector('.ant-modal-wrap')!);
    fireEvent.click(document.querySelector('.ant-modal-wrap')!);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(error.mock.calls.flat().join(' ')).not.toContain('maskClosable');
});
