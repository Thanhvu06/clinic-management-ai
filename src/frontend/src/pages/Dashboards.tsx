import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import styles from './Dashboards.module.css';
import axiosClient from '../api/axiosClient';
import type { ApiResponse } from '../types';
import { 
    CalendarCheck, Users, Stethoscope, ShieldPlus, 
    CalendarDays, History, Package, Pill 
} from 'lucide-react';

export const PatientDashboard: React.FC = () => {
    const { user } = useAuth();
    return (
        <div>
            <div className={styles.hero}>
                <h1 className={styles.heroTitle}>Xin chào, {user?.fullName}!</h1>
                <p className={styles.heroSubtitle}>Chào mừng bạn đến với hệ thống chăm sóc sức khỏe thông minh ClinicCare AI.</p>
                <div style={{ marginTop: '20px' }}>
                    <Link to="/patient/book" className="btn-primary" style={{ backgroundColor: 'white', color: 'var(--c-navy)' }}>
                        <CalendarCheck size={20} />
                        Đặt lịch khám ngay
                    </Link>
                </div>
            </div>

            <h2 className={styles.sectionTitle}>Truy cập nhanh</h2>
            <div className="grid-cards">
                <Link to="/patient/appointments" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarDays size={24} /></div>
                    <div className={styles.actionTitle}>Lịch hẹn của tôi</div>
                    <div className={styles.actionDesc}>Theo dõi các lịch hẹn sắp tới và trạng thái.</div>
                </Link>
                <Link to="/patient/revisit" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarDays size={24} /></div>
                    <div className={styles.actionTitle}>Tái khám</div>
                    <div className={styles.actionDesc}>Phản hồi các đề xuất tái khám từ bác sĩ.</div>
                </Link>
                <Link to="/patient/profile" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Users size={24} /></div>
                    <div className={styles.actionTitle}>Hồ sơ cá nhân</div>
                    <div className={styles.actionDesc}>Cập nhật thông tin liên hệ và tình trạng sức khỏe.</div>
                </Link>
            </div>
        </div>
    );
};

