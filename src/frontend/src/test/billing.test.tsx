import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { InvoiceReceiptModal } from '../components/billing/InvoiceReceiptModal';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { PatientInvoices } from '../pages/patient/PatientInvoices';
import { AdminBilling } from '../pages/admin/AdminBilling';
import { billingApi } from '../api/billingApi';
import { DialogProvider } from '../contexts/DialogContext';
import {
    InvoiceStatus,
    InvoiceSourceType,
    PaymentMethod,
    PaymentStatus,
    type InvoiceDetailDto,
    type InvoiceDto,
    type BillingKpiDto,
    type RevenueReportDto,
    type SpecialtyFeeDto,
} from '../types';

vi.mock('../api/billingApi', () => ({
    billingApi: {
        reception: {
            getInvoices: vi.fn(),
            getInvoiceById: vi.fn(),
            createInvoiceFromAppointment: vi.fn(),
            createInvoiceFromHealthPackage: vi.fn(),
            processPayment: vi.fn(),
            cancelInvoice: vi.fn(),
            getTodayKpi: vi.fn(),
        },
        patient: {
            getMyInvoices: vi.fn(),
            getMyInvoiceDetail: vi.fn(),
        },
        admin: {
            getRevenueReport: vi.fn(),
            getSpecialtyFees: vi.fn(),
            updateSpecialtyFee: vi.fn(),
        },
    },
}));

const mockPaidInvoice: InvoiceDetailDto = {
    id: 1,
    invoiceCode: 'INV-20260906-0001',
    patientId: 10,
    patientName: 'Nguyễn Văn An',
    patientPhone: '0901234567',
    sourceType: InvoiceSourceType.Appointment,
    sourceTypeName: 'Khám chuyên khoa',
    appointmentId: 101,
    appointmentCode: 'APT-260906-0001',
    status: InvoiceStatus.Paid,
    statusName: 'Paid',
    subtotal: 200000,
    totalAmount: 200000,
    createdAtUtc: new Date().toISOString(),
    paidAtUtc: new Date().toISOString(),
    createdByUserName: 'rec@test.com',
    paidByUserName: 'rec@test.com',
    items: [
        {
            id: 1,
            invoiceId: 1,
            itemCode: 'SP01-FEE',
            description: 'Phí khám Nội tổng quát',
            quantity: 1,
            unitPrice: 200000,
            lineTotal: 200000,
            referenceType: 'Specialty',
            referenceId: 1,
        },
    ],
    payments: [
        {
            id: 1,
            paymentCode: 'PAY-20260906-0001',
            invoiceId: 1,
            invoiceCode: 'INV-20260906-0001',
            amount: 200000,
            method: PaymentMethod.Cash,
            methodName: 'Tiền mặt tại quầy',
            receivedByUserId: 'guid-rec',
            receivedByUserName: 'rec@test.com',
            receivedAtUtc: new Date().toISOString(),
            status: PaymentStatus.Succeeded,
            statusName: 'Succeeded',
            referenceCode: 'CASH-REF-01',
            note: 'Khách gửi đủ tiền',
        },
    ],
};

const mockUnpaidInvoice: InvoiceDto = {
    id: 2,
    invoiceCode: 'INV-20260906-0002',
    patientId: 11,
    patientName: 'Trần Thị Bích',
    patientPhone: '0912345678',
    sourceType: InvoiceSourceType.HealthPackageRegistration,
    sourceTypeName: 'Gói khám sức khỏe',
    healthPackageRegistrationId: 5,
    registrationCode: 'PKG-260906-0001',
    status: InvoiceStatus.Unpaid,
    statusName: 'Unpaid',
    subtotal: 1500000,
    totalAmount: 1500000,
    createdAtUtc: new Date().toISOString(),
};

const mockKpi: BillingKpiDto = {
    todayUnpaidInvoices: 3,
    todayPaidInvoices: 5,
    todayCancelledInvoices: 1,
    todayRevenue: 2500000,
};

const mockRevenueReport: RevenueReportDto = {
    fromDate: '2026-08-01',
    toDate: '2026-09-06',
    totalRevenue: 25000000,
    totalSucceededTransactions: 45,
    dailyBreakdown: [
        {
            date: '2026-09-05',
            revenue: 3500000,
            succeededPaymentsCount: 7,
            paidInvoicesCount: 7,
        },
    ],
    statusBreakdown: {
        unpaidCount: 4,
        unpaidAmount: 2000000,
        paidCount: 45,
        paidAmount: 25000000,
        cancelledCount: 2,
        cancelledAmount: 500000,
    },
};

const mockSpecialtyFees: SpecialtyFeeDto[] = [
    {
        id: 1,
        specialtyCode: 'SP01',
        name: 'Nội tổng quát',
        consultationFee: 200000,
        isActive: true,
    },
    {
        id: 2,
        specialtyCode: 'SP02',
        name: 'Nhi khoa',
        consultationFee: 180000,
        isActive: true,
    },
];

