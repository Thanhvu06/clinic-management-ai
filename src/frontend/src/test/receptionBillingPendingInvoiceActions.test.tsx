import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { billingApi } from '../api/billingApi';
import axiosClient from '../api/axiosClient';
import { InvoiceStatus } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));
vi.mock('../components/billing/InvoiceReceiptModal', () => ({ InvoiceReceiptModal: () => null }));
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

describe('ReceptionBilling pending invoice queue actions', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue({
            success: true,
            message: 'OK',
            data: { todayUnpaidInvoices: 1, todayPaidInvoices: 0, todayCancelledInvoices: 0, todayRevenue: 0 },
        });
        vi.mocked(billingApi.reception.getInvoices).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                items: [
                    {
                        id: 501,
                        invoiceCode: 'INV-261108-0501',
                        patientId: 10,
                        patientName: 'Nguyễn Văn Hoà',
                        patientPhone: '0901234567',
                        sourceType: 1,
                        sourceTypeName: 'Lịch khám chuyên khoa',
                        patientVisitId: 1001,
                        visitCode: 'VIS-20261108-1001',
                        status: InvoiceStatus.Unpaid,
                        statusName: 'Chờ thanh toán',
                        subtotal: 200000,
                        totalAmount: 200000,
                        createdAtUtc: '2026-11-08T08:00:00Z',
                    },
                ],
                page: 1,
                pageSize: 10,
                totalItems: 1,
                totalPages: 1,
            },
        });
        vi.mocked(billingApi.reception.getInvoiceById).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                id: 501,
                invoiceCode: 'INV-261108-0501',
                patientId: 10,
                patientName: 'Nguyễn Văn Hoà',
                patientPhone: '0901234567',
                sourceType: 1,
                sourceTypeName: 'Lịch khám chuyên khoa',
                patientVisitId: 1001,
                visitCode: 'VIS-20261108-1001',
                status: InvoiceStatus.Paid,
                statusName: 'Đã thanh toán',
                subtotal: 200000,
                totalAmount: 200000,
                createdAtUtc: '2026-11-08T08:00:00Z',
                items: [],
                payments: [],
            },
        });
    });

    it('shows Thu tiền and Xem instead of Lập hóa đơn for visits with a pending invoice and no unbilled items', async () => {
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                items: [
                    {
                        visitId: 1001,
                        visitCode: 'VIS-20261108-1001',
                        patientId: 10,
                        patientName: 'Nguyễn Văn Hoà',
                        phoneNumber: '0901234567',
                        departmentName: 'Khoa Nội tổng quát',
                        doctorName: 'BS Nguyễn Minh Khải',
                        visitDate: '2026-11-08',
                        status: 'Completed',
                        unbilledItemCount: 1,
                        estimatedTotal: 200000,
                        hasPendingPayment: true,
                        hasUnbilledItems: false,
                        pendingInvoiceId: 501,
                        pendingInvoiceCode: 'INV-261108-0501',
                    },
                ],
                page: 1,
                pageSize: 10,
                totalItems: 1,
                totalPages: 1,
            },
        });

        render(<ReceptionBilling />);

        fireEvent.click(screen.getByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/ }));

        expect(await screen.findByText('VIS-20261108-1001')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Thu tiền/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Xem/i })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /^Lập hóa đơn$/i })).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /Thu tiền/i }));
        expect(await screen.findByText('Thu tiền hóa đơn INV-261108-0501')).toBeInTheDocument();
    }, 15000);

    it('shows warning and Xem hóa đơn when a visit has both a pending invoice and unbilled items', async () => {
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                items: [
                    {
                        visitId: 1002,
                        visitCode: 'VIS-20261108-1002',
                        patientId: 11,
                        patientName: 'Trần Thị Bích',
                        departmentName: 'Khoa Nội tổng quát',
                        doctorName: 'BS Nguyễn Minh Khải',
                        visitDate: '2026-11-08',
                        status: 'InBilling',
                        unbilledItemCount: 1,
                        estimatedTotal: 350000,
                        hasPendingPayment: true,
                        hasUnbilledItems: true,
                        pendingInvoiceId: 502,
                        pendingInvoiceCode: 'INV-261108-0502',
                    },
                ],
                page: 1,
                pageSize: 10,
                totalItems: 1,
                totalPages: 1,
            },
        });

        render(<ReceptionBilling />);

        fireEvent.click(screen.getByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/ }));

        expect(await screen.findByText('Có hóa đơn chờ thanh toán')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Xem hóa đơn/i })).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /^Lập hóa đơn$/i })).not.toBeInTheDocument();
    }, 15000);

    it('refreshes unbilled visits queue after creating an invoice or processing a payment', async () => {
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                items: [
                    {
                        visitId: 1001,
                        visitCode: 'VIS-20261108-1001',
                        patientId: 10,
                        patientName: 'Nguyễn Văn Hoà',
                        phoneNumber: '0901234567',
                        departmentName: 'Khoa Nội tổng quát',
                        doctorName: 'BS Nguyễn Minh Khải',
                        visitDate: '2026-11-08',
                        status: 'Completed',
                        unbilledItemCount: 1,
                        estimatedTotal: 200000,
                        hasPendingPayment: true,
                        hasUnbilledItems: false,
                        pendingInvoiceId: 501,
                        pendingInvoiceCode: 'INV-261108-0501',
                    },
                ],
                page: 1,
                pageSize: 10,
                totalItems: 1,
                totalPages: 1,
            },
        });
        vi.mocked(axiosClient.get).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                items: [{ id: 201, appointmentCode: 'APT-0201', patientName: 'Trần Thị Mai', appointmentDate: '2026-11-08', status: 'Completed' }],
                totalItems: 1,
                totalPages: 1,
            },
        });
        vi.mocked(billingApi.reception.createInvoiceFromAppointment).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                id: 503,
                invoiceCode: 'INV-261108-0503',
                patientId: 12,
                patientName: 'Trần Thị Mai',
                patientPhone: '0909999999',
                sourceType: 1,
                sourceTypeName: 'Lịch khám chuyên khoa',
                status: InvoiceStatus.Unpaid,
                statusName: 'Chờ thanh toán',
                subtotal: 180000,
                totalAmount: 180000,
                createdAtUtc: '2026-11-08T09:00:00Z',
                items: [],
                payments: [],
            },
        });
        vi.mocked(billingApi.reception.processPayment).mockResolvedValue({
            success: true,
            message: 'OK',
            data: {
                id: 1,
                paymentCode: 'PAY-001',
                invoiceId: 501,
                invoiceCode: 'INV-261108-0501',
                amount: 200000,
                method: 1,
                methodName: 'Tiền mặt',
                receivedByUserId: 'u1',
                receivedAtUtc: '2026-11-08T09:05:00Z',
                status: 1,
                statusName: 'Thành công',
            },
        });

        render(<ReceptionBilling />);

        await waitFor(() => expect(billingApi.reception.getUnbilledVisits).toHaveBeenCalledTimes(1));

        fireEvent.click(screen.getByRole('button', { name: 'Lập hóa đơn mới' }));
        fireEvent.click(screen.getByRole('radio', { name: 'Lịch hẹn đã khám' }));
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Chọn lịch hẹn đã khám xong *' }));
        fireEvent.click(await screen.findByText('APT-0201 — Trần Thị Mai — 08/11/2026'));
        vi.mocked(billingApi.reception.getUnbilledVisits).mockClear();
        fireEvent.click(screen.getByRole('button', { name: 'Tạo hóa đơn' }));

        await waitFor(() => expect(billingApi.reception.createInvoiceFromAppointment).toHaveBeenCalledWith({ appointmentId: 201 }));
        await waitFor(() => expect(billingApi.reception.getUnbilledVisits).toHaveBeenCalledTimes(1));

        fireEvent.click(screen.getByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/ }));
        fireEvent.click(await screen.findByRole('button', { name: /Thu tiền/i }));
        vi.mocked(billingApi.reception.getUnbilledVisits).mockClear();
        fireEvent.click(await screen.findByRole('button', { name: 'Xác nhận đã thu tiền' }));

        await waitFor(() => expect(billingApi.reception.processPayment).toHaveBeenCalledWith(501, expect.objectContaining({ amount: 200000 })));
        await waitFor(() => expect(billingApi.reception.getUnbilledVisits).toHaveBeenCalledTimes(1));
    }, 15000);
});
