import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Button } from 'antd';
import { diagnosticApi } from '../../api/diagnosticApi';
import { EmptyState, InlineError, LoadingState } from '../../components/common';
import type { DiagnosticOrderDto } from '../../types';
import { diagnosticNotificationTime, isCriticalDiagnosticFlag } from '../../utils/diagnosticLabels';
import { encodeCode39 } from '../../utils/code39';
import styles from './DiagnosticResultPrint.module.css';

export const DiagnosticResultPrint = ({ actor = 'technician' }: { actor?: 'technician' | 'doctor' }) => {
    const { id } = useParams<{ id: string }>();
    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const load = useCallback(async (signal?: AbortSignal) => {
        setLoading(true); setError(''); setOrder(null);
        try {
            const response = await (actor === 'doctor' ? diagnosticApi.getDoctorOrderById(Number(id)) : diagnosticApi.getTechnicianOrderById(Number(id)));
            if (signal?.aborted) return;
            if (!response.success) throw new Error(response.message || 'Không thể tải phiếu kết quả.');
            setOrder(response.data || null);
        } catch (failure: unknown) {
            if (!signal?.aborted) setError((failure as { message?: string })?.message || 'Không thể tải phiếu kết quả.');
        } finally { if (!signal?.aborted) setLoading(false); }
    }, [id, actor]);
    useEffect(() => {
        const controller = new AbortController();
        void Promise.resolve().then(() => { if (!controller.signal.aborted) void load(controller.signal); });
        return () => controller.abort();
    }, [load]);
    if (loading) return <LoadingState message="Đang tải phiếu kết quả..." />;
    if (error) return <InlineError message={error} onRetry={() => void load()} />;
    if (!order) return <EmptyState title="Không tìm thấy phiếu kết quả." />;
    if (order.status !== 'Completed' && order.verificationStatus !== 'Verified') return <EmptyState title="Kết quả chưa được duyệt, chưa thể in." />;
    const performedBy = [...new Set(order.items.map(item => item.result?.resultedByUserName).filter(Boolean))].join(', ') || order.completedByUserName || '—';
    const time = (value?: string | null) => value ? diagnosticNotificationTime(/(?:Z|[+-]\d{2}:\d{2})$/.test(value) ? value : `${value}Z`) : '—';
    return <div className={styles.page}>
        <div className={styles.toolbar}><Link to={actor === 'doctor' ? '/doctor/appointments' : `/diagnostics/orders/${order.id}`}>Quay lại</Link><Button type="primary" onClick={() => window.print()}>In</Button></div>
        <article className={styles.sheet} aria-label="Phiếu kết quả cận lâm sàng">
            <header className={styles.header}><strong>{order.facilityName || 'ClinicCare AI'}</strong><h1>PHIẾU KẾT QUẢ CẬN LÂM SÀNG</h1><p>Mã phiếu: <strong>{order.orderCode}</strong></p></header>
            <section className={styles.patient} aria-label="Thông tin bệnh nhân">
                <p>Họ tên: <strong>{order.patientName}</strong></p><p>Giới tính: {order.patientGender === 'Female' ? 'Nữ' : order.patientGender === 'Male' ? 'Nam' : '—'} • Tuổi: {order.patientAge ?? '—'} • SĐT: {order.patientPhone || '—'}</p>
                <p>Bác sĩ chỉ định: {order.orderingDoctorName}</p><p>Chỉ định lâm sàng: {order.clinicalIndication}</p>
            </section>
            {order.items.filter(item => item.status !== 'Cancelled').map(item => {
                const barcode = item.specimenBarcode ? encodeCode39(item.specimenBarcode) : null;
                return <section key={item.id} className={styles.service} aria-label={`Kết quả: ${item.serviceName}`}>
                    <h2>{item.serviceName} <span className={styles.code}>({item.serviceCode})</span></h2>
                    {item.result?.values?.length ? <table aria-label={`Chỉ số: ${item.serviceName}`} className={styles.values}>
                        <thead><tr><th>Chỉ số</th><th>Kết quả</th><th>Đơn vị</th><th>Tham chiếu</th><th>Cờ</th></tr></thead>
                        <tbody>{item.result.values.map(value => <tr key={value.parameterId} className={isCriticalDiagnosticFlag(value.flag) ? styles.critical : undefined}>
                            <td>{value.parameterName} ({value.parameterCode})</td><td>{value.value}</td><td>{value.unit}</td><td>{value.referenceRangeText}</td>
                            <td>{value.flag === 'Normal' ? '—' : `${value.flag.endsWith('High') || value.flag === 'High' ? 'H' : 'L'}${isCriticalDiagnosticFlag(value.flag) ? ' *' : ''}`}</td>
                        </tr>)}</tbody>
                    </table> : <p className={styles.textResult}>{item.result?.resultText || 'Chưa có kết quả.'}</p>}
                    {item.result?.hasCriticalValue && <p className={styles.critical}>* Giá trị nguy hiểm</p>}
                    {item.result?.conclusion && <p>Kết luận: {item.result.conclusion}</p>}
                    {barcode && <div className={styles.barcodeBlock}><svg className={styles.barcode} role="img" aria-label={`Mã vạch ${item.specimenBarcode}`} viewBox={`0 0 ${barcode.width} 28`} preserveAspectRatio="none">{barcode.bars.map(bar => <rect key={bar.x} x={bar.x} y={0} width={bar.width} height={28} />)}</svg><span>{item.specimenBarcode}</span></div>}
                </section>;
            })}
            <footer className={styles.footer}><p>Người thực hiện: {performedBy} • {time(order.completedAtUtc)}</p><p>Người duyệt: <strong>{order.verifiedByUserName || '—'}</strong> • {time(order.verifiedAtUtc)}</p>{order.verificationNote && <p>Ghi chú duyệt: {order.verificationNote}</p>}<p>Kết quả chỉ có giá trị khi có chữ ký người duyệt</p><div className={styles.signature}>Chữ ký người duyệt</div></footer>
        </article>
    </div>;
};
