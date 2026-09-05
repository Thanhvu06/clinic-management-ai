import React, { useState } from 'react';
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import styles from './MainLayout.module.css';
import { 
    LayoutDashboard, CalendarDays, CalendarCheck, 
    History, Users, Stethoscope, 
    ShieldPlus, LogOut, Menu, X, ShieldAlert, Pill, Package 
} from 'lucide-react';

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
        return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
    };

    const NavItem = ({ to, icon: Icon, label }: { to: string, icon: any, label: string }) => {
        const isActive = location.pathname === to || location.pathname.startsWith(to + '/');
        return (
            <li>
                <Link 
                    to={to} 
                    className={`${styles.navLink} ${isActive ? styles.navLinkActive : ''}`}
                    onClick={() => setIsMobileMenuOpen(false)}
                >
                    <Icon size={20} className={styles.navIcon} />
                    <span>{label}</span>
                </Link>
            </li>
        );
    };

    const renderMenu = () => {
        switch (user?.role) {
            case 'Receptionist':
                return (
                    <>
                        <NavItem to="/reception" icon={LayoutDashboard} label="Bàn làm việc" />
                        <NavItem to="/reception/appointments" icon={CalendarCheck} label="Lịch hẹn" />
                        <NavItem to="/reception/package-registrations" icon={Package} label="Đăng ký gói khám" />
                        <NavItem to="/reception/change-requests" icon={History} label="Yêu cầu đổi/hủy" />
                    </>
                );
            case 'Doctor':
                return (
                    <>
                        <NavItem to="/doctor" icon={LayoutDashboard} label="Bàn làm việc" />
                        <NavItem to="/doctor/appointments" icon={CalendarDays} label="Lịch khám của tôi" />
                        <NavItem to="/doctor/leave-requests" icon={CalendarCheck} label="Yêu cầu nghỉ" />
                    </>
                );
            case 'Admin':
                return (
                    <>
                        <NavItem to="/admin" icon={LayoutDashboard} label="Tổng quan" />
                        <NavItem to="/admin/accounts" icon={ShieldAlert} label="Quản lý tài khoản" />
                        <NavItem to="/admin/specialties" icon={Stethoscope} label="Quản lý chuyên khoa" />
                        <NavItem to="/admin/doctors" icon={Users} label="Quản lý bác sĩ" />
                        <NavItem to="/admin/packages" icon={Package} label="Gói khám sức khỏe" />
                        <NavItem to="/admin/medicines" icon={Pill} label="Danh mục thuốc" />
                        <NavItem to="/admin/work-schedules" icon={CalendarCheck} label="Lịch làm việc & Slots" />
                        <NavItem to="/admin/leaves" icon={CalendarDays} label="Yêu cầu nghỉ" />
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
            default:
                return null;
        }
    };

    return (
        <div className={styles.layout}>
            {/* Mobile Header */}
            <div className={styles.mobileHeader}>
                <div className={styles.mobileLogo}>
                    <ShieldPlus size={24} className={styles.logoIcon} />
                    <span>ClinicCare AI</span>
                </div>
                <button 
                    className={styles.hamburgerBtn}
                    onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                >
                    {isMobileMenuOpen ? <X size={24} /> : <Menu size={24} />}
                </button>
            </div>

            {/* Sidebar */}
            <aside className={`${styles.sidebar} ${isMobileMenuOpen ? styles.sidebarOpen : ''}`}>
                <div className={styles.sidebarHeader}>
                    <ShieldPlus size={28} className={styles.logoIcon} />
                    <span className={styles.logoText}>ClinicCare AI</span>
                </div>
                
                <nav className={styles.nav}>
                    <div className={styles.navSectionTitle}>MENU CHÍNH</div>
                    <ul>{renderMenu()}</ul>
                </nav>

                <div className={styles.userCard}>
                    <div className={styles.userAvatar}>
                        {getInitials(user?.fullName)}
                    </div>
                    <div className={styles.userInfo}>
                        <div className={styles.userName}>{user?.fullName}</div>
                        <div className={styles.userRole}>
                            {user?.role === 'Patient' ? 'Bệnh nhân' : 
                             user?.role === 'Receptionist' ? 'Lễ tân' : 
                             user?.role === 'Doctor' ? 'Bác sĩ' : 
                             user?.role === 'Pharmacist' ? 'Dược sĩ' : 
                             user?.role === 'Admin' ? 'Quản trị viên' : user?.role}
                        </div>
                    </div>
                    <button className={styles.logoutIconBtn} onClick={handleLogout} title="Đăng xuất">
                        <LogOut size={18} />
                    </button>
                </div>
            </aside>

            {/* Mobile overlay */}
            {isMobileMenuOpen && (
                <div className={styles.mobileOverlay} onClick={() => setIsMobileMenuOpen(false)} />
            )}

            {/* Main Content */}
            <main className={styles.main}>
                <header className={styles.header}>
                    <h2 className={styles.pageTitle}>
                        {/* A simple mapping could go here, or just a generic greeting */}
                        Hệ thống phòng khám thông minh
                    </h2>
                    <div className={styles.headerRight}>
                        <div className={styles.headerAvatar}>
                            {getInitials(user?.fullName)}
                        </div>
                    </div>
                </header>
                <div className={styles.content}>
                    <Outlet />
                </div>
            </main>
        </div>
    );
};
