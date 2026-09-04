import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Pill, CalendarCheck, History, AlertTriangle, CheckCircle2, Clock, RefreshCw } from 'lucide-react';
import styles from '../Dashboards.module.css';

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
        <div>
            <div className={styles.hero}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                    <div>
                        <h1 className={styles.heroTitle}>Bàn làm việc Dược sĩ</h1>
                        <p className={styles.heroSubtitle}>
                            Xin chào, {user?.fullName || 'Dược sĩ'}! Quản lý cấp phát đơn thuốc và kiểm soát xuất - nhập kho dược.
                        </p>
                    </div>
                    <button
                        className="btn-secondary"
                        onClick={fetchStats}
                        style={{ backgroundColor: 'rgba(255, 255, 255, 0.2)', color: 'white', borderColor: 'rgba(255, 255, 255, 0.4)', display: 'flex', alignItems: 'center', gap: '6px' }}
                    >
                        <RefreshCw size={16} /> Cập nhật số liệu
                    </button>
                </div>
            </div>

            <h2 className={styles.sectionTitle}>Tình hình kho & Đơn thuốc</h2>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '16px', marginBottom: '32px' }}>
                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #f59e0b' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ fontSize: '0.85rem', color: '#b45309', fontWeight: 600 }}>ĐƠN THUỐC CHỜ CẤP</div>
                        <Clock size={20} color="#f59e0b" />
                    </div>
                    <div style={{ fontSize: '2rem', fontWeight: 800, color: '#b45309', marginTop: '6px' }}>
                        {loading ? '...' : (stats?.pendingPrescriptionsCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.825rem', color: 'var(--c-muted)', marginTop: '4px' }}>Cần chuẩn bị & cấp phát</div>
                </div>

                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #10b981' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ fontSize: '0.85rem', color: '#047857', fontWeight: 600 }}>ĐÃ CẤP HÔM NAY</div>
                        <CheckCircle2 size={20} color="#10b981" />
                    </div>
                    <div style={{ fontSize: '2rem', fontWeight: 800, color: '#047857', marginTop: '6px' }}>
                        {loading ? '...' : (stats?.dispensedTodayCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.825rem', color: 'var(--c-muted)', marginTop: '4px' }}>Đơn thuốc hoàn thành</div>
                </div>

                <div className="card" style={{ padding: '20px', borderLeft: (stats?.lowStockCount ?? 0) > 0 ? '4px solid #dc2626' : '4px solid #10b981' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ fontSize: '0.85rem', color: (stats?.lowStockCount ?? 0) > 0 ? '#dc2626' : 'var(--c-muted)', fontWeight: 600 }}>CẢNH BÁO TỒN KHO</div>
                        <AlertTriangle size={20} color={(stats?.lowStockCount ?? 0) > 0 ? '#dc2626' : '#10b981'} />
                    </div>
                    <div style={{ fontSize: '2rem', fontWeight: 800, color: (stats?.lowStockCount ?? 0) > 0 ? '#dc2626' : 'var(--c-navy-dark)', marginTop: '6px' }}>
                        {loading ? '...' : (stats?.lowStockCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.825rem', color: 'var(--c-muted)', marginTop: '4px' }}>Mặt hàng dưới định mức</div>
                </div>

                <div className="card" style={{ padding: '20px', borderLeft: '4px solid #0284c7' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ fontSize: '0.85rem', color: '#0369a1', fontWeight: 600 }}>DANH MỤC THUỐC</div>
                        <Pill size={20} color="#0284c7" />
                    </div>
                    <div style={{ fontSize: '2rem', fontWeight: 800, color: '#0369a1', marginTop: '6px' }}>
                        {loading ? '...' : (stats?.totalActiveMedicines ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.825rem', color: 'var(--c-muted)', marginTop: '4px' }}>Đang lưu hành trong kho</div>
                </div>
            </div>

            <h2 className={styles.sectionTitle}>Nghiệp vụ dược chính</h2>
            <div className="grid-cards">
                <Link to="/pharmacy/prescriptions" className={styles.actionCard}>
                    <div className={styles.actionIcon}><CalendarCheck size={24} /></div>
                    <div className={styles.actionTitle}>Cấp phát đơn thuốc</div>
                    <div className={styles.actionDesc}>Tiếp nhận đơn thuốc từ bác sĩ, kiểm tra số lượng và trừ kho cấp thuốc.</div>
                </Link>
                <Link to="/pharmacy/medicines" className={styles.actionCard}>
                    <div className={styles.actionIcon}><Pill size={24} /></div>
                    <div className={styles.actionTitle}>Danh mục thuốc</div>
                    <div className={styles.actionDesc}>Tra cứu tồn kho, định mức tối thiểu và trạng thái thuốc.</div>
                </Link>
                <Link to="/pharmacy/inventory" className={styles.actionCard}>
                    <div className={styles.actionIcon}><History size={24} /></div>
                    <div className={styles.actionTitle}>Lịch sử kho & Nhập hàng</div>
                    <div className={styles.actionDesc}>Theo dõi lịch sử nhập/xuất/điều chỉnh và ghi nhận lô thuốc mới.</div>
                </Link>
            </div>
        </div>
    );
};
