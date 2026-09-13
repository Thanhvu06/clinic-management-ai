import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { CheckInTicketModal } from '../components/CheckInTicketModal';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { billingApi } from '../api/billingApi';
import { DialogProvider } from '../contexts/DialogContext';
import type { CheckInTicketDto, InvoiceDetailDto, BillingKpiDto } from '../types';
import { InvoiceStatus, InvoiceSourceType } from '../types';

vi.mock('../api/billingApi', () => ({
    billingApi: {
        reception: {
            getInvoices: vi.fn(),
            getInvoiceById: vi.fn(),
            createInvoiceFromAppointment: vi.fn(),
            createInvoiceFromHealthPackage: vi.fn(),
            createInvoiceFromVisit: vi.fn(),
            processPayment: vi.fn(),
            cancelInvoice: vi.fn(),
            getTodayKpi: vi.fn(),
        },
    },
}));

const mockTicket: CheckInTicketDto = {
    visitId: 101,
    visitCode: 'VIS-20260913-0101',
    appointmentId: null,
    appointmentCode: null,
    patientId: 42,
    patientName: 'NGUYỄN VĂN THỬ NGHIỆM',
    medicalRecordNumber: 'BN-2026-000042',
    phoneNumber: '0988776655',
    queueNumber: 15,
    queueDisplay: 'STT 15',
    facilityId: 1,
    facilityName: 'Bệnh viện Đa khoa Quốc tế ClinicCare',
    departmentId: 3,
    departmentName: 'Khoa Nội Tổng Quát',
    roomId: 10,
    roomNumber: 'P.302',
    assignedDoctorId: 5,
    doctorName: 'BS. CK1 Trần Văn Tuấn',
    checkedInAtUtc: new Date().toISOString(),
    receptionistName: 'Lễ tân Hoàng Thị Mai',
    status: 'WaitingForDoctor',
    priority: 'Normal',
    arrivalType: 'WalkIn',
};

const mockKpi: BillingKpiDto = {
    todayUnpaidInvoices: 2,
    todayPaidInvoices: 4,
    todayCancelledInvoices: 0,
    todayRevenue: 1500000,
};

const mockVisitInvoice: InvoiceDetailDto = {
    id: 99,
    invoiceCode: 'INV-20260913-0099',
    patientId: 42,
    patientName: 'NGUYỄN VĂN THỬ NGHIỆM',
    patientPhone: '0988776655',
    sourceType: InvoiceSourceType.Appointment,
    sourceTypeName: 'Lượt khám ngoại trú',
    patientVisitId: 101,
    visitCode: 'VIS-20260913-0101',
    status: InvoiceStatus.Unpaid,
    statusName: 'Unpaid',
    subtotal: 350000,
    totalAmount: 350000,
    createdAtUtc: new Date().toISOString(),
    items: [
        {
            id: 1,
            invoiceId: 99,
            itemCode: 'KHAM-NOI',
            description: 'Khám bệnh: Khoa Nội Tổng Quát',
            quantity: 1,
            unitPrice: 150000,
            lineTotal: 150000,
            referenceType: 'Consultation',
            referenceId: 3,
        },
        {
            id: 2,
            invoiceId: 99,
            itemCode: 'XN-HUYET-HOC',
            description: 'Dịch vụ Cận lâm sàng: Tổng phân tích tế bào máu',
            quantity: 1,
            unitPrice: 200000,
            lineTotal: 200000,
            referenceType: 'Diagnostic',
            referenceId: 7,
        },
    ],
    payments: [],
};

describe('Connected Outpatient Care Journey - Frontend Tests', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    describe('CheckInTicketModal Component', () => {
        it('renders ticket details with STT, MRN, patient info, room and doctor', () => {
            const handleClose = vi.fn();
            render(
                <CheckInTicketModal
                    isOpen={true}
                    ticket={mockTicket}
                    onClose={handleClose}
                />
            );

            expect(screen.getByText('PHIẾU KHÁM BỆNH & SỐ THỨ TỰ')).toBeInTheDocument();
            expect(screen.getByText('STT 15')).toBeInTheDocument();
            expect(screen.getByText(/BN-2026-000042/)).toBeInTheDocument();
            expect(screen.getByText('NGUYỄN VĂN THỬ NGHIỆM')).toBeInTheDocument();
            expect(screen.getByText('Khoa Nội Tổng Quát')).toBeInTheDocument();
            expect(screen.getByText('P.302')).toBeInTheDocument();
            expect(screen.getByText('BS. CK1 Trần Văn Tuấn')).toBeInTheDocument();
            expect(screen.getByText(/0988776655/)).toBeInTheDocument();
        });

        it('triggers window.print when clicking In phiếu khám button', () => {
            const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {});
            render(
                <CheckInTicketModal
                    isOpen={true}
                    ticket={mockTicket}
                    onClose={vi.fn()}
                />
            );

            const printBtn = screen.getByRole('button', { name: /In phiếu khám/i });
            fireEvent.click(printBtn);

            expect(printSpy).toHaveBeenCalled();
            printSpy.mockRestore();
        });

        it('does not render when isOpen is false', () => {
            render(
                <CheckInTicketModal
                    isOpen={false}
                    ticket={mockTicket}
                    onClose={vi.fn()}
                />
            );

            expect(screen.queryByText('PHIẾU KHÁM BỆNH & SỐ THỨ TỰ')).not.toBeInTheDocument();
        });
    });

    describe('ReceptionBilling - Visit Invoice Creation', () => {
        it('allows selecting visit source type and creates visit invoice via billingApi', async () => {
            vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue({
                success: true,
                message: 'OK',
                data: mockKpi,
            });

            vi.mocked(billingApi.reception.getInvoices).mockResolvedValue({
                success: true,
                message: 'OK',
                data: {
                    items: [],
                    page: 1,
                    pageSize: 10,
                    totalItems: 0,
                    totalPages: 1,
                },
            });

            vi.mocked(billingApi.reception.createInvoiceFromVisit).mockResolvedValue({
                success: true,
                message: 'Lập hóa đơn viện phí cho lượt khám thành công.',
                data: mockVisitInvoice,
            });

            render(
                <DialogProvider>
                    <MemoryRouter>
                        <ReceptionBilling />
                    </MemoryRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('Quản lý Hóa đơn & Thu ngân')).toBeInTheDocument();
            });

            // Click "Lập hóa đơn mới"
            const openModalBtn = screen.getByRole('button', { name: /Lập hóa đơn mới/i });
            fireEvent.click(openModalBtn);

            expect(screen.getByText('Lượt khám ngoại trú (Visit)')).toBeInTheDocument();

            // Select Visit radio option
            const visitRadio = screen.getByLabelText(/Lượt khám ngoại trú \(Visit\)/i);
            fireEvent.click(visitRadio);

            // Enter Patient Visit ID
            const input = screen.getByPlaceholderText(/Ví dụ: 1/i);
            fireEvent.change(input, { target: { value: '101' } });

            // Submit
            const submitBtn = screen.getByRole('button', { name: /Tạo hóa đơn/i });
            fireEvent.click(submitBtn);

            await waitFor(() => {
                expect(billingApi.reception.createInvoiceFromVisit).toHaveBeenCalledWith({
                    patientVisitId: 101,
                });
            });
        });
    });
});
