import type { ReactNode } from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { billingApi } from '../api/billingApi';
import { doctorApi } from '../api/doctorApi';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { AdminUsers } from '../pages/admin/AdminUsers';
import { AdminMedicines } from '../pages/admin/AdminMedicines';
import { AdminBilling } from '../pages/admin/AdminBilling';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import type { DoctorScheduleDayDto, RevenueReportDto } from '../types';
import '../theme/browserCompat';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn() } }));
vi.mock('../api/billingApi', () => ({ billingApi: {
    admin: { getRevenueReport: vi.fn(), getSpecialtyFees: vi.fn(), updateSpecialtyFee: vi.fn() },
    reception: { getTodayKpi: vi.fn(), getInvoices: vi.fn(), getUnbilledVisits: vi.fn() },
} }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getSchedule: vi.fn(), previewLeave: vi.fn(), createLeaveRequest: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => dialog,
    DialogProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

const ok = <T,>(data: T) => ({ success: true, message: '', data });
const get = vi.mocked(axiosClient.get);
const mount = (page: ReactNode) => render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter>{page}</MemoryRouter></CopilotResourceProvider></ConfigProvider>);
const isoDate = (value: Date) => [value.getFullYear(), String(value.getMonth() + 1).padStart(2, '0'), String(value.getDate()).padStart(2, '0')].join('-');

beforeEach(() => { vi.resetAllMocks(); });
afterEach(() => cleanup());

describe('AdminUsers table', () => {
    beforeEach(() => {
        get.mockResolvedValue(ok({ items: [
            { id: 'u1', fullName: 'Dược sĩ Lan', email: 'lan@example.com', phoneNumber: '0901', roles: ['Pharmacist', 'Doctor'], isActive: true, createdAt: '2026-10-01T00:00:00' },
            { id: 'u2', fullName: 'Kỹ thuật viên Hòa', email: 'hoa@example.com', phoneNumber: '0902', roles: ['DiagnosticTechnician'], isActive: false, createdAt: '2026-10-01T00:00:00' },
        ], totalItems: 2 }) as never);
    });

    it('renders every role with the default tag instead of one colour per role', async () => {
        mount(<AdminUsers />);
        const table = await screen.findByRole('table');
        const tags = ['Dược sĩ', 'Bác sĩ', 'Kỹ thuật viên'].map(label => within(table).getByText(label).closest('.ant-tag') as HTMLElement);
        for (const tag of tags) expect(tag.className).not.toMatch(/ant-tag-(green|purple|processing|error|success|blue|red|warning)/);
    });

    it('uses a plain text lock button that still asks for confirmation', async () => {
        mount(<AdminUsers />);
        const row = (await screen.findByText('Dược sĩ Lan')).closest('tr')!;
        const lock = within(row).getByRole('button', { name: 'Khóa' });
        expect(lock).toHaveClass('ant-btn-text');
        expect(lock).not.toHaveClass('ant-btn-dangerous');
        fireEvent.click(lock);
        expect(dialog.showConfirm).toHaveBeenCalledWith('Bạn có chắc chắn muốn khóa tài khoản này?', expect.any(Function));
        const unlockRow = screen.getByText('Kỹ thuật viên Hòa').closest('tr')!;
        expect(within(unlockRow).getByRole('button', { name: 'Mở khóa' })).toHaveClass('ant-btn-text');
    });
});

describe('AdminMedicines table', () => {
    beforeEach(() => {
        get.mockImplementation(async (url: string) => url.includes('medicine-categories') ? ok([]) : ok({ items: [
            { id: 1, code: 'MED-OK', name: 'Thuốc đủ', unit: 'Viên', stockQuantity: 100, reorderLevel: 10, isActive: true, isPrescriptionRequired: false },
            { id: 2, code: 'MED-LOW', name: 'Thuốc thiếu', unit: 'Hộp', stockQuantity: 5, reorderLevel: 10, isActive: true, isPrescriptionRequired: true },
        ], totalItems: 2 }) as never);
    });

    it('merges stock and reorder level into one column and warns only below the minimum', async () => {
        mount(<AdminMedicines />);
        const table = await screen.findByRole('table', { name: 'Danh mục thuốc' });
        expect(within(table).getByRole('columnheader', { name: 'Còn dùng được' })).toBeInTheDocument();
        for (const removed of ['Kê đơn', 'Mức cảnh báo', 'Tồn kho hiện tại']) expect(within(table).queryByRole('columnheader', { name: removed })).not.toBeInTheDocument();
        const okCell = within(within(table).getByText('Thuốc đủ').closest('tr')!).getByText('100').closest('.ant-typography') as HTMLElement;
        expect(okCell).toHaveTextContent('100 · tối thiểu 10');
        expect(okCell).not.toHaveClass('ant-typography-warning');
        const lowCell = within(within(table).getByText('Thuốc thiếu').closest('tr')!).getByText('5').closest('.ant-typography') as HTMLElement;
        expect(lowCell).toHaveTextContent('5 · tối thiểu 10');
        expect(lowCell).toHaveClass('ant-typography-warning');
        expect(within(lowCell).getByTitle('Tồn kho dưới ngưỡng cảnh báo!')).toBeInTheDocument();
    });

    it('shows the prescription tag next to the medicine name', async () => {
        mount(<AdminMedicines />);
        const table = await screen.findByRole('table', { name: 'Danh mục thuốc' });
        const lowName = within(table).getByText('Thuốc thiếu').closest('td') as HTMLElement;
        expect(within(lowName).getByText('Kê đơn').closest('.ant-tag')).toBeInTheDocument();
        expect(within(within(table).getByText('Thuốc đủ').closest('td') as HTMLElement).queryByText('Kê đơn')).not.toBeInTheDocument();
    });
});

describe('DoctorSchedule weekly list', () => {
    const days: DoctorScheduleDayDto[] = [
        { date: '2026-10-19', dayOfWeekName: 'Thứ Hai', shifts: [{
            scheduleId: 1, shiftName: 'Ca sáng', startTime: '08:00:00', endTime: '11:30:00', isActive: true, totalSlots: 2, bookedSlots: 1, availableSlots: 1,
            slots: [
                { slotId: 11, slotDate: '2026-10-19', startTime: '08:00:00', endTime: '08:30:00', isBooked: true, appointmentId: 5, appointmentCode: 'APT-5', patientName: 'Người bệnh A', status: 'Confirmed' },
                { slotId: 12, slotDate: '2026-10-19', startTime: '08:30:00', endTime: '09:00:00', isBooked: false },
            ],
        }] },
        { date: '2026-10-20', dayOfWeekName: 'Thứ Ba', shifts: [] },
    ];

    it('lists each day on one row, shows "Nghỉ" for empty days and the shifts with slots otherwise', async () => {
        vi.mocked(doctorApi.getSchedule).mockResolvedValue(ok(days) as never);
        mount(<DoctorSchedule />);
        const off = await screen.findByTestId('schedule-day-2026-10-20');
        expect(within(off).getByText('Thứ Ba')).toBeInTheDocument();
        expect(within(off).getByText('20/10')).toBeInTheDocument();
        expect(within(off).getByText('Nghỉ')).toBeInTheDocument();
        expect(within(off).queryByText(/Ca /)).not.toBeInTheDocument();
        const work = screen.getByTestId('schedule-day-2026-10-19');
        expect(within(work).getByText('19/10')).toBeInTheDocument();
        expect(within(work).getByText('Ca sáng: 08:00 - 11:30')).toBeInTheDocument();
        expect(within(work).getByText('1/2 khung giờ đã được đặt')).toBeInTheDocument();
        for (const text of ['08:00 - 08:30', 'Đã đặt', 'Người bệnh A', 'APT-5', '08:30 - 09:00', 'Trống']) expect(within(work).getByText(text)).toBeInTheDocument();
        expect(within(work).queryByText('Nghỉ')).not.toBeInTheDocument();
        expect(screen.getAllByRole('listitem')).toHaveLength(2);
        expect(document.querySelectorAll('.ant-card')).toHaveLength(0);
    });

    it('renders the leave button as a normal button', async () => {
        vi.mocked(doctorApi.getSchedule).mockResolvedValue(ok(days) as never);
        mount(<DoctorSchedule />);
        const button = await screen.findByRole('button', { name: 'Đăng ký nghỉ phép' });
        expect(button).not.toHaveClass('ant-btn-dangerous');
        expect(button).not.toHaveClass('ant-btn-primary');
    });
});

describe('PharmacyInventory default view', () => {
    it('opens on the transaction table and the grid cards have no fake pill picture', async () => {
        get.mockImplementation(async (url: string) => ok(url === '/medicines/active'
            ? [{ id: 1, code: 'NO-IMG', name: 'Thuốc không ảnh', unit: 'Viên', stockQuantity: 20, reorderLevel: 5, imageUrl: null, unitPrice: 1000, isPrescriptionRequired: false }]
            : { items: [{ id: 9, medicineId: 1, medicineCode: 'NO-IMG', medicineName: 'Thuốc không ảnh', unit: 'Viên', type: 'StockIn', quantityChange: 20, balanceAfter: 20, actorName: 'Dược sĩ', createdAt: '2026-10-20T08:00:00Z' }], totalItems: 1 }) as never);
        mount(<PharmacyInventory />);
        expect(await screen.findByRole('table')).toBeInTheDocument();
        expect(screen.getByRole('radio', { name: 'Bảng' })).toBeChecked();
        expect(screen.queryByRole('region', { name: 'Lưới thuốc trong kho' })).not.toBeInTheDocument();
        fireEvent.click(screen.getByText('Lưới'));
        const grid = screen.getByRole('region', { name: 'Lưới thuốc trong kho' });
        expect(await within(grid).findByText('Thuốc không ảnh')).toBeInTheDocument();
        expect(within(grid).queryByRole('img')).not.toBeInTheDocument();
        expect(grid.querySelector('svg.lucide-pill')).toBeNull();
    });
});

describe('ReceptionBilling view switch', () => {
    it('switches between invoices and the unbilled queue with a Segmented control and keeps the counts', async () => {
        vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue(ok({ todayUnpaidInvoices: 0, todayPaidInvoices: 0, todayCancelledInvoices: 0, todayRevenue: 0 }) as never);
        vi.mocked(billingApi.reception.getInvoices).mockResolvedValue(ok({ items: [], page: 1, totalItems: 7, totalPages: 1 }) as never);
        vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue(ok({ items: [], page: 1, totalItems: 3, totalPages: 1 }) as never);
        mount(<ReceptionBilling />);
        const invoices = await screen.findByRole('radio', { name: 'Danh sách hóa đơn (7)' });
        const unbilled = await screen.findByRole('radio', { name: 'Hàng đợi chờ lập hóa đơn (3)' });
        expect(invoices.closest('.ant-segmented')).not.toBeNull();
        expect(invoices).toBeChecked();
        expect(screen.getByPlaceholderText('Mã HĐ, mã khám, tên hoặc SĐT bệnh nhân...')).toBeInTheDocument();
        fireEvent.click(unbilled);
        expect(await screen.findByText('Hiện không có lượt khám nào chờ lập hóa đơn.')).toBeInTheDocument();
        expect(unbilled).toBeChecked();
        expect(screen.queryByPlaceholderText('Mã HĐ, mã khám, tên hoặc SĐT bệnh nhân...')).not.toBeInTheDocument();
        fireEvent.click(invoices);
        expect(await screen.findByPlaceholderText('Mã HĐ, mã khám, tên hoặc SĐT bệnh nhân...')).toBeInTheDocument();
        expect(billingApi.reception.getUnbilledVisits).toHaveBeenCalledTimes(1);
    });
});

describe('AdminBilling date range', () => {
    const report: RevenueReportDto = { fromDate: '2026-09-01', toDate: '2026-09-30', totalRevenue: 0, totalSucceededTransactions: 2, dailyBreakdown: [], statusBreakdown: { unpaidCount: 0, unpaidAmount: 0, paidCount: 0, paidAmount: 0, cancelledCount: 0, cancelledAmount: 0 } };

    it('uses native date inputs, loads the last 30 days and requests the edited range', async () => {
        vi.mocked(billingApi.admin.getRevenueReport).mockResolvedValue(ok(report) as never);
        const { container } = mount(<AdminBilling />);
        await screen.findByText('2 lượt');
        const today = new Date(); const from = new Date(today); from.setDate(from.getDate() - 30);
        expect(billingApi.admin.getRevenueReport).toHaveBeenCalledWith(isoDate(from), isoDate(today));
        const fromInput = screen.getByLabelText('Từ ngày');
        const toInput = screen.getByLabelText('Đến ngày');
        expect(fromInput).toHaveAttribute('type', 'date');
        expect(toInput).toHaveAttribute('type', 'date');
        expect(fromInput).toHaveValue(isoDate(from));
        expect(container.querySelector('.ant-picker')).toBeNull();
        fireEvent.change(fromInput, { target: { value: '2026-09-01' } });
        fireEvent.change(toInput, { target: { value: '2026-09-30' } });
        fireEvent.click(screen.getByRole('button', { name: 'Xem báo cáo' }));
        await waitFor(() => expect(billingApi.admin.getRevenueReport).toHaveBeenLastCalledWith('2026-09-01', '2026-09-30'));
    });
});
