import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { DoctorQueue } from '../pages/doctor/DoctorQueue';
import { DoctorExaminationPage } from '../pages/doctor/DoctorExaminationPage';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';
import { billingApi } from '../api/billingApi';
import { doctorApi } from '../api/doctorApi';
import { diagnosticApi } from '../api/diagnosticApi';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({
    default: {
        get: vi.fn(),
        post: vi.fn(),
    },
}));

vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => ({
        showAlert: vi.fn(),
        showConfirm: vi.fn(),
        showToast: vi.fn(),
    }),
}));

vi.mock('../components/billing/InvoiceReceiptModal', () => ({
    InvoiceReceiptModal: () => null,
}));

vi.mock('../components/copilot/copilotResourceContext', () => ({
    useCopilotResource: () => ({ setSelection: vi.fn() }),
    CopilotResourceProvider: ({ children }: any) => children,
}));

vi.mock('../api/billingApi', () => ({
    billingApi: {
        reception: {
            getTodayKpi: vi.fn(),
            getInvoices: vi.fn(),
            getUnbilledVisits: vi.fn(),
            getInvoiceById: vi.fn(),
            createInvoiceFromVisit: vi.fn(),
            createInvoiceFromAppointment: vi.fn(),
            createInvoiceFromHealthPackage: vi.fn(),
            processPayment: vi.fn(),
            cancelInvoice: vi.fn(),
        },
    },
}));

vi.mock('../api/doctorApi', () => ({
    doctorApi: {
        getQueue: vi.fn(),
        checkInAppointment: vi.fn(),
        startConsultation: vi.fn(),
        markNoShow: vi.fn(),
        getVisitPatientClinicalContext: vi.fn(),
        getPatientClinicalContext: vi.fn(),
        getAppointmentHistory: vi.fn(),
        getActiveMedicines: vi.fn(),
    },
}));

vi.mock('../api/diagnosticApi', () => ({
    diagnosticApi: {
        getDoctorOrdersByVisit: vi.fn(),
        getCatalog: vi.fn(),
    },
}));

