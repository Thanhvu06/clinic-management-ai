import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Button } from 'antd';
import { diagnosticApi } from '../../api/diagnosticApi';
import { EmptyState, InlineError, LoadingState } from '../../components/common';
import type { DiagnosticOrderDto } from '../../types';
import { diagnosticNotificationTime } from '../../utils/diagnosticLabels';
import { encodeCode39 } from '../../utils/code39';
import styles from './SpecimenLabelPrint.module.css';

export const SpecimenLabelPrint = () => {
    const { id } = useParams<{ id: string }>();
    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const load = useCallback(async (signal?: AbortSignal) => {
        setLoading(true); setError(''); setOrder(null);
        try {
            const response = await diagnosticApi.getTechnicianOrderById(Number(id));
            if (signal?.aborted) return;
            if (!response.success) throw new Error(response.message || 'Không thể tải nhãn mẫu.');
            setOrder(response.data || null);
        } catch (err: unknown) {
            if (!signal?.aborted) setError(err instanceof Error ? err.message : 'Không thể tải nhãn mẫu.');
        } finally { if (!signal?.aborted) setLoading(false); }
    }, [id]);
    useEffect(() => {
        const controller = new AbortController();
        void Promise.resolve().then(() => { if (!controller.signal.aborted) void load(controller.signal); });
        return () => controller.abort();
    }, [load]);
    if (loading) return <LoadingState message="Đang tải nhãn mẫu..." />;
    if (error) return <InlineError message={error} onRetry={() => void load()} />;
    const items = order?.items.filter(item => item.specimenBarcode) || [];
    if (!order || items.length === 0) return <EmptyState title="Chưa có mẫu nào được lấy" />;
    return <div className={styles.page}>
        <div className={styles.toolbar}><Link to={`/diagnostics/orders/${order.id}`}>Quay lại phiếu</Link><Button type="primary" onClick={() => window.print()}>In nhãn</Button></div>
        <h1 className={styles.title}>Nhãn mẫu bệnh phẩm — {order.orderCode}</h1>
        <div className={styles.labels}>
            {items.map(item => {
                const barcode = encodeCode39(item.specimenBarcode!);
                return <article key={item.id} className={styles.label} aria-label={`Nhãn ${item.specimenBarcode}`}>
                    <strong className={styles.patientName}>{order.patientName}</strong>
                    <div>{order.patientGender?.toLowerCase() === 'female' ? 'Nữ' : order.patientGender?.toLowerCase() === 'male' ? 'Nam' : 'Chưa cập nhật'} / {order.patientAge ?? '—'} tuổi • {order.orderCode}</div>
                    <div className={styles.serviceName}>{item.serviceName}</div>
                    <div className={styles.sampleType}>{item.specimenType} / {item.containerType}</div>
                    <div>Lấy mẫu: {item.collectedAtUtc ? diagnosticNotificationTime(item.collectedAtUtc) : '—'}</div>
                    <svg className={styles.barcode} role="img" aria-label={`Mã vạch ${item.specimenBarcode}`} viewBox={`0 0 ${barcode.width} 28`} preserveAspectRatio="none">
                        {barcode.bars.map(bar => <rect key={bar.x} x={bar.x} y={0} width={bar.width} height={28} />)}
                    </svg>
                    <div className={styles.barcodeText}>{item.specimenBarcode}</div>
                </article>;
            })}
        </div>
    </div>;
};
