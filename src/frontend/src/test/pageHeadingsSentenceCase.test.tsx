import type React from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { TechnicianDashboard } from '../pages/technician/TechnicianDashboard';
import { TechnicianVerificationQueue } from '../pages/technician/TechnicianVerificationQueue';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';
import { ReceptionPackageRegistrations } from '../pages/reception/ReceptionPackageRegistrations';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { AdminFacilities } from '../pages/admin/AdminFacilities';
import { AdminBilling } from '../pages/admin/AdminBilling';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import { DoctorQueue } from '../pages/doctor/DoctorQueue';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { PharmacyDashboard } from '../pages/pharmacy/PharmacyDashboard';
import { PatientPackageRegistrations } from '../pages/patient/PatientPackageRegistrations';
import { PackageRegistration } from '../pages/patient/PackageRegistration';
import { PatientDiagnosticResults } from '../pages/patient/PatientDiagnosticResults';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({
    pkg: { id: 3, code: 'PKG-3', name: 'Gói tổng quát', targetAudience: 'Người lớn', description: 'Khám tổng quát', price: 1500000, includedServices: ['Xét nghiệm máu'], isActive: true },
}));

vi.mock('../api/axiosClient', () => {
    const pending = () => new Promise(() => {});
    const get = (url: string) => url === '/health-packages/3'
        ? Promise.resolve({ success: true, message: 'Success', data: mocks.pkg })
        : pending();
    return { default: { get, post: pending, put: pending, patch: pending, delete: pending } };
});
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: { id: 'u1', fullName: 'Nguyễn Văn Minh', role: 'Pharmacist' }, logout: vi.fn(), isAuthenticated: true }) }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showToast: vi.fn(), showAlert: vi.fn(), showConfirm: vi.fn() }) }));

afterEach(cleanup);

function mount(element: React.ReactNode, path = '/page', routePath = '/page') {
    return render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={[path]}><Routes>
        <Route path={routePath} element={element} />
    </Routes></MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}