describe('Manual Test UI Polish', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    it('renders visitDate formatted as dd/MM/yyyy in ReceptionBilling queue', async () => {
        vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue({
            success: true,
            message: 'OK',
            data: { todayUnpaidInvoices: 0, todayPaidInvoices: 0, todayCancelledInvoices: 0, todayRevenue: 0 },
        });
        vi.mocked(billingApi.reception.getInvoices).mockResolvedValue({
            success: true,
            message: 'OK',
            data: { items: [], totalItems: 0, page: 1, pageSize: 10, totalPages: 0 },
        });
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                items: [
                    {
                        visitId: 901,
                        visitCode: 'VIS-20261108-F4F24DAC',
                        patientId: 101,
                        patientName: 'Lê Hoàng Nam',
                        medicalRecordNumber: 'MRN-901',
                        phoneNumber: '0901234567',
                        departmentName: 'Khoa Ngoại',
                        doctorName: 'BS. Trần Văn B',
                        visitDate: '2026-11-08',
                        status: 'InPharmacy',
                        unbilledItemCount: 2,
                        estimatedTotal: 350000,
                        hasPendingPayment: false,
                        hasUnbilledItems: true,
                    },
                ],
                totalItems: 1,
                page: 1,
                pageSize: 10,
                totalPages: 1,
            },
        });

        render(<ReceptionBilling />);

        const unbilledTab = await screen.findByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/i });
        fireEvent.click(unbilledTab);

        await screen.findByText('VIS-20261108-F4F24DAC');
        // visitDate '2026-11-08' must be formatted as '08/11/2026'
        expect(screen.getByText('08/11/2026')).toBeInTheDocument();
        expect(screen.queryByText('2026-11-08')).not.toBeInTheDocument();
    });

    it('displays DatePicker in DoctorQueue formatted as DD/MM/YYYY and sends yyyy-MM-dd to doctorApi.getQueue', async () => {
        vi.mocked(doctorApi.getQueue).mockResolvedValue({
            success: true,
            message: '',
            data: [],
        });

        const { container } = render(
            <MemoryRouter>
                <DoctorQueue />
            </MemoryRouter>
        );

        await waitFor(() => expect(doctorApi.getQueue).toHaveBeenCalled());
        const input = container.querySelector('.ant-picker input') as HTMLInputElement;
        expect(input).toBeInTheDocument();

        // Simulate choosing a date
        fireEvent.change(input, { target: { value: '2026-11-15' } });
        await waitFor(() => {
            expect(doctorApi.getQueue).toHaveBeenCalledWith('2026-11-15');
        });
    });

    it('removes leaked enum text (Trạng thái Issued) in DoctorExaminationWorkspace and uses Mã lượt khám in PharmacyPrescriptions modal', async () => {
        // Test DoctorExaminationWorkspace: complete consultation modal
        const context = {
            patientId: 1,
            patientName: 'Bệnh nhân bàn khám',
            patientPhone: '0901234567',
            patientGender: 'Male',
            patientDob: '1990-01-01',
            totalPastVisits: 0,
            pastVisits: [],
            currentAppointment: {
                id: 1,
                appointmentCode: 'APT-1',
                appointmentDate: '2026-10-20',
                reason: 'Đau đầu',
                startTime: '08:00:00',
                endTime: '08:30:00',
                status: 'InConsultation',
            },
            encounter: {
                diagnosis: 'Viêm họng cấp',
                diagnosisCode: 'J02.9',
                chiefComplaint: 'Đau họng',
                summary: 'Bệnh nhẹ',
            },
            prescription: {
                id: 55,
                status: 'Draft',
                items: [
                    {
                        medicineId: 1,
                        medicineCode: 'MED-1',
                        medicineName: 'Paracetamol 500mg',
                        quantity: 10,
                        unit: 'Viên',
                        dosageInstructions: 'Ngày 2 viên',
                    },
                ],
            },
            vitalSigns: {
                id: 1,
                appointmentId: 1,
                recordedAtUtc: '2026-10-20T02:00:00Z',
                recordedByUserName: 'BS. Hòa',
                temperature: 37.2,
                bloodPressureSystolic: 120,
                bloodPressureDiastolic: 80,
                heartRate: 75,
                respiratoryRate: 18,
                weight: 65,
                height: 170,
                bmi: 22.5,
                spO2: 98,
            },
            vitalHistory: [],
        };

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue({
            success: true,
            message: '',
            data: context as any,
        });
        vi.mocked(doctorApi.getAppointmentHistory).mockResolvedValue({ success: true, message: '', data: [] });
        vi.mocked(doctorApi.getActiveMedicines).mockResolvedValue({
            success: true,
            message: '',
            data: [{ id: 1, code: 'MED-1', name: 'Paracetamol 500mg', unit: 'Viên', stockQuantity: 100 }],
        });
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue({ success: true, message: '', data: [] });
        vi.mocked(diagnosticApi.getCatalog).mockResolvedValue({ success: true, message: '', data: [] });

        const { unmount } = render(
            <ConfigProvider theme={{ token: { motion: false } }}>
                <MemoryRouter initialEntries={['/doctor/visits/1/examination']}>
                    <Routes>
                        <Route path="/doctor/visits/:visitId/examination" element={<DoctorExaminationPage />} />
                    </Routes>
                </MemoryRouter>
            </ConfigProvider>
        );

        await screen.findByText('Bệnh nhân bàn khám');

        // Check completion modal with prescription checkbox
        const finishBtn = await screen.findByRole('button', { name: /Hoàn tất khám/i });
        fireEvent.click(finishBtn);

        // Verify leaked enum text is removed
        expect(screen.queryByText(/Trạng thái Issued/i)).not.toBeInTheDocument();
        expect(screen.getByText('Chốt và phát hành đơn thuốc sang Dược sĩ')).toBeInTheDocument();

        unmount();

        // Test PharmacyPrescriptions modal label
        vi.mocked(axiosClient.get).mockImplementation((url: string) => {
            if (url.includes('/pharmacy/prescriptions/10')) {
                return Promise.resolve({
                    success: true,
                    data: {
                        id: 10,
                        appointmentId: 101,
                        appointmentCode: 'VIS-20261108-0001',
                        patientName: 'Trần Văn C',
                        patientPhone: '0912345678',
                        doctorName: 'BS. Khải',
                        status: 'Issued',
                        items: [],
                        createdAt: '2026-11-08T08:00:00Z',
                    },
                });
            }
            return Promise.resolve({
                success: true,
                data: {
                    items: [
                        {
                            id: 10,
                            appointmentId: 101,
                            appointmentCode: 'VIS-20261108-0001',
                            patientName: 'Trần Văn C',
                            patientPhone: '0912345678',
                            doctorName: 'BS. Khải',
                            status: 'Issued',
                            itemCount: 0,
                            createdAt: '2026-11-08T08:00:00Z',
                        },
                    ],
                    totalCount: 1,
                },
            });
        });

        render(
            <MemoryRouter>
                <PharmacyPrescriptions />
            </MemoryRouter>
        );

        await screen.findByText('Trần Văn C');
        fireEvent.click(screen.getByRole('button', { name: /Xem & cấp phát/i }));

        await waitFor(() => {
            expect(screen.getByText(/Mã lượt khám:\s*VIS-20261108-0001/i)).toBeInTheDocument();
            expect(screen.queryByText(/Mã lịch hẹn:/i)).not.toBeInTheDocument();
        });
    });

    it('scrolls smoothly to diagnostic orders section when clicking Xem kết quả ngay', async () => {
        const scrollIntoViewMock = vi.fn();
        HTMLElement.prototype.scrollIntoView = scrollIntoViewMock;

        const contextWithUnreviewedOrder = {
            patientId: 1,
            patientName: 'Bệnh nhân CLS',
            patientPhone: '0901234567',
            patientGender: 'Male',
            patientDob: '1990-01-01',
            totalPastVisits: 0,
            pastVisits: [],
            currentAppointment: {
                id: 1,
                appointmentCode: 'APT-1',
                appointmentDate: '2026-10-20',
                reason: 'Khám kiểm tra',
                startTime: '08:00:00',
                endTime: '08:30:00',
                status: 'InConsultation',
            },
            encounter: {
                diagnosis: 'Theo dõi',
                diagnosisCode: 'R69',
                chiefComplaint: 'Đau ngực',
                summary: '',
            },
            vitalSigns: null,
            vitalHistory: [],
        };

        vi.mocked(doctorApi.getVisitPatientClinicalContext).mockResolvedValue({
            success: true,
            message: '',
            data: contextWithUnreviewedOrder as any,
        });
        vi.mocked(doctorApi.getAppointmentHistory).mockResolvedValue({ success: true, message: '', data: [] });
        vi.mocked(doctorApi.getActiveMedicines).mockResolvedValue({ success: true, message: '', data: [] });
        vi.mocked(diagnosticApi.getDoctorOrdersByVisit).mockResolvedValue({
            success: true,
            message: '',
            data: [
                {
                    id: 101,
                    orderCode: 'DX-20261108-001',
                    status: 'Completed',
                    reviewedAtUtc: null,
                    items: [],
                } as any,
            ],
        });
        vi.mocked(diagnosticApi.getCatalog).mockResolvedValue({ success: true, message: '', data: [] });

        render(
            <ConfigProvider theme={{ token: { motion: false } }}>
                <MemoryRouter initialEntries={['/doctor/visits/1/examination']}>
                    <Routes>
                        <Route path="/doctor/visits/:visitId/examination" element={<DoctorExaminationPage />} />
                    </Routes>
                </MemoryRouter>
            </ConfigProvider>
        );

        await screen.findByText('Bệnh nhân CLS');
        const viewResultBtn = await screen.findByRole('button', { name: /Xem kết quả ngay/i });
        fireEvent.click(viewResultBtn);

        await waitFor(() => {
            expect(scrollIntoViewMock).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
        });
    });
});
