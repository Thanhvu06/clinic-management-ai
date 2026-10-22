import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { readFileSync } from 'node:fs';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { billingApi } from '../api/billingApi';
import { organizationApi } from '../api/organizationApi';
import { patientVisitApi } from '../api/patientVisitApi';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { InvoiceSourceType, InvoiceStatus } from '../types';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getDepartments: vi.fn() } }));
vi.mock('../api/patientVisitApi', () => ({ patientVisitApi: { receptionCheckInAppointment: vi.fn() } }));
vi.mock('../api/billingApi', () => ({ billingApi: { reception: { getTodayKpi: vi.fn(), getInvoices: vi.fn(), getUnbilledVisits: vi.fn() } } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: vi.fn() }) }));

const ok = <T,>(data: T) => ({ success: true, message: '', data });
const appointment = {
    id: 77, appointmentCode: 'AP-77', patientId: 1, patientName: 'Người bệnh tiếp nhận', patientPhone: '0900000000', doctorId: 2, doctorName: 'Bác sĩ A',
    specialtyId: 3, specialtyName: 'Nội khoa', appointmentDate: '2026-10-22', startTime: '08:00:00', endTime: '08:30:00', reason: 'Khám', status: 'Confirmed', facilityId: 9,
};
const ticket = {
    visitId: 5, visitCode: 'VIS-5', patientId: 1, patientName: 'Người bệnh tiếp nhận', medicalRecordNumber: 'MRN-1', phoneNumber: '0900000000',
    queueNumber: 12, queueDisplay: 'A012', facilityId: 9, facilityName: 'Cơ sở X', departmentId: 19, departmentName: 'Khoa Nội',
    checkedInAtUtc: '2026-10-22T01:00:00Z', receptionistName: 'Lễ tân', status: 'WaitingForDoctor', priority: 'Normal', arrivalType: 'Scheduled',
};

afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('U4 reception check-in modal', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date(2026, 9, 22, 8, 0));
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => url.includes('/history') ? ok([]) as never : ok({ items: [appointment], totalItems: 1 }) as never);
        vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([{ id: 19, facilityId: 9, specialtyId: 3, code: 'NOI', name: 'Khoa Nội', isActive: true }]) as never);
        vi.mocked(patientVisitApi.receptionCheckInAppointment).mockResolvedValue(ok(ticket) as never);
    });

    const openDetail = async () => {
        render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter><ReceptionAppointments /></MemoryRouter></ConfigProvider>);
        fireEvent.click(within((await screen.findByText('AP-77')).closest('tr')!).getByRole('button', { name: 'Chi tiết' }));
        return screen.getByRole('dialog');
    };

    it('uses the plain department label, hint and button copy', async () => {
        const dialog = await openDetail();
        expect(within(dialog).getByLabelText('Khoa tiếp nhận *')).toBeInTheDocument();
        expect(within(dialog).getByText('Đã chọn sẵn theo chuyên khoa của lịch hẹn, có thể đổi.')).toBeInTheDocument();
        expect(within(dialog).queryByText(/Copilot|Backend/)).not.toBeInTheDocument();
        const button = within(dialog).getByRole('button', { name: 'Tiếp nhận & cấp STT' });
        expect(button.getAttribute('style')).toBeNull();
        expect(button.className).toMatch(/modalCheckInButton/);
        expect(readFileSync('src/pages/reception/ReceptionAppointments.module.css', 'utf8')).toMatch(/\.modalCheckInButton \{ min-height: 40px; \}/);
    });

    it('closes the appointment detail before showing the queue ticket after a successful check-in', async () => {
        const dialog = await openDetail();
        fireEvent.click(within(dialog).getByRole('button', { name: 'Tiếp nhận & cấp STT' }));
        await waitFor(() => expect(patientVisitApi.receptionCheckInAppointment).toHaveBeenCalledWith(77));
        expect(await screen.findByText('Phiếu tiếp nhận khám')).toBeInTheDocument();
        expect(screen.getByText('012')).toBeInTheDocument();
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(screen.queryByText('#AP-77')).not.toBeInTheDocument();
    });
});

describe('U6 reception billing copy', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue(ok({ todayUnpaidInvoices: 1, todayPaidInvoices: 0, todayCancelledInvoices: 0, todayRevenue: 0 }) as never);
        vi.mocked(billingApi.reception.getInvoices).mockResolvedValue(ok({ items: [{
            id: 2, invoiceCode: 'INV-UNPAID-2', patientId: 1, patientName: 'Người thanh toán', patientPhone: '0900000000',
            sourceType: InvoiceSourceType.Appointment, sourceTypeName: 'Khám chuyên khoa', status: InvoiceStatus.Unpaid, statusName: 'Unpaid',
            subtotal: 100000, totalAmount: 100000, createdAtUtc: '2026-10-22T01:00:00Z',
        }], page: 1, pageSize: 10, totalItems: 1, totalPages: 1 }) as never);
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue(ok({ items: [], page: 1, pageSize: 10, totalItems: 0, totalPages: 1 }) as never);
        vi.mocked(axiosClient.get).mockResolvedValue(ok({ items: [], totalItems: 0, totalPages: 1 }) as never);
    });

    it('explains the visit source in plain Vietnamese', async () => {
        render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter><ReceptionBilling /></MemoryRouter></ConfigProvider>);
        fireEvent.click(screen.getByRole('button', { name: 'Lập hóa đơn mới' }));
        fireEvent.click(screen.getByRole('radio', { name: 'Lượt khám ngoại trú' }));
        expect(screen.getByText('Chỉ hiện lượt khám đã xong và còn khoản chưa thu.')).toBeInTheDocument();
        expect(screen.queryByText(/In-Billing/)).not.toBeInTheDocument();
    });

    it('lists payment methods without English in brackets', async () => {
        render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter><ReceptionBilling /></MemoryRouter></ConfigProvider>);
        fireEvent.click(await screen.findByTitle('Thu tiền hóa đơn'));
        const dialog = screen.getByRole('dialog');
        expect(within(dialog).getByTitle('Tiền mặt')).toBeInTheDocument();
        fireEvent.mouseDown(within(dialog).getAllByRole('combobox')[0]);
        expect(await screen.findByText('Chuyển khoản')).toBeInTheDocument();
        expect(screen.queryByText(/\(Cash\)|\(ManualBankTransfer\)/)).not.toBeInTheDocument();
    });
});
