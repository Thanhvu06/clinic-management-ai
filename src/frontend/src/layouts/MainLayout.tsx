import React, { useState } from 'react';
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import styles from './MainLayout.module.css';
import { 
    LayoutDashboard, CalendarDays, CalendarCheck, 
    History, Users, Stethoscope, 
    ShieldPlus, LogOut, Menu, X, ShieldAlert, Pill, Package, Calendar,
    Receipt, TrendingUp, FlaskConical
} from 'lucide-react';
import { NotificationBell } from '../components/common/NotificationBell';

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

        if (pathname.startsWith('/reception/billing')) return 'Quản lý Hóa đơn & Thu ngân';
        if (pathname.startsWith('/reception/appointments')) return 'Quản lý lịch hẹn';
        if (pathname.startsWith('/reception/package-registrations')) return 'Đăng ký gói khám';
        if (pathname.startsWith('/reception/change-requests')) return 'Yêu cầu đổi/hủy lịch';
        if (pathname.startsWith('/reception')) return 'Bàn tiếp tân phòng khám';

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

    const NavItem = ({ to, icon: Icon, label }: { to: string; icon: any; label: string }) => {
        const isActive = location.pathname === to || (to !== '/doctor' && to !== '/admin' && to !== '/reception' && to !== '/pharmacy' && to !== '/diagnostics' && location.pathname.startsWith(to));
        return (
            <li>
                <Link 
                    to={to} 
                    className={`${styles.navLink} ${isActive ? styles.navLinkActive : ''}`}
                    onClick={() => setIsMobileMenuOpen(false)}
                >
                    <Icon size={18} className={styles.navIcon} />
                    <span>{label}</span>
                </Link>
            </li>
        );
    };

    const renderMenu = () => {
        switch (user?.role) {
            case 'Doctor':
                return (
                    <>
                        <NavItem to="/doctor" icon={LayoutDashboard} label="Bàn làm việc" />
                        <NavItem to="/doctor/queue" icon={Users} label="Hàng đợi khám" />
                        <NavItem to="/doctor/schedule" icon={CalendarCheck} label="Lịch trực & ca khám" />
                        <NavItem to="/doctor/appointments" icon={CalendarDays} label="Danh sách lịch khám" />
                        <NavItem to="/doctor/leave-requests" icon={History} label="Yêu cầu nghỉ phép" />
                    </>
                );
            case 'Receptionist':
                return (
                    <>
                        <NavItem to="/reception" icon={LayoutDashboard} label="Bàn làm việc" />
                        <NavItem to="/reception/appointments" icon={CalendarCheck} label="Quản lý lịch hẹn" />
                        <NavItem to="/reception/package-registrations" icon={Package} label="Đăng ký gói khám" />
                        <NavItem to="/reception/billing" icon={Receipt} label="Thu ngân & Hóa đơn" />
                        <NavItem to="/reception/change-requests" icon={History} label="Yêu cầu đổi/hủy" />
                    </>
                );
            case 'Admin':
                return (
                    <>
                        <NavItem to="/admin" icon={LayoutDashboard} label="Tổng quan" />
                        <NavItem to="/admin/billing" icon={TrendingUp} label="Doanh thu & Biểu phí" />
                        <NavItem to="/admin/accounts" icon={ShieldAlert} label="Quản lý tài khoản" />
                        <NavItem to="/admin/specialties" icon={Stethoscope} label="Quản lý chuyên khoa" />
                        <NavItem to="/admin/doctors" icon={Users} label="Quản lý bác sĩ" />
                        <NavItem to="/admin/packages" icon={Package} label="Gói khám sức khỏe" />
                        <NavItem to="/admin/medicines" icon={Pill} label="Danh mục thuốc" />
                        <NavItem to="/admin/work-schedules" icon={CalendarCheck} label="Lịch trực & Slots" />
                        <NavItem to="/admin/leaves" icon={CalendarDays} label="Duyệt nghỉ phép" />
                        <NavItem to="/admin/audit-logs" icon={History} label="Nhật ký hệ thống" />
                    </>
                );
            case 'Pharmacist':
                return (
                    <>
                        <NavItem to="/pharmacy" icon={LayoutDashboard} label="Bàn làm việc" />
                        <NavItem to="/pharmacy/prescriptions" icon={CalendarCheck} label="Đơn thuốc chờ cấp" />
                        <NavItem to="/pharmacy/medicines" icon={Pill} label="Danh mục thuốc" />
                        <NavItem to="/pharmacy/inventory" icon={History} label="Lịch sử kho" />
                    </>
                );
            case 'DiagnosticTechnician':
                return (
                    <>
                        <NavItem to="/diagnostics" icon={FlaskConical} label="Hàng đợi chỉ định" />
                    </>
                );
            default:
                return null;
        }
    };

    return (
        <div className={styles.layout}>
            {/* Mobile Top Bar */}
            <div className={styles.mobileHeader}>
                <div className={styles.mobileLogo}>
                    <div className={styles.logoBadge} style={{ width: 32, height: 32 }}>
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
                    {isMobileMenuOpen ? <X size={24} /> : <Menu size={24} />}
                </button>
            </div>

            {/* Mobile Backdrop Overlay */}
            {isMobileMenuOpen && (
                <div className={styles.mobileOverlay} onClick={() => setIsMobileMenuOpen(false)} />
            )}

            {/* Sidebar Navigation */}
            <aside className={`${styles.sidebar} ${isMobileMenuOpen ? styles.sidebarOpen : ''}`}>
                <div className={styles.sidebarHeader}>
                    <div className={styles.logoBadge}>
                        <ShieldPlus size={22} />
                    </div>
                    <div>
                        <div className={styles.logoText}>ClinicCare AI</div>
                        <div style={{ fontSize: '0.7rem', color: 'var(--c-teal)', fontWeight: 500 }}>
                            Hệ Thống Y Tế Thông Minh
                        </div>
                    </div>
                </div>
                
                <nav className={styles.nav}>
                    <div className={styles.navSectionTitle}>CHỨC NĂNG CHÍNH</div>
                    <ul className={styles.navList}>{renderMenu()}</ul>
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
            </aside>

            {/* Main Content Area */}
            <main className={styles.main}>
                {/* Desktop Topbar Header */}
                <header className={styles.header}>
                    <div className={styles.pageHeaderTitle}>
                        <span>{getPageTitle(location.pathname)}</span>
                    </div>

                    <div className={styles.headerRight}>
                        <NotificationBell />
                        <div className={styles.currentDateBadge}>
                            <Calendar size={14} style={{ color: 'var(--c-primary)' }} />
                            <span>{todayFormatted}</span>
                        </div>
                        <div className={styles.headerRoleBadge}>
                            {getRoleName(user?.role)}
                        </div>
                        <div className={styles.userAvatar} style={{ width: 34, height: 34, fontSize: '0.8rem' }}>
                            {getInitials(user?.fullName)}
                        </div>
                    </div>
                </header>

                {/* Page Outlet */}
                <div className={styles.content}>
                    <Outlet />
                </div>
            </main>
        </div>
    );
};
