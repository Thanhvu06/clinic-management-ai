import type React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { MainLayout } from '../layouts/MainLayout';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { PharmacyDashboard } from '../pages/pharmacy/PharmacyDashboard';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import { AdminDashboard } from '../pages/Dashboards';
import type { DiagnosticOrderDto } from '../types';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({
    orders: vi.fn(), technicianStats: vi.fn(),
    user: { id: 'admin-1', fullName: 'Nguyễn Văn Minh', role: 'Admin' },
}));

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getDashboard: vi.fn() } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrders: mocks.orders, getTechnicianStats: mocks.technicianStats } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getMyFacilities: vi.fn().mockResolvedValue({ success: true, data: [{ id: 1, code: 'CS1', name: 'Cơ sở 1', isActive: true }] }),
} }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }) }));
vi.mock('../pages/reception/MpiPatientSearchModal', () => ({
    MpiPatientSearchModal: ({ isOpen }: { isOpen: boolean }) => isOpen ? <div>Tra cứu MPI đang mở</div> : null,
}));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mocks.user, logout: vi.fn(), isAuthenticated: true }) }));
vi.mock('../components/RoleCopilotPanel', () => ({ RoleCopilotPanel: () => null }));
vi.mock('../components/common/NotificationBell', () => ({ NotificationBell: () => null }));

const ok = <T,>(data: T) => ({ success: true, message: 'Success', data });
const get = vi.mocked(axiosClient.get);
type Routes = Record<string, () => unknown>;

function routeGets(routes: Routes) {
    get.mockImplementation(async (url: string) => {
        const key = Object.keys(routes).find(prefix => url.startsWith(prefix));
        if (!key) return ok({ items: [] });
        return routes[key]();
    });
}

function LocationProbe() {
    return <output data-testid="location">{useLocation().pathname}</output>;
}

function mount(element: React.ReactNode, path = '/page') {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[path]}>
        <LocationProbe />
        <Routes><Route path={path} element={element} /><Route path="*" element={null} /></Routes>
    </MemoryRouter></ConfigProvider>);
}

const strip = (name: string) => screen.getByRole('group', { name });
const cell = (label: string) => screen.getByText(label).parentElement as HTMLElement;
const location = () => screen.getByTestId('location');

beforeEach(() => {
    vi.clearAllMocks();
});
afterEach(cleanup);

describe('Doctor dashboard', () => {
    const dashboard = (currentShift: unknown) => ok({
        currentShift,
        kpis: { totalAppointmentsToday: 12, waitingCount: 3, inConsultationCount: 1, completedCount: 6, noShowCount: 2 },
        nextPatient: null, todayQueue: [], upcomingAppointments: [],
    });

    it('shows four metrics, the case count in the title and the shift as one line', async () => {
        vi.mocked(doctorApi.getDashboard).mockResolvedValue(dashboard('Ca sáng (07:00 - 11:30)') as never);
        mount(<DoctorDashboard />);
        expect(await screen.findByText('Ca sáng 07:00–11:30')).toBeInTheDocument();
        expect(screen.queryByText('· 12 ca hôm nay')).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { level: 1, name: 'Bàn làm việc bác sĩ' })).toBeInTheDocument();
        expect(strip('Số liệu hôm nay').children).toHaveLength(4);
        expect(screen.getByRole('link', { name: /Đang chờ khám/ })).toHaveAttribute('href', '/doctor/queue');
        expect(within(cell('Đang chờ khám')).getByText('3')).toHaveAttribute('data-tone', 'attention');
        for (const [label, value] of [['Đang khám', '1'], ['Đã khám xong', '6'], ['Vắng mặt', '2']]) {
            expect(within(cell(label)).getByText(value)).toBeInTheDocument();
        }
        expect(screen.queryByText('Tổng số ca hôm nay')).not.toBeInTheDocument();
        expect(screen.queryByText(/Ca trực hôm nay/)).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Bệnh nhân tiếp theo' })).toBeInTheDocument();
    });

    it('says there is no shift today when none is assigned', async () => {
        vi.mocked(doctorApi.getDashboard).mockResolvedValue(dashboard('Không có ca trực') as never);
        mount(<DoctorDashboard />);
        expect(await screen.findByText('Hôm nay không có ca trực')).toBeInTheDocument();
    });

    it('retries the dashboard from the inline error', async () => {
        vi.mocked(doctorApi.getDashboard).mockRejectedValueOnce(new Error('offline')).mockResolvedValue(dashboard(null) as never);
        mount(<DoctorDashboard />);
        fireEvent.click(await screen.findByRole('button', { name: /Thử lại/ }));
        expect(await screen.findByText('Hôm nay không có ca trực')).toBeInTheDocument();
        expect(doctorApi.getDashboard).toHaveBeenCalledTimes(2);
    });
});

