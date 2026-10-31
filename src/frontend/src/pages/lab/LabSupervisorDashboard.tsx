import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Tag } from 'antd';
import { RefreshCw } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import { DataTable, EmptyState, InlineError, LoadingState, PageHeader, Pagination } from '../../components/common';
import type { DiagnosticOrderDto } from '../../types';
import { diagnosticNotificationTime } from '../../utils/diagnosticLabels';
import styles from './LabSupervisorDashboard.module.css';

export const VerificationQueue = ({ dashboard = false }: { dashboard?: boolean }) => {
    const [items, setItems] = useState<DiagnosticOrderDto[]>([]);
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [loadedAt, setLoadedAt] = useState(0);
    const load = useCallback(async (signal?: AbortSignal) => {
        setLoading(true); setError('');
        const queueTask = async () => {
            try {
                const response = await diagnosticApi.getSharedVerificationQueue({ page, pageSize: 15 });
                if (signal?.aborted) return;
                if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải hàng chờ duyệt.');
                setItems(response.data.items); setTotal(response.data.totalItems); setLoadedAt(Date.now());
            } catch (failure: unknown) {
                if (!signal?.aborted) setError((failure as { message?: string })?.message || 'Không thể tải hàng chờ duyệt.');
            } finally { if (!signal?.aborted) setLoading(false); }
        };
        await queueTask();
    }, [page]);
    useEffect(() => {
        const controller = new AbortController();
        void Promise.resolve().then(() => { if (!controller.signal.aborted) void load(controller.signal); });
        return () => controller.abort();
    }, [load]);
    useEffect(() => {
        const timer = window.setInterval(() => setLoadedAt(Date.now()), 60000);
        return () => window.clearInterval(timer);
    }, []);
    const minutes = (order: DiagnosticOrderDto) => order.submittedForVerificationAtUtc ? Math.max(0, Math.floor((loadedAt - new Date(order.submittedForVerificationAtUtc).getTime()) / 60000)) : 0;
    return <div className={styles.page}>
        <PageHeader title={dashboard ? 'Tổng quan xét nghiệm' : 'Duyệt kết quả CLS'} subtitle={dashboard ? 'Đối chiếu kết quả trong cơ sở được phân công' : 'Duyệt kết quả do bác sĩ khác chỉ định'} actions={<Button disabled={loading} onClick={() => void load()}><RefreshCw size={16} aria-hidden="true" />{loading ? 'Đang tải...' : 'Làm mới'}</Button>} />
        {loading ? <LoadingState message="Đang tải hàng chờ duyệt..." /> : error ? <InlineError message={error} onRetry={() => void load()} /> : <>
            {items.length === 0 ? <EmptyState title="Không có phiếu chờ duyệt" /> : <div className={styles.table}>
                <DataTable ariaLabel="Phiếu chờ duyệt" data={items} keyExtractor={order => order.id} columns={[
                    { header: 'Mã phiếu', accessor: 'orderCode' }, { header: 'Bệnh nhân', accessor: 'patientName' },
                    { header: 'Dịch vụ', accessor: order => <>{order.items.filter(item => item.status !== 'Cancelled').map(item => <div key={item.id}>{item.serviceName}</div>)}{order.items.some(item => item.status !== 'Cancelled' && item.result?.hasCriticalValue) && <Tag className={styles.danger}>Có giá trị nguy hiểm</Tag>}{order.hasDeltaFailure && <Tag color="warning">Lệch delta</Tag>}</> },
                    { header: 'Người gửi', accessor: order => order.submittedForVerificationByUserName || '—' },
                    { header: 'Thời gian chờ', accessor: order => <div className={minutes(order) > 60 ? styles.danger : minutes(order) > 30 ? styles.warning : undefined}><div>{order.submittedForVerificationAtUtc ? diagnosticNotificationTime(order.submittedForVerificationAtUtc) : '—'}</div><strong>{minutes(order)} phút</strong>{minutes(order) > 60 ? <div>Chờ quá 60 phút</div> : minutes(order) > 30 ? <div>Chờ quá 30 phút</div> : null}</div> },
                    { header: 'Thao tác', accessor: order => <Link className={styles.link} to={dashboard ? `/lab/orders/${order.id}` : `/doctor/diagnostic-verification/${order.id}`}>Mở để duyệt</Link> },
                ]} />
                <Pagination page={page} totalPages={Math.ceil(total / 15)} totalRecords={total} onPageChange={setPage} />
            </div>}
        </>}
    </div>;
};

export const LabSupervisorDashboard = () => <VerificationQueue dashboard />;
