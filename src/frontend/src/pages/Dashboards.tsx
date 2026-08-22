import React from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import styles from './Dashboards.module.css';
import { 
    CalendarCheck, Users, Stethoscope, ShieldPlus, 
    CalendarDays, BarChart3, History 
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
    return (
        <div>
            <div className={styles.hero}>
                <h1 className={styles.heroTitle}>Quản lý vận hành</h1>
                <p className={styles.heroSubtitle}>Trung tâm kiểm soát toàn bộ hệ thống phòng khám.</p>
            </div>

            <h2 className={styles.sectionTitle}>Quản lý dữ liệu</h2>
            <div className="grid-cards">
                <Link to="/admin/accounts" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Users size={24} /></div>
                    <div className={styles.actionTitle}>Tài khoản</div>
                    <div className={styles.actionDesc}>Quản lý nhân sự và bệnh nhân.</div>
                </Link>
                <Link to="/admin/doctors" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Stethoscope size={24} /></div>
                    <div className={styles.actionTitle}>Bác sĩ</div>
                    <div className={styles.actionDesc}>Hồ sơ bác sĩ và chuyên môn.</div>
                </Link>
                <Link to="/admin/specialties" className={styles.actionCard}>
                    <div className={styles.actionIcon}><ShieldPlus size={24} /></div>
                    <div className={styles.actionTitle}>Chuyên khoa</div>
                    <div className={styles.actionDesc}>Quản lý chuyên khoa và cấu hình AI.</div>
                </Link>
                <Link to="/admin/work-schedules" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarCheck size={24} /></div>
                    <div className={styles.actionTitle}>Lịch làm việc</div>
                    <div className={styles.actionDesc}>Phân bổ lịch và sinh ca khám.</div>
                </Link>
                <Link to="/admin/leaves" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarDays size={24} /></div>
                    <div className={styles.actionTitle}>Yêu cầu nghỉ</div>
                    <div className={styles.actionDesc}>Duyệt yêu cầu xin nghỉ của bác sĩ.</div>
                </Link>
            </div>

            <h2 className={styles.sectionTitle} style={{ marginTop: '30px' }}>Thống kê tổng quan</h2>
            <div className={styles.emptyState}>
                <BarChart3 size={48} className={styles.emptyIcon} />
                <h3>Chưa có dữ liệu thống kê</h3>
                <p>Biểu đồ và số liệu sẽ hiển thị khi hệ thống cập nhật endpoint báo cáo.</p>
            </div>
        </div>
    );
};

export const ReceptionistDashboard: React.FC = () => {
    return (
        <div>
            <div className={styles.hero}>
                <h1 className={styles.heroTitle}>Bàn làm việc lễ tân</h1>
                <p className={styles.heroSubtitle}>Lịch mới → Xác nhận → Xử lý yêu cầu đổi/hủy → Theo dõi lịch ảnh hưởng.</p>
            </div>
            
            <h2 className={styles.sectionTitle}>Nghiệp vụ hằng ngày</h2>
            <div className="grid-cards">
                <Link to="/reception/appointments" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarCheck size={24} /></div>
                    <div className={styles.actionTitle}>Quản lý lịch hẹn</div>
                    <div className={styles.actionDesc}>Xác nhận và tra cứu lịch hẹn của bệnh nhân.</div>
                </Link>
                <Link to="/reception/change-requests" className={styles.actionCard}>
                    <div className={styles.actionIcon}><History size={24} /></div>
                    <div className={styles.actionTitle}>Yêu cầu đổi/hủy lịch</div>
                    <div className={styles.actionDesc}>Hàng chờ duyệt yêu cầu từ bệnh nhân.</div>
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
