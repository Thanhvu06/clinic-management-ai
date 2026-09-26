import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import { DiagnosticOrderPrint } from '../pages/doctor/DiagnosticOrderPrint';
import { PatientDiagnosticResults } from '../pages/patient/PatientDiagnosticResults';
import { DialogProvider } from '../contexts/DialogContext';
import { diagnosticApi } from '../api/diagnosticApi';
import type { DiagnosticOrderDto, TechnicianDiagnosticStatsDto } from '../types';

// Mock diagnosticApi
vi.mock('../api/diagnosticApi', () => ({
    diagnosticApi: {
        getTechnicianOrders: vi.fn(),
        getTechnicianStats: vi.fn(),
        getDoctorOrderById: vi.fn(),
        getPatientOrders: vi.fn(),
        getPatientVitals: vi.fn(),
    }
}));

const mockStats: TechnicianDiagnosticStatsDto = {
    orderedCount: 3,
    inProgressCount: 1,
    completedTodayCount: 5
};

const mockOrder: DiagnosticOrderDto = {
    id: 1,
    appointmentId: 10,
    appointmentCode: 'APPT-2026-0010',
    orderCode: 'DX-20260907-0001',
    patientId: 2,
    patientName: 'Nguyễn Văn An',
    patientPhone: '0901234567',
    patientGender: 'Male',
    patientAge: 35,
    orderingDoctorId: 5,
    orderingDoctorName: 'BS. Lê Minh',
    specialtyName: 'Tim mạch',
    clinicalIndication: 'Đau thắt ngực khi vận động',
    note: 'Lấy mẫu sáng sớm khi đói',
    status: 'Ordered',
    orderedAtUtc: '2026-09-07T08:30:00Z',
    items: [
        {
            id: 101,
            diagnosticOrderId: 1,
            diagnosticServiceId: 1,
            serviceCode: 'CBC',
            serviceName: 'Tổng phân tích tế bào máu (CBC)',
            category: 'Laboratory',
            status: 'Ordered',
            result: null
        }
    ]
};

const mockCompletedOrder: DiagnosticOrderDto = {
    ...mockOrder,
    status: 'Completed',
    completedAtUtc: '2026-09-07T09:15:00Z',
    completedByUserName: 'KTV Trần Khoa',
    reviewedAtUtc: '2026-09-07T09:30:00Z',
    reviewedByDoctorName: 'BS. Lê Minh',
    items: [
        {
            id: 101,
            diagnosticOrderId: 1,
            diagnosticServiceId: 1,
            serviceCode: 'CBC',
            serviceName: 'Tổng phân tích tế bào máu (CBC)',
            category: 'Laboratory',
            status: 'Completed',
            result: {
                id: 501,
                diagnosticOrderItemId: 101,
                resultText: 'Bạch cầu (WBC): 7.5, Hồng cầu (RBC): 4.8, Tiểu cầu: 250',
                conclusion: 'Các chỉ số tế bào máu trong giới hạn sinh lý bình thường.',
                referenceRange: 'WBC: 4.0-10.0 G/L; RBC: 4.0-5.5 T/L; PLT: 150-400 G/L',
                unit: 'G/L',
                resultedAtUtc: '2026-09-07T09:15:00Z',
                resultedByUserId: 'tech-uuid',
                resultedByUserName: 'KTV Trần Khoa'
            }
        }
    ]
};

