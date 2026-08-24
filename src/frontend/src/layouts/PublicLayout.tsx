import React, { useState } from 'react';
import { Outlet, Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import styles from './PublicLayout.module.css';
import { 
    Phone, Clock, MapPin, ShieldPlus, Menu, X, 
    UserCircle, Calendar, FileText, Pill, LogOut, ChevronDown 
} from 'lucide-react';


export const PublicLayout: React.FC = () => {
    const { isAuthenticated, user, logout } = useAuth();
    const navigate = useNavigate();
    const [isDropdownOpen, setIsDropdownOpen] = useState(false);
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

    const handleLogout = () => {
        logout();
        navigate('/login');
    };

    const isPatient = user?.role === 'Patient';
    const isStaff = user && user.role !== 'Patient';

    return (
        <div className={styles.layout}>
            {/* Top Bar */}
            <div className={styles.topBar}>
                <div className={styles.topBarContainer}>
                    <div className={styles.topBarInfo}>
                        <div className={styles.topBarInfoItem}>
                            <Clock size={14} /> Giờ làm việc: 07:00 - 19:00 (Thứ 2 - Chủ Nhật)
                        </div>
                    </div>
                    <div className={styles.topBarInfo}>
                        <div className={styles.topBarInfoItem}>
                            <Phone size={14} /> Hotline: 1900 1234
                        </div>
                        <div className={styles.topBarInfoItem}>
                            <MapPin size={14} /> Hệ thống 40+ phòng khám
                        </div>
                    </div>
                </div>
            </div>

            {/* Main Header */}
            <header className={styles.header}>
                <div className={styles.headerContainer}>
                    <Link to="/" className={styles.logoArea}>
                        <ShieldPlus size={32} />
                        ClinicCare AI
                    </Link>

                    <nav className={styles.nav}>
                        <Link to="/" className={styles.navLink}>Trang chủ</Link>
                        <a href="/#specialties" className={styles.navLink}>Chuyên khoa</a>
                        <a href="/#doctors" className={styles.navLink}>Bác sĩ</a>
                        <Link to="/patient/ai-consultation" className={styles.navLink}>Tư vấn AI</Link>
                    </nav>

                    <div className={styles.authArea}>
                        {!isAuthenticated ? (
                            <>
                                <Link to="/login" className={styles.btnLogin}>Đăng nhập</Link>
                                <Link to="/register" className={styles.btnRegister}>Đăng ký ngay</Link>
                            </>
                        ) : (
                            <div className={styles.userDropdown}>
                                <button 
                                    className={styles.userToggle}
                                    onClick={() => setIsDropdownOpen(!isDropdownOpen)}
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
                                                <Link to="/patient/appointments" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                    <Calendar size={18} /> Lịch hẹn của tôi
                                                </Link>
                                                <Link to="/patient/revisit" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                    <FileText size={18} /> Tái khám
                                                </Link>
                                                <Link to="/patient/prescriptions" className={styles.dropdownItem} onClick={() => setIsDropdownOpen(false)}>
                                                    <Pill size={18} /> Đơn thuốc của tôi
                                                </Link>
                                            </>
                                        ) : (
                                            <Link 
                                                to={isStaff ? `/${user?.role.toLowerCase()}` : '/'} 
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
                        )}
                        {!isStaff && (
                            <Link to="/patient/book" className={styles.btnRegister} style={{ marginLeft: '10px' }}>
                                Đặt lịch khám
                            </Link>
                        )}
                    </div>

                    <button 
                        className={styles.hamburger}
                        onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                    >
                        {isMobileMenuOpen ? <X size={28} /> : <Menu size={28} />}
                    </button>
                </div>

                {/* Mobile Menu */}
                <div className={`${styles.mobileMenu} ${isMobileMenuOpen ? styles.open : ''}`}>
                    <Link to="/" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Trang chủ</Link>
                    <a href="/#specialties" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Chuyên khoa</a>
                    <a href="/#doctors" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Bác sĩ</a>
                    <Link to="/patient/ai-consultation" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Tư vấn AI</Link>
                    {!isAuthenticated ? (
                        <>
                            <Link to="/login" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Đăng nhập</Link>
                            <Link to="/register" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Đăng ký ngay</Link>
                        </>
                    ) : (
                         isPatient ? (
                            <>
                                <Link to="/patient/profile" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Hồ sơ cá nhân</Link>
                                <Link to="/patient/appointments" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Lịch hẹn của tôi</Link>
                                <Link to="/patient/revisit" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Tái khám</Link>
                                <Link to="/patient/prescriptions" className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Đơn thuốc của tôi</Link>
                                <button onClick={handleLogout} className={styles.navLink} style={{ textAlign: 'left', background: 'transparent', border: 'none', padding: 0, cursor: 'pointer', color: 'var(--c-danger)' }}>Đăng xuất</button>
                            </>
                         ) : (
                            <>
                                <Link to={`/${user?.role.toLowerCase()}`} className={styles.navLink} onClick={() => setIsMobileMenuOpen(false)}>Bảng điều khiển quản trị</Link>
                                <button onClick={handleLogout} className={styles.navLink} style={{ textAlign: 'left', background: 'transparent', border: 'none', padding: 0, cursor: 'pointer', color: 'var(--c-danger)' }}>Đăng xuất</button>
                            </>
                         )
                    )}
                    {!isStaff && (
                        <Link to="/patient/book" className={styles.btnRegister} style={{ display: 'inline-block', textAlign: 'center', marginTop: '10px' }} onClick={() => setIsMobileMenuOpen(false)}>
                            Đặt lịch khám
                        </Link>
                    )}
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
                        <p style={{ marginTop: '20px' }}><MapPin size={16} /> 123 Nguyễn Văn Linh, Quận 7, TP.HCM</p>
                        <p><Phone size={16} /> Hotline: 1900 1234 (7:00 - 19:00)</p>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Dịch vụ</h3>
                        <Link to="/patient/book" className={styles.footerLink}>Đặt lịch khám</Link>
                        <Link to="/patient/ai-consultation" className={styles.footerLink}>Tư vấn AI</Link>
                        <a href="/#specialties" className={styles.footerLink}>Khám chuyên khoa</a>
                        <a href="/#doctors" className={styles.footerLink}>Khám tổng quát</a>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Thông tin</h3>
                        <span className={styles.footerText}>Về chúng tôi</span>
                        <span className={styles.footerText}>Đội ngũ bác sĩ</span>
                        <span className={styles.footerText}>Hướng dẫn đặt lịch</span>
                        <span className={styles.footerText}>Câu hỏi thường gặp</span>
                    </div>
                    <div className={styles.footerCol}>
                        <h3>Chính sách</h3>
                        <span className={styles.footerText}>Chính sách bảo mật</span>
                        <span className={styles.footerText}>Điều khoản dịch vụ</span>
                        <span className={styles.footerText}>Quy định sử dụng AI</span>
                        <p style={{ fontSize: '0.8rem', marginTop: '10px', color: '#64748b' }}>
                            * Lưu ý: Hệ thống AI chỉ đưa ra gợi ý chuyên khoa dựa trên triệu chứng, không thay thế chẩn đoán của bác sĩ.
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
