import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Tag } from 'antd';
import { RefreshCw } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import { DataTable, EmptyState, InlineError, LoadingState, PageHeader, Pagination } from '../../components/common';
import type { DiagnosticOrderDto } from '../../types';
import { diagnosticNotificationTime } from '../../utils/diagnosticLabels';
import styles from './TechnicianVerificationQueue.module.css';

export const TechnicianVerificationQueue = () => {
    const [items, setItems] = useState<DiagnosticOrderDto[]>([]);
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [loadedAt, setLoadedAt] = useState(0);
    const load = useCallback(async (signal?: AbortSignal) => {
        setLoading(true); setError('');
        try {
            const response = await diagnosticApi.getVerificationQueue({ page, pageSize: 15 });
            if (signal?.aborted) return;
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải phiếu chờ duyệt.');
            setItems(response.data.items); setTotal(response.data.totalItems); setLoadedAt(Date.now());
        } catch (failure: unknown) {
            if (!signal?.aborted) setError((failure as { message?: string })?.message || 'Không thể tải phiếu chờ duyệt.');
        } finally { if (!signal?.aborted) setLoading(false); }
    }, [page]);
    useEffect(() => {
        const controller = new AbortController();
        void Promise.resolve().then(() => { if (!controller.signal.aborted) void load(controller.signal); });
        return () => controller.abort();
    }, [load]);
    return <div className={styles.page}>
        <PageHeader title="Chờ duyệt kết quả" subtitle="Đối chiếu kết quả do kỹ thuật viên khác nhập" actions={<Button disabled={loading} onClick={() => void load()}><RefreshCw size={16} aria-hidden="true" />{loading ? 'Đang tải...' : 'Làm mới'}</Button>} />
        {loading ? <LoadingState message="Đang tải phiếu chờ duyệt..." /> : error ? <InlineError message={error} onRetry={() => void load()} /> : items.length === 0 ? <EmptyState title="Không có phiếu chờ duyệt" /> : <div className={styles.table}>
            <DataTable ariaLabel="Phiếu chờ duyệt" data={items} keyExtractor={order => order.id} columns={[
                { header: 'Mã phiếu', accessor: 'orderCode' },
                { header: 'Bệnh nhân', accessor: 'patientName' },
                { header: 'Dịch vụ', accessor: order => <>{order.items.filter(item => item.status !== 'Cancelled').map(item => <div key={item.id}>{item.serviceName}</div>)}{order.items.some(item => item.result?.hasCriticalValue) && <Tag className={styles.critical}>Có giá trị nguy hiểm</Tag>}</> },
                { header: 'Người gửi', accessor: order => order.submittedForVerificationByUserName || '—' },
                { header: 'Thời gian chờ duyệt', accessor: order => order.submittedForVerificationAtUtc ? <><div>{diagnosticNotificationTime(order.submittedForVerificationAtUtc)}</div><div>{Math.max(0, Math.floor((loadedAt - new Date(order.submittedForVerificationAtUtc).getTime()) / 60000))} phút</div></> : '—' },
                { header: 'Thao tác', accessor: order => <Link className={styles.link} to={`/diagnostics/orders/${order.id}`}>Mở phiếu</Link> },
            ]} />
            <Pagination page={page} totalPages={Math.ceil(total / 15)} totalRecords={total} onPageChange={setPage} />
        </div>}
    </div>;
};
