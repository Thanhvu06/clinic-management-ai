import type React from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MainLayout } from '../layouts/MainLayout';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { DoctorExaminationPage } from '../pages/doctor/DoctorExaminationPage';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { PharmacyDashboard } from '../pages/pharmacy/PharmacyDashboard';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { AdminFacilities } from '../pages/admin/AdminFacilities';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({
    user: { id: 'staff-1', fullName: 'Nguyễn Văn Minh', role: 'Doctor' },
    order: {
        id: 1, appointmentId: 10, appointmentCode: 'APT-1', orderCode: 'DX-1', patientId: 2, patientName: 'Trần Thị An',
        patientPhone: '0901234567', orderingDoctorId: 5, orderingDoctorName: 'BS. An', specialtyName: 'Nội khoa',
        clinicalIndication: 'Kiểm tra', status: 'InProgress', verificationStatus: 'NotRequired', orderedAtUtc: '2026-10-16T01:00:00Z',
        rowVersion: 'v1', items: [],
    },
}));

vi.mock('../api/axiosClient', () => {
    const pending = () => new Promise(() => {});
    const get = (url: string) => url === '/diagnostics/orders/1'
        ? Promise.resolve({ success: true, message: 'Success', data: mocks.order })
        : pending();
    return { default: { get, post: pending, put: pending, patch: pending, delete: pending } };
});
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mocks.user, logout: vi.fn(), isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showToast: vi.fn(), showAlert: vi.fn(), showConfirm: vi.fn() }) }));
vi.mock('../components/RoleCopilotPanel', () => ({ RoleCopilotPanel: () => null }));
vi.mock('../components/common/NotificationBell', () => ({ NotificationBell: () => <span>Thông báo</span> }));

afterEach(cleanup);

function mount(role: string, path: string, routePath: string, element: React.ReactNode) {
    mocks.user = { ...mocks.user, role };
    return render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[path]}><Routes>
        <Route element={<MainLayout />}><Route path={routePath} element={element} /></Route>
    </Routes></MemoryRouter></ConfigProvider>);
}

const header = (container: HTMLElement) => container.querySelector('header.ant-layout-header') as HTMLElement;

describe('MainLayout shell cleanup', () => {
    it('drops the uppercase nav section label and uses sentence-case tagline', () => {
        mount('Doctor', '/doctor', '/doctor', <div>Nội dung</div>);
        expect(screen.queryByText('CHỨC NĂNG CHÍNH')).not.toBeInTheDocument();
        expect(screen.queryByText('Hệ Thống Y Tế Thông Minh')).not.toBeInTheDocument();
        expect(screen.getByText('Hệ thống y tế thông minh')).toBeInTheDocument();
    });

    it('keeps only date, role, bell and avatar in the header without a page title', () => {
        const { container } = mount('Receptionist', '/reception/billing', '/reception/billing', <div>Nội dung</div>);
        const bar = header(container);
        expect(bar).toHaveTextContent('Thông báo');
        expect(bar).toHaveTextContent('Lễ tân');
        expect(bar).toHaveTextContent('NM');
        expect(bar).toHaveTextContent(/\d{2}\/\d{2}\/\d{4}/);
        expect(bar).not.toHaveTextContent('Quản lý Hóa đơn & Thu ngân');
        expect(bar.querySelector('h1')).toBeNull();
    });

    it.each([
        { role: 'Doctor', path: '/doctor', routePath: '/doctor', element: <DoctorDashboard />, title: 'Bàn làm việc bác sĩ' },
        { role: 'Doctor', path: '/doctor/appointments/5/examination', routePath: '/doctor/appointments/:id/examination', element: <DoctorExaminationPage />, title: 'Bàn khám lâm sàng' },
        { role: 'Doctor', path: '/doctor/visits/7/examination', routePath: '/doctor/visits/:visitId/examination', element: <DoctorExaminationPage />, title: 'Bàn khám lâm sàng' },
        { role: 'Receptionist', path: '/reception', routePath: '/reception', element: <ReceptionWorkspace />, title: 'Bàn làm việc lễ tân' },
        { role: 'Pharmacist', path: '/pharmacy', routePath: '/pharmacy', element: <PharmacyDashboard />, title: 'Bàn làm việc Dược sĩ' },
        { role: 'DiagnosticTechnician', path: '/diagnostics', routePath: '/diagnostics', element: <TechnicianDashboard />, title: 'Bàn làm việc cận lâm sàng' },
        { role: 'DiagnosticTechnician', path: '/diagnostics/orders/1', routePath: '/diagnostics/orders/:id', element: <TechnicianOrderDetail />, title: 'Thực hiện phiếu chỉ định CLS' },
        { role: 'Admin', path: '/admin/facilities', routePath: '/admin/facilities', element: <AdminFacilities />, title: 'Mạng lưới cơ sở' },
    ])('renders exactly one h1 on $path ($role)', async ({ role, path, routePath, element, title }) => {
        const { container } = mount(role, path, routePath, element);
        expect(await screen.findByRole('heading', { level: 1, name: title })).toBeInTheDocument();
        expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
        expect(header(container)).not.toHaveTextContent(title);
    });
});
