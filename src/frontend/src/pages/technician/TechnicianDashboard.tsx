import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { 
    Clock, CheckCircle, PlayCircle, Search, RefreshCw, 
    FileText, ArrowRight
} from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto, TechnicianDiagnosticStatsDto } from '../../types';
import { Button, Card, Input, Select } from 'antd';
import { PageHeader, StatCard, StatusBadge, DataTable, LoadingState, EmptyState } from '../../components/common';
import styles from './TechnicianDashboard.module.css';
import { useDialog } from '../../contexts/DialogContext';

export const TechnicianDashboard: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert } = useDialog();

    const [orders, setOrders] = useState<DiagnosticOrderDto[]>([]);
    const [stats, setStats] = useState<TechnicianDiagnosticStatsDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [statusFilter, setStatusFilter] = useState<string>('');
    const [dateFilter, setDateFilter] = useState<string>('');
    const [searchTerm, setSearchTerm] = useState<string>('');
    const [page, setPage] = useState<number>(1);
    const [, setTotalItems] = useState<number>(0);

    const loadData = useCallback(async (silent = false) => {
        if (!silent) setLoading(true);
        try {
            const [ordersRes, statsRes] = await Promise.all([
                diagnosticApi.getTechnicianOrders({
                    status: statusFilter || undefined,
                    date: dateFilter || undefined,
                    search: searchTerm || undefined,
                    page,
                    pageSize: 15
                }),
                diagnosticApi.getTechnicianStats()
            ]);

            if (ordersRes.success && ordersRes.data) {
                setOrders(ordersRes.data.items);
                setTotalItems(ordersRes.data.totalItems);
            }

            if (statsRes.success && statsRes.data) {
                setStats(statsRes.data);
            }
        } catch (err: any) {
            if (!silent) {
                showAlert(err.message || 'Không thể tải danh sách chỉ định cận lâm sàng.', 'Lỗi', 'error');
            }
        } finally {
            if (!silent) setLoading(false);
        }
    }, [statusFilter, dateFilter, searchTerm, page, showAlert]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    // Background polling (every 25s)
    useEffect(() => {
        const timer = setInterval(() => {
            loadData(true);
        }, 25000);
        return () => clearInterval(timer);
    }, [loadData]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        loadData();
    };

    const statusBadge = (status: string) => <StatusBadge status={status === 'Ordered' ? 'pending' : status === 'InProgress' ? 'confirmed' : status} label={status === 'Ordered' ? 'Chờ thực hiện' : status === 'InProgress' ? 'Đang thực hiện' : status === 'Completed' ? 'Đã hoàn tất' : status === 'Cancelled' ? 'Đã hủy' : '—'} />;

    return (
        <div className={styles.dashboard}>
            <PageHeader title="Bàn Làm Việc Cận Lâm Sàng" subtitle="Quản lý tiếp nhận mẫu, thực hiện xét nghiệm & chẩn đoán hình ảnh" actions={<Button onClick={() => loadData(false)}><RefreshCw size={15} aria-hidden="true" /> Làm mới</Button>} />
            <div className={styles.statGrid}>
                <StatCard title="Chờ thực hiện" value={stats?.orderedCount ?? 0} icon={<Clock size={24} />} color="warning" />
                <StatCard title="Đang thực hiện" value={stats?.inProgressCount ?? 0} icon={<PlayCircle size={24} />} color="info" />
                <StatCard title="Hoàn tất hôm nay" value={stats?.completedTodayCount ?? 0} icon={<CheckCircle size={24} />} color="success" />
            </div>
            <Card className={styles.filters}>
                <div className={styles.filterRow}>
                    <form onSubmit={handleSearchSubmit} className={styles.searchForm}>
                        <Input type="text" value={searchTerm} onChange={e => setSearchTerm(e.target.value)} placeholder="Tìm theo mã phiếu, mã hẹn, tên bệnh nhân, SĐT..." prefix={<Search size={16} />} />
                        <Button type="primary" htmlType="submit">Tìm</Button>
                    </form>
                    <div className={styles.filterControls}>
                        <Select className={styles.statusFilter} value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                            {value:'',label:'Tất cả trạng thái'}, {value:'Ordered',label:'Chờ thực hiện'}, {value:'InProgress',label:'Đang thực hiện'}, {value:'Completed',label:'Đã hoàn tất'}, {value:'Cancelled',label:'Đã hủy'}
                        ]} />
                        <Input type="date" className={styles.dateFilter} value={dateFilter} onChange={e => { setDateFilter(e.target.value); setPage(1); }} />
                        {(statusFilter || dateFilter || searchTerm) && <Button onClick={() => { setStatusFilter(''); setDateFilter(''); setSearchTerm(''); setPage(1); }}>Xóa lọc</Button>}
                    </div>
                </div>
            </Card>
            {loading ? <LoadingState message="Đang tải danh sách chỉ định..." /> : orders.length === 0 ? <EmptyState icon={<FileText size={40} />} title="Không tìm thấy phiếu chỉ định nào" description="Thử thay đổi bộ lọc hoặc tìm kiếm lại." /> : (
                <div className={styles.tableScroll}>
                    <DataTable data={orders} keyExtractor={order => order.id} columns={[
                        {header:'Mã phiếu',accessor:order => <><strong className={styles.orderCode}>{order.orderCode}</strong><div className={styles.secondary}>#{order.appointmentCode}</div></>},
                        {header:'Bệnh nhân',accessor:order => <><strong>{order.patientName}</strong><div className={styles.secondary}>{order.patientPhone || 'Không có SĐT'} • {order.patientAge ? `${order.patientAge}t` : ''}</div></>},
                        {header:'Bác sĩ & Chuyên khoa',accessor:order => <><div>{order.orderingDoctorName}</div><div className={styles.secondary}>{order.specialtyName || '---'}</div></>},
                        {header:'Dịch vụ chỉ định',accessor:order => <><div>{order.items.length} dịch vụ</div><div className={styles.serviceNames} title={order.items.map(i => i.serviceName).join(', ')}>{order.items.map(i => i.serviceName).join(', ')}</div></>},
                        {header:'Thời gian',accessor:order => new Date(order.orderedAtUtc).toLocaleString('vi-VN',{hour:'2-digit',minute:'2-digit',day:'2-digit',month:'2-digit'})},
                        {header:'Trạng thái',align:'center',accessor:order => statusBadge(order.status)},
                        {header:'Thao tác',align:'right',accessor:order => <Button type={order.status === 'Ordered' || order.status === 'InProgress' ? 'primary' : 'default'} onClick={() => navigate(`/diagnostics/orders/${order.id}`)}>{order.status === 'Ordered' ? 'Tiếp nhận' : order.status === 'InProgress' ? 'Nhập kết quả' : 'Xem chi tiết'}<ArrowRight size={14} aria-hidden="true" /></Button>}
                    ]} />
                </div>
            )}
        </div>
    );
};