export const AdminDashboard: React.FC = () => {
    const [stats, setStats] = useState<any>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        axiosClient.get<any, ApiResponse<any>>('/admin/stats')
            .then(res => {
                if (res.success && res.data) setStats(res.data);
            })
            .catch(() => {})
            .finally(() => setLoading(false));
    }, []);

    return (
        <div>
            <div className={styles.hero}>
                <h1 className={styles.heroTitle}>Quản lý vận hành hệ thống</h1>
                <p className={styles.heroSubtitle}>Trung tâm kiểm soát toàn bộ phòng khám ClinicCare AI.</p>
            </div>

            <h2 className={styles.sectionTitle}>Chỉ số hệ thống thời gian thực</h2>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '16px', marginBottom: '32px' }}>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #0284c7' }}>
                    <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', fontWeight: 600 }}>LỊCH HÔM NAY</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.totalAppointmentsToday ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '4px' }}>Tổng: {stats?.totalAppointmentsAll ?? 0} lượt đặt</div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #10b981' }}>
                    <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', fontWeight: 600 }}>BỆNH NHÂN HỆ THỐNG</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.totalPatients ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '4px' }}>Hồ sơ y tế điện tử</div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #6366f1' }}>
                    <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', fontWeight: 600 }}>ĐỘI NGŨ BÁC SĨ</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.totalDoctors ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '4px' }}>Đang công tác</div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #f59e0b' }}>
                    <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', fontWeight: 600 }}>ĐƠN THUỐC ĐÃ KÊ</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.totalPrescriptions ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '4px' }}>Toàn hệ thống</div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: stats?.lowStockMedicinesCount > 0 ? '4px solid #dc2626' : '4px solid #10b981' }}>
                    <div style={{ fontSize: '0.85rem', color: stats?.lowStockMedicinesCount > 0 ? '#dc2626' : 'var(--c-muted)', fontWeight: 600 }}>THUỐC SẮP HẾT HÀNG</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: stats?.lowStockMedicinesCount > 0 ? '#dc2626' : 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.lowStockMedicinesCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '4px' }}>Cần nhập bổ sung</div>
                </div>
            </div>

            <h2 className={styles.sectionTitle}>Quản lý dữ liệu & Danh mục</h2>
            <div className="grid-cards">
                <Link to="/admin/accounts" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Users size={24} /></div>
                    <div className={styles.actionTitle}>Tài khoản</div>
                    <div className={styles.actionDesc}>Quản lý nhân sự và bệnh nhân.</div>
                </Link>
                <Link to="/admin/doctors" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Stethoscope size={24} /></div>
                    <div className={styles.actionTitle}>Bác sĩ</div>
                    <div className={styles.actionDesc}>Hồ sơ bác sĩ và phân khoa chuyên môn.</div>
                </Link>
                <Link to="/admin/specialties" className={styles.actionCard}>
                    <div className={styles.actionIcon}><ShieldPlus size={24} /></div>
                    <div className={styles.actionTitle}>Chuyên khoa</div>
                    <div className={styles.actionDesc}>Quản lý chuyên khoa và cấu hình AI.</div>
                </Link>
                <Link to="/admin/packages" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Package size={24} /></div>
                    <div className={styles.actionTitle}>Gói khám sức khỏe</div>
                    <div className={styles.actionDesc}>Quản lý bảng giá và danh mục gói khám.</div>
                </Link>
                <Link to="/admin/medicines" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Pill size={24} /></div>
                    <div className={styles.actionTitle}>Danh mục thuốc</div>
                    <div className={styles.actionDesc}>Quản lý thuốc, đơn vị và định mức tồn.</div>
                </Link>
                <Link to="/admin/work-schedules" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarCheck size={24} /></div>
                    <div className={styles.actionTitle}>Lịch làm việc & Slots</div>
                    <div className={styles.actionDesc}>Phân bổ lịch trực và sinh ca khám.</div>
                </Link>
                <Link to="/admin/leaves" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarDays size={24} /></div>
                    <div className={styles.actionTitle}>Yêu cầu nghỉ phép</div>
                    <div className={styles.actionDesc}>Duyệt yêu cầu xin nghỉ của bác sĩ.</div>
                </Link>
                <Link to="/admin/audit-logs" className={styles.actionCard}>
                    <div className={styles.actionIcon}><History size={24} /></div>
                    <div className={styles.actionTitle}>Nhật ký hệ thống</div>
                    <div className={styles.actionDesc}>Tra cứu sự kiện và kiểm toán dữ liệu.</div>
                </Link>
            </div>
        </div>
    );
};