describe('Diagnostic Workflow Frontend Tests', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('TechnicianDashboard renders queue and statistics correctly', async () => {
        vi.mocked(diagnosticApi.getTechnicianStats).mockResolvedValue({
            success: true, message: 'Success',
            data: mockStats,
        });

        vi.mocked(diagnosticApi.getTechnicianOrders).mockResolvedValue({
            success: true, message: 'Success',
            data: {
                items: [mockOrder],
                page: 1,
                pageSize: 20,
                totalItems: 1
            },
        });

        render(
            <DialogProvider>
                <MemoryRouter>
                    <TechnicianDashboard />
                </MemoryRouter>
            </DialogProvider>
        );

        expect(await screen.findByText(/Bàn Làm Việc Cận Lâm Sàng/i)).toBeInTheDocument();
        expect(screen.getByText('3')).toBeInTheDocument();
        expect(screen.getByText('DX-20260907-0001')).toBeInTheDocument();
        expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
        expect(screen.getByText('Tổng phân tích tế bào máu (CBC)')).toBeInTheDocument();
    });

    it('DiagnosticOrderPrint renders printable slip correctly', async () => {
        vi.mocked(diagnosticApi.getDoctorOrderById).mockResolvedValue({
            success: true, message: 'Success',
            data: mockOrder,
        });

        render(
            <MemoryRouter initialEntries={['/doctor/diagnostic-orders/1/print']}>
                <Routes>
                    <Route path="/doctor/diagnostic-orders/:id/print" element={<DiagnosticOrderPrint />} />
                </Routes>
            </MemoryRouter>
        );

        expect(await screen.findByRole('heading', { name: /Phiếu Chỉ Định Cận Lâm Sàng/i })).toBeInTheDocument();
        expect(screen.getByText(/DX-20260907-0001/i)).toBeInTheDocument();
        expect(screen.getByText('Nguyễn Văn An')).toBeInTheDocument();
        expect(screen.getAllByText('BS. Lê Minh').length).toBeGreaterThanOrEqual(1);
        expect(screen.getByText('Tổng phân tích tế bào máu (CBC)')).toBeInTheDocument();
        expect(screen.getByText(/Đau thắt ngực khi vận động/)).toBeInTheDocument();
    });

    it('PatientDiagnosticResults renders completed diagnostic results accordion', async () => {
        vi.mocked(diagnosticApi.getPatientOrders).mockResolvedValue({
            success: true, message: 'Success',
            data: {
                items: [mockCompletedOrder],
                totalItems: 1,
                page: 1,
                pageSize: 20
            },
        });

        vi.mocked(diagnosticApi.getPatientVitals).mockResolvedValue({
            success: true, message: 'Success',
            data: [],
        });

        render(
            <DialogProvider>
                <MemoryRouter>
                    <PatientDiagnosticResults />
                </MemoryRouter>
            </DialogProvider>
        );

        expect(await screen.findByRole('heading', { name: /Kết Quả Cận Lâm Sàng & Sinh Hiệu/i })).toBeInTheDocument();
        expect(screen.getByText('DX-20260907-0001')).toBeInTheDocument();
        expect(screen.getByText('Đã có kết luận bác sĩ')).toBeInTheDocument();

        // Check test item results (first item is expanded by default in PatientDiagnosticResults)
        expect(await screen.findByText(/Tổng phân tích tế bào máu/i)).toBeInTheDocument();
        expect(screen.getByText(/Bạch cầu/)).toBeInTheDocument();
        expect(screen.getByText(/Các chỉ số tế bào máu trong giới hạn sinh lý bình thường/)).toBeInTheDocument();
    });

    it('DiagnosticOrderPrint handles undefined gender, fallback specialty, and displays preparation instructions', async () => {
        const orderWithUndefinedGender: DiagnosticOrderDto = {
            ...mockOrder,
            id: 2,
            patientGender: undefined,
            specialtyName: '',
            items: [
                {
                    id: 102,
                    diagnosticOrderId: 2,
                    diagnosticServiceId: 2,
                    serviceCode: 'US-ABDOMEN',
                    serviceName: 'Siêu âm ổ bụng tổng quát',
                    category: 'Imaging',
                    status: 'Ordered',
                    preparationInstructions: 'Nhịn ăn ít nhất 6 tiếng trước khi siêu âm',
                    result: null
                }
            ]
        };

        vi.mocked(diagnosticApi.getDoctorOrderById).mockResolvedValue({
            success: true,
            message: 'Success',
            data: orderWithUndefinedGender
        });

        render(
            <MemoryRouter initialEntries={['/doctor/diagnostic-orders/2/print']}>
                <Routes>
                    <Route path="/doctor/diagnostic-orders/:id/print" element={<DiagnosticOrderPrint />} />
                </Routes>
            </MemoryRouter>
        );

        expect(await screen.findByRole('heading', { name: /Phiếu Chỉ Định Cận Lâm Sàng/i })).toBeInTheDocument();
        expect(screen.getByText('Chưa cập nhật')).toBeInTheDocument();
        expect(screen.getByText('Siêu âm ổ bụng tổng quát')).toBeInTheDocument();
        expect(screen.getByText(/Nhịn ăn ít nhất 6 tiếng trước khi siêu âm/)).toBeInTheDocument();

        // Must not contain fake hotline or address
        expect(screen.queryByText(/123 Đường Y Tế/i)).not.toBeInTheDocument();
        expect(screen.queryByText(/1900 1234/i)).not.toBeInTheDocument();
    });

    it('DiagnosticOrderPrint displays error state when order fetch fails', async () => {
        vi.mocked(diagnosticApi.getDoctorOrderById).mockRejectedValue(
            new Error('Network error or order not found')
        );

        render(
            <MemoryRouter initialEntries={['/doctor/diagnostic-orders/999/print']}>
                <Routes>
                    <Route path="/doctor/diagnostic-orders/:id/print" element={<DiagnosticOrderPrint />} />
                </Routes>
            </MemoryRouter>
        );

        expect(await screen.findByText(/Network error or order not found/i)).toBeInTheDocument();
    });
});


