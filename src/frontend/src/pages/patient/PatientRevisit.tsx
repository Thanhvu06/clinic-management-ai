import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Col, Descriptions, Form, Input, Modal, Row } from 'antd';
import { useNavigate } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { Breadcrumb } from '../../components/Breadcrumb';
import { PageHeader } from '../../components/common/PageHeader';
import { StatusBadge } from '../../components/common/StatusBadge';
import { EmptyState } from '../../components/common/EmptyState';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';
import { buildRevisitBookingUrl } from '../../utils/revisitBookingHelper';
import { formatDateOnly } from '../../utils/formatters';
import styles from './PatientRevisit.module.css';

interface RevisitRequestView {
    id: number; appointmentId: number; doctorId: number; specialtyId: number; suggestedDate: string;
    note?: string; status: string; newAppointmentId?: number | null; doctorName: string; specialtyName: string;
}
const statuses: Record<string, { status: string; label: string }> = {
    PendingPatientResponse: { status: 'Pending', label: 'Chờ phản hồi' },
    Accepted: { status: 'Approved', label: 'Đã chấp nhận' },
    Rejected: { status: 'Rejected', label: 'Đã từ chối' },
    ConvertedToAppointment: { status: 'Confirmed', label: 'Đã đặt lịch' }
};
export const PatientRevisit: React.FC = () => {
    const [requests, setRequests] = useState<RevisitRequestView[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [actionLoading, setActionLoading] = useState<number | null>(null);
    const [rejectModal, setRejectModal] = useState({ isOpen: false, id: null as number | null, reason: '', error: '' });
    const { showAlert } = useDialog();
    const navigate = useNavigate();
    const fetchRequests = useCallback(async () => {
        setLoading(true); setError('');
        try {
            const res = await axiosClient.get<any, ApiResponse<{ items: RevisitRequestView[] } | RevisitRequestView[]>>('/revisit-requests');
            if (!res.success) throw new Error(res.message || 'Không thể tải lời mời tái khám.');
            setRequests(Array.isArray(res.data) ? res.data : res.data?.items || []);
        } catch (err: any) { setError(err?.message || 'Không thể tải lời mời tái khám.'); }
        finally { setLoading(false); }
    }, []);
    useEffect(() => { void fetchRequests(); }, [fetchRequests]);
    const handleAccept = (request: RevisitRequestView) => {
        const bookingUrl = buildRevisitBookingUrl(request);
        if (!bookingUrl) {
            showAlert(
                'Đề xuất tái khám thiếu thông tin bác sĩ hoặc chuyên khoa. Vui lòng tải lại trang.',
                'Không thể đặt lịch',
                'error'
            );
            return;
        }
        navigate(bookingUrl);
    };
    const closeRejectModal = () => {
        if (actionLoading !== null) return;
        setRejectModal({ isOpen: false, id: null, reason: '', error: '' });
    };
    const submitReject = async () => {
        const { id, reason } = rejectModal;
        if (!id || actionLoading !== null) return;
        if (reason.trim().length < 5) {
            setRejectModal(p => ({ ...p, error: 'Vui lòng nhập lý do (ít nhất 5 ký tự).' })); return;
        }
        setActionLoading(id); setRejectModal(p => ({ ...p, error: '' }));
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/revisit-requests/${id}/reject`, { reason });
            if (!res.success) throw new Error(res.message || 'Không thể từ chối');
            setRejectModal({ isOpen: false, id: null, reason: '', error: '' });
            showAlert('Đã từ chối lời mời tái khám.', 'Thành công', 'success');
            await fetchRequests();
        } catch (err: any) { setRejectModal(p => ({ ...p, error: err?.message || 'Có lỗi xảy ra' })); }
        finally { setActionLoading(null); }
    };
    const pending = requests.filter(r => r.status === 'PendingPatientResponse');
    const history = requests.filter(r => r.status !== 'PendingPatientResponse');
    const badge = (status: string) => <StatusBadge {...(statuses[status] || { status: 'Unknown', label: 'Chưa xác định' })} />;
    const busy = actionLoading !== null;
    return <div className={styles.page}>
        <Breadcrumb items={[{ label: 'Trang chủ', path: '/patient' }, { label: 'Tái khám' }]} />
        <PageHeader title="Lời mời tái khám" />
        {loading ? <LoadingState message="Đang tải lời mời tái khám..." /> : error ? <InlineError message={error} onRetry={fetchRequests} /> : <>
            {pending.length === 0 ? <EmptyState title="Không có lời mời mới" description="Sức khỏe của bạn đang rất tốt, hãy duy trì nhé!" /> : <Row gutter={[16, 16]}>{pending.map(r => <Col key={r.id} xs={24} md={12}><Card title="Bác sĩ yêu cầu tái khám" extra={badge(r.status)}>
                <div className={styles.content}><Descriptions column={1} items={[{ key: 'date', label: 'Ngày gợi ý', children: formatDateOnly(r.suggestedDate) }, { key: 'doctor', label: 'Bác sĩ phụ trách', children: r.doctorName || '...' }]} />
                    {r.note && <Alert type="info" title="Lời nhắn từ bác sĩ" description={`"${r.note}"`} />}
                    <div className={styles.actions}><Button danger onClick={() => setRejectModal({ isOpen: true, id: r.id, reason: '', error: '' })} disabled={busy}>Từ chối</Button><Button type="primary" onClick={() => handleAccept(r)} disabled={busy}>Đặt lịch ngay</Button></div>
                </div>
            </Card></Col>)}</Row>}
            {history.length > 0 && <section className={styles.history}><h3>Lịch sử tái khám</h3>{history.map(r => <Card key={r.id} size="small" extra={badge(r.status)}><p>Ngày hẹn: {formatDateOnly(r.suggestedDate)}</p><p>Bác sĩ: <strong>{r.doctorName}</strong></p>{r.note && <p>Lý do: {r.note}</p>}</Card>)}</section>}
        </>}
        <Modal open={rejectModal.isOpen} title="Từ chối tái khám" onCancel={closeRejectModal} maskClosable={!busy} closable={!busy} keyboard={!busy} footer={<div className={styles.actions}><Button onClick={closeRejectModal} disabled={busy}>Đóng</Button><Button danger onClick={submitReject} disabled={busy}>{busy ? 'Đang xử lý...' : 'Xác nhận từ chối'}</Button></div>}>
            <p>Xin hãy cho chúng tôi biết lý do bạn không thể tham gia tái khám đợt này.</p>
            <Form layout="vertical"><Form.Item label="Lý do từ chối (*)" htmlFor="revisit-reason"><Input.TextArea id="revisit-reason" rows={3} placeholder="Vui lòng nhập lý do (ví dụ: Đã khỏe lại, Bận công tác...)" value={rejectModal.reason} onChange={e => setRejectModal(p => ({ ...p, reason: e.target.value }))} disabled={busy} /></Form.Item></Form>
            {rejectModal.error && <Alert type="error" showIcon title={rejectModal.error} />}
        </Modal>
    </div>;
};