describe('Sentence-case page headings', () => {
    it.each([
        { name: 'TechnicianDashboard', element: <TechnicianDashboard />, title: 'Bàn làm việc cận lâm sàng', old: 'Bàn Làm Việc Cận Lâm Sàng' },
        { name: 'WalkInPatientRegistration', element: <WalkInPatientRegistration />, title: 'Tiếp nhận người bệnh', old: 'Quy Trình Tiếp Nhận Người Bệnh Khám' },
        { name: 'AdminFacilities', element: <AdminFacilities />, title: 'Mạng lưới cơ sở', old: 'Quản trị Mạng lưới Bệnh viện & Cơ sở' },
        { name: 'ReceptionPackageRegistrations', element: <ReceptionPackageRegistrations />, title: 'Đăng ký gói khám', old: 'Quản lý Đăng ký Gói khám Sức khỏe' },
        { name: 'ReceptionBilling', element: <ReceptionBilling />, title: 'Thu ngân & hóa đơn', old: 'Quản lý Hóa đơn & Thu ngân' },
        { name: 'PatientPackageRegistrations', element: <PatientPackageRegistrations />, title: 'Gói khám đã đăng ký', old: 'Gói Khám Sức Khỏe Đã Đăng Ký' },
        { name: 'PackageRegistration', element: <PackageRegistration />, path: '/patient/health-packages/3/register', routePath: '/patient/health-packages/:id/register', title: 'Đăng ký gói khám', old: 'Đăng ký Gói khám Sức khỏe' },
        { name: 'PatientDiagnosticResults', element: <PatientDiagnosticResults />, title: 'Kết quả cận lâm sàng', old: 'Kết Quả Cận Lâm Sàng & Sinh Hiệu' },
        { name: 'ReceptionWorkspace', element: <ReceptionWorkspace />, title: 'Bàn làm việc lễ tân', old: 'Bàn Làm Việc Lễ Tân' },
    ])('$name shows the sentence-case title', async ({ element, path, routePath, title, old }) => {
        mount(element, path, routePath);
        expect(await screen.findByRole('heading', { level: 1, name: title })).toBeInTheDocument();
        expect(screen.queryByText(old)).not.toBeInTheDocument();
    });

    it.each([
        { name: 'DoctorDashboard', element: <DoctorDashboard />, title: 'Bàn làm việc Bác sĩ', removed: 'Hệ thống điều phối khám lâm sàng và quản lý hồ sơ y tế bệnh nhân hôm nay' },
        { name: 'DoctorQueue', element: <DoctorQueue />, title: 'Hàng đợi khám lâm sàng', removed: 'Theo dõi thứ tự tiếp nhận, đo sinh hiệu và điều phối bệnh nhân vào phòng khám.' },
        { name: 'DoctorSchedule', element: <DoctorSchedule />, title: 'Lịch trực & Ca khám tuần', removed: 'Xem lịch trực được phân bổ, danh sách các khung giờ và thông tin bệnh nhân đã đặt.' },
        { name: 'AdminFacilities', element: <AdminFacilities />, title: 'Mạng lưới cơ sở', removed: 'Quản lý cây tổ chức: Cơ sở ➔ Tòa nhà ➔ Khoa phòng ➔ Phòng bệnh ➔ Giường bệnh' },
        { name: 'AdminBilling', element: <AdminBilling />, title: 'Doanh thu & Biểu phí phòng khám', removed: 'Theo dõi thực thu tài chính, cấu hình phí khám chuyên khoa và bảng giá cận lâm sàng' },
        { name: 'WalkInPatientRegistration', element: <WalkInPatientRegistration />, title: 'Tiếp nhận người bệnh', removed: 'Quy trình 3 bước chuẩn: Tra cứu hồ sơ cũ ➔ Chuẩn bị lượt khám ➔ Cấp số thứ tự & In phiếu' },
        { name: 'ReceptionPackageRegistrations', element: <ReceptionPackageRegistrations />, title: 'Đăng ký gói khám', removed: 'Xác nhận và hỗ trợ bệnh nhân đã đăng ký các gói khám định kỳ trực tuyến.' },
        { name: 'ReceptionBilling', element: <ReceptionBilling />, title: 'Thu ngân & hóa đơn', removed: 'Thu tiền tại quầy và theo dõi hóa đơn của phòng khám' },
        { name: 'TechnicianDashboard', element: <TechnicianDashboard />, title: 'Bàn làm việc cận lâm sàng', removed: 'Quản lý tiếp nhận mẫu, thực hiện xét nghiệm & chẩn đoán hình ảnh' },
        { name: 'TechnicianVerificationQueue', element: <TechnicianVerificationQueue />, title: 'Chờ duyệt kết quả', removed: 'Đối chiếu kết quả do kỹ thuật viên khác nhập' },
        { name: 'PharmacyDashboard', element: <PharmacyDashboard />, title: 'Bàn làm việc Dược sĩ', removed: /Xin chào/ },
    ])('$name no longer renders its filler subtitle', async ({ element, title, removed }) => {
        mount(element);
        expect(await screen.findByRole('heading', { level: 1, name: title })).toBeInTheDocument();
        expect(screen.queryByText(removed)).not.toBeInTheDocument();
    });

    it.each([
        { name: 'PatientPackageRegistrations', element: <PatientPackageRegistrations />, kept: 'Theo dõi tình trạng xét duyệt và quản lý các yêu cầu đăng ký gói khám của bạn.' },
        { name: 'PatientDiagnosticResults', element: <PatientDiagnosticResults />, kept: 'Theo dõi lịch sử xét nghiệm, siêu âm và chỉ số sức khỏe của bạn tại ClinicCare' },
    ])('$name keeps its patient-facing subtitle', async ({ element, kept }) => {
        mount(element);
        expect(await screen.findByText(kept)).toBeInTheDocument();
    });

    it('ReceptionWorkspace drops the title icon and live clock but keeps refresh and duty facility', async () => {
        mount(<ReceptionWorkspace />);
        const heading = await screen.findByRole('heading', { level: 1, name: 'Bàn làm việc lễ tân' });
        expect(heading.parentElement?.querySelector('svg')).toBeNull();
        expect(screen.queryByText(/\d{2}:\d{2}:\d{2}/)).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Làm mới/ })).toBeInTheDocument();
        expect(screen.getByText('Cơ sở trực:')).toBeInTheDocument();
    });
});
