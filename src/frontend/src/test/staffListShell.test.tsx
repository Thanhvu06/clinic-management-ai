import type React from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { Button, ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MainLayout } from '../layouts/MainLayout';
import { EmptyState } from '../components/common/EmptyState';
import { FilterBar } from '../components/common/FilterBar';
import { DoctorQueue } from '../pages/doctor/DoctorQueue';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { DoctorAppointments } from '../pages/doctor/DoctorAppointments';
import { DoctorLeaveRequests } from '../pages/doctor/DoctorLeaveRequests';
import { AdminBilling } from '../pages/admin/AdminBilling';
import { AdminWorkSchedules } from '../pages/admin/AdminWorkSchedules';
import { AdminLeaves } from '../pages/admin/AdminLeaves';
import { AdminAuditLogs } from '../pages/admin/AdminAuditLogs';
import { AdminHealthPackages } from '../pages/admin/AdminHealthPackages';
import { AdminMedicines } from '../pages/admin/AdminMedicines';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';
import { PharmacyPrescriptions } from '../pages/pharmacy/PharmacyPrescriptions';
import { PharmacyDashboard } from '../pages/pharmacy/PharmacyDashboard';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { ReceptionChangeRequests } from '../pages/reception/ReceptionChangeRequests';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { ReceptionPackageRegistrations } from '../pages/reception/ReceptionPackageRegistrations';
import { NotificationsPage } from '../pages/common/NotificationsPage';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ user: { id: 'staff-1', fullName: 'Nguyễn Văn Minh', role: 'Doctor' } }));

vi.mock('../api/axiosClient', () => {
    const pending = () => new Promise(() => {});
    return { default: { get: pending, post: pending, put: pending, patch: pending, delete: pending } };
});
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mocks.user, logout: vi.fn(), isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => ({ showToast: vi.fn(), showAlert: vi.fn(), showConfirm: vi.fn() }),
    DialogProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));
vi.mock('../components/RoleCopilotPanel', () => ({ RoleCopilotPanel: () => null }));
vi.mock('../components/common/NotificationBell', () => ({ NotificationBell: () => null }));

afterEach(cleanup);

function mount(role: string, path: string, element: React.ReactNode) {
    mocks.user = { ...mocks.user, role };
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}><Routes>
        <Route element={<MainLayout />}><Route path={path} element={element} /></Route>
    </Routes></MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}

describe('EmptyState', () => {
    it('renders a compact text block without the large illustration, even when an icon is passed', () => {
        const { container } = render(<EmptyState icon={<svg data-testid="legacy-icon" />} title="Không có dữ liệu" description="Thử bộ lọc khác" />);
        expect(screen.getByText('Không có dữ liệu')).toBeInTheDocument();
        expect(screen.getByText('Thử bộ lọc khác')).toBeInTheDocument();
        expect(screen.queryByTestId('legacy-icon')).not.toBeInTheDocument();
        expect(container.querySelector('svg, img, .ant-empty-image')).toBeNull();
        expect(screen.queryByRole('heading')).not.toBeInTheDocument();
    });

    it('renders an optional action button', () => {
        const onClick = vi.fn();
        render(<EmptyState title="Chưa có lô thuốc" action={<Button onClick={onClick}>Nhập lô mới</Button>} />);
        fireEvent.click(screen.getByRole('button', { name: 'Nhập lô mới' }));
        expect(onClick).toHaveBeenCalledTimes(1);
    });
});

describe('FilterBar', () => {
    it('is a plain row without a card and puts a magnifier at the start of the search box', () => {
        const onSearchChange = vi.fn();
        const { container } = render(<FilterBar searchTerm="" onSearchChange={onSearchChange} searchPlaceholder="Tìm theo tên"><span>Bộ lọc khác</span></FilterBar>);
        const bar = screen.getByTestId('filter-bar');
        expect(container.querySelector('.ant-card')).toBeNull();
        expect(bar.querySelector('.ant-input-prefix svg')).not.toBeNull();
        expect(screen.getByText('Bộ lọc khác')).toBeInTheDocument();
        fireEvent.change(screen.getByPlaceholderText('Tìm theo tên'), { target: { value: 'Minh' } });
        expect(onSearchChange).toHaveBeenCalledWith('Minh');
    });
});