describe('Billing Module Frontend Tests', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    describe('InvoiceReceiptModal', () => {
        it('renders clinic branding, items, amounts, and payment verification for paid invoice', () => {
            const handleClose = vi.fn();
            render(
                <InvoiceReceiptModal
                    isOpen={true}
                    onClose={handleClose}
                    invoice={mockPaidInvoice}
                />
            );

            expect(screen.getByText('HỆ THỐNG PHÒNG KHÁM ĐA KHOA CLINICCARE AI')).toBeInTheDocument();
            expect(screen.getByText(/INV-20260906-0001/)).toBeInTheDocument();
            expect(screen.getAllByText('Nguyễn Văn An').length).toBe(2);
            expect(screen.getByText('Phí khám Nội tổng quát')).toBeInTheDocument();
            expect(screen.getByText(/PAY-20260906-0001/)).toBeInTheDocument();
            expect(screen.getByText(/In phiếu thu \/ hóa đơn/)).toBeInTheDocument();
        });

        it('renders nothing when closed', () => {
            const { container } = render(
                <InvoiceReceiptModal
                    isOpen={false}
                    onClose={vi.fn()}
                    invoice={mockPaidInvoice}
                />
            );
            expect(container.firstChild).toBeNull();
        });
    });

    describe('ReceptionBilling Page', () => {
        it('renders receptionist KPI cards and list of invoices with actions', async () => {
            vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValueOnce({
                success: true,
                message: '',
                data: mockKpi,
            });

            vi.mocked(billingApi.reception.getInvoices).mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    items: [mockUnpaidInvoice],
                    page: 1,
                    pageSize: 10,
                    totalItems: 1,
                    totalPages: 1,
                },
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
                expect(screen.getByText('3')).toBeInTheDocument(); // Unpaid KPI
                expect(screen.getByText('5')).toBeInTheDocument(); // Paid KPI
                expect(screen.getByText('INV-20260906-0002')).toBeInTheDocument();
                expect(screen.getByText('Trần Thị Bích')).toBeInTheDocument();
                expect(screen.getByTitle('Thu tiền hóa đơn')).toBeInTheDocument();
                expect(screen.getByTitle('Hủy hóa đơn')).toBeInTheDocument();
            });
        });

        it('opens payment modal when clicking Thu tiền', async () => {
            vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValueOnce({
                success: true,
                message: '',
                data: mockKpi,
            });

            vi.mocked(billingApi.reception.getInvoices).mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    items: [mockUnpaidInvoice],
                    page: 1,
                    pageSize: 10,
                    totalItems: 1,
                    totalPages: 1,
                },
            });

            render(
                <DialogProvider>
                    <MemoryRouter>
                        <ReceptionBilling />
                    </MemoryRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByTitle('Thu tiền hóa đơn')).toBeInTheDocument();
            });

            fireEvent.click(screen.getByTitle('Thu tiền hóa đơn'));

            expect(screen.getByText(/Thu tiền hóa đơn INV-20260906-0002/)).toBeInTheDocument();
            expect(screen.getByText('Xác nhận đã thu tiền')).toBeInTheDocument();
        });
    });

    describe('PatientInvoices Page', () => {
        it('renders patient invoices, shows counter payment advice, and strictly omits payment buttons', async () => {
            vi.mocked(billingApi.patient.getMyInvoices).mockResolvedValueOnce({
                success: true,
                message: '',
                data: {
                    items: [mockUnpaidInvoice],
                    page: 1,
                    pageSize: 10,
                    totalItems: 1,
                    totalPages: 1,
                },
            });

            render(
                <DialogProvider>
                    <MemoryRouter>
                        <PatientInvoices />
                    </MemoryRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('Hóa đơn dịch vụ của tôi')).toBeInTheDocument();
                expect(screen.getByText(/Quý khách vui lòng xuất trình mã hóa đơn/)).toBeInTheDocument();
                expect(screen.getByText('INV-20260906-0002')).toBeInTheDocument();
                expect(screen.getByText('Xem phiếu')).toBeInTheDocument();
            });

            // Ensure patient cannot see staff cashiering action
            expect(screen.queryByText('Thu tiền')).not.toBeInTheDocument();
            expect(screen.queryByText('Hủy hóa đơn')).not.toBeInTheDocument();
        });
    });

    describe('AdminBilling Page', () => {
        it('renders revenue reports and specialty fee configuration tabs', async () => {
            vi.mocked(billingApi.admin.getRevenueReport).mockResolvedValueOnce({
                success: true,
                message: '',
                data: mockRevenueReport,
            });

            vi.mocked(billingApi.admin.getSpecialtyFees).mockResolvedValueOnce({
                success: true,
                message: '',
                data: mockSpecialtyFees,
            });

            render(
                <DialogProvider>
                    <MemoryRouter>
                        <AdminBilling />
                    </MemoryRouter>
                </DialogProvider>
            );

            await waitFor(() => {
                expect(screen.getByText('Doanh thu & Biểu phí phòng khám')).toBeInTheDocument();
                expect(screen.getByText('45 lượt')).toBeInTheDocument();
                expect(screen.getByText('Bảng chi tiết doanh thu theo từng ngày')).toBeInTheDocument();
            });

            // Switch to specialty fees tab
            fireEvent.click(screen.getByText('Quản lý biểu phí'));

            await waitFor(() => {
                expect(screen.getByText('Nội tổng quát')).toBeInTheDocument();
                expect(screen.getByText('Nhi khoa')).toBeInTheDocument();
                expect(screen.getAllByText('Sửa mức phí').length).toBe(2);
            });
        });
    });
});
