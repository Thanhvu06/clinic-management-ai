import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { 
    Clock, CheckCircle, PlayCircle, Search, RefreshCw, 
    FileText, ArrowRight, FlaskConical, Waves, Scan, Stethoscope
} from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticCategory, DiagnosticOrderDto, DiagnosticOrderStatus, TechnicianDiagnosticStatsDto } from '../../types';
import { Button, Card, Input, Select, Tag } from 'antd';
import { PageHeader, StatCard, StatusBadge, DataTable, LoadingState, EmptyState, InlineError, Pagination } from '../../components/common';
import { diagnosticCategoryLabels, diagnosticStatusLabels, diagnosticStatusTone } from '../../utils/diagnosticLabels';
import { toLocalDateString } from '../../utils/formatters';
import styles from './TechnicianDashboard.module.css';

const categoryIcons = { Laboratory: FlaskConical, Ultrasound: Waves, Imaging: Scan, Other: Stethoscope };

export const TechnicianDashboard: React.FC = () => {
    const navigate = useNavigate();

    const [orders, setOrders] = useState<DiagnosticOrderDto[]>([]);
    const [stats, setStats] = useState<TechnicianDiagnosticStatsDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
    const [categoryFilter, setCategoryFilter] = useState<DiagnosticCategory | ''>('');
    const [statusFilter, setStatusFilter] = useState<string>('');
    const [dateFilter, setDateFilter] = useState<string>('');
    const [searchTerm, setSearchTerm] = useState<string>('');
    const [page, setPage] = useState<number>(1);
    const [totalItems, setTotalItems] = useState<number>(0);
    const isMounted = useRef(false);
    const requestVersion = useRef(0);

    useEffect(() => {
        isMounted.current = true;
        return () => { isMounted.current = false; };
    }, []);

    const loadData = useCallback(async (silent = false) => {
        const version = silent ? requestVersion.current : ++requestVersion.current;
        if (!isMounted.current || version !== requestVersion.current) return;
        if (!silent) { setLoading(true); setError(null); }
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

            if (!isMounted.current || version !== requestVersion.current) return;
            if (!ordersRes.success || !ordersRes.data) throw new Error(ordersRes.message || 'Không thể tải danh sách chỉ định cận lâm sàng.');
            setOrders(ordersRes.data.items);
            setTotalItems(ordersRes.data.totalItems);

            if (statsRes.success && statsRes.data) {
                setStats(statsRes.data);
            }
            setLastUpdated(new Date());
            setError(null);
        } catch (err: unknown) {
            if (!silent && isMounted.current && version === requestVersion.current) {
                setError((err as { message?: string } | null)?.message || 'Không thể tải danh sách chỉ định cận lâm sàng.');
            }
        } finally {
            if (!silent && isMounted.current && version === requestVersion.current) setLoading(false);
        }
    }, [statusFilter, dateFilter, searchTerm, page]);

    useEffect(() => {
        void Promise.resolve().then(() => loadData());
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

    const selectStat = (status: DiagnosticOrderStatus) => {
        setStatusFilter(status);
        setDateFilter(status === 'Completed' ? toLocalDateString(new Date()) : '');
        setPage(1);
    };
    const statFilters = [
        { status: 'Ordered' as const, title: 'Chờ thực hiện', value: stats?.orderedCount ?? 0, icon: Clock, color: 'warning' as const },
        { status: 'InProgress' as const, title: 'Đang thực hiện', value: stats?.inProgressCount ?? 0, icon: PlayCircle, color: 'info' as const },
        { status: 'Completed' as const, title: 'Hoàn tất hôm nay', value: stats?.completedTodayCount ?? 0, icon: CheckCircle, color: 'success' as const },
    ];
    // The API has no category parameter: this filter applies only to the current page.
    const visibleOrders = categoryFilter ? orders.filter(order => order.items.some(item => item.category === categoryFilter)) : orders;
    const waitTime = (order: DiagnosticOrderDto) => {
        if (order.status === 'Completed' || order.status === 'Cancelled') return new Date(order.orderedAtUtc).toLocaleString('vi-VN', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' });
        const orderedAt = new Date(order.orderedAtUtc).getTime();
        const minutes = Math.max(0, Math.floor(((lastUpdated?.getTime() ?? orderedAt) - orderedAt) / 60000));
        const text = minutes >= 60 ? `Chờ ${Math.floor(minutes / 60)} giờ ${minutes % 60} phút` : `Chờ ${minutes} phút`;
        return <span className={minutes > 60 ? styles.waitDanger : minutes > 30 ? styles.waitWarning : styles.waitTime}>{text}</span>;
    };

    return (
        <div className={styles.dashboard}>
            <PageHeader title="Bàn Làm Việc Cận Lâm Sàng" subtitle="Quản lý tiếp nhận mẫu, thực hiện xét nghiệm & chẩn đoán hình ảnh" actions={<Button disabled={loading} onClick={() => loadData(false)}><RefreshCw size={15} aria-hidden="true" />{loading ? 'Đang tải...' : 'Làm mới'}</Button>} />
            <div className={styles.statGrid}>
                {statFilters.map(stat => {
                    const Icon = stat.icon;
                    const active = statusFilter === stat.status && (stat.status !== 'Completed' || dateFilter === toLocalDateString(new Date()));
                    return <div key={stat.status} role="button" tabIndex={0} aria-label={`Lọc: ${stat.title}`} aria-pressed={active} className={`${styles.statFilter} ${active ? styles.statFilterActive : ''}`} onClick={event => { if (event.target === event.currentTarget) selectStat(stat.status); }} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectStat(stat.status); } }}>
                        <StatCard title={stat.title} value={stat.value} icon={<Icon size={24} />} color={stat.color} onClick={() => selectStat(stat.status)} />
                    </div>;
                })}
                <StatCard title="Chờ trưởng khoa duyệt" value={stats?.pendingVerificationCount ?? 0} icon={<Clock size={24} />} color="info" />
                <StatCard title="Bị trả lại cần sửa" value={stats?.returnedCount ?? 0} icon={<FileText size={24} />} color="warning" />
                <StatCard title="Chờ quá 30 phút" value={stats?.overdueCount ?? 0} icon={<Clock size={24} />} color="warning" />
            </div>
            {lastUpdated && <p className={styles.updatedAt}>Cập nhật lúc {lastUpdated.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}</p>}
            <Card className={styles.filters}>
                <div className={styles.filterRow}>
                    <form onSubmit={handleSearchSubmit} className={styles.searchForm}>
                        <Input type="text" value={searchTerm} onChange={e => setSearchTerm(e.target.value)} placeholder="Tìm theo mã phiếu, mã hẹn, tên bệnh nhân, SĐT..." prefix={<Search size={16} />} />
                        <Button type="primary" htmlType="submit" disabled={loading}>{loading ? 'Đang tải...' : 'Tìm'}</Button>
                    </form>
                    <div className={styles.filterControls}>
                        <Select aria-label="Trạng thái phiếu" className={styles.statusFilter} value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                            {value:'',label:'Tất cả trạng thái'}, {value:'Ordered',label:'Chờ thực hiện'}, {value:'InProgress',label:'Đang thực hiện'}, {value:'Completed',label:'Đã hoàn tất'}, {value:'Cancelled',label:'Đã hủy'}
                        ]} />
                        <Input aria-label="Ngày chỉ định" type="date" className={styles.dateFilter} value={dateFilter} onChange={e => { setDateFilter(e.target.value); setPage(1); }} />
                        <Select aria-label="Loại dịch vụ" className={styles.categoryFilter} value={categoryFilter} onChange={setCategoryFilter} options={[{ value: '', label: 'Tất cả' }, ...Object.entries(diagnosticCategoryLabels).map(([value, label]) => ({ value, label }))]} />
                        {(statusFilter || dateFilter || searchTerm || categoryFilter) && <Button onClick={() => { setStatusFilter(''); setDateFilter(''); setSearchTerm(''); setCategoryFilter(''); setPage(1); }}>Xóa lọc</Button>}
                    </div>
                </div>
            </Card>
            {loading ? <LoadingState message="Đang tải danh sách chỉ định..." /> : error ? <InlineError message={error} onRetry={() => { void loadData(); }} /> : visibleOrders.length === 0 ? <EmptyState icon={<FileText size={40} />} title="Không tìm thấy phiếu chỉ định nào" description="Thử thay đổi bộ lọc hoặc tìm kiếm lại." /> : (
                <div className={styles.tableScroll}>
                    <DataTable data={visibleOrders} keyExtractor={order => order.id} columns={[
                        {header:'Mã phiếu',accessor:order => <><strong className={styles.orderCode}>{order.orderCode}</strong><div className={styles.secondary}>#{order.appointmentCode}</div></>},
                        {header:'Bệnh nhân',accessor:order => <><strong>{order.patientName}</strong><div className={styles.secondary}>{order.patientPhone || 'Không có SĐT'} • {order.patientAge ? `${order.patientAge}t` : ''}</div></>},
                        {header:'Bác sĩ & Chuyên khoa',accessor:order => <><div>{order.orderingDoctorName}</div><div className={styles.secondary}>{order.specialtyName || '---'}</div></>},
                        {header:'Dịch vụ chỉ định',accessor:order => <><div className={styles.categoryTags}>{[...new Set(order.items.map(item => item.category))].map(category => { const Icon = categoryIcons[category]; return <Tag key={category} className={styles.categoryTag}><Icon size={13} aria-hidden="true" />{diagnosticCategoryLabels[category]}</Tag>; })}</div><div className={styles.serviceNames}>{order.items.slice(0, 2).map(item => <div key={item.id}>{item.serviceName}</div>)}{order.items.length > 2 && <span>+{order.items.length - 2}</span>}</div></>},
                        {header:'Thời gian chờ',accessor:waitTime},
                        {header:'Trạng thái',align:'center',accessor:order => <><StatusBadge status={diagnosticStatusTone[order.status]} label={diagnosticStatusLabels[order.status]} />{order.verificationStatus === 'Pending' && <Tag color="processing">Chờ duyệt</Tag>}{order.verificationStatus === 'Returned' && <Tag color="warning">Bị trả lại</Tag>}</>},
                        {header:'Thao tác',align:'right',accessor:order => <Button type={order.verificationStatus !== 'Pending' && (order.status === 'Ordered' || order.status === 'InProgress') ? 'primary' : 'default'} onClick={() => navigate(`/diagnostics/orders/${order.id}`)}>{order.status === 'Ordered' ? 'Tiếp nhận' : order.status === 'InProgress' ? order.verificationStatus === 'Pending' ? 'Xem (đang chờ duyệt)' : order.verificationStatus === 'Returned' ? 'Sửa kết quả' : 'Nhập kết quả' : 'Xem chi tiết'}<ArrowRight size={14} aria-hidden="true" /></Button>}
                    ]} />
                </div>
            )}
            {!loading && !error && <Pagination page={page} totalPages={Math.ceil(totalItems / 15)} totalRecords={totalItems} onPageChange={setPage} />}
        </div>
    );
};
