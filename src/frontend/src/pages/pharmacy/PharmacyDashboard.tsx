import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Pill, CalendarCheck, History, AlertTriangle, CheckCircle2, Clock, RefreshCw } from 'lucide-react';
import { Button, Card } from 'antd';
import { PageHeader, StatCard } from '../../components/common';
import styles from './PharmacyDashboard.module.css';

interface PharmacyStats {
    pendingPrescriptionsCount: number;
    dispensedTodayCount: number;
    lowStockCount: number;
    totalActiveMedicines: number;
}

export const PharmacyDashboard: React.FC = () => {
    const { user } = useAuth();
    const [stats, setStats] = useState<PharmacyStats | null>(null);
    const [loading, setLoading] = useState(true);

    const fetchStats = async () => {
        setLoading(true);
        try {
            const res = await axiosClient.get<any, ApiResponse<PharmacyStats>>('/pharmacy/dashboard');
            if (res.success && res.data) {
                setStats(res.data);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchStats();
    }, []);

    return (
        <div className={styles.dashboard}>
            <PageHeader title="Bàn làm việc Dược sĩ" subtitle={`Xin chào, ${user?.fullName || 'Dược sĩ'}! Quản lý cấp phát đơn thuốc và kiểm soát xuất - nhập kho dược.`} actions={<Button onClick={fetchStats}><RefreshCw size={16} aria-hidden="true" /> Cập nhật số liệu</Button>} />
            <h2 className={styles.sectionTitle}>Tình hình kho & Đơn thuốc</h2>
            <div className={styles.statGrid}>
                <StatCard title="ĐƠN THUỐC CHỜ CẤP" value={loading ? '...' : (stats?.pendingPrescriptionsCount ?? 0)} subtitle="Cần chuẩn bị & cấp phát" icon={<Clock size={20} />} color="warning" />
                <StatCard title="ĐÃ CẤP HÔM NAY" value={loading ? '...' : (stats?.dispensedTodayCount ?? 0)} subtitle="Đơn thuốc hoàn thành" icon={<CheckCircle2 size={20} />} color="success" />
                <StatCard title="CẢNH BÁO TỒN KHO" value={loading ? '...' : (stats?.lowStockCount ?? 0)} subtitle="Mặt hàng dưới định mức" icon={<AlertTriangle size={20} />} color={(stats?.lowStockCount ?? 0) > 0 ? 'danger' : 'success'} />
                <StatCard title="DANH MỤC THUỐC" value={loading ? '...' : (stats?.totalActiveMedicines ?? 0)} subtitle="Đang lưu hành trong kho" icon={<Pill size={20} />} color="info" />
            </div>
            <h2 className={styles.sectionTitle}>Nghiệp vụ dược chính</h2>
            <div className={styles.actionGrid}>
                <Link to="/pharmacy/prescriptions" className={styles.actionLink}><Card hoverable><CalendarCheck size={24} className={styles.actionIcon} /><div className={styles.actionTitle}>Cấp phát đơn thuốc</div><div className={styles.secondary}>Tiếp nhận đơn thuốc từ bác sĩ, kiểm tra số lượng và trừ kho cấp thuốc.</div></Card></Link>
                <Link to="/pharmacy/medicines" className={styles.actionLink}><Card hoverable><Pill size={24} className={styles.actionIcon} /><div className={styles.actionTitle}>Danh mục thuốc</div><div className={styles.secondary}>Tra cứu tồn kho, định mức tối thiểu và trạng thái thuốc.</div></Card></Link>
                <Link to="/pharmacy/inventory" className={styles.actionLink}><Card hoverable><History size={24} className={styles.actionIcon} /><div className={styles.actionTitle}>Lịch sử kho & Nhập hàng</div><div className={styles.secondary}>Theo dõi lịch sử nhập/xuất/điều chỉnh và ghi nhận lô thuốc mới.</div></Card></Link>
            </div>
        </div>
    );
};