describe('Reception workspace', () => {
    beforeEach(() => routeGets({
        '/reception/stats': () => ok({ appointmentsToday: 9, pendingAppointmentsToday: 2, confirmedAppointmentsToday: 4, completedAppointmentsToday: 3, unbilledCount: 5 }),
        '/reception/appointments': () => ok({ items: [], totalItems: 0 }),
    }));

    it('shows four metrics and header actions instead of the action cards', async () => {
        mount(<ReceptionWorkspace />, '/reception');
        const pendingCell = () => within(strip('Số liệu tiếp nhận hôm nay')).getByText('Chờ xác nhận').parentElement as HTMLElement;
        await waitFor(() => expect(within(pendingCell()).getByText('2')).toHaveAttribute('data-tone', 'attention'));
        expect(strip('Số liệu tiếp nhận hôm nay').children).toHaveLength(4);
        expect(screen.getByRole('button', { name: 'Thu ngân 5' })).toBeInTheDocument();
        for (const removed of ['Tiếp nhận người bệnh mới', 'Mở tra cứu hồ sơ bệnh nhân', 'Mở thu ngân viện phí', 'Quản lý đăng ký gói khám', 'Tra cứu hồ sơ y bạ (MPI)']) {
            expect(screen.queryByText(removed)).not.toBeInTheDocument();
        }
        expect(screen.getByRole('button', { name: 'Tiếp nhận người bệnh' })).toHaveClass('ant-btn-primary');
    });

    it.each([
        ['Tiếp nhận người bệnh', '/reception/walk-in'],
        ['Thu ngân 5', '/reception/billing'],
        ['Gói khám', '/reception/package-registrations'],
    ])('%s navigates to %s', async (name, route) => {
        mount(<ReceptionWorkspace />, '/reception');
        fireEvent.click(await screen.findByRole('button', { name }));
        expect(location()).toHaveTextContent(route);
    });

    it('opens the patient lookup from the header', async () => {
        mount(<ReceptionWorkspace />, '/reception');
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu hồ sơ' }));
        expect(await screen.findByText('Tra cứu MPI đang mở')).toBeInTheDocument();
    });
});

describe('Pharmacy dashboard', () => {
    const prescription = { id: 7, patientName: 'Trần Thị An', doctorName: 'BS. Lê Minh', itemCount: 3, createdAt: '2026-10-05T07:03:00', status: 'Issued' };

    it('shows three metrics and the five newest pending prescriptions', async () => {
        routeGets({
            '/pharmacy/dashboard': () => ok({ pendingPrescriptionsCount: 4, dispensedTodayCount: 6, lowStockCount: 0, totalActiveMedicines: 80 }),
            '/pharmacy/prescriptions': () => ok({ items: [prescription], totalItems: 1 }),
        });
        mount(<PharmacyDashboard />, '/pharmacy');
        const row = (await screen.findByText('Trần Thị An')).closest('tr')!;
        expect(get).toHaveBeenCalledWith('/pharmacy/prescriptions?page=1&pageSize=5&status=Issued');
        expect(within(row).getByText('05/10/2026 07:03')).toBeInTheDocument();
        expect(within(row).getByText('BS. Lê Minh')).toBeInTheDocument();
        expect(within(row).getByText('3')).toBeInTheDocument();
        expect(strip('Số liệu nhà thuốc').children).toHaveLength(3);
        expect(screen.getByRole('link', { name: /Đơn chờ cấp/ })).toHaveAttribute('href', '/pharmacy/prescriptions');
        expect(screen.getByRole('link', { name: /Thuốc dưới định mức/ })).toHaveAttribute('href', '/pharmacy/inventory');
        expect(within(cell('Thuốc dưới định mức')).getByText('0')).toHaveAttribute('data-tone', 'default');
        expect(screen.queryByText('Nghiệp vụ dược chính')).not.toBeInTheDocument();
        expect(screen.queryByText(/Danh mục thuốc/i)).not.toBeInTheDocument();
        fireEvent.click(within(row).getByRole('button', { name: 'Cấp phát' }));
        expect(location()).toHaveTextContent('/pharmacy/prescriptions');
    });

    it('retries the pending list after an error and shows the empty state', async () => {
        let fail = true;
        routeGets({
            '/pharmacy/dashboard': () => ok({ pendingPrescriptionsCount: 0, dispensedTodayCount: 0, lowStockCount: 0, totalActiveMedicines: 0 }),
            '/pharmacy/prescriptions': () => { if (fail) throw new Error('offline'); return ok({ items: [], totalItems: 0 }); },
        });
        mount(<PharmacyDashboard />, '/pharmacy');
        expect(await screen.findByText('Không thể tải danh sách đơn chờ cấp.')).toBeInTheDocument();
        fail = false;
        fireEvent.click(screen.getByRole('button', { name: /Thử lại/ }));
        expect(await screen.findByText('Không có đơn chờ cấp')).toBeInTheDocument();
    });
});

