import { Button, Input, Select, Modal } from 'antd';
import { PageHeader, DataTable, StatusBadge, LoadingState, EmptyState, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './ReceptionChangeRequests.module.css';
import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { History, Eye, CheckCircle, XCircle, ArrowRight, RefreshCw, Filter } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface ChangeRequest {
    id: number;
    appointmentId: number;
    requestType: string;
    requestedSlotId: number | null;
    reason: string | null;
    status: string;
    createdAt: string;
    appointmentCode?: string;
    patientName?: string;
    doctorName?: string;
    specialtyName?: string;
    currentSlotDate?: string;
    currentStartTime?: string;
    currentEndTime?: string;
    requestedSlotDate?: string;
    requestedStartTime?: string;
    requestedEndTime?: string;
}

interface AppointmentInfo {
    id: number;
    appointmentCode: string;
    patientName: string;
    patientPhone?: string;
    doctorName: string;
    specialtyName: string;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    status: string;
}

interface ChangeRequestPagedResult {
    items: ChangeRequest[];
    totalItems: number;
    page: number;
    pageSize: number;
    totalPages: number;
}

export const ReceptionChangeRequests: React.FC = () => {
    const [requests, setRequests] = useState<ChangeRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [statusFilter, setStatusFilter] = useState('');
    const [typeFilter, setTypeFilter] = useState('');

    // Detail Modal
    const [modal, setModal] = useState<{ isOpen: boolean, req: ChangeRequest | null }>({ isOpen: false, req: null });
    const [actionLoading, setActionLoading] = useState(false);
    const [adminNote, setAdminNote] = useState('');

    // Additional data for Modal
    const [appointmentInfo, setAppointmentInfo] = useState<AppointmentInfo | null>(null);
    const [aptLoading, setAptLoading] = useState(false);

    const fetchRequests = async () => {
        setLoading(true);
        setError(null);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (statusFilter) params.append('status', statusFilter);
            if (typeFilter) params.append('requestType', typeFilter);

            const res = await axiosClient.get<unknown, ApiResponse<ChangeRequestPagedResult>>(`/reception/change-requests?${params.toString()}`);
            if (res.success && res.data) {
                setRequests(res.data.items);
                setTotalItems(res.data.totalItems);
            } else {
                setError(res.message || 'Không thể tải danh sách yêu cầu thay đổi.');
            }
        } catch (err: unknown) {
            const errorObj = err as { message?: string; response?: { data?: { message?: string } } };
            setError(errorObj?.response?.data?.message || errorObj?.message || 'Có lỗi xảy ra khi kết nối máy chủ.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, [page, statusFilter, typeFilter]);

    const openDetail = async (req: ChangeRequest) => {
        setModal({ isOpen: true, req });
        setAdminNote('');
        setAppointmentInfo(null);
        
        // Fetch original appointment to display details
        setAptLoading(true);
        try {
            const res = await axiosClient.get<unknown, ApiResponse<AppointmentInfo>>(`/reception/appointments/${req.appointmentId}`);
            if (res.success && res.data) {
                setAppointmentInfo(res.data);
            }
        } catch (_) {
            // Optional appointment info fetch
        } finally {
            setAptLoading(false);
        }
    };

    const { showAlert, showConfirm } = useDialog();

    const handleAction = async (action: 'approve-reschedule' | 'approve-cancellation' | 'reject') => {
        const req = modal.req;
        if (!req) return;

        if (action === 'reject' && (!adminNote.trim() || adminNote.trim().length < 5)) {
            showAlert('Lý do từ chối phải có ít nhất 5 ký tự.', 'Lỗi', 'error');
            return;
        }
        
        let confirmMsg = '';
        if (action === 'approve-reschedule') confirmMsg = 'Hệ thống sẽ chuyển lịch hẹn sang ca khám mới và giải phóng ca khám cũ. Xác nhận đổi lịch?';
        else if (action === 'approve-cancellation') confirmMsg = 'Xác nhận hủy lịch hẹn này và giải phóng ca khám? Hành động này không thể hoàn tác.';
        else confirmMsg = 'Từ chối yêu cầu của bệnh nhân? Lịch hẹn và ca khám cũ vẫn sẽ được giữ nguyên.';

        showConfirm(confirmMsg, async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<unknown, ApiResponse<null>>(`/reception/change-requests/${req.id}/${action}`, {
                    reason: adminNote.trim(),
                    note: adminNote.trim()
                });
                if (res.success) {
                    showAlert('Xử lý yêu cầu thành công.', 'Thành công', 'success');
                    fetchRequests();
                    setModal({ isOpen: false, req: null });
                }
            } catch (err: unknown) {
                const errorObj = err as { errorCode?: string; message?: string; response?: { data?: { errorCode?: string; message?: string } } };
                const code = errorObj?.response?.data?.errorCode || errorObj?.errorCode;
                const msg = errorObj?.response?.data?.message || errorObj?.message;

                if (code === 'CHANGE_REQUEST_ALREADY_PROCESSED') {
                    showAlert('Yêu cầu này đã được người khác xử lý hoặc đã rút.', 'Thông báo', 'warning');
                } else if (code === 'SLOT_TAKEN' || code === 'TARGET_SLOT_ALREADY_BOOKED' || code === 'SLOT_ALREADY_BOOKED') {
                    showAlert('Khung giờ mới vừa được người khác chọn hoặc đã bị khóa. Vui lòng liên hệ bệnh nhân để chọn lịch khác.', 'Lỗi', 'error');
                } else if (code === 'INVALID_CHANGE_REQUEST') {
                    showAlert('Yêu cầu không còn hợp lệ hoặc đã được xử lý.', 'Lỗi', 'error');
                } else if (code === 'ACTIVE_CHANGE_REQUEST_EXISTS') {
                    showAlert('Lịch hẹn đang có yêu cầu chờ xử lý.', 'Lỗi', 'error');
                } else if (code === 'DOCTOR_NOT_AVAILABLE' || code === 'DOCTOR_MISMATCH') {
                    showAlert(msg || 'Bác sĩ không khả dụng cho khung giờ này.', 'Lỗi', 'error');
                } else if (code === 'PATIENT_TIME_CONFLICT') {
                    showAlert('Bệnh nhân đã có lịch khám khác trùng thời gian với ca khám mới.', 'Lỗi', 'error');
                } else if (code === 'RESOURCE_NOT_FOUND') {
                    showAlert('Không tìm thấy dữ liệu yêu cầu.', 'Lỗi', 'error');
                } else if (code === 'FORBIDDEN') {
                    showAlert('Bạn không có quyền thực hiện thao tác này.', 'Lỗi', 'error');
                } else {
                    showAlert(msg || 'Có lỗi xảy ra khi xử lý yêu cầu.', 'Lỗi', 'error');
                }
                
                // Reload if conflict or state mismatch
                if (['SLOT_TAKEN', 'TARGET_SLOT_ALREADY_BOOKED', 'SLOT_ALREADY_BOOKED', 'INVALID_CHANGE_REQUEST', 'INVALID_STATE', 'CHANGE_REQUEST_ALREADY_PROCESSED'].includes(code || '')) {
                    fetchRequests();
                    setModal({ isOpen: false, req: null });
                }
            } finally {
                setActionLoading(false);
            }
        });
    };

    const formatDateTime = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit'
            }).format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    const translateType = (type: string) => <StatusBadge status={type === 'Cancellation' ? 'Cancelled' : 'Confirmed'} label={type === 'Reschedule' ? 'Đổi lịch' : type === 'Cancellation' ? 'Hủy lịch' : '—'} />;
    const translateStatus = (status: string) => <StatusBadge status={status} label={({Pending:'Chờ xử lý',Approved:'Đã duyệt',Rejected:'Đã từ chối',Withdrawn:'Đã rút'} as Record<string,string>)[status] || '—'} />;
    const totalPages = Math.max(1, Math.ceil(totalItems / 10));
    const columns: DataTableColumn<ChangeRequest>[] = [
        {header:'Lịch gốc',accessor:req => <><div className={styles.appointmentCode}>{req.appointmentCode ? `#${req.appointmentCode}` : `ID: #${req.appointmentId}`}</div>{req.patientName && <div>BN: <strong>{req.patientName}</strong></div>}{req.doctorName && <div className={styles.secondaryText}>BS: {req.doctorName}</div>}</>},
        {header:'Loại Y/C & Chi tiết',accessor:req => <>{translateType(req.requestType)}{req.requestType === 'Reschedule' && <div className={styles.requestDetail}>{req.requestedSlotDate ? <span>Đổi sang: <strong>{req.requestedSlotDate}</strong> ({req.requestedStartTime?.substring(0, 5)} - {req.requestedEndTime?.substring(0, 5)})</span> : <span>Slot mong muốn: #{req.requestedSlotId}</span>}</div>}<div className={styles.requestReason}>Lý do: {req.reason || 'Không ghi chú'}</div></>},
        {header:'Thời điểm tạo',accessor:req => formatDateTime(req.createdAt)},
        {header:'Trạng thái',accessor:req => translateStatus(req.status)},
        {header:'Thao tác',align:'right',accessor:req => <Button onClick={() => openDetail(req)} icon={<Eye size={14} />}>Xử lý</Button>},
    ];
    return (
        <div className={styles.page}>
            <PageHeader title="Yêu cầu đổi/hủy lịch" badge={<History size={24} className={styles.primaryIcon} />} actions={<Button onClick={() => fetchRequests()} icon={<RefreshCw size={14} />}>Làm mới</Button>} />
            <div className={styles.filters}><span className={styles.filterLabel}><Filter size={16} /> Bộ lọc:</span><Select className={styles.filterSelect} value={typeFilter} onChange={value => { setTypeFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả loại yêu cầu'},{value:'Reschedule',label:'Đổi lịch'},{value:'Cancellation',label:'Hủy lịch'}]} /><Select className={styles.filterSelect} value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả trạng thái'},{value:'Pending',label:'Chờ xử lý'},{value:'Approved',label:'Đã duyệt'},{value:'Rejected',label:'Đã từ chối'},{value:'Withdrawn',label:'Đã rút'}]} /></div>
            {error ? <InlineError title="" message={error} onRetry={() => fetchRequests()} /> : loading ? <LoadingState message="Đang tải dữ liệu..." /> : requests.length === 0 ? <EmptyState title="Không tìm thấy yêu cầu nào." /> : <div className={styles.tableScroll}><DataTable columns={columns} data={requests} keyExtractor={req => req.id} /></div>}
            <div className={styles.pagination}><div className={styles.secondaryText}>Trang {page} / {totalPages} (Tổng cộng: {totalItems} yêu cầu)</div><div className={styles.paginationActions}><Button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1 || loading}>Trước</Button><Button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page >= totalPages || loading}>Sau</Button></div></div>
            {modal.isOpen && modal.req && <Modal open width={620} className={styles.detailModal} onCancel={() => setModal({isOpen:false,req:null})} mask={{closable:false}} keyboard={false} title={<>Chi tiết yêu cầu {translateType(modal.req.requestType)} #{modal.req.id}</>} footer={modal.req.status === 'Pending' ? <><Button danger onClick={() => handleAction('reject')} disabled={actionLoading} icon={<XCircle size={18} />}>Từ chối yêu cầu</Button><Button type="primary" onClick={() => handleAction(modal.req?.requestType === 'Reschedule' ? 'approve-reschedule' : 'approve-cancellation')} disabled={actionLoading} icon={<CheckCircle size={18} />}>Duyệt {modal.req.requestType === 'Reschedule' ? 'đổi lịch' : 'hủy lịch'}</Button></> : <div className={styles.resolvedFooter}><div>Trạng thái: {translateStatus(modal.req.status)}</div><Button onClick={() => setModal({isOpen:false,req:null})}>Đóng</Button></div>}>
                {aptLoading ? <LoadingState message="Đang tải thông tin lịch khám..." /> : appointmentInfo ? <div className={styles.appointmentPanel}><div className={styles.sectionTitle}>Thông tin bệnh nhân</div><div><span className={styles.secondaryText}>Tên:</span> <strong>{appointmentInfo.patientName}</strong> {appointmentInfo.patientPhone ? <>- <span className={styles.secondaryText}>SĐT:</span> {appointmentInfo.patientPhone}</> : null}</div><hr className={styles.divider} /><div className={styles.sectionTitle}>Lịch hiện tại</div><div><span className={styles.secondaryText}>Bác sĩ:</span> {appointmentInfo.doctorName} ({appointmentInfo.specialtyName})</div><div><span className={styles.secondaryText}>Thời gian:</span> <strong>{appointmentInfo.appointmentDate}</strong> ({appointmentInfo.startTime?.substring(0,5)} - {appointmentInfo.endTime?.substring(0,5)})</div></div> : modal.req.patientName ? <div className={styles.appointmentPanel}><div className={styles.sectionTitle}>Thông tin lịch hẹn #{modal.req.appointmentCode || modal.req.appointmentId}</div><div><span className={styles.secondaryText}>Bệnh nhân:</span> <strong>{modal.req.patientName}</strong></div><div><span className={styles.secondaryText}>Bác sĩ:</span> {modal.req.doctorName} ({modal.req.specialtyName})</div><div><span className={styles.secondaryText}>Thời gian hiện tại:</span> <strong>{modal.req.currentSlotDate}</strong> ({modal.req.currentStartTime?.substring(0,5)} - {modal.req.currentEndTime?.substring(0,5)})</div></div> : null}
                <section className={styles.detailSection}><div className={styles.sectionTitle}>Lý do của bệnh nhân:</div><div className={styles.reasonPanel}>{modal.req.reason || <span className={styles.secondaryText}>Không có ghi chú</span>}</div></section>
                {modal.req.requestType === 'Reschedule' && <div className={styles.slotComparison}><div className={styles.slotInfo}><div className={styles.slotLabel}>Lịch hiện tại</div><div>{modal.req.currentSlotDate || appointmentInfo?.appointmentDate} ({modal.req.currentStartTime?.substring(0, 5) || appointmentInfo?.startTime?.substring(0, 5)} - {modal.req.currentEndTime?.substring(0, 5) || appointmentInfo?.endTime?.substring(0, 5)})</div></div><ArrowRight size={20} className={styles.primaryIcon} /><div className={styles.slotInfo}><div className={styles.slotLabel}>Ca khám mong muốn</div><div className={styles.requestedSlot}>{modal.req.requestedSlotDate ? <span>{modal.req.requestedSlotDate} ({modal.req.requestedStartTime?.substring(0, 5)} - {modal.req.requestedEndTime?.substring(0, 5)})</span> : <span>Slot #{modal.req.requestedSlotId}</span>}</div></div></div>}
                {modal.req.status === 'Pending' && <div className={styles.detailSection}><label className={styles.sectionTitle}>Ghi chú xử lý (Gửi đến bệnh nhân)</label><Input.TextArea rows={2} value={adminNote} onChange={e => setAdminNote(e.target.value)} placeholder="Nhập ghi chú khi duyệt hoặc lý do từ chối (tối thiểu 5 ký tự khi từ chối)..." className={styles.actionNote} /></div>}
            </Modal>}
        </div>
    );
};