export const ReceptionistDashboard: React.FC = () => {
    const [stats, setStats] = useState<any>(null);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        axiosClient.get<any, ApiResponse<any>>('/reception/stats')
            .then(res => {
                if (res.success && res.data) setStats(res.data);
            })
            .catch(() => {})
            .finally(() => setLoading(false));
    }, []);

    return (
        <div>
            <div className={styles.hero}>
                <h1 className={styles.heroTitle}>Bàn làm việc lễ tân</h1>
                <p className={styles.heroSubtitle}>Tiếp đón bệnh nhân → Xác nhận lịch → Điều phối ca khám → Xử lý yêu cầu đổi/hủy.</p>
            </div>
            
            <h2 className={styles.sectionTitle}>Tình trạng lịch hẹn hôm nay</h2>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px', marginBottom: '32px' }}>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #0284c7' }}>
                    <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', fontWeight: 600 }}>TỔNG LỊCH HÔM NAY</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.appointmentsToday ?? 0)}
                    </div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #f59e0b' }}>
                    <div style={{ fontSize: '0.85rem', color: '#b45309', fontWeight: 600 }}>CHỜ XÁC NHẬN</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#b45309', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.pendingAppointmentsToday ?? 0)}
                    </div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #3b82f6' }}>
                    <div style={{ fontSize: '0.85rem', color: '#1d4ed8', fontWeight: 600 }}>ĐÃ XÁC NHẬN (CHỜ KHÁM)</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#1d4ed8', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.confirmedAppointmentsToday ?? 0)}
                    </div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #10b981' }}>
                    <div style={{ fontSize: '0.85rem', color: '#047857', fontWeight: 600 }}>ĐÃ KHÁM XONG</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#047857', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.completedAppointmentsToday ?? 0)}
                    </div>
                </div>
                <div className="card" style={{ padding: '20px', borderLeft: stats?.pendingChangeRequests > 0 ? '4px solid #dc2626' : '4px solid #94a3b8' }}>
                    <div style={{ fontSize: '0.85rem', color: stats?.pendingChangeRequests > 0 ? '#dc2626' : 'var(--c-muted)', fontWeight: 600 }}>YÊU CẦU ĐỔI / HỦY CHỜ DUYỆT</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: stats?.pendingChangeRequests > 0 ? '#dc2626' : 'var(--c-navy-dark)', marginTop: '4px' }}>
                        {loading ? '...' : (stats?.pendingChangeRequests ?? 0)}
                    </div>
                </div>
            </div>

            <h2 className={styles.sectionTitle}>Nghiệp vụ hằng ngày</h2>
            <div className="grid-cards">
                <Link to="/reception/appointments" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarCheck size={24} /></div>
                    <div className={styles.actionTitle}>Quản lý lịch hẹn</div>
                    <div className={styles.actionDesc}>Xác nhận và điều phối lịch hẹn của bệnh nhân.</div>
                </Link>
                <Link to="/reception/change-requests" className={styles.actionCard}>
                    <div className={styles.actionIcon}><History size={24} /></div>
                    <div className={styles.actionTitle}>Yêu cầu đổi/hủy lịch</div>
                    <div className={styles.actionDesc}>Hàng chờ duyệt và xử lý đổi/hủy lịch từ bệnh nhân.</div>
                </Link>
            </div>
        </div>
    );
};

export const DoctorDashboard: React.FC = () => {
    const { user } = useAuth();
    return (
        <div>
            <div className={styles.hero}>
                <h1 className={styles.heroTitle}>Xin chào, Bác sĩ {user?.fullName || ''}</h1>
                <p className={styles.heroSubtitle}>Xem lịch khám và xử lý các ca bệnh hôm nay.</p>
            </div>
            
            <h2 className={styles.sectionTitle}>Chức năng</h2>
            <div className="grid-cards">
                <Link to="/doctor/appointments" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarDays size={24} /></div>
                    <div className={styles.actionTitle}>Lịch khám của tôi</div>
                    <div className={styles.actionDesc}>Danh sách bệnh nhân và cập nhật kết quả.</div>
                </Link>
                <Link to="/doctor/leave-requests" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarCheck size={24} /></div>
                    <div className={styles.actionTitle}>Yêu cầu nghỉ</div>
                    <div className={styles.actionDesc}>Đăng ký nghỉ phép và theo dõi trạng thái duyệt.</div>
                </Link>
            </div>
        </div>
    );
};

export const ForbiddenPage: React.FC = () => {
    return (
        <div className={styles.emptyState} style={{ minHeight: '80vh', border: 'none', background: 'transparent' }}>
            <ShieldPlus size={64} style={{ color: 'var(--c-danger)', marginBottom: '20px' }} />
            <h1 style={{ color: 'var(--c-text-dark)', marginBottom: '10px' }}>403 - Truy cập bị từ chối</h1>
            <p>Tài khoản của bạn không có quyền xem trang này.</p>
            <Link to="/" className="btn-primary" style={{ marginTop: '20px' }}>Quay lại trang chủ</Link>
        </div>
    );
};

export const NotFoundPage: React.FC = () => {
    return (
        <div className={styles.emptyState} style={{ minHeight: '80vh', border: 'none', background: 'transparent' }}>
            <h1 style={{ color: 'var(--c-text-dark)', marginBottom: '10px', fontSize: '3rem' }}>404</h1>
            <p>Trang bạn yêu cầu không tồn tại.</p>
            <Link to="/" className="btn-primary" style={{ marginTop: '20px' }}>Quay lại trang chủ</Link>
        </div>
    );
};