describe('Technician dashboard', () => {
    const order: DiagnosticOrderDto = {
        id: 1, appointmentId: 1, appointmentCode: 'APT-1', orderCode: 'DX-PENDING', patientId: 1, patientName: 'Bệnh nhân',
        patientPhone: '', patientGender: 'Male', orderingDoctorId: 1, orderingDoctorName: 'Bác sĩ', specialtyName: 'Nội khoa',
        clinicalIndication: '', status: 'InProgress', verificationStatus: 'Pending', orderedAtUtc: '2026-10-15T01:00:00Z', items: [],
    };
    beforeEach(() => {
        mocks.orders.mockResolvedValue(ok({ items: [order, { ...order, id: 2, orderCode: 'DX-RETURNED', verificationStatus: 'Returned' }], totalItems: 2 }));
        mocks.technicianStats.mockResolvedValue(ok({ orderedCount: 1, inProgressCount: 2, completedTodayCount: 3, pendingVerificationCount: 7, returnedCount: 8, overdueCount: 9 }));
    });

    it('shows four metrics and filters the table by verification state', async () => {
        mount(<TechnicianDashboard />, '/diagnostics');
        await screen.findByText('DX-RETURNED');
        expect(strip('Số liệu cận lâm sàng').children).toHaveLength(4);
        expect(within(cell('Chờ thực hiện')).getByText('1')).toHaveAttribute('data-tone', 'attention');
        expect(within(cell('Chờ quá 30 phút')).getByText('9')).toHaveAttribute('data-tone', 'attention');

        const pending = screen.getByRole('button', { name: 'Chờ trưởng khoa duyệt 7' });
        fireEvent.click(pending);
        await waitFor(() => expect(mocks.orders).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'InProgress', page: 1 })));
        expect(await screen.findByText('DX-PENDING')).toBeInTheDocument();
        expect(screen.queryByText('DX-RETURNED')).not.toBeInTheDocument();
        expect(pending).toHaveAttribute('aria-pressed', 'true');

        const returned = screen.getByRole('button', { name: 'Bị trả lại cần sửa 8' });
        fireEvent.click(returned);
        expect(await screen.findByText('DX-RETURNED')).toBeInTheDocument();
        expect(screen.queryByText('DX-PENDING')).not.toBeInTheDocument();
        expect(returned).toHaveAttribute('aria-pressed', 'true');
        expect(pending).toHaveAttribute('aria-pressed', 'false');

        fireEvent.click(returned);
        expect(await screen.findByText('DX-PENDING')).toBeInTheDocument();
        expect(screen.getByText('DX-RETURNED')).toBeInTheDocument();
    });

    it('clears the verification filter when a status metric is chosen', async () => {
        mount(<TechnicianDashboard />, '/diagnostics');
        await screen.findByText('DX-RETURNED');
        fireEvent.click(screen.getByRole('button', { name: 'Bị trả lại cần sửa 8' }));
        await waitFor(() => expect(screen.queryByText('DX-PENDING')).not.toBeInTheDocument());
        fireEvent.click(screen.getByRole('button', { name: 'Lọc: Đang thực hiện' }));
        expect(await screen.findByText('DX-PENDING')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Bị trả lại cần sửa 8' })).toHaveAttribute('aria-pressed', 'false');
    });
});

