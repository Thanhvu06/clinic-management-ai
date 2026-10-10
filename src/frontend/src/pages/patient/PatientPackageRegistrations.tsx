import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Descriptions, Form, Input, Modal, Tag } from 'antd';
import { Link } from 'react-router-dom';
import { Package } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { formatDateOnly, formatDisplayDate, formatVndCurrency } from '../../utils/formatters';
import { Breadcrumb } from '../../components/Breadcrumb';
import { PageHeader } from '../../components/common/PageHeader';
import { StatusBadge } from '../../components/common/StatusBadge';
import { EmptyState } from '../../components/common/EmptyState';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';
import styles from './PatientPackageRegistrations.module.css';

interface PackageRegistrationItem {
    id: number; registrationCode: string; healthPackageId: number;
    healthPackageName?: string; packageName?: string; healthPackageCode?: string; packageCode?: string;
    healthPackagePrice?: number; packagePrice?: number; preferredDate: string; contactPhone: string;
    note?: string; notes?: string; adminNotes?: string; cancellationReason?: string; status: string; createdAt: string;
}
const statusLabels: Record<string, string> = { Pending: 'Chờ xác nhận', Confirmed: 'Đã xác nhận', Completed: 'Đã hoàn tất', Cancelled: 'Đã hủy' };
export const PatientPackageRegistrations: React.FC = () => {
    const [registrations, setRegistrations] = useState<PackageRegistrationItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [cancelModalItem, setCancelModalItem] = useState<PackageRegistrationItem | null>(null);
    const [cancelReason, setCancelReason] = useState('');
    const [isCancelling, setIsCancelling] = useState(false);
    const [cancelError, setCancelError] = useState('');
    const fetchRegistrations = useCallback(async () => {
        setLoading(true); setError('');
        try {
            const res = await axiosClient.get<any, any>('/patient/health-package-registrations');
            if (!res.success) throw new Error(res.message || 'Không thể tải danh sách gói khám đã đăng ký.');
            setRegistrations(res.data || []);
        } catch (err: any) { setError(err?.message || 'Có lỗi xảy ra khi tải dữ liệu.'); }
        finally { setLoading(false); }
    }, []);
    useEffect(() => { void fetchRegistrations(); }, [fetchRegistrations]);
    const handleConfirmCancel = async () => {
        if (!cancelModalItem || isCancelling) return;
        setIsCancelling(true); setCancelError('');
        try {
            const res = await axiosClient.post<any, any>(`/patient/health-package-registrations/${cancelModalItem.id}/cancel`, { cancellationReason: cancelReason.trim() || 'Người bệnh chủ động hủy qua trang web' });
            if (!res.success) throw new Error(res.message || 'Hủy đăng ký không thành công.');
            setCancelModalItem(null); setCancelReason(''); await fetchRegistrations();
        } catch (err: any) { setCancelError(err?.message || 'Có lỗi xảy ra khi hủy đăng ký.'); }
        finally { setIsCancelling(false); }
    };
    const closeModal = () => { if (!isCancelling) setCancelModalItem(null); };
    return <div className={styles.page}>
        <Breadcrumb items={[{ label: 'Trang chủ', path: '/patient' }, { label: 'Gói khám đã đăng ký' }]} />
        <PageHeader title="Gói Khám Sức Khỏe Đã Đăng Ký" subtitle="Theo dõi tình trạng xét duyệt và quản lý các yêu cầu đăng ký gói khám của bạn." actions={<Link to="/health-packages"><Button type="primary" icon={<Package size={16} />}>Đăng ký gói khám mới</Button></Link>} />
        {loading ? <LoadingState message="Đang tải danh sách đăng ký..." /> : error ? <InlineError message={error} onRetry={fetchRegistrations} /> : registrations.length === 0 ? <EmptyState title="Bạn chưa đăng ký gói khám sức khỏe nào" description="ClinicCare cung cấp nhiều gói tầm soát tổng quát định kỳ phù hợp cho cá nhân và gia đình với chi phí hợp lý." action={<Link to="/health-packages"><Button type="primary">Khám phá danh mục gói khám</Button></Link>} /> : registrations.map(reg =>
            <Card key={reg.id} title={<div className={styles.heading}><Tag>{reg.registrationCode}</Tag><StatusBadge status={reg.status} label={statusLabels[reg.status] || reg.status} /><span>{reg.healthPackageName || reg.packageName}</span></div>} extra={<div className={styles.price}><span>Chi phí trọn gói</span><strong>{formatVndCurrency(reg.healthPackagePrice ?? reg.packagePrice ?? 0)}</strong></div>}>
                <div className={styles.content}><Descriptions column={{ xs: 1, md: 3 }} items={[{ key: 'date', label: 'Ngày khám mong muốn:', children: formatDateOnly(reg.preferredDate) }, { key: 'phone', label: 'Số điện thoại liên hệ:', children: reg.contactPhone }, { key: 'created', label: 'Thời gian gửi đăng ký:', children: formatDisplayDate(reg.createdAt) }]} />
                    {(reg.note || reg.notes) && <p><strong>Ghi chú của bạn:</strong> {reg.note || reg.notes}</p>}
                    {reg.adminNotes && <Alert type="success" title="Ghi chú từ phòng khám:" description={reg.adminNotes} />}
                    {reg.cancellationReason && <Alert type="error" title="Lý do hủy:" description={reg.cancellationReason} />}
                    <div className={styles.actions}><Link to={`/health-packages/${reg.healthPackageId}`}>Xem chi tiết gói khám</Link>{reg.status === 'Pending' && <Button danger onClick={() => { setCancelModalItem(reg); setCancelReason(''); setCancelError(''); }}>Hủy đăng ký</Button>}</div>
                </div>
            </Card>)}
        <Modal open={!!cancelModalItem} title="Xác nhận hủy đăng ký gói khám" onCancel={closeModal} maskClosable={!isCancelling} closable={!isCancelling} keyboard={!isCancelling} footer={<div className={styles.modalActions}><Button onClick={closeModal} disabled={isCancelling}>Đóng</Button><Button danger onClick={handleConfirmCancel} disabled={isCancelling}>{isCancelling ? 'Đang xử lý...' : 'Xác nhận hủy'}</Button></div>}>
            <p>Bạn có chắc chắn muốn hủy yêu cầu đăng ký gói khám <strong>{cancelModalItem?.healthPackageName || cancelModalItem?.packageName}</strong> (Mã: {cancelModalItem?.registrationCode})?</p>
            {cancelError && <Alert type="error" showIcon title={cancelError} />}
            <Form layout="vertical"><Form.Item label="Lý do hủy (Không bắt buộc)" htmlFor="package-cancel-reason"><Input.TextArea id="package-cancel-reason" value={cancelReason} onChange={e => setCancelReason(e.target.value)} placeholder="Thay đổi kế hoạch, dời ngày, hoặc lý do khác..." rows={3} disabled={isCancelling} /></Form.Item></Form>
        </Modal>
    </div>;
};
