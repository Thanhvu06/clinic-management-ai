import { formatPrescriptionCode } from '../../utils/prescriptionCode';
import React, { useState, useEffect, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { RefreshCw } from 'lucide-react';
import { Button } from 'antd';
import { PageHeader, MetricStrip, DataTable, LoadingState, EmptyState, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import { getApiErrorMessage } from '../../utils/apiError';
import { formatDateTime } from '../../utils/formatters';
import styles from './PharmacyDashboard.module.css';
interface PharmacyStats {
    pendingPrescriptionsCount: number;
    dispensedTodayCount: number;
    lowStockCount: number;
    totalActiveMedicines: number;
}

interface PendingPrescription {
    id: number;
    code?: string;
    prescriptionCode?: string;
    patientName: string;
    doctorName: string;
    itemCount: number;
    createdAt: string;
}

export const PharmacyDashboard: React.FC = () => {
    const navigate = useNavigate();
    const [stats, setStats] = useState<PharmacyStats | null>(null);
    const [loading, setLoading] = useState(true);
    const [pending, setPending] = useState<PendingPrescription[]>([]);
    const [pendingLoading, setPendingLoading] = useState(true);
    const [pendingError, setPendingError] = useState<string | null>(null);

    const fetchStats = useCallback(async () => {
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
    }, []);

    const fetchPending = useCallback(async () => {
        setPendingLoading(true);
        setPendingError(null);
        try {
            const res = await axiosClient.get<any, ApiResponse<{ items: PendingPrescription[] }>>('/pharmacy/prescriptions?page=1&pageSize=5&status=Issued');
            if (!res.success || !res.data) throw new Error(res.message);
            setPending(res.data.items ?? []);
        } catch (err) {
            setPendingError(getApiErrorMessage(err, 'Không thể tải danh sách đơn chờ cấp.'));
        } finally {
            setPendingLoading(false);
        }
    }, []);

    const refresh = useCallback(() => {
        void fetchStats();
        void fetchPending();
    }, [fetchStats, fetchPending]);

    useEffect(() => {
        void Promise.resolve().then(refresh);
    }, [refresh]);

    const pendingColumns: DataTableColumn<PendingPrescription>[] = [
        { header: 'Mã đơn', accessor: item => formatPrescriptionCode(item) },
        { header: 'Thời gian kê', accessor: item => <span className={styles.numeric}>{formatDateTime(item.createdAt)}</span>, width: 150 },
        { header: 'Người bệnh', accessor: 'patientName' },
        { header: 'Bác sĩ', accessor: 'doctorName' },
        { header: 'Số thuốc', accessor: item => <span className={styles.numeric}>{item.itemCount}</span>, align: 'right', width: 100 },
        { header: 'Thao tác', align: 'right', width: 120, accessor: item => <Button size="small" onClick={() => navigate(`/pharmacy/prescriptions?id=${item.id}`)}>Cấp phát</Button> },
    ];

    const busy = loading || pendingLoading;

    return (
        <div className={styles.dashboard}>
            <PageHeader title="Bàn làm việc dược sĩ" actions={<Button onClick={refresh} disabled={busy}><RefreshCw size={16} aria-hidden="true" /> {busy ? 'Đang tải...' : 'Cập nhật số liệu'}</Button>} />
            <MetricStrip
                ariaLabel="Số liệu nhà thuốc"
                items={[
                    { label: 'Đơn chờ cấp', value: loading ? '...' : (stats?.pendingPrescriptionsCount ?? 0), tone: 'attention', to: '/pharmacy/prescriptions' },
                    { label: 'Đã cấp hôm nay', value: loading ? '...' : (stats?.dispensedTodayCount ?? 0) },
                    { label: 'Thuốc dưới định mức', value: loading ? '...' : (stats?.lowStockCount ?? 0), tone: 'attention', to: '/pharmacy/inventory' },
                ]}
            />
            <section className={styles.section} aria-label="Đơn chờ cấp">
                <div className={styles.sectionHeader}>
                    <h2 className={styles.sectionTitle}>Đơn chờ cấp</h2>
                    <Link to="/pharmacy/prescriptions">Xem tất cả</Link>
                </div>
                {pendingLoading ? (
                    <LoadingState message="Đang tải đơn chờ cấp..." height="160px" />
                ) : pendingError ? (
                    <InlineError message={pendingError} onRetry={fetchPending} />
                ) : pending.length === 0 ? (
                    <EmptyState title="Không có đơn chờ cấp" />
                ) : (
                    <DataTable columns={pendingColumns} data={pending} keyExtractor={item => item.id} ariaLabel="Đơn chờ cấp" />
                )}
            </section>
        </div>
    );
};
