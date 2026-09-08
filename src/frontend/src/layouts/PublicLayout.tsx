import React, { useState, useEffect, useRef } from 'react';
import { Outlet, Link, NavLink, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { getRoleDashboardPath } from '../utils/roleRoutes';
import styles from './PublicLayout.module.css';
import { AppointmentLookupModal } from '../components/AppointmentLookupModal';
import { 
    MapPin, ShieldPlus, Menu, X, 
    UserCircle, Calendar, FileText, Pill, LogOut, ChevronDown, Search, PackageCheck, Bell, Receipt,
    FlaskConical
} from 'lucide-react';
import { NotificationBell } from '../components/common/NotificationBell';

export const PublicLayout: React.FC = () => {
    const { isAuthenticated, user, logout } = useAuth();
    const navigate = useNavigate();
    const location = useLocation();
    const [isDropdownOpen, setIsDropdownOpen] = useState(false);
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
    const [isLookupModalOpen, setIsLookupModalOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);

    const handleLogout = () => {
        logout();
        navigate('/login');
    };

    const isPatient = user?.role === 'Patient';
    const isStaff = user && user.role !== 'Patient';

    // Close menus when route changes
    useEffect(() => {
        setIsDropdownOpen(false);
        setIsMobileMenuOpen(false);
    }, [location.pathname]);

    // Handle Escape key and click outside
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

            {/* Utility Top Bar (Tier 1) */}
            <div className={styles.topBar}>
                <div className={styles.topBarContainer}>
                    <div className={styles.topBarInfo}>
                        <div className={styles.topBarInfoItem}>
                            Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống.
                        </div>
                    </div>
                    <div className={styles.topBarInfo}>
                        <button 
                            type="button"
                            onClick={() => setIsLookupModalOpen(true)}
                            className={styles.topBarBtn}
                            title="Tra cứu lịch hẹn"
                        >
                            <Search size={13} /> Tra cứu lịch hẹn
                        </button>
                        <Link to="/locations" className={styles.topBarInfoItem} style={{ textDecoration: 'none' }}>
                            <MapPin size={14} /> Hệ thống cơ sở phòng khám
                        </Link>
                    </div>
                </div>
            </div>

            {/* Main Navigation (Tier 2) */}
            <header className={styles.header}>
                <div className={styles.headerContainer}>
                    <Link to="/" className={styles.logoArea}>
                        <ShieldPlus size={32} />
                        ClinicCare AI
                    </Link>

                    <nav className={styles.nav}>
                        <NavLink to="/" end className={navLinkClass}>Trang chủ</NavLink>
                        <NavLink to="/specialties" className={navLinkClass}>Chuyên khoa</NavLink>
                        <NavLink to="/health-packages" className={navLinkClass}>Gói khám</NavLink>
                        <NavLink to="/doctors" className={navLinkClass}>Bác sĩ</NavLink>
                        <NavLink to="/locations" className={navLinkClass}>Điểm khám</NavLink>
                        <NavLink to="/patient/ai-consultation" className={navLinkClass}>Tư vấn AI</NavLink>
                    </nav>

                    <div className={styles.authArea}>
                        {!isAuthenticated ? (
                            <>
                                <Link to="/login" className={styles.btnLogin}>Đăng nhập</Link>
                                <Link to="/register" className={styles.btnRegister}>Đăng ký ngay</Link>
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
                                        <span style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>
                                            {user?.fullName}
                                        </span>
                                        <ChevronDown size={16} color="var(--c-text-light)" />
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
                                                <UserCircle size={18} /> Bảng điều khiển quản trị
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
                            <Link to="/patient/book" className={styles.btnBookHeader}>
                                <Calendar size={16} /> Đặt lịch khám
                            </Link>
                        )}
                    </div>

                    <button 
                        className={styles.hamburger}
                        onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                        aria-label="Mở thực đơn điều hướng"
                    >
                        {isMobileMenuOpen ? <X size={26} /> : <Menu size={26} />}
                    </button>
                </div>

                {/* Mobile Backdrop */}
                <div 
                    className={`${styles.backdrop} ${isMobileMenuOpen ? styles.open : ''}`}
                    onClick={() => setIsMobileMenuOpen(false)}
                />

                {/* Mobile Drawer */}
                <div className={`${styles.mobileMenu} ${isMobileMenuOpen ? styles.open : ''}`}>
                    <div className={styles.mobileMenuHeader}>
                        <div className={styles.logoArea} style={{ fontSize: '1.2rem' }}>
                            <ShieldPlus size={24} />
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
                    <NavLink to="/specialties" className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>Chuyên khoa</NavLink>
                    <NavLink to="/health-packages" className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>Gói khám</NavLink>
                    <NavLink to="/doctors" className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>Bác sĩ</NavLink>
                    <NavLink to="/locations" className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>Điểm khám</NavLink>
                    <NavLink to="/patient/ai-consultation" className={navLinkClass} onClick={() => setIsMobileMenuOpen(false)}>Tư vấn AI</NavLink>
                    
                    <button 
                        type="button" 
                        onClick={() => { setIsMobileMenuOpen(false); setIsLookupModalOpen(true); }}
                        className={styles.navLink}
                        style={{ textAlign: 'left', background: 'transparent', border: 'none', padding: '8px 0', cursor: 'pointer', color: 'var(--c-primary)' }}
                    >
                        🔍 Tra cứu lịch hẹn
                    </button>

                    <div style={{ borderTop: '1px solid var(--c-border)', paddingTop: '16px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        {!isAuthenticated ? (
                            <>
                                <Link to="/login" className={styles.btnLogin} style={{ textAlign: 'center' }} onClick={() => setIsMobileMenuOpen(false)}>
                                    Đăng nhập
                                </Link>
                                <Link to="/register" className={styles.btnRegister} style={{ textAlign: 'center', justifyContent: 'center' }} onClick={() => setIsMobileMenuOpen(false)}>
                                    Đăng ký tài khoản
                                </Link>
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
                                    <button onClick={handleLogout} className={styles.navLink} style={{ textAlign: 'left', background: 'transparent', border: 'none', padding: 0, cursor: 'pointer', color: 'var(--c-danger)' }}>Đăng xuất</button>
                                </>
                            ) : (
                                <>
                                    <Link to={getRoleDashboardPath(user?.role)} className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Bảng điều khiển quản trị</Link>
                                    <button onClick={handleLogout} className={styles.navLink} style={{ textAlign: 'left', background: 'transparent', border: 'none', padding: 0, cursor: 'pointer', color: 'var(--c-danger)' }}>Đăng xuất</button>
                                </>
                            )
                        )}
                        {!isStaff && (
                            <Link to="/patient/book" className={styles.btnBookHeader} style={{ justifyContent: 'center', marginTop: '10px' }} onClick={() => setIsMobileMenuOpen(false)}>
                                <Calendar size={16} /> Đặt lịch khám
                            </Link>
                        )}
                    </div>
                </div>
            </header>

            {/* Main Content */}
            <main className={styles.mainContent}>
                <Outlet />
            </main>

            {/* Footer */}
            <footer className={styles.footer}>
                <div className={styles.footerContainer}>
                    <div className={styles.footerCol}>
                        <div className={styles.logoArea} style={{ color: 'white', marginBottom: '20px' }}>
                            <ShieldPlus size={28} />
                            ClinicCare AI
                        </div>
                        <p>Hệ thống phòng khám đa khoa thông minh tích hợp trí tuệ nhân tạo, mang lại trải nghiệm khám chữa bệnh nhanh chóng, chính xác và tiện lợi.</p>
                        <p style={{ marginTop: '20px' }}>Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống.</p>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Dịch vụ & Đặt hẹn</h3>
                        <Link to="/patient/book" className={styles.footerLink}>Đặt lịch khám chuyên khoa</Link>
                        <Link to="/health-packages" className={styles.footerLink}>Gói khám sức khỏe</Link>
                        <Link to="/specialties" className={styles.footerLink}>Danh mục Chuyên khoa</Link>
                        <Link to="/doctors" className={styles.footerLink}>Đội ngũ Bác sĩ</Link>
                        <Link to="/patient/ai-consultation" className={styles.footerLink}>Tư vấn sơ bộ với AI</Link>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Hệ thống cơ sở</h3>
                        <Link to="/locations" className={styles.footerLink}>Tất cả điểm khám</Link>
                        <span className={styles.footerText}>Cơ sở TP. Hồ Chí Minh</span>
                        <span className={styles.footerText}>Cơ sở Hà Nội</span>
                        <span className={styles.footerText}>Cơ sở Đà Nẵng</span>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Chính sách & Pháp lý</h3>
                        <span className={styles.footerText}>Chính sách bảo mật dữ liệu y tế</span>
                        <span className={styles.footerText}>Điều khoản sử dụng dịch vụ</span>
                        <span className={styles.footerText}>Quy định định tuyến AI</span>
                        <p style={{ fontSize: '0.8rem', marginTop: '10px', color: '#94a3b8', lineHeight: 1.5 }}>
                            * Lưu ý: Trợ lý AI chỉ đưa ra định hướng tham khảo dựa trên mô tả triệu chứng, không thay thế chẩn đoán y khoa chuyên sâu của bác sĩ.
                        </p>
                    </div>
                </div>
                <div className={styles.footerBottom}>
                    &copy; {new Date().getFullYear()} ClinicCare AI. Tất cả quyền được bảo lưu.
                </div>
            </footer>
        </div>
    );
};
