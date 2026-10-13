import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Alert, Button, Input, Modal, Tag } from 'antd';
import { diagnosticApi } from '../../api/diagnosticApi';
import { useAuth } from '../../auth/AuthContext';
import { useDialog } from '../../contexts/DialogContext';
import { DataTable, EmptyState, InlineError, LoadingState, PageHeader } from '../../components/common';
import type { DiagnosticOrderDto } from '../../types';
import { diagnosticNotificationTime, isCriticalDiagnosticFlag } from '../../utils/diagnosticLabels';
import styles from './DiagnosticVerificationReview.module.css';
import { DiagnosticResultPrint } from './DiagnosticResultPrint';

export const VerificationResultPrint = () => {
    const { id } = useParams<{ id: string }>();
    const { user } = useAuth();
    const returnPath = user?.role === 'Doctor' ? `/doctor/diagnostic-verification/${id}` : user?.role === 'DiagnosticTechnician' ? `/diagnostics/orders/${id}` : `/lab/orders/${id}`;
    return <DiagnosticResultPrint actor="verification" returnPath={returnPath} />;
};

export const DiagnosticVerificationReview = () => {
    const { id } = useParams<{ id: string }>();
    const { user } = useAuth();
    const navigate = useNavigate();
    const { showToast } = useDialog();
    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [note, setNote] = useState('');
    const [reason, setReason] = useState('');
    const [returnOpen, setReturnOpen] = useState(false);
    const [saving, setSaving] = useState(false);
    const version = useRef({ active: false });
    const home = user?.role === 'Doctor' ? '/doctor/diagnostic-verification' : user?.role === 'DiagnosticTechnician' ? '/diagnostics/verification' : '/lab';
    const load = useCallback(async (signal?: AbortSignal) => {
        setLoading(true); setError(''); setOrder(null);
        try {
            const response = await diagnosticApi.getVerificationOrder(Number(id));
            if (signal?.aborted) return;
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải phiếu duyệt.');
            setOrder(response.data);
        } catch (failure: unknown) {
            if (!signal?.aborted) setError((failure as { message?: string })?.message || 'Không thể tải phiếu duyệt.');
        } finally { if (!signal?.aborted) setLoading(false); }
    }, [id]);
    useEffect(() => {
        const controller = new AbortController();
        const lifecycle = { active: true };
        version.current = lifecycle;
        void Promise.resolve().then(() => { if (!controller.signal.aborted) { setNote(''); setReason(''); setReturnOpen(false); setSaving(false); void load(controller.signal); } });
        return () => { lifecycle.active = false; controller.abort(); };
    }, [load]);
    const submit = async (action: 'verify' | 'return') => {
        if (!order?.canCurrentUserVerify || saving || (action === 'return' && reason.trim().length < 5)) return;
        const requestVersion = version.current;
        const isCurrent = () => requestVersion.active && requestVersion === version.current;
        setSaving(true); setError('');
        try {
            const response = action === 'verify' ? await diagnosticApi.verifySharedOrder(order.id, { note: note.trim() || undefined, rowVersion: order.rowVersion }) : await diagnosticApi.returnSharedOrder(order.id, { reason: reason.trim(), rowVersion: order.rowVersion });
            if (!isCurrent()) return;
            if (!response.success) throw new Error(response.message || 'Không thể cập nhật phiếu.');
            showToast(action === 'verify' ? 'Đã duyệt kết quả.' : 'Đã trả lại kết quả.');
            navigate(home);
        } catch (failure: unknown) {
            if (!isCurrent()) return;
            const message = (failure as { message?: string })?.message || 'Không thể cập nhật phiếu.';
            if ((failure as { status?: number })?.status === 409) { setReturnOpen(false); await load(); }
            setError(message);
        } finally { if (isCurrent()) setSaving(false); }
    };
    if (loading) return <LoadingState message="Đang tải phiếu duyệt..." />;
    if (!order) return <div className={styles.page}><Link to={home}>Quay lại hàng chờ</Link>{error ? <InlineError message={error} onRetry={() => void load()} /> : <EmptyState title="Không tìm thấy phiếu duyệt." />}</div>;
    return <div className={styles.page}>
        <PageHeader title={`Duyệt kết quả ${order.orderCode}`} actions={<Link to={home}>Quay lại hàng chờ</Link>} />
        {error && <InlineError message={error} onRetry={() => void load()} />}
        <section className={styles.panel} aria-label="Thông tin phiếu"><h2>{order.patientName}</h2><p>{order.patientGender === 'Female' ? 'Nữ' : order.patientGender === 'Male' ? 'Nam' : '—'} • {order.patientAge ?? '—'} tuổi • {order.patientPhone || '—'}</p><p>Bác sĩ chỉ định: {order.orderingDoctorName}</p><p>Chỉ định lâm sàng: {order.clinicalIndication}</p><p>Người gửi: {order.submittedForVerificationByUserName || '—'} • {order.submittedForVerificationAtUtc ? diagnosticNotificationTime(order.submittedForVerificationAtUtc) : '—'}</p></section>
        {order.status === 'Completed' ? <section className={styles.panel}><Alert type="success" showIcon message={`Đã duyệt bởi ${order.verifiedByUserName || '—'} lúc ${order.verifiedAtUtc ? diagnosticNotificationTime(order.verifiedAtUtc) : '—'}`} />{order.verificationNote && <p>{order.verificationNote}</p>}<Link className={styles.link} to={`/diagnostic-verification/orders/${order.id}/result-print`}>In phiếu kết quả</Link></section> : order.verificationStatus === 'Returned' ? <Alert type="warning" showIcon message={`Đã trả lại: ${order.returnReason || '—'}`} /> : null}
        {order.items.filter(item => item.status !== 'Cancelled').map(item => <section key={item.id} className={styles.panel} aria-label={`Kết quả: ${item.serviceName}`}>
            <h2>{item.serviceName} <span className={styles.muted}>({item.serviceCode})</span></h2>
            {item.specimenType && <div className={styles.specimen}><strong>Mẫu bệnh phẩm</strong><p>{item.specimenType} • {item.containerType || '—'}</p><p>Trạng thái mẫu: {item.specimenStatus === 'Received' ? 'Đã nhận mẫu' : item.specimenStatus === 'Rejected' ? 'Mẫu bị từ chối' : item.specimenStatus === 'Collected' ? 'Đã lấy mẫu' : 'Chờ lấy mẫu'}</p><p>Mã vạch: {item.specimenBarcode || '—'}</p>{item.collectedByUserName && <p>Người lấy: {item.collectedByUserName}</p>}{item.receivedByUserName && <p>Người nhận: {item.receivedByUserName}</p>}{item.rejectionNote && <p>Lý do từ chối: {item.rejectionNote}</p>}</div>}
            {item.result?.hasCriticalValue && <Alert type="error" showIcon message="Có giá trị nguy hiểm" />}
            {item.result?.values?.length ? <div className={styles.table}><DataTable ariaLabel={`Chỉ số: ${item.serviceName}`} data={item.result.values} keyExtractor={value => value.parameterId} columns={[
                { header: 'Chỉ số', accessor: value => `${value.parameterName} (${value.parameterCode})` }, { header: 'Kết quả', accessor: value => <span className={isCriticalDiagnosticFlag(value.flag) ? styles.critical : undefined}>{value.value}</span> },
                { header: 'Đơn vị', accessor: 'unit' }, { header: 'Tham chiếu', accessor: 'referenceRangeText' }, { header: 'Cờ', accessor: value => <Tag className={isCriticalDiagnosticFlag(value.flag) ? styles.critical : undefined}>{value.flag === 'Normal' ? 'Bình thường' : `${value.flag.endsWith('High') ? 'H' : 'L'}${isCriticalDiagnosticFlag(value.flag) ? ' — NGUY HIỂM' : ''}`}</Tag> },
            ]} /></div> : <p className={styles.resultText}>{item.result?.resultText || 'Chưa có kết quả.'}</p>}
            {item.result?.conclusion && <p>Kết luận: {item.result.conclusion}</p>}<p className={styles.muted}>Người nhập: {item.result?.resultedByUserName || '—'}</p>
        </section>)}
        {order.canCurrentUserVerify ? <section className={styles.panel}><label htmlFor="verification-note">Ghi chú duyệt (không bắt buộc)</label><Input.TextArea id="verification-note" maxLength={500} value={note} disabled={saving} onChange={event => setNote(event.target.value)} /><div className={styles.actions}><Button type="primary" disabled={saving} onClick={() => void submit('verify')}>{saving ? 'Đang xử lý...' : 'Duyệt kết quả'}</Button><Button danger disabled={saving} onClick={() => { setReason(''); setReturnOpen(true); }}>Trả lại</Button></div></section> : order.status !== 'Completed' && <Alert type="info" showIcon message={order.verificationStatus === 'Returned' ? 'Phiếu đã trả lại, cần người nhập chỉnh sửa và gửi duyệt lại.' : user?.role === 'Doctor' ? 'Bác sĩ chỉ định không được tự duyệt kết quả phiếu của mình.' : 'Bạn là người gửi hoặc nhập kết quả nên không thể tự duyệt.'} />}
        <Modal title="Trả lại kết quả" open={returnOpen} getContainer={false} onCancel={() => { if (!saving) setReturnOpen(false); }} footer={<div className={styles.actions}><Button disabled={saving} onClick={() => setReturnOpen(false)}>Hủy</Button><Button danger type="primary" disabled={saving || reason.trim().length < 5} onClick={() => void submit('return')}>{saving ? 'Đang trả lại...' : 'Xác nhận trả lại'}</Button></div>}>
            <label htmlFor="verification-reason">Lý do trả lại (ít nhất 5 ký tự)</label><Input.TextArea id="verification-reason" maxLength={500} disabled={saving} value={reason} onChange={event => setReason(event.target.value)} />
        </Modal>
    </div>;
};