describe('Admin dashboard', () => {
    const leave = { id: 4, doctorId: 2, doctorName: 'BS. Phạm Hà', startDateTime: '2026-10-20T08:00:00', endDateTime: '2026-10-20T12:00:00', reason: 'Việc riêng', status: 'Pending' };
    const log = { id: 9, userFullName: 'Quản trị viên', action: 'Update', entityName: 'Medicine', description: 'Cập nhật giá thuốc', createdAt: '2026-10-21T09:15:00' };

    it('replaces the banner and category cards with metrics, pending leaves and recent activity', async () => {
        routeGets({
            '/admin/stats': () => ok({ totalAppointmentsToday: 11, totalPatients: 250, totalDoctors: 14, lowStockMedicinesCount: 3 }),
            '/admin/leave-requests': () => ok({ items: [leave], totalItems: 1 }),
            '/admin/audit-logs': () => ok({ items: [log], totalItems: 1 }),
        });
        mount(<AdminDashboard />, '/admin');
        expect(screen.getByRole('heading', { level: 1, name: 'Tổng quan' })).toBeInTheDocument();
        expect(await screen.findByText('BS. Phạm Hà')).toBeInTheDocument();
        expect(get).toHaveBeenCalledWith('/admin/leave-requests?page=1&pageSize=5&status=Pending');
        expect(get).toHaveBeenCalledWith('/admin/audit-logs?page=1&pageSize=8');
        await waitFor(() => expect(within(cell('Thuốc sắp hết hàng')).getByText('3')).toHaveAttribute('data-tone', 'attention'));
        expect(strip('Số liệu hệ thống').children).toHaveLength(4);
        expect(screen.getByRole('link', { name: /Thuốc sắp hết hàng/ })).toHaveAttribute('href', '/admin/medicines');
        for (const [label, value] of [['Lịch hôm nay', '11'], ['Bệnh nhân', '250'], ['Bác sĩ đang công tác', '14']]) {
            expect(within(cell(label)).getByText(value)).toBeInTheDocument();
        }
        for (const removed of ['Quản lý vận hành hệ thống', 'Quản lý dữ liệu & Danh mục', 'Nhật ký hệ thống', 'Lịch làm việc & Slots']) {
            expect(screen.queryByText(removed)).not.toBeInTheDocument();
        }
        const logRow = screen.getByText('Cập nhật giá thuốc').closest('tr')!;
        expect(within(logRow).getByText('09:15 21/10/2026')).toBeInTheDocument();
        expect(within(logRow).getByText('Quản trị viên')).toBeInTheDocument();
        fireEvent.click(within(screen.getByText('BS. Phạm Hà').closest('tr')!).getByRole('button', { name: 'Duyệt' }));
        expect(location()).toHaveTextContent('/admin/leaves');
    });

    it('shows each block error with retry and the empty states', async () => {
        let fail = true;
        routeGets({
            '/admin/stats': () => ok({ totalAppointmentsToday: 0, totalPatients: 0, totalDoctors: 0, lowStockMedicinesCount: 0 }),
            '/admin/leave-requests': () => { if (fail) throw new Error('offline'); return ok({ items: [], totalItems: 0 }); },
            '/admin/audit-logs': () => { if (fail) throw new Error('offline'); return ok({ items: [], totalItems: 0 }); },
        });
        mount(<AdminDashboard />, '/admin');
        expect(await screen.findByText('Không thể tải đơn nghỉ phép chờ duyệt.')).toBeInTheDocument();
        expect(screen.getByText('Không thể tải hoạt động gần đây.')).toBeInTheDocument();
        fail = false;
        for (const button of screen.getAllByRole('button', { name: /Thử lại/ })) fireEvent.click(button);
        expect(await screen.findByText('Không có đơn nghỉ phép chờ duyệt')).toBeInTheDocument();
        expect(await screen.findByText('Chưa có hoạt động nào')).toBeInTheDocument();
    });

    it('retries the overview metrics after a failure', async () => {
        let fail = true;
        routeGets({
            '/admin/stats': () => { if (fail) throw new Error('offline'); return ok({ totalAppointmentsToday: 5, totalPatients: 1, totalDoctors: 1, lowStockMedicinesCount: 0 }); },
        });
        mount(<AdminDashboard />, '/admin');
        expect(await screen.findByText('Không thể tải số liệu tổng quan.')).toBeInTheDocument();
        expect(within(cell('Lịch hôm nay')).getByText('—')).toBeInTheDocument();
        fail = false;
        fireEvent.click(screen.getByRole('button', { name: /Thử lại/ }));
        expect(await within(cell('Lịch hôm nay')).findByText('5')).toBeInTheDocument();
    });
});

describe('Admin menu', () => {
    it('labels the work schedule entry in Vietnamese and keeps its route', () => {
        render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={['/admin']}><Routes>
            <Route element={<MainLayout />}><Route path="/admin" element={<div>Nội dung</div>} /></Route>
        </Routes></MemoryRouter></ConfigProvider>);
        expect(screen.queryByText('Lịch trực & Slots')).not.toBeInTheDocument();
        expect(screen.getByRole('link', { name: /Lịch trực & khung giờ/ })).toHaveAttribute('href', '/admin/work-schedules');
    });
});
