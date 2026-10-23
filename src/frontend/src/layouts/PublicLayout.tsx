import { useChatContext } from '../contexts/ChatContext';
import React, { useState, useEffect, useRef } from 'react';
import { Outlet, Link, NavLink, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { getRoleDashboardPath } from '../utils/roleRoutes';
import { PublicClinicFooter } from './PublicClinicFooter';
import styles from './PublicLayout.module.css';
import { AppointmentLookupModal } from '../components/AppointmentLookupModal';
import {
    ShieldPlus, Menu, X,
    UserCircle, Calendar, FileText, Pill, LogOut, ChevronDown, PackageCheck, Bell, Receipt,
    FlaskConical
} from 'lucide-react';
import { NotificationBell } from '../components/common/NotificationBell';
import { LinkButton } from '../components/common/LinkButton';

const navItems = [
    { to: '/specialties', label: 'Chuyên khoa' },
    { to: '/doctors', label: 'Bác sĩ' },
    { to: '/health-packages', label: 'Gói khám' },
    { to: '/medicines', label: 'Bảng giá thuốc' },
    { to: '/locations', label: 'Cơ sở khám' },
];

export const PublicLayout: React.FC = () => {
    const { isAuthenticated, user, logout } = useAuth();
    const { openChat } = useChatContext();
    const navigate = useNavigate();
    const location = useLocation();
    const [isDropdownOpen, setIsDropdownOpen] = useState(false);
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
    const [isLookupModalOpen, setIsLookupModalOpen] = useState(false);
    const [isStaffAiModalOpen, setIsStaffAiModalOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    const handleLogout = () => {
        logout();
        navigate('/login');
    };

    const isPatient = user?.role === 'Patient';
    const isStaff = Boolean(user && user.role !== 'Patient');

    const handleAiConsultationClick = (e: React.MouseEvent) => {
        e.preventDefault();
        if (isStaff) {
            setIsStaffAiModalOpen(true);
        } else {
            openChat();
        }
    };

    const openLookup = () => {
        setIsMobileMenuOpen(false);
        setIsLookupModalOpen(true);
    };

    useEffect(() => {
        setIsDropdownOpen(false);
        setIsMobileMenuOpen(false);
    }, [location.pathname]);

    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                setIsDropdownOpen(false);
                setIsMobileMenuOpen(false);
            }
        };

        const handleClickOutside = (e: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
                setIsDropdownOpen(false);
            }
        };

        window.addEventListener('keydown', handleKeyDown);
        document.addEventListener('mousedown', handleClickOutside);
        return () => {
            window.removeEventListener('keydown', handleKeyDown);
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, []);

    const navLinkClass = ({ isActive }: { isActive: boolean }) =>
        isActive ? `${styles.navLink} ${styles.active}` : styles.navLink;

    return (
        <div className={styles.layout}>
            <AppointmentLookupModal isOpen={isLookupModalOpen} onClose={() => setIsLookupModalOpen(false)} />

            <header className={styles.header}>
                <div className={styles.headerContainer}>
                    <Link to="/" className={styles.logoArea}>
                        <ShieldPlus size={22} aria-hidden="true" />
                        ClinicCare AI
                    </Link>

                    <nav className={styles.nav} aria-label="Điều hướng chính">
                        {navItems.map(item => <NavLink key={item.to} to={item.to} className={navLinkClass}>{item.label}</NavLink>)}
                        <NavLink to="/patient/ai-consultation" className={navLinkClass} onClick={handleAiConsultationClick}>Trợ lý AI</NavLink>
                    </nav>

                    <div className={styles.authArea}>
                        {!isAuthenticated ? (
                            <>
                                <button type="button" onClick={openLookup} className={styles.textLink}>
                                    Tra cứu lịch hẹn
                                </button>
                                <Link to="/login" className={styles.textLink}>Đăng nhập</Link>
                                <Link to="/register" className={styles.textLink}>Đăng ký</Link>
                            </>
                        ) : (
                            <>
                                <NotificationBell />
                                <div className={styles.userDropdown} ref={dropdownRef}>
                                    <button
                                        className={styles.userToggle}
                                        onClick={() => setIsDropdownOpen(!isDropdownOpen)}
                                        aria-expanded={isDropdownOpen}
                                        aria-haspopup="true"
                                    >
                                        <div className={styles.avatar}>
                                            {user?.fullName?.charAt(0).toUpperCase() || 'U'}
                                        </div>
                                        <span className={styles.userName}>
                                            {user?.fullName}
                                        </span>
                                        <ChevronDown size={16} color="var(--cc-color-text-secondary)" />
                                    </button>

                                    {isDropdownOpen && (
                                        <div className={styles.dropdownMenu}>
                                            {isPatient ? (
                                                <>
                                                    <Link to="/patient/profile" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <UserCircle size={18} /> Hồ sơ cá nhân
                                                    </Link>
                                                    <Link to="/patient/notifications" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <Bell size={18} /> Thông báo của tôi
                                                    </Link>
                                                    <Link to="/patient/appointments" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <Calendar size={18} /> Lịch hẹn của tôi
                                                    </Link>
                                                    <Link to="/patient/health-package-registrations" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <PackageCheck size={18} /> Gói khám đã đăng ký
                                                    </Link>
                                                    <Link to="/patient/revisit" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <FileText size={18} /> Lịch tái khám
                                                    </Link>
                                                    <Link to="/patient/prescriptions" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <Pill size={18} /> Đơn thuốc của tôi
                                                    </Link>
                                                    <Link to="/patient/diagnostic-results" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <FlaskConical size={18} /> Kết quả cận lâm sàng
                                                    </Link>
                                                    <Link to="/patient/invoices" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                        <Receipt size={18} /> Hóa đơn của tôi
                                                    </Link>
                                                </>
                                            ) : (
                                                <Link
                                                    to={getRoleDashboardPath(user?.role)}
                                                    className={styles.dropdownItem}
                                                    onClick={() => setIsDropdownOpen(false)}
                                                >
                                                    <UserCircle size={18} /> Về bàn làm việc
                                                </Link>
                                            )}
                                            <button onClick={handleLogout} className={`${styles.dropdownItem} ${styles.logout}`}>
                                                <LogOut size={18} /> Đăng xuất
                                            </button>
                                        </div>
                                    )}
                                </div>
                            </>
                        )}
                        {!isStaff && (
                            <LinkButton type="primary" to="/patient/book" className={styles.bookButton}>
                                Đặt lịch khám
                            </LinkButton>
                        )}
                    </div>

                    <button
                        className={styles.hamburger}
                        onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                        aria-label="Mở thực đơn điều hướng"
                    >
                        {isMobileMenuOpen ? <X size={24} /> : <Menu size={24} />}
                    </button>
                </div>

                <div
                    className={`${styles.backdrop} ${isMobileMenuOpen ? styles.open : ''}`}
                    onClick={() => setIsMobileMenuOpen(false)}
                />

                <div className={`${styles.mobileMenu} ${isMobileMenuOpen ? styles.open : ''}`}>
                    <div className={styles.mobileMenuHeader}>
                        <div className={styles.logoArea}>
                            <ShieldPlus size={22} aria-hidden="true" />
                            ClinicCare AI
                        </div>
                        <button
                            className={styles.closeMobileBtn}
                            onClick={() => setIsMobileMenuOpen(false)}
                            aria-label="Đóng thực đơn"
                        >
                            <X size={22} />
                        </button>
                    </div>

                    <NavLink to="/" end className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>Trang chủ</NavLink>
                    {navItems.map(item => (
                        <NavLink key={item.to} to={item.to} className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>{item.label}</NavLink>
                    ))}
                    <NavLink to="/patient/ai-consultation" className={navLinkClass} onClick={(e) => {
                        setIsMobileMenuOpen(false);
                        handleAiConsultationClick(e);
                    }}>Trợ lý AI</NavLink>
                    <button type="button" onClick={openLookup} className={`${styles.navLink} ${styles.mobileLookup}`}>
                        Tra cứu lịch hẹn
                    </button>

                    <div className={styles.mobileAuth}>
                        {!isAuthenticated ? (
                            <>
                                <Link to="/login" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Đăng nhập</Link>
                                <Link to="/register" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Đăng ký</Link>
                            </>
                        ) : (
                            isPatient ? (
                                <>
                                    <Link to="/patient/profile" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Hồ sơ cá nhân</Link>
                                    <Link to="/patient/appointments" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Lịch hẹn của tôi</Link>
                                    <Link to="/patient/health-package-registrations" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Gói khám đã đăng ký</Link>
                                    <Link to="/patient/revisit" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Lịch tái khám</Link>
                                    <Link to="/patient/prescriptions" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Đơn thuốc của tôi</Link>
                                    <Link to="/patient/diagnostic-results" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Kết quả cận lâm sàng</Link>
                                    <Link to="/patient/invoices" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Hóa đơn của tôi</Link>
                                    <button onClick={handleLogout} className={`${styles.navLink} ${styles.mobileLogout}`}>Đăng xuất</button>
                                </>
                            ) : (
                                <>
                                    <Link to={getRoleDashboardPath(user?.role)} className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Về bàn làm việc</Link>
                                    <button onClick={handleLogout} className={`${styles.navLink} ${styles.mobileLogout}`}>Đăng xuất</button>
                                </>
                            )
                        )}
                        {!isStaff && (
                            <LinkButton type="primary" block to="/patient/book" onClick={() => setIsMobileMenuOpen(false)}>
                                Đặt lịch khám
                            </LinkButton>
                        )}
                    </div>
                </div>
            </header>

            <main className={styles.mainContent}>
                <Outlet />
            </main>

            <footer className={styles.footer}>
                <div className={styles.footerContainer}>
                    <div className={styles.footerCol}>
                        <div className={styles.footerBrand}>ClinicCare AI</div>
                        <PublicClinicFooter />
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Khám bệnh</h3>
                        <Link to="/patient/book" className={styles.footerLink}>Đặt lịch khám</Link>
                        <Link to="/specialties" className={styles.footerLink}>Chuyên khoa</Link>
                        <Link to="/doctors" className={styles.footerLink}>Bác sĩ</Link>
                        <Link to="/health-packages" className={styles.footerLink}>Gói khám sức khỏe</Link>
                        <Link to="/patient/ai-consultation" className={styles.footerLink} onClick={handleAiConsultationClick}>Hỏi trợ lý AI</Link>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Tra cứu</h3>
                        <button type="button" onClick={openLookup} className={styles.footerLink}>Tra cứu lịch hẹn</button>
                        <Link to="/medicines" className={styles.footerLink}>Bảng giá thuốc</Link>
                        <Link to="/locations" className={styles.footerLink}>Cơ sở khám</Link>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Chính sách</h3>
                        <span className={styles.footerText}>Chính sách bảo mật dữ liệu y tế</span>
                        <span className={styles.footerText}>Điều khoản sử dụng</span>
                    </div>
                </div>
                <div className={styles.footerBottom}>
                    <span>© 2026 ClinicCare AI</span>
                    <span>Trợ lý AI chỉ gợi ý chuyên khoa để tham khảo, không thay thế chẩn đoán của bác sĩ.</span>
                </div>
            </footer>

            {isStaffAiModalOpen && (
                <div className={styles.overlay1}>
                    <div className={styles.surface1} role="dialog" aria-modal="true" aria-labelledby="staff-ai-title">
                        <div className={styles.flexLayout1}>
                            <span>Trợ lý AI đặt lịch</span>
                            <button
                                type="button"
                                onClick={() => setIsStaffAiModalOpen(false)}
                                className={styles.button1}
                                aria-label="Đóng"
                            >
                                <X size={20} />
                            </button>
                        </div>
                        <div className={styles.spacing1}>
                            <h3 id="staff-ai-title" className={styles.heading1}>
                                Tính năng dành cho người bệnh
                            </h3>
                            <p className={styles.paragraph1}>
                                Bạn đang đăng nhập bằng tài khoản nhân viên <strong>{user?.fullName}</strong>.
                            </p>
                            <p className={styles.paragraph2}>
                                Trợ lý AI đặt lịch chỉ dành cho tài khoản người bệnh. Nhân viên dùng trợ lý trong bàn làm việc của mình.
                            </p>
                            <div className={styles.stackLayout1}>
                                <button
                                    type="button"
                                    onClick={() => {
                                        setIsStaffAiModalOpen(false);
                                        navigate(getRoleDashboardPath(user?.role));
                                    }}
                                    className={styles.button2}
                                >
                                    Về bàn làm việc
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setIsStaffAiModalOpen(false)}
                                    className={styles.button3}
                                >
                                    Đóng
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
