import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import { AdminFacilities } from '../pages/admin/AdminFacilities';
import { AdminBilling } from '../pages/admin/AdminBilling';
import { organizationApi } from '../api/organizationApi';
import { billingApi } from '../api/billingApi';
import { diagnosticApi } from '../api/diagnosticApi';
import axiosClient from '../api/axiosClient';
import type { ApiResponse, RevenueReportDto } from '../types';
import type { FacilityDto, DepartmentDto, RoomDto, BedDto, StaffFacilityAssignmentDto } from '../api/organizationApi';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn() }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => dialog }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn(), getBedsByRoom: vi.fn(),
    getStaffAssignments: vi.fn(), createFacility: vi.fn(), createDepartment: vi.fn(),
    createRoom: vi.fn(), createBed: vi.fn(), createStaffAssignment: vi.fn(), deleteStaffAssignment: vi.fn(),
} }));
vi.mock('../api/billingApi', () => ({ billingApi: { admin: {
    getRevenueReport: vi.fn(), getSpecialtyFees: vi.fn(), updateSpecialtyFee: vi.fn(),
} } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getDiagnosticPricing: vi.fn(), updateDiagnosticPrice: vi.fn() } }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

const ok = <T,>(data: T): ApiResponse<T> => ({ success: true, message: '', data });
const facility: FacilityDto = { id: 1, code: 'FAC-A', name: 'Cơ sở A', address: 'Địa chỉ A', city: 'TP.HCM', phone: '0281234567', isActive: true, buildingCount: 1, departmentCount: 1 };
const department: DepartmentDto = { id: 10, facilityId: 1, facilityName: facility.name, code: 'K-NOI', name: 'Khoa Nội', departmentType: 1, departmentTypeName: 'Clinical', isActive: true, roomCount: 1 };
const room: RoomDto = { id: 100, departmentId: 10, departmentName: department.name, facilityId: 1, facilityName: facility.name, roomNumber: 'P101', name: 'Phòng A', roomType: 2, roomTypeName: 'Inpatient', floorNumber: 1, maxCapacity: 4, isActive: true, bedCount: 1, availableBedCount: 0 };
const bed: BedDto = { id: 1000, roomId: 100, roomNumber: room.roomNumber, departmentId: 10, departmentName: department.name, bedNumber: 'G01', bedType: 3, bedTypeName: 'Icu', status: 2, statusName: 'Occupied', dailyRate: 500000, isActive: true };
const staff: StaffFacilityAssignmentDto = { id: 5, userId: 'staff-1', userName: 'bsminh', fullName: 'Nguyễn Minh', email: 'minh@example.com', phoneNumber: '0901234567', facilityId: 1, facilityName: facility.name, departmentId: 10, departmentName: department.name, role: 'Doctor', isPrimary: true, isActive: true, assignedAtUtc: '2026-10-10T00:00:00Z' };
const report: RevenueReportDto = { fromDate: '2026-09-11', toDate: '2026-10-11', totalRevenue: 500000, totalSucceededTransactions: 45, dailyBreakdown: [{ date: '2026-10-11', revenue: 500000, succeededPaymentsCount: 1, paidInvoicesCount: 1 }], statusBreakdown: { unpaidCount: 4, unpaidAmount: 2000000, paidCount: 1, paidAmount: 500000, cancelledCount: 2, cancelledAmount: 500000 } };
function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>(done => { resolve = done; });
    return { promise, resolve };
}
const showFacilities = () => render(<ConfigProvider theme={{ token: { motion: false } }}><AdminFacilities /></ConfigProvider>);
const showBilling = () => render(<ConfigProvider theme={{ token: { motion: false } }}><AdminBilling /></ConfigProvider>);
async function openAssignment() {
    fireEvent.click(await screen.findByRole('tab', { name: /Phân công nhân sự/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Phân công nhân viên mới' }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Nhân viên *' })).toBeEnabled());
}
function fillFacility() {
    for (const [placeholder, value] of [
        ['VD: FAC-CS04', 'FAC-NEW'], ['VD: Bệnh viện Đa khoa ClinicCare CS4', 'Cơ sở mới'],
        ['TP. Hồ Chí Minh', 'TP.HCM'], ['028 1234 5678', '0281234567'], ['Số 100 Đường ABC, Phường X, Quận Y', '123 Đường A'],
    ]) fireEvent.change(screen.getByPlaceholderText(placeholder), { target: { value } });
}
async function changeDate(name: string, value: string) {
    const input = screen.getByRole('textbox', { name });
    fireEvent.change(input, { target: { value } });
    fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' });
    fireEvent.blur(input);
    await waitFor(() => expect(input).toHaveValue(value));
}
beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(organizationApi.getFacilities).mockResolvedValue(ok([facility]));
    vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([department]));
    vi.mocked(organizationApi.getRooms).mockResolvedValue(ok([room]));
    vi.mocked(organizationApi.getBedsByRoom).mockResolvedValue(ok([bed]));
    vi.mocked(organizationApi.getStaffAssignments).mockResolvedValue(ok([staff]));
    vi.mocked(organizationApi.createStaffAssignment).mockResolvedValue(ok(staff));
    vi.mocked(axiosClient.get).mockResolvedValue(ok({ items: [
        { id: 'staff-1', fullName: 'Nguyễn Minh', email: 'minh@example.com', roles: ['Doctor', 'Patient'] },
        { id: 'patient-1', fullName: 'Chỉ bệnh nhân', email: 'patient@example.com', roles: ['Patient'] },
    ] }));
    vi.mocked(billingApi.admin.getRevenueReport).mockResolvedValue(ok(report));
    vi.mocked(billingApi.admin.getSpecialtyFees).mockResolvedValue(ok([]));
    vi.mocked(diagnosticApi.getDiagnosticPricing).mockResolvedValue(ok([]));
});

