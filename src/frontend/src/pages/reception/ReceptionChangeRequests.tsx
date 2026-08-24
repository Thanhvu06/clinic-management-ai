import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { History, Eye, X, CheckCircle, XCircle, ArrowRight, RefreshCw } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface ChangeRequest {
    id: number;
    appointmentId: number;
    requestType: string;
    requestedSlotId: number | null;
    reason: string | null;
    status: string;
    createdAt: string;
}

export const ReceptionChangeRequests: React.FC = () => {
    const [requests, setRequests] = useState<ChangeRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [statusFilter, setStatusFilter] = useState('');

    // Detail Modal
    const [modal, setModal] = useState<{ isOpen: boolean, req: ChangeRequest | null }>({ isOpen: false, req: null });
    const [actionLoading, setActionLoading] = useState(false);
    const [adminNote, setAdminNote] = useState('');

    // Additional data for Modal
    const [appointmentInfo, setAppointmentInfo] = useState<any>(null);
    const [aptLoading, setAptLoading] = useState(false);

    const fetchRequests = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/change-requests?${params.toString()}`);
            if (res.success && res.data) {
                setRequests(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            //
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, [page, statusFilter]);

    const openDetail = async (req: ChangeRequest) => {
        setModal({ isOpen: true, req });
        setAdminNote('');
        setAppointmentInfo(null);
        
        // Fetch original appointment to display details
        setAptLoading(true);
        try {
            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments/${req.appointmentId}`);
            if (res.success) {
                setAppointmentInfo(res.data);
            }
        } catch (error) {
            //
        } finally {
            setAptLoading(false);
        }
    };

    const { showAlert, showConfirm } = useDialog();

    const handleAction = async (action: 'approve-reschedule' | 'approve-cancellation' | 'reject') => {
        const req = modal.req;
        if (!req) return;
        
        let confirmMsg = '';
        if (action === 'approve-reschedule') confirmMsg = 'Hệ thống sẽ chuyển lịch hẹn sang ca khám mới và giải phóng ca khám cũ. Xác nhận đổi lịch?';
        else if (action === 'approve-cancellation') confirmMsg = 'Xác nhận hủy lịch hẹn này? Hành động này không thể hoàn tác.';
        else confirmMsg = 'Từ chối yêu cầu của bệnh nhân? Lịch hẹn cũ vẫn sẽ được giữ nguyên.';

        showConfirm(confirmMsg, async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/reception/change-requests/${req.id}/${action}`, {
                    reason: adminNote
                });
                if (res.success) {
                    showAlert('Xử lý yêu cầu thành công.', 'Thành công', 'success');
                    fetchRequests();
                    setModal({ isOpen: false, req: null });
                }
            } catch (error: any) {
                const code = error?.errorCode;
                if (code === 'SLOT_TAKEN') showAlert('Khung giờ mới vừa được người khác chọn. Vui lòng liên hệ bệnh nhân để chọn lịch khác.', 'Lỗi', 'error');
                else if (code === 'INVALID_CHANGE_REQUEST') showAlert('Yêu cầu không còn hợp lệ hoặc đã được xử lý.', 'Lỗi', 'error');
                else if (code === 'ACTIVE_CHANGE_REQUEST_EXISTS') showAlert('Lịch hẹn đang có yêu cầu chờ xử lý.', 'Lỗi', 'error');
                else if (code === 'DOCTOR_NOT_AVAILABLE') showAlert('Bác sĩ không còn làm việc trong khung giờ này.', 'Lỗi', 'error');
                else if (code === 'RESOURCE_NOT_FOUND') showAlert('Không tìm thấy dữ liệu yêu cầu.', 'Lỗi', 'error');
                else if (code === 'FORBIDDEN') showAlert('Bạn không có quyền thực hiện thao tác này.', 'Lỗi', 'error');
                else showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
                
                // Reload if conflict
                if (['SLOT_TAKEN', 'INVALID_CHANGE_REQUEST'].includes(code)) {
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

    const translateType = (type: string) => {
        if (type === 'Reschedule') return <span className="badge badge-info">Đổi lịch</span>;
        if (type === 'Cancellation') return <span className="badge badge-danger">Hủy lịch</span>;
        return <span className="badge badge-muted">{type}</span>;
    };

    const translateStatus = (status: string) => {
        switch (status) {
            case 'Pending': return <span className="badge badge-warning">Chờ xử lý</span>;
            case 'Approved': return <span className="badge badge-success">Đã duyệt</span>;
            case 'Rejected': return <span className="badge badge-danger">Đã từ chối</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <History size={24} /> Yêu cầu đổi/hủy lịch
                </h2>
                <button className="btn-secondary" onClick={() => fetchRequests()} style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                    <RefreshCw size={14} /> Làm mới
                </button>
            </div>

            <div className="card" style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ width: '250px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Pending">Chờ xử lý</option>
                            <option value="Approved">Đã duyệt</option>
                            <option value="Rejected">Đã từ chối</option>
                        </select>
                    </div>
                </div>
            </div>

            <div className="card" style={{ padding: 0 }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải dữ liệu...</div>
                ) : (
                    <div className="table-responsive">
                        <table className="table">
                            <thead>
                                <tr>
                                    <th>Lịch gốc</th>
                                    <th>Loại Y/C</th>
                                    <th>Thời điểm tạo</th>
                                    <th>Trạng thái</th>
                                    <th style={{ textAlign: 'right' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                            {requests.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy yêu cầu nào.
                                    </td>
                                </tr>
                            ) : requests.map(req => (
                                <tr key={req.id} style={{ borderBottom: '1px solid var(--c-border)' }}>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 600 }}>ID Lịch: #{req.appointmentId}</div>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-muted)', marginTop: '4px', maxWidth: '250px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            Lý do: {req.reason || 'Không ghi chú'}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        {translateType(req.requestType)}
                                        {req.requestType === 'Reschedule' && req.requestedSlotId && (
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                                                Slot mong muốn: #{req.requestedSlotId}
                                            </div>
                                        )}
                                    </td>
                                    <td style={{ padding: '16px', fontSize: '0.9rem' }}>
                                        {formatDateTime(req.createdAt)}
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        {translateStatus(req.status)}
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'right' }}>
                                        <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => openDetail(req)}>
                                            <Eye size={14} style={{ marginRight: '4px' }}/> Xử lý
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    </div>
                )}
            </div>

            <div style={{ marginTop: '16px', color: 'var(--c-muted)', fontSize: '0.9rem' }}>
                Tổng cộng: {totalItems} yêu cầu
            </div>

            {/* Detail Modal */}
            {modal.isOpen && modal.req && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '600px', maxHeight: '90vh', overflowY: 'auto' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                Chi tiết yêu cầu {translateType(modal.req.requestType)}
                            </h3>
                            <button onClick={() => setModal({ isOpen: false, req: null })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>

                        {aptLoading ? (
                            <div style={{ padding: '20px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải thông tin lịch khám...</div>
                        ) : appointmentInfo ? (
                            <div style={{ marginBottom: '20px', padding: '16px', background: 'var(--c-bg)', borderRadius: '8px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                <div style={{ fontWeight: 600, color: 'var(--c-navy-dark)' }}>Thông tin bệnh nhân</div>
                                <div><span style={{ color: 'var(--c-muted)' }}>Tên:</span> {appointmentInfo.patientName} - <span style={{ color: 'var(--c-muted)' }}>SĐT:</span> {appointmentInfo.patientPhone}</div>
                                <hr style={{ border: 'none', borderTop: '1px dashed var(--c-border)', margin: '8px 0' }} />
                                <div style={{ fontWeight: 600, color: 'var(--c-navy-dark)' }}>Lịch hiện tại</div>
                                <div><span style={{ color: 'var(--c-muted)' }}>Bác sĩ:</span> {appointmentInfo.doctorName} ({appointmentInfo.specialtyName})</div>
                                <div>
                                    <span style={{ color: 'var(--c-muted)' }}>Thời gian:</span> {appointmentInfo.appointmentDate} ({appointmentInfo.startTime.substring(0,5)} - {appointmentInfo.endTime.substring(0,5)})
                                </div>
                            </div>
                        ) : (
                            <div style={{ padding: '12px', background: 'var(--c-warning-bg)', color: 'var(--c-warning)', borderRadius: '6px', marginBottom: '20px' }}>
                                Không thể lấy chi tiết lịch hẹn gốc. (ID: {modal.req.appointmentId})
                            </div>
                        )}

                        <div style={{ marginBottom: '24px' }}>
                            <div style={{ fontWeight: 500, marginBottom: '8px' }}>Lý do của bệnh nhân:</div>
                            <div style={{ background: 'var(--c-bg)', padding: '12px', borderRadius: '6px', fontSize: '0.95rem' }}>
                                {modal.req.reason || <span style={{ color: 'var(--c-muted)' }}>Không có ghi chú</span>}
                            </div>
                        </div>

                        {modal.req.requestType === 'Reschedule' && modal.req.status === 'Pending' && (
                            <div style={{ marginBottom: '24px', padding: '12px', border: '1px solid var(--c-info)', borderRadius: '8px', background: 'var(--c-info-bg)', display: 'flex', alignItems: 'center', gap: '16px' }}>
                                <div style={{ flex: 1 }}>
                                    <div style={{ fontSize: '0.85rem', color: 'var(--c-info)' }}>Đang chọn lịch cũ</div>
                                    <div style={{ fontWeight: 500 }}>ID Lịch: #{modal.req.appointmentId}</div>
                                </div>
                                <ArrowRight size={20} color="var(--c-info)" />
                                <div style={{ flex: 1 }}>
                                    <div style={{ fontSize: '0.85rem', color: 'var(--c-info)' }}>Muốn đổi sang</div>
                                    <div style={{ fontWeight: 500 }}>Slot mới: #{modal.req.requestedSlotId}</div>
                                </div>
                            </div>
                        )}

                        {modal.req.status === 'Pending' && (
                            <>
                                <div style={{ marginBottom: '16px' }}>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Ghi chú xử lý (Tùy chọn)</label>
                                    <textarea 
                                        className="form-textarea" 
                                        rows={2}
                                        value={adminNote} 
                                        onChange={e => setAdminNote(e.target.value)} 
                                        placeholder="Ghi chú khi duyệt/từ chối..."
                                    />
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', paddingTop: '16px', borderTop: '1px solid var(--c-border)' }}>
                                    <button 
                                        className="btn-danger" 
                                        style={{ background: 'white', color: 'var(--c-danger)', border: '1px solid var(--c-danger)' }} 
                                        onClick={() => handleAction('reject')}
                                        disabled={actionLoading}
                                    >
                                        <XCircle size={18} style={{ marginRight: '4px' }}/> Từ chối yêu cầu
                                    </button>
                                    <button 
                                        className="btn-primary" 
                                        onClick={() => handleAction(modal.req?.requestType === 'Reschedule' ? 'approve-reschedule' : 'approve-cancellation')}
                                        disabled={actionLoading}
                                    >
                                        <CheckCircle size={18} style={{ marginRight: '4px' }}/> Duyệt {modal.req.requestType === 'Reschedule' ? 'đổi lịch' : 'hủy lịch'}
                                    </button>
                                </div>
                            </>
                        )}
                        
                        {modal.req.status !== 'Pending' && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                <div>Trạng thái: {translateStatus(modal.req.status)}</div>
                                <button className="btn-secondary" onClick={() => setModal({ isOpen: false, req: null })}>Đóng</button>
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
};
