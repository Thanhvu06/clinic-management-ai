import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { billingApi } from '../api/billingApi';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/billingApi', () => ({ billingApi: { reception: {
    getTodayKpi: vi.fn(), getInvoices: vi.fn(), getUnbilledVisits: vi.fn(),
    createInvoiceFromVisit: vi.fn(), createInvoiceFromAppointment: vi.fn(), createInvoiceFromHealthPackage: vi.fn(),
} } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));

const visit = { visitId: 101, visitCode: 'VIS-0101', patientId: 1, patientName: 'Nguyễn Văn An', visitDate: '2026-10-10', departmentName: 'Khoa Nội', doctorName: 'BS Minh', status: 'ReadyForBilling', unbilledItemCount: 1, estimatedTotal: 200000 };
const appointment = { id: 201, appointmentCode: 'APT-0201', patientName: 'Trần Thị Mai', appointmentDate: '2026-10-11', status: 'Completed' };
const registration = { id: 301, registrationCode: 'PKG-0301', patientName: 'Lê Minh Hùng', preferredDate: '2026-10-12', status: 'Confirmed' };
const cases = [
    { source: 'visit', radio: 'Lượt khám ngoại trú (Visit)', field: 'Chọn lượt khám *', label: 'VIS-0101 — Nguyễn Văn An — 10/10/2026', create: 'createInvoiceFromVisit' as const, payload: { patientVisitId: 101 } },
    { source: 'appointment', radio: 'Lịch khám bệnh (Appointment)', field: 'Chọn lịch hẹn đã khám xong *', label: 'APT-0201 — Trần Thị Mai — 11/10/2026', create: 'createInvoiceFromAppointment' as const, payload: { appointmentId: 201 } },
    { source: 'package', radio: 'Gói khám sức khỏe (Đã xác nhận)', field: 'Chọn đăng ký gói khám đã xác nhận *', label: 'PKG-0301 — Lê Minh Hùng — 12/10/2026', create: 'createInvoiceFromHealthPackage' as const, payload: { healthPackageRegistrationId: 301 } },
];

const mount = async () => {
    render(<MemoryRouter><ReceptionBilling /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Lập hóa đơn mới' }));
};
const openSource = async (entry: typeof cases[number]) => {
    fireEvent.click(screen.getByRole('radio', { name: entry.radio }));
    const select = screen.getByRole('combobox', { name: entry.field });
    fireEvent.mouseDown(select);
    return select;
};

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue({ success: true, message: '', data: { todayUnpaidInvoices: 0, todayPaidInvoices: 0, todayCancelledInvoices: 0, todayRevenue: 0 } });
    vi.mocked(billingApi.reception.getInvoices).mockResolvedValue({ success: true, message: '', data: { items: [], totalItems: 0, totalPages: 1, page: 1, pageSize: 10 } });
    vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({ success: true, message: '', data: { items: [visit], totalItems: 1, totalPages: 1, page: 1, pageSize: 10 } });
    vi.mocked(axiosClient.get).mockImplementation(async url => ({ success: true, message: '', data: { items: String(url).includes('/appointments?') ? [appointment] : [registration], totalItems: 1, totalPages: 1 } }));
    for (const entry of cases) vi.mocked(billingApi.reception[entry.create]).mockResolvedValue({ success: false, message: 'Synthetic response' });
});

describe('ReceptionBilling readable invoice source picker', () => {
    it.each(cases)('selects $source by readable label and sends the original internal id', async entry => {
        await mount();
        await openSource(entry);
        expect(screen.getByRole('button', { name: 'Tạo hóa đơn' })).toBeDisabled();
        fireEvent.click(await screen.findByText(entry.label));
        fireEvent.click(screen.getByRole('button', { name: 'Tạo hóa đơn' }));
        await waitFor(() => expect(billingApi.reception[entry.create]).toHaveBeenCalledWith(entry.payload));
        if (entry.source === 'visit') expect(billingApi.reception.getUnbilledVisits).toHaveBeenCalledWith(undefined, 1, 10);
        else expect(axiosClient.get).toHaveBeenCalledWith(expect.stringContaining(entry.source === 'appointment' ? 'status=Completed' : 'status=Confirmed'));
    });

    it.each(cases)('keeps creation disabled when $source has no entries', async entry => {
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue({ success: true, message: '', data: { items: [], totalItems: 0, totalPages: 1, page: 1, pageSize: 10 } });
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, message: '', data: { items: [], totalItems: 0, totalPages: 1 } });
        await mount();
        await openSource(entry);
        expect(await screen.findByText('Không có mục nào chờ lập hóa đơn')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Tạo hóa đơn' })).toBeDisabled();
        expect(billingApi.reception[entry.create]).not.toHaveBeenCalled();
    });

    it('clears the previous selection on source change and retries a failed list request', async () => {
        await mount();
        await openSource(cases[0]);
        fireEvent.click(await screen.findByText(cases[0].label));
        expect(screen.getByRole('button', { name: 'Tạo hóa đơn' })).not.toBeDisabled();
        vi.mocked(axiosClient.get).mockRejectedValueOnce(new Error('Synthetic failure'));
        fireEvent.click(screen.getByRole('radio', { name: cases[1].radio }));
        expect(screen.getByRole('button', { name: 'Tạo hóa đơn' })).toBeDisabled();
        fireEvent.click(await screen.findByRole('button', { name: 'Thử lại' }));
        fireEvent.mouseDown(screen.getByRole('combobox', { name: cases[1].field }));
        fireEvent.click(await screen.findByText(cases[1].label));
        fireEvent.click(screen.getByRole('button', { name: 'Tạo hóa đơn' }));
        await waitFor(() => expect(billingApi.reception.createInvoiceFromAppointment).toHaveBeenCalledWith({ appointmentId: 201 }));
        expect(billingApi.reception.createInvoiceFromVisit).not.toHaveBeenCalled();
    });

    it('discards a late response from the previously selected source', async () => {
        let resolveVisits!: (value: Awaited<ReturnType<typeof billingApi.reception.getUnbilledVisits>>) => void;
        vi.mocked(billingApi.reception.getUnbilledVisits).mockReturnValue(new Promise(resolve => { resolveVisits = resolve; }));
        await mount();
        await openSource(cases[1]);
        fireEvent.click(await screen.findByText(cases[1].label));
        resolveVisits({ success: true, message: '', data: { items: [visit], totalItems: 1, totalPages: 1, page: 1, pageSize: 10 } });
        await waitFor(() => expect(screen.getByRole('button', { name: 'Tạo hóa đơn' })).not.toBeDisabled());
        fireEvent.click(screen.getByRole('button', { name: 'Tạo hóa đơn' }));
        await waitFor(() => expect(billingApi.reception.createInvoiceFromAppointment).toHaveBeenCalledWith({ appointmentId: 201 }));
    });

    it('loads later list pages so a visit outside the first page can be selected', async () => {
        vi.mocked(billingApi.reception.getUnbilledVisits).mockImplementation(async (_facility, page) => ({ success: true, message: '', data: { items: [page === 2 ? { ...visit, visitId: 102, visitCode: 'VIS-0102' } : visit], totalItems: 11, totalPages: 2, page: page || 1, pageSize: 10 } }));
        await mount();
        await openSource(cases[0]);
        fireEvent.click(await screen.findByText('VIS-0102 — Nguyễn Văn An — 10/10/2026'));
        fireEvent.click(screen.getByRole('button', { name: 'Tạo hóa đơn' }));
        await waitFor(() => expect(billingApi.reception.createInvoiceFromVisit).toHaveBeenCalledWith({ patientVisitId: 102 }));
    });
});
