import React, { useState } from 'react';
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom';
import { Layout, Menu } from 'antd';
import type { MenuProps } from 'antd';
import { useAuth } from '../auth/AuthContext';
import styles from './MainLayout.module.css';
import {
    LayoutDashboard, CalendarDays, CalendarCheck,
    History, Users, Stethoscope,
    ShieldPlus, LogOut, Menu as MenuIcon, X, ShieldAlert, Pill, Package, Calendar,
    Receipt, TrendingUp, FlaskConical, Building2, UserPlus
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { NotificationBell } from '../components/common/NotificationBell';
import { RoleCopilotPanel } from '../components/RoleCopilotPanel';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { layout } from '../theme/tokens';

interface NavEntry {
    to: string;
    icon: LucideIcon;
    label: string;
}

// Thứ tự, nhãn và route theo vai trò giữ nguyên như sidebar cũ.
const NAV_BY_ROLE: Record<string, NavEntry[]> = {
    Doctor: [
        { to: '/doctor', icon: LayoutDashboard, label: 'Bàn làm việc' },
        { to: '/doctor/queue', icon: Users, label: 'Hàng đợi khám' },
        { to: '/doctor/schedule', icon: CalendarCheck, label: 'Lịch trực & ca khám' },
        { to: '/doctor/appointments', icon: CalendarDays, label: 'Danh sách lịch khám' },
        { to: '/doctor/leave-requests', icon: History, label: 'Yêu cầu nghỉ phép' },
    ],
    Receptionist: [
        { to: '/reception', icon: LayoutDashboard, label: 'Bàn làm việc' },
        { to: '/reception/walk-in', icon: UserPlus, label: 'Tiếp nhận bệnh nhân' },
        { to: '/reception/appointments', icon: CalendarCheck, label: 'Quản lý lịch hẹn' },
        { to: '/reception/package-registrations', icon: Package, label: 'Đăng ký gói khám' },
        { to: '/reception/billing', icon: Receipt, label: 'Thu ngân & Hóa đơn' },
        { to: '/reception/change-requests', icon: History, label: 'Yêu cầu đổi/hủy' },
    ],
    Admin: [
        { to: '/admin', icon: LayoutDashboard, label: 'Tổng quan' },
        { to: '/admin/facilities', icon: Building2, label: 'Mạng lưới cơ sở' },
        { to: '/admin/billing', icon: TrendingUp, label: 'Doanh thu & Biểu phí' },
        { to: '/admin/accounts', icon: ShieldAlert, label: 'Quản lý tài khoản' },
        { to: '/admin/specialties', icon: Stethoscope, label: 'Quản lý chuyên khoa' },
        { to: '/admin/doctors', icon: Users, label: 'Quản lý bác sĩ' },
        { to: '/admin/packages', icon: Package, label: 'Gói khám sức khỏe' },
        { to: '/admin/medicines', icon: Pill, label: 'Danh mục thuốc' },
        { to: '/admin/work-schedules', icon: CalendarCheck, label: 'Lịch trực & Slots' },
        { to: '/admin/leaves', icon: CalendarDays, label: 'Duyệt nghỉ phép' },
        { to: '/admin/audit-logs', icon: History, label: 'Nhật ký hệ thống' },
    ],
    Pharmacist: [
        { to: '/pharmacy', icon: LayoutDashboard, label: 'Bàn làm việc' },
        { to: '/pharmacy/prescriptions', icon: CalendarCheck, label: 'Đơn thuốc chờ cấp' },
        { to: '/pharmacy/medicines', icon: Pill, label: 'Danh mục thuốc' },
        { to: '/pharmacy/inventory', icon: History, label: 'Lịch sử kho' },
    ],
    DiagnosticTechnician: [
        { to: '/diagnostics', icon: FlaskConical, label: 'Hàng đợi chỉ định' },
    ],
};

// Trang gốc theo vai trò chỉ sáng khi khớp chính xác, không khớp theo tiền tố.
const ROLE_HOME_ROUTES = new Set(['/doctor', '/admin', '/reception', '/pharmacy', '/diagnostics']);

const isNavActive = (to: string, pathname: string) =>
    pathname === to || (!ROLE_HOME_ROUTES.has(to) && pathname.startsWith(to));

export const MainLayout: React.FC = () => {
    const { user, logout } = useAuth();
    const navigate = useNavigate();
    const location = useLocation();
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

    const handleLogout = () => {
        logout();
        navigate('/login');
    };

    const getInitials = (name?: string) => {
        if (!name) return 'U';
        const parts = name.trim().split(' ');
        if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    };

    const getRoleName = (role?: string) => {
        switch (role) {
            case 'Doctor': return 'Bác sĩ';
            case 'Receptionist': return 'Lễ tân';
            case 'Admin': return 'Quản trị viên';
            case 'Pharmacist': return 'Dược sĩ';
            case 'DiagnosticTechnician': return 'Kỹ thuật viên CLS';
            case 'Patient': return 'Bệnh nhân';
            default: return role || 'Người dùng';
        }
    };

    const getPageTitle = (pathname: string) => {
        if (pathname.startsWith('/doctor/queue')) return 'Hàng đợi khám bệnh';
        if (pathname.startsWith('/doctor/schedule')) return 'Lịch trực & ca khám';
        if (pathname.startsWith('/doctor/appointments') && pathname.includes('/examination')) return 'Bàn khám lâm sàng';
        if (pathname.startsWith('/doctor/appointments')) return 'Danh sách lịch khám';
        if (pathname.startsWith('/doctor/leave-requests')) return 'Quản lý nghỉ phép';
        if (pathname.startsWith('/doctor')) return 'Bàn làm việc Bác sĩ';

        if (pathname.startsWith('/reception/walk-in')) return 'Tiếp nhận bệnh nhân vãng lai';
        if (pathname.startsWith('/reception/billing')) return 'Quản lý Hóa đơn & Thu ngân';
        if (pathname.startsWith('/reception/appointments')) return 'Quản lý lịch hẹn';
        if (pathname.startsWith('/reception/package-registrations')) return 'Đăng ký gói khám';
        if (pathname.startsWith('/reception/change-requests')) return 'Yêu cầu đổi/hủy lịch';
        if (pathname.startsWith('/reception')) return 'Bàn tiếp tân phòng khám';

        if (pathname.startsWith('/admin/facilities')) return 'Mạng lưới cơ sở & Phòng bệnh';
        if (pathname.startsWith('/admin/billing')) return 'Doanh thu & Biểu phí phòng khám';
        if (pathname.startsWith('/admin/accounts')) return 'Quản trị tài khoản';
        if (pathname.startsWith('/admin/specialties')) return 'Quản lý chuyên khoa';
        if (pathname.startsWith('/admin/doctors')) return 'Quản lý đội ngũ bác sĩ';
        if (pathname.startsWith('/admin/packages')) return 'Gói khám sức khỏe';
        if (pathname.startsWith('/admin/medicines')) return 'Danh mục dược phẩm';
        if (pathname.startsWith('/admin/work-schedules')) return 'Phân ca & Lịch làm việc';
        if (pathname.startsWith('/admin/leaves')) return 'Phê duyệt nghỉ phép';
        if (pathname.startsWith('/admin/audit-logs')) return 'Nhật ký hệ thống';
        if (pathname.startsWith('/admin')) return 'Tổng quan quản trị';

        if (pathname.startsWith('/pharmacy/prescriptions')) return 'Đơn thuốc chờ cấp';
        if (pathname.startsWith('/pharmacy/medicines')) return 'Kho dược phẩm';
        if (pathname.startsWith('/pharmacy/inventory')) return 'Biến động tồn kho';
        if (pathname.startsWith('/pharmacy')) return 'Bàn làm việc Dược sĩ';

        if (pathname.startsWith('/diagnostics/orders/')) return 'Thực hiện phiếu chỉ định CLS';
        if (pathname.startsWith('/diagnostics')) return 'Bàn làm việc Cận lâm sàng';

        return 'ClinicCare AI - Quản trị y tế';
    };

    // Format current date in Vietnamese
    const todayFormatted = new Intl.DateTimeFormat('vi-VN', {
        weekday: 'long',
        day: '2-digit',
        month: '2-digit',
        year: 'numeric'
    }).format(new Date());

    const navEntries = (user?.role && NAV_BY_ROLE[user.role]) || [];
    const menuItems: MenuProps['items'] = navEntries.map(({ to, icon: Icon, label }) => ({
        key: to,
        icon: <Icon size={18} />,
        label: (
            <Link to={to} onClick={() => setIsMobileMenuOpen(false)}>
                {label}
            </Link>
        ),
    }));
    const selectedKeys = navEntries.filter(entry => isNavActive(entry.to, location.pathname)).map(entry => entry.to);

    return (
        <Layout className={styles.layout}>
            {/* Mobile Backdrop Overlay */}
            {isMobileMenuOpen && (
                <div className={styles.mobileOverlay} onClick={() => setIsMobileMenuOpen(false)} />
            )}

            {/* Sidebar Navigation */}
            <Layout.Sider
                width={layout.sidebarWidth}
                trigger={null}
                className={`${styles.sidebar} ${isMobileMenuOpen ? styles.sidebarOpen : ''}`}
            >
                <div className={styles.sidebarHeader}>
                    <div className={styles.logoBadge}>
                        <ShieldPlus size={22} />
                    </div>
                    <div>
                        <div className={styles.logoText}>ClinicCare AI</div>
                        <div className={styles.logoTagline}>Hệ Thống Y Tế Thông Minh</div>
                    </div>
                </div>

                <nav className={styles.nav}>
                    <div className={styles.navSectionTitle}>CHỨC NĂNG CHÍNH</div>
                    <Menu
                        mode="inline"
                        theme="dark"
                        selectedKeys={selectedKeys}
                        items={menuItems}
                        className={styles.menu}
                    />
                </nav>

                {/* User Profile Footer Card */}
                <div className={styles.userCard}>
                    <div className={styles.userAvatar}>
                        {getInitials(user?.fullName)}
                    </div>
                    <div className={styles.userInfo}>
                        <div className={styles.userName} title={user?.fullName}>
                            {user?.fullName || 'Người dùng'}
                        </div>
                        <div className={styles.userRole}>
                            {getRoleName(user?.role)}
                        </div>
                    </div>
                    <button
                        type="button"
                        className={styles.logoutBtn}
                        onClick={handleLogout}
                        title="Đăng xuất khỏi hệ thống"
                    >
                        <LogOut size={16} />
                    </button>
                </div>
            </Layout.Sider>

            {/* Main Content Area */}
            <Layout className={styles.main}>
                {/* Mobile Top Bar */}
                <div className={styles.mobileHeader}>
                    <div className={styles.mobileLogo}>
                        <div className={`${styles.logoBadge} ${styles.logoBadgeSmall}`}>
                            <ShieldPlus size={20} />
                        </div>
                        <span>ClinicCare AI</span>
                    </div>
                    <button
                        type="button"
                        className={styles.hamburgerBtn}
                        onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                        aria-label="Toggle Navigation"
                    >
                        {isMobileMenuOpen ? <X size={24} /> : <MenuIcon size={24} />}
                    </button>
                </div>

                {/* Desktop Topbar Header */}
                <Layout.Header className={styles.header}>
                    <div className={styles.pageHeaderTitle}>
                        <span>{getPageTitle(location.pathname)}</span>
                    </div>

                    <div className={styles.headerRight}>
                        <NotificationBell />
                        <div className={styles.currentDateBadge}>
                            <Calendar size={14} className={styles.currentDateIcon} />
                            <span>{todayFormatted}</span>
                        </div>
                        <div className={styles.headerRoleBadge}>
                            {getRoleName(user?.role)}
                        </div>
                        <div className={`${styles.userAvatar} ${styles.userAvatarSmall}`}>
                            {getInitials(user?.fullName)}
                        </div>
                    </div>
                </Layout.Header>

                {/* Page Outlet */}
                <CopilotResourceProvider key={`${location.pathname}${location.search}`}>
                    <Layout.Content className={styles.content}>
                        <Outlet />
                    </Layout.Content>
                    <RoleCopilotPanel />
                </CopilotResourceProvider>
            </Layout>
        </Layout>
    );
};