describe('Admin Facilities batch 3', () => {
    it('shows a backend payload message in the correct alert argument order', async () => {
        vi.mocked(organizationApi.createFacility).mockRejectedValue({ message: 'Mã cơ sở đã tồn tại.' });
        showFacilities();
        fireEvent.click(screen.getByRole('button', { name: 'Thêm Cơ sở Mới' }));
        fillFacility(); fireEvent.click(screen.getByRole('button', { name: 'Lưu Cơ sở' }));
        await waitFor(() => expect(dialog.showAlert).toHaveBeenCalledWith('Mã cơ sở đã tồn tại.', 'Lỗi', 'error'));
    });
    it('renders backend full name, email, role and department in staff columns', async () => {
        showFacilities();
        fireEvent.click(await screen.findByRole('tab', { name: /Phân công nhân sự/ }));
        const table = await screen.findByRole('table');
        for (const value of [staff.fullName, staff.email, staff.role, staff.departmentName!]) expect(within(table).getByText(value)).toBeInTheDocument();
        expect(within(table).getByText('ID: staff-1')).toBeInTheDocument();
    });
    it('selects staff, excludes Patient-only users, and submits no assignment notes', async () => {
        showFacilities(); await openAssignment();
        expect(axiosClient.get).toHaveBeenCalledWith('/admin/users?isActive=true&pageSize=100');
        expect(screen.queryByPlaceholderText('Nhập User GUID của nhân viên...')).not.toBeInTheDocument();
        expect(screen.queryByLabelText('Ghi chú phân công')).not.toBeInTheDocument();
        await userEvent.click(screen.getByRole('combobox', { name: 'Nhân viên *' }));
        expect(screen.queryByText('Chỉ bệnh nhân (patient@example.com)')).not.toBeInTheDocument();
        await userEvent.click((await screen.findAllByText('Nguyễn Minh (minh@example.com)')).at(-1)!);
        fireEvent.click(screen.getByRole('button', { name: 'Lưu Phân công' }));
        await waitFor(() => expect(organizationApi.createStaffAssignment).toHaveBeenCalledWith({ userId: 'staff-1', facilityId: 1, role: 'Receptionist', departmentId: undefined, isPrimary: true }));
    });
    it('submits a pending facility only once and blocks cancel and escape', async () => {
        const pending = deferred<ApiResponse<FacilityDto>>();
        vi.mocked(organizationApi.createFacility).mockReturnValue(pending.promise);
        showFacilities(); fireEvent.click(screen.getByRole('button', { name: 'Thêm Cơ sở Mới' })); fillFacility();
        const button = screen.getByRole('button', { name: 'Lưu Cơ sở' });
        fireEvent.click(button); fireEvent.click(button);
        expect(organizationApi.createFacility).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('button', { name: 'Đang lưu...' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Hủy' })).toBeDisabled();
        fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' });
        expect(screen.getByRole('dialog')).toBeInTheDocument();
        await act(async () => pending.resolve(ok(facility)));
    });
    it('ignores late structure and staff responses from the previously selected facility', async () => {
        const pendingRooms = deferred<ApiResponse<RoomDto[]>>();
        const pendingDepts = deferred<ApiResponse<DepartmentDto[]>>();
        const pendingStaff = deferred<ApiResponse<StaffFacilityAssignmentDto[]>>();
        vi.mocked(organizationApi.getFacilities).mockResolvedValue(ok([facility, { ...facility, id: 2, name: 'Cơ sở B', code: 'FAC-B' }]));
        vi.mocked(organizationApi.getRooms).mockImplementation(params => params?.facilityId === 1 ? pendingRooms.promise : Promise.resolve(ok([{ ...room, id: 200, facilityId: 2, name: 'Phòng B' }])));
        vi.mocked(organizationApi.getDepartments).mockImplementation(id => id === 1 ? pendingDepts.promise : Promise.resolve(ok([{ ...department, id: 20, facilityId: 2 }])));
        vi.mocked(organizationApi.getStaffAssignments).mockImplementation(id => id === 1 ? pendingStaff.promise : Promise.resolve(ok([{ ...staff, id: 6, fullName: 'Nhân viên B' }])));
        showFacilities(); fireEvent.click(await screen.findByRole('button', { name: /Cơ sở B/ }));
        expect(await screen.findByText('P101 - Phòng B')).toBeInTheDocument();
        await act(async () => { pendingRooms.resolve(ok([room])); pendingDepts.resolve(ok([department])); pendingStaff.resolve(ok([staff])); });
        expect(screen.queryByText('P101 - Phòng A')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('tab', { name: /Phân công nhân sự/ }));
        expect(await screen.findByText('Nhân viên B')).toBeInTheDocument();
        expect(screen.queryByText(staff.fullName)).not.toBeInTheDocument();
    });
    it('keeps structure usable when staff loading throws and shows an inline retry', async () => {
        vi.mocked(organizationApi.getStaffAssignments).mockRejectedValue(new Error('Không thể tải nhân sự.'));
        showFacilities(); expect(await screen.findByText('P101 - Phòng A')).toBeInTheDocument();
        expect(dialog.showAlert).not.toHaveBeenCalled();
        fireEvent.click(screen.getByRole('tab', { name: /Phân công nhân sự/ }));
        expect(await screen.findByText('Không thể tải nhân sự.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });
    it('localizes occupied ICU beds by numeric enums', async () => {
        showFacilities(); fireEvent.click(await screen.findByRole('button', { name: /P101 - Phòng A/ }));
        expect(await screen.findByText('Đang sử dụng')).toBeInTheDocument();
        expect(screen.getByText('Giường hồi sức (ICU)')).toBeInTheDocument();
        expect(screen.queryByText('Occupied')).not.toBeInTheDocument();
    });
    it('clears old beds immediately and ignores a late response from the old room', async () => {
        const pending = deferred<ApiResponse<BedDto[]>>();
        vi.mocked(organizationApi.getRooms).mockResolvedValue(ok([room, { ...room, id: 200, roomNumber: 'P202', name: 'Phòng B' }]));
        vi.mocked(organizationApi.getBedsByRoom).mockImplementation(id => id === 100 ? pending.promise : Promise.resolve(ok([{ ...bed, id: 2000, roomId: 200, bedNumber: 'G-B' }])));
        showFacilities(); fireEvent.click(await screen.findByRole('button', { name: /P101 - Phòng A/ }));
        expect(screen.queryByText('G01')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: /P202 - Phòng B/ }));
        expect(await screen.findByText('G-B')).toBeInTheDocument();
        await act(async () => pending.resolve(ok([bed])));
        expect(screen.queryByText('G01')).not.toBeInTheDocument();
        expect(screen.getByText('G-B')).toBeInTheDocument();
    });
    it('disables assignment save when the user catalogue fails', async () => {
        vi.mocked(axiosClient.get).mockRejectedValue({ message: 'Không thể tải tài khoản.' });
        showFacilities(); fireEvent.click(await screen.findByRole('tab', { name: /Phân công nhân sự/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Phân công nhân viên mới' }));
        expect(await screen.findByText('Không thể tải tài khoản.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Lưu Phân công' })).toBeDisabled();
    });
});

describe('Admin Billing batch 3', () => {
    it('passes a newly computed 30-day range after editing the date fields', async () => {
        showBilling(); await screen.findByText('45 lượt');
        await changeDate('Từ ngày', '2020-01-01'); await changeDate('Đến ngày', '2020-02-01');
        vi.mocked(billingApi.admin.getRevenueReport).mockClear();
        fireEvent.click(screen.getByRole('button', { name: '30 ngày gần nhất' }));
        const today = new Date(); const from = new Date(today); from.setDate(from.getDate() - 30);
        const date = (value: Date) => [value.getFullYear(), String(value.getMonth() + 1).padStart(2, '0'), String(value.getDate()).padStart(2, '0')].join('-');
        await waitFor(() => expect(billingApi.admin.getRevenueReport).toHaveBeenCalledWith(date(from), date(today)));
        expect(screen.getByRole('textbox', { name: 'Từ ngày' })).toHaveValue(date(from));
    });
    it('rejects an inverted date range without requesting a report', async () => {
        showBilling(); await screen.findByText('45 lượt');
        await changeDate('Từ ngày', '2027-01-01'); await changeDate('Đến ngày', '2026-01-01');
        vi.mocked(billingApi.admin.getRevenueReport).mockClear();
        fireEvent.click(screen.getByRole('button', { name: 'Xem báo cáo' }));
        expect(await screen.findByText('Từ ngày phải trước hoặc bằng đến ngày.')).toBeInTheDocument();
        expect(billingApi.admin.getRevenueReport).not.toHaveBeenCalled();
    });
    it('renders DateOnly 2026-10-11 as 11/10/2026', async () => {
        showBilling(); expect(await screen.findByText('11/10/2026')).toBeInTheDocument();
    });
    it('replaces a failing specialty fee table with InlineError and a retry', async () => {
        vi.mocked(billingApi.admin.getSpecialtyFees).mockRejectedValue({ message: 'Không thể tải biểu phí.' });
        showBilling(); await screen.findByText('45 lượt');
        fireEvent.click(screen.getByRole('tab', { name: 'Phí khám chuyên khoa' }));
        expect(await screen.findByText('Không thể tải biểu phí.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
        expect(dialog.showAlert).not.toHaveBeenCalled();
    });
    it('loads diagnostic pricing only when its tab is opened', async () => {
        showBilling(); await screen.findByText('45 lượt');
        expect(diagnosticApi.getDiagnosticPricing).not.toHaveBeenCalled();
        expect(billingApi.admin.getSpecialtyFees).not.toHaveBeenCalled();
        fireEvent.click(screen.getByRole('tab', { name: 'Bảng giá Cận lâm sàng' }));
        await waitFor(() => expect(diagnosticApi.getDiagnosticPricing).toHaveBeenCalledTimes(1));
        expect(await screen.findByText('Chưa có dịch vụ cận lâm sàng nào trong hệ thống.')).toBeInTheDocument();
    });
    it('shows the specialty fee empty state', async () => {
        showBilling(); await screen.findByText('45 lượt');
        fireEvent.click(screen.getByRole('tab', { name: 'Phí khám chuyên khoa' }));
        expect(await screen.findByText('Chưa có chuyên khoa nào.')).toBeInTheDocument();
        expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });
});
