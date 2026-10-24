import { formatAuditTime } from '../../utils/auditTime';
import { getAuditActionLabel, getAuditEntityLabel } from '../../utils/auditLogLabels';
import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Button } from 'antd';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { PageHeader, MetricStrip, DataTable, LoadingState, EmptyState, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import { getApiErrorMessage } from '../../utils/apiError';
import { formatDateTime } from '../../utils/formatters';
import styles from './AdminDashboard.module.css';

interface AdminStats {
    totalAppointmentsToday: number;
    totalPatients: number;
    totalDoctors: number;
    lowStockMedicinesCount: number;
}

interface PendingLeave {
    id: number;
    doctorName: string;
    startDateTime: string;
    endDateTime: string;
    reason: string;
}

interface AuditLogEntry {
    action: string;
    entityName: string;
    id: number;
    userFullName: string;
    description: string;
    createdAt: string;
}

function usePagedList<T>(url: string, fallback: string) {
    const [items, setItems] = useState<T[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const load = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await axiosClient.get<any, ApiResponse<{ items: T[] }>>(url);
            if (!res.success || !res.data) throw new Error(res.message);
            setItems(res.data.items ?? []);
        } catch (err) {
            setError(getApiErrorMessage(err, fallback));
        } finally {
            setLoading(false);
        }
    }, [url, fallback]);

    useEffect(() => {
        void Promise.resolve().then(load);
    }, [load]);

    return { items, loading, error, load };
}

export const AdminDashboard: React.FC = () => {
    const navigate = useNavigate();
    const [stats, setStats] = useState<AdminStats | null>(null);
    const [statsLoading, setStatsLoading] = useState(true);
    const [statsError, setStatsError] = useState<string | null>(null);
    const leaves = usePagedList<PendingLeave>('/admin/leave-requests?page=1&pageSize=5&status=Pending', 'Không thể tải đơn nghỉ phép chờ duyệt.');
    const logs = usePagedList<AuditLogEntry>('/admin/audit-logs?page=1&pageSize=8', 'Không thể tải hoạt động gần đây.');

    const loadStats = useCallback(async () => {
        setStatsLoading(true);
        setStatsError(null);
        try {
            const res = await axiosClient.get<any, ApiResponse<AdminStats>>('/admin/stats');
            if (!res.success || !res.data) throw new Error(res.message);
            setStats(res.data);
        } catch (err) {
            setStatsError(getApiErrorMessage(err, 'Không thể tải số liệu tổng quan.'));
        } finally {
            setStatsLoading(false);
        }
    }, []);

    useEffect(() => {
        void Promise.resolve().then(loadStats);
    }, [loadStats]);

    const metric = (value: number | undefined) => statsLoading ? '...' : statsError ? '—' : (value ?? 0);

    const leaveColumns: DataTableColumn<PendingLeave>[] = [
        { header: 'Bác sĩ', accessor: 'doctorName' },
        { header: 'Thời gian nghỉ', accessor: item => <span className={styles.numeric}>{formatDateTime(item.startDateTime)} – {formatDateTime(item.endDateTime)}</span> },
        { header: 'Thao tác', align: 'right', accessor: () => <Button size="small" onClick={() => navigate('/admin/leaves')}>Duyệt</Button> },
    ];

    const logColumns: DataTableColumn<AuditLogEntry>[] = [
        { header: 'Thời gian', accessor: item => <span className={styles.numeric}>{formatAuditTime(item.createdAt)}</span> },
        { header: 'Người làm', accessor: 'userFullName', className: styles.actor },
        { header: 'Mô tả', className: styles.description, accessor: item => <>
            <span className={styles.activityHeading}><strong>{getAuditActionLabel(item.action)}</strong><span className={styles.activityEntity}>{getAuditEntityLabel(item.entityName)}</span></span>
            {item.description && <span className={styles.activityText}>{item.description}</span>}
        </> },
    ];

    return (
        <div className={styles.dashboard}>
            <PageHeader title="Tổng quan" />
            {statsError && <InlineError message={statsError} onRetry={loadStats} />}
            <MetricStrip
                ariaLabel="Số liệu hệ thống"
                items={[
                    { label: 'Lịch hôm nay', value: metric(stats?.totalAppointmentsToday) },
                    { label: 'Bệnh nhân', value: metric(stats?.totalPatients) },
                    { label: 'Bác sĩ đang công tác', value: metric(stats?.totalDoctors) },
                    { label: 'Thuốc sắp hết hàng', value: metric(stats?.lowStockMedicinesCount), tone: 'attention', to: '/admin/medicines' },
                ]}
            />
            <div className={styles.panels}>
                <section className={styles.panel} aria-label="Đơn nghỉ phép chờ duyệt">
                    <div className={styles.panelHeader}>
                        <h2 className={styles.panelTitle}>Đơn nghỉ phép chờ duyệt</h2>
                        <Link to="/admin/leaves">Xem tất cả</Link>
                    </div>
                    {leaves.loading ? (
                        <LoadingState message="Đang tải đơn nghỉ phép..." height="160px" />
                    ) : leaves.error ? (
                        <InlineError message={leaves.error} onRetry={leaves.load} />
                    ) : leaves.items.length === 0 ? (
                        <EmptyState title="Không có đơn nghỉ phép chờ duyệt" />
                    ) : (
                        <DataTable columns={leaveColumns} data={leaves.items.slice(0, 5)} keyExtractor={item => item.id} ariaLabel="Đơn nghỉ phép chờ duyệt" />
                    )}
                </section>
                <section className={styles.panel} aria-label="Hoạt động gần đây">
                    <div className={styles.panelHeader}>
                        <h2 className={styles.panelTitle}>Hoạt động gần đây</h2>
                        <Link to="/admin/audit-logs">Xem nhật ký</Link>
                    </div>
                    {logs.loading ? (
                        <LoadingState message="Đang tải hoạt động..." height="160px" />
                    ) : logs.error ? (
                        <InlineError message={logs.error} onRetry={logs.load} />
                    ) : logs.items.length === 0 ? (
                        <EmptyState title="Chưa có hoạt động nào" />
                    ) : (
                        <DataTable columns={logColumns} data={logs.items.slice(0, 8)} keyExtractor={item => item.id} ariaLabel="Hoạt động gần đây" />
                    )}
                </section>
            </div>
        </div>
    );
};
