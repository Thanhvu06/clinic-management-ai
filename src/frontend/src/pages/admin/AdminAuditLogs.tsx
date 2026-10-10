import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { RefreshCw, Clock, User } from 'lucide-react';
import { Button, Select, Tag, Typography } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminAuditLogs.module.css';

interface AuditLog {
    id: number;
    userId: string;
    userFullName: string;
    action: string;
    entityName: string;
    entityId: string;
    description: string;
    createdAt: string;
}

export const AdminAuditLogs: React.FC = () => {
    const [logs, setLogs] = useState<AuditLog[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [actionFilter, setActionFilter] = useState('');
    const [entityFilter, setEntityFilter] = useState('');

    const fetchLogs = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '15'
            });
            if (actionFilter) params.append('action', actionFilter);
            if (entityFilter) params.append('entityName', entityFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/audit-logs?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải nhật ký kiểm toán.');
            if (res.success && res.data) {
                setLogs(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải nhật ký kiểm toán.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchLogs();
    }, [page, actionFilter, entityFilter]);

    const formatDateTime = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                dateStyle: 'short',
                timeStyle: 'medium'
            }).format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    const getActionBadge = (action: string) => {
        const act = action.toUpperCase();
        if (act.includes('CREATE') || act.includes('ADD') || act.includes('INITIAL')) {
            return <Tag color="success">{action}</Tag>;
        }
        if (act.includes('UPDATE') || act.includes('EDIT') || act.includes('CHANGE')) {
            return <Tag color="processing">{action}</Tag>;
        }
        if (act.includes('DELETE') || act.includes('CANCEL') || act.includes('REMOVE')) {
            return <Tag color="error">{action}</Tag>;
        }
        return <Tag color="default">{action}</Tag>;
    };

    return (
        <div className={styles.auditPage}>
            <PageHeader title="Nhật ký kiểm toán hệ thống (Audit Logs)" actions={
                <Button icon={<RefreshCw size={16} />} onClick={fetchLogs}>Làm mới</Button>
            } />
            <FilterBar>
                <Select className={styles.auditFilter} aria-label="Loại hành động" value={actionFilter}
                    onChange={value => { setActionFilter(value); setPage(1); }} options={[
                        { value: '', label: 'Tất cả loại hành động' }, { value: 'Create', label: 'Create / Thêm mới' },
                        { value: 'Update', label: 'Update / Cập nhật' }, { value: 'Delete', label: 'Delete / Xóa' },
                        { value: 'StatusChange', label: 'StatusChange / Đổi trạng thái' }, { value: 'Login', label: 'Login / Đăng nhập' }
                    ]} />
                <Select className={styles.auditFilter} aria-label="Đối tượng tác động" value={entityFilter}
                    onChange={value => { setEntityFilter(value); setPage(1); }} options={[
                        { value: '', label: 'Tất cả đối tượng tác động' }, { value: 'User', label: 'User / Tài khoản' },
                        { value: 'Doctor', label: 'Doctor / Bác sĩ' }, { value: 'Specialty', label: 'Specialty / Chuyên khoa' },
                        { value: 'Appointment', label: 'Appointment / Lịch hẹn' }, { value: 'HealthPackage', label: 'HealthPackage / Gói khám' },
                        { value: 'Medicine', label: 'Medicine / Thuốc' }
                    ]} />
            </FilterBar>
            {loading ? <LoadingState message="Đang tải nhật ký kiểm toán..." /> : loadError ? <InlineError message={loadError} onRetry={fetchLogs} />
                : logs.length === 0 ? <EmptyState title="Chưa có bản ghi nhật ký kiểm toán nào." /> : <>
                    <div className={styles.auditTable}>
                        <DataTable data={logs} keyExtractor={log => log.id} columns={[
                            { header: 'Thời gian', accessor: log => <div className={styles.auditTime}><Clock size={14} /><span>{formatDateTime(log.createdAt)}</span></div> },
                            { header: 'Người thực hiện', accessor: log => <div className={styles.auditActor}><User size={14} /><span>{log.userFullName}</span></div> },
                            { header: 'Hành động', accessor: log => getActionBadge(log.action) },
                            { header: 'Thực thể', accessor: log => <Tag>{log.entityName}</Tag> },
                            { header: 'Mã tham chiếu', accessor: log => <Typography.Text code>{log.entityId || '-'}</Typography.Text> },
                            { header: 'Mô tả chi tiết', accessor: 'description' }
                        ]} />
                    </div>
                    <Pagination page={page} totalPages={Math.ceil(totalItems / 15)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.auditTotal}>Tổng số: {totalItems} sự kiện ghi nhận</div>}
        </div>
    );
};
