import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { organizationApi } from '../api/organizationApi';
import { DialogProvider } from '../contexts/DialogContext';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { toLocalDateString } from '../utils/formatters';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/patientVisitApi', () => ({ patientVisitApi: { receptionCheckInAppointment: vi.fn(), getCheckInTicket: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getMyFacilities: vi.fn(),
    getDepartments: vi.fn(),
} }));

const today = toLocalDateString();
const shift = (days: number) => { const d = new Date(); d.setDate(d.getDate() + days); return toLocalDateString(d); };
const ddmmyyyy = (iso: string) => iso.split('-').reverse().join('/');

const row = (id: number, appointmentDate: string, status = 'Pending') => ({
    id, appointmentCode: `APT-RC-${id}`, patientId: id, patientName: `Người bệnh ${id}`, patientPhone: '0900000000',
    doctorId: 2, doctorName: 'Bác sĩ A', specialtyId: 3, specialtyName: 'Nội khoa', appointmentDate,
    startTime: '04:00:00', endTime: '04:30:00', reason: 'Khám', status, facilityId: 9, facilityName: 'Cơ sở X',
});

const todayRows = [row(1, today, 'Confirmed'), row(2, today, 'Confirmed'), row(3, today)];
const pendingRows = [row(3, today), row(4, shift(1)), row(5, shift(1)), row(6, shift(2)), row(7, shift(3)), row(8, shift(4))];

const LocationProbe = () => <output data-testid="location">{useLocation().pathname + useLocation().search}</output>;

describe('reception workspace counts, dates and detail link', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(organizationApi.getMyFacilities).mockResolvedValue({ success: true, data: [{ id: 9, name: 'Cơ sở X', isActive: true }] } as never);
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.startsWith('/reception/stats')) return { success: true, data: {
                appointmentsToday: todayRows.length, pendingAppointmentsToday: 1, pendingAppointmentsTotal: pendingRows.length,
                confirmedAppointmentsToday: 2, completedAppointmentsToday: 0 } } as never;
            const rows = url.includes('tab=pending') ? pendingRows : todayRows;
            return { success: true, data: { items: rows, totalItems: rows.length } } as never;
        });
    });

    const tabCount = (name: RegExp) => screen.getByRole('button', { name }).querySelector('span:last-child')?.textContent;

    it('matches every badge to the rows of its list from the first load (H4)', async () => {
        render(<MemoryRouter><DialogProvider><ReceptionWorkspace /></DialogProvider></MemoryRouter>);
        await screen.findByText('APT-RC-1');
        await waitFor(() => expect(tabCount(/^Chờ xác nhận/)).toBe(String(pendingRows.length)));
        expect(tabCount(/^Hôm nay/)).toBe(String(todayRows.length));
        const strip = screen.getByLabelText('Số liệu tiếp nhận hôm nay');
        expect(within(strip).getByText('Chờ xác nhận hôm nay')).toBeInTheDocument();
        expect(within(strip).queryByText('Chờ xác nhận')).not.toBeInTheDocument();
    }, 15_000);

    it('keeps both tab badges equal to their rows after switching to the pending list (H4)', async () => {
        render(<MemoryRouter><DialogProvider><ReceptionWorkspace /></DialogProvider></MemoryRouter>);
        await screen.findByText('APT-RC-1');
        fireEvent.click(screen.getByRole('button', { name: /^Chờ xác nhận/ }));
        await screen.findByText('APT-RC-8');
        expect(screen.getAllByText(/^APT-RC-/)).toHaveLength(pendingRows.length);
        expect(tabCount(/^Chờ xác nhận/)).toBe(String(pendingRows.length));
        expect(tabCount(/^Hôm nay/)).toBe(String(todayRows.length));
    }, 15_000);

    it('shows dd/MM/yyyy next to the time for rows that are not today (H5)', async () => {
        render(<MemoryRouter><DialogProvider><ReceptionWorkspace /></DialogProvider></MemoryRouter>);
        await screen.findByText('APT-RC-1');
        fireEvent.click(screen.getByRole('button', { name: /^Chờ xác nhận/ }));
        const futureRow = (await screen.findByText('APT-RC-4')).closest('tr')!;
        expect(within(futureRow).getByText('04:00 - 04:30')).toBeInTheDocument();
        expect(within(futureRow).getByText(ddmmyyyy(shift(1)))).toBeInTheDocument();
        const todayRow = screen.getByText('APT-RC-3').closest('tr')!;
        expect(within(todayRow).queryByText(ddmmyyyy(today))).not.toBeInTheDocument();
    }, 15_000);

    it('links the eye button to that appointment\'s detail (H6)', async () => {
        render(<MemoryRouter><DialogProvider><ReceptionWorkspace /></DialogProvider></MemoryRouter>);
        await screen.findByText('APT-RC-2');
        expect(screen.getByRole('link', { name: 'Xem chi tiết lịch hẹn APT-RC-2' })).toHaveAttribute('href', '/reception/appointments?appointmentId=2');
    }, 15_000);
});

describe('reception appointments deep link (H6)', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(organizationApi.getDepartments).mockResolvedValue({ success: true, data: [] } as never);
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.includes('/history')) return { success: true, data: [] } as never;
            if (url === '/reception/appointments/55') return { success: true, data: row(55, shift(1)) } as never;
            if (url === '/reception/appointments/404') throw Object.assign(new Error('missing'), { response: { data: { message: 'Lịch hẹn không tồn tại.' } } });
            return { success: true, data: { items: [row(1, today)], totalItems: 1 } } as never;
        });
    });

    const mount = (entry: string) => render(
        <MemoryRouter initialEntries={[entry]}>
            <DialogProvider>
                <LocationProbe />
                <Routes><Route path="/reception/appointments" element={<ReceptionAppointments />} /></Routes>
            </DialogProvider>
        </MemoryRouter>
    );

    it('opens the detail dialog of the linked appointment, even when it is not on the first page', async () => {
        mount('/reception/appointments?appointmentId=55');
        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).getByText('APT-RC-55')).toBeInTheDocument();
        expect(within(dialog).getByText(/Chi tiết lịch hẹn/)).toBeInTheDocument();
        expect(axiosClient.get).toHaveBeenCalledWith('/reception/appointments/55');

        fireEvent.click(within(dialog).getByRole('button', { name: 'Đóng' }));
        await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/reception\/appointments$/));
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    }, 15_000);

    it('shows a retryable error when the linked appointment cannot be loaded', async () => {
        mount('/reception/appointments?appointmentId=404');
        expect(await screen.findByText('Không mở được lịch hẹn')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Thử lại/ })).toBeInTheDocument();
    }, 15_000);
});