describe('Page titles match the sidebar label', () => {
    it.each([
        { role: 'Doctor', path: '/doctor/queue', element: <DoctorQueue />, title: 'Hàng đợi khám' },
        { role: 'Doctor', path: '/doctor/schedule', element: <DoctorSchedule />, title: 'Lịch trực & ca khám' },
        { role: 'Doctor', path: '/doctor/appointments', element: <DoctorAppointments />, title: 'Danh sách lịch khám' },
        { role: 'Doctor', path: '/doctor/leave-requests', element: <DoctorLeaveRequests />, title: 'Yêu cầu nghỉ phép' },
        { role: 'Admin', path: '/admin/billing', element: <AdminBilling />, title: 'Doanh thu & biểu phí' },
        { role: 'Admin', path: '/admin/work-schedules', element: <AdminWorkSchedules />, title: 'Lịch trực & khung giờ' },
        { role: 'Admin', path: '/admin/leaves', element: <AdminLeaves />, title: 'Duyệt nghỉ phép' },
        { role: 'Admin', path: '/admin/audit-logs', element: <AdminAuditLogs />, title: 'Nhật ký hệ thống' },
        { role: 'Admin', path: '/admin/packages', element: <AdminHealthPackages />, title: 'Gói khám sức khỏe' },
        { role: 'Admin', path: '/admin/medicines', element: <AdminMedicines />, title: 'Danh mục thuốc' },
        { role: 'Pharmacist', path: '/pharmacy/medicines', element: <AdminMedicines />, title: 'Danh mục thuốc' },
        { role: 'Pharmacist', path: '/pharmacy/inventory', element: <PharmacyInventory />, title: 'Lịch sử kho' },
        { role: 'Pharmacist', path: '/pharmacy/prescriptions', element: <PharmacyPrescriptions />, title: 'Đơn thuốc chờ cấp' },
        { role: 'Receptionist', path: '/reception/walk-in', element: <WalkInPatientRegistration />, title: 'Tiếp nhận người bệnh' },
        { role: 'Receptionist', path: '/reception/billing', element: <ReceptionBilling />, title: 'Thu ngân & hóa đơn' },
        { role: 'Receptionist', path: '/reception/change-requests', element: <ReceptionChangeRequests />, title: 'Yêu cầu đổi/hủy lịch' },
    ])('$path h1 equals the active menu item "$title"', async ({ role, path, element, title }) => {
        const { container } = mount(role, path, element);
        const heading = await screen.findByRole('heading', { level: 1 });
        expect(heading).toHaveTextContent(title);
        expect(heading.textContent?.trim()).toBe(title);
        expect(container.querySelector('.ant-menu-item-selected')).toHaveTextContent(title);
    });

    it('shows the renamed reception and admin menu entries', () => {
        mount('Receptionist', '/reception/x', <div />);
        for (const label of ['Tiếp nhận người bệnh', 'Thu ngân & hóa đơn', 'Yêu cầu đổi/hủy lịch']) {
            expect(screen.getByRole('link', { name: label })).toBeInTheDocument();
        }
        expect(screen.queryByText('Tiếp nhận bệnh nhân')).not.toBeInTheDocument();
        expect(screen.queryByText('Thu ngân & Hóa đơn')).not.toBeInTheDocument();
        cleanup();
        mount('Admin', '/admin/x', <div />);
        expect(screen.getByRole('link', { name: 'Doanh thu & biểu phí' })).toBeInTheDocument();
        expect(screen.queryByText('Doanh thu & Biểu phí')).not.toBeInTheDocument();
    });

    it('titles the pharmacist desk in sentence case and the notification page without subtitle', async () => {
        mount('Pharmacist', '/pharmacy', <PharmacyDashboard />);
        expect(await screen.findByRole('heading', { level: 1, name: 'Bàn làm việc dược sĩ' })).toBeInTheDocument();
        cleanup();
        mount('Receptionist', '/notifications', <NotificationsPage />);
        const heading = await screen.findByRole('heading', { level: 1, name: 'Thông báo' });
        expect(heading.querySelector('svg')).toBeNull();
        expect(screen.queryByText('Trung tâm thông báo')).not.toBeInTheDocument();
        expect(screen.queryByText(/Quản lý toàn bộ thông báo/)).not.toBeInTheDocument();
    });

    it.each([
        { path: '/reception/appointments', element: <ReceptionAppointments />, title: 'Quản lý lịch hẹn' },
        { path: '/reception/billing', element: <ReceptionBilling />, title: 'Thu ngân & hóa đơn' },
        { path: '/reception/package-registrations', element: <ReceptionPackageRegistrations />, title: 'Đăng ký gói khám' },
        { path: '/reception/change-requests', element: <ReceptionChangeRequests />, title: 'Yêu cầu đổi/hủy lịch' },
    ])('$path has no icon beside the title', async ({ path, element, title }) => {
        mount('Receptionist', path, element);
        const heading = await screen.findByRole('heading', { level: 1, name: title });
        expect(heading.parentElement?.querySelector('svg')).toBeNull();
        expect(screen.queryByText('Bộ lọc:')).not.toBeInTheDocument();
    });
});
