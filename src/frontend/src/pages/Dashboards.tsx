import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import styles from './Dashboards.module.css';
import axiosClient from '../api/axiosClient';
import type { ApiResponse } from '../types';
import { 
    CalendarCheck, Users, Stethoscope, ShieldPlus, 
    CalendarDays, History, Package, Pill 
} from 'lucide-react';

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
            <div className={styles.gridLayout1}>
                <div className={["card", styles.spacing1].join(' ')}>
                    <div className={styles.text1}>LỊCH HÔM NAY</div>
                    <div className={styles.text2}>
                        {loading ? '...' : (stats?.totalAppointmentsToday ?? 0)}
                    </div>
                    <div className={styles.text3}>Tổng: {stats?.totalAppointmentsAll ?? 0} lượt đặt</div>
                </div>
                <div className={["card", styles.spacing2].join(' ')}>
                    <div className={styles.text1}>BỆNH NHÂN HỆ THỐNG</div>
                    <div className={styles.text2}>
                        {loading ? '...' : (stats?.totalPatients ?? 0)}
                    </div>
                    <div className={styles.text3}>Hồ sơ y tế điện tử</div>
                </div>
                <div className={["card", styles.spacing3].join(' ')}>
                    <div className={styles.text1}>ĐỘI NGŨ BÁC SĨ</div>
                    <div className={styles.text2}>
                        {loading ? '...' : (stats?.totalDoctors ?? 0)}
                    </div>
                    <div className={styles.text3}>Đang công tác</div>
                </div>
                <div className={["card", styles.spacing4].join(' ')}>
                    <div className={styles.text1}>ĐƠN THUỐC ĐÃ KÊ</div>
                    <div className={styles.text2}>
                        {loading ? '...' : (stats?.totalPrescriptions ?? 0)}
                    </div>
                    <div className={styles.text3}>Toàn hệ thống</div>
                </div>
                <div className={["card", styles.spacing5, (stats?.lowStockMedicinesCount > 0 ? styles.state1 : styles.state2)].join(' ')}>
                    <div className={[styles.text4, (stats?.lowStockMedicinesCount > 0 ? styles.state3 : styles.state4)].join(' ')}>THUỐC SẮP HẾT HÀNG</div>
                    <div className={[styles.text5, (stats?.lowStockMedicinesCount > 0 ? styles.state3 : styles.state5)].join(' ')}>
                        {loading ? '...' : (stats?.lowStockMedicinesCount ?? 0)}
                    </div>
                    <div className={styles.text3}>Cần nhập bổ sung</div>
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

export const ForbiddenPage: React.FC = () => {
    return (
        <div className={[styles.emptyState, styles.surface1].join(' ')}>
            <ShieldPlus size={64} className={styles.icon1} />
            <h1 className={styles.heading1}>403 - Truy cập bị từ chối</h1>
            <p>Tài khoản của bạn không có quyền xem trang này.</p>
            <Link to="/" className={["btn-primary", styles.icon2].join(' ')}>Quay lại trang chủ</Link>
        </div>
    );
};

export const NotFoundPage: React.FC = () => {
    return (
        <div className={[styles.emptyState, styles.surface1].join(' ')}>
            <h1 className={styles.heading2}>404</h1>
            <p>Trang bạn yêu cầu không tồn tại.</p>
            <Link to="/" className={["btn-primary", styles.icon2].join(' ')}>Quay lại trang chủ</Link>
        </div>
    );
};
