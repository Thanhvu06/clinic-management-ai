import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarCheck, Plus, XCircle, Clock, RefreshCw, X } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface LeaveRequest {
    id: number;
    startDateTime: string;
    endDateTime: string;
    reason: string;
    status: string;
    adminNote: string | null;
}

export const DoctorLeaveRequests: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [requests, setRequests] = useState<LeaveRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modalOpen, setModalOpen] = useState(false);
    const [formLoading, setFormLoading] = useState(false);
    
    // Create Form
    const [startDate, setStartDate] = useState('');
    const [endDate, setEndDate] = useState('');
    const [reason, setReason] = useState('');
    const [formError, setFormError] = useState('');

    const fetchRequests = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/leave-requests?${params.toString()}`);
            if (res.success && res.data) {
                setRequests(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error handling
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, [page, statusFilter]);

    const handleCreate = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');

        if (new Date(startDate) >= new Date(endDate)) {
            setFormError('Thời gian bắt đầu phải trước thời gian kết thúc.');
            return;
        }
        if (new Date(startDate) < new Date()) {
            setFormError('Không thể xin nghỉ trong quá khứ.');
            return;
        }

        setFormLoading(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/doctor/leave-requests', {
                startDateTime: startDate,
                endDateTime: endDate,
                reason
            });
            if (res.success) {
                showAlert('Tạo yêu cầu nghỉ thành công.', 'Thành công', 'success');
                setModalOpen(false);
                setStartDate('');
                setEndDate('');
                setReason('');
                fetchRequests();
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleWithdraw = async (id: number) => {
        showConfirm('Bạn có chắc chắn muốn rút lại yêu cầu nghỉ này?', async () => {
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/leave-requests/${id}/withdraw`, {});
                if (res.success) {
                    showAlert('Đã rút yêu cầu nghỉ.', 'Thành công', 'success');
                    fetchRequests();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
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

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <span className="badge badge-warning">Chờ duyệt</span>;
            case 'Approved': return <span className="badge badge-success">Đã duyệt</span>;
            case 'Rejected': return <span className="badge badge-danger">Đã từ chối</span>;
            case 'Cancelled': return <span className="badge badge-default">Đã rút</span>;
            default: return <span className="badge badge-default">{status}</span>;
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <CalendarCheck size={24} /> Yêu cầu nghỉ
                </h2>
                <div style={{ display: 'flex', gap: '12px' }}>
                    <button className="btn-secondary" onClick={() => fetchRequests()} style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                        <RefreshCw size={14} /> Làm mới
                    </button>
                    <button className="btn-primary" onClick={() => setModalOpen(true)} style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                        <Plus size={16} /> Tạo yêu cầu mới
                    </button>
                </div>
            </div>

            <div className="card" style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ width: '250px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Pending">Chờ duyệt</option>
                            <option value="Approved">Đã duyệt</option>
                            <option value="Rejected">Đã từ chối</option>
                            <option value="Cancelled">Đã rút</option>
                        </select>
                    </div>
                </div>
            </div>

            <div className="card table-responsive" style={{ padding: 0 }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải dữ liệu...</div>
                ) : (
                    <table className="table">
                        <thead>
                            <tr>
                                <th>Thời gian nghỉ</th>
                                <th>Lý do</th>
                                <th>Ghi chú quản trị</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {requests.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Chưa có yêu cầu nghỉ nào.
                                    </td>
                                </tr>
                            ) : requests.map(req => (
                                <tr key={req.id} style={{ borderBottom: '1px solid var(--c-border)' }}>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 500, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                            <Clock size={14} color="var(--c-teal)"/> Từ: {formatDateTime(req.startDateTime)}
                                        </div>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-muted)', marginTop: '4px', marginLeft: '20px' }}>
                                            Đến: {formatDateTime(req.endDateTime)}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontSize: '0.9rem', maxWidth: '250px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            {req.reason}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-danger)' }}>
                                            {req.adminNote || '-'}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        {getStatusBadge(req.status)}
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'right' }}>
                                        {req.status === 'Pending' && (
                                            <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => handleWithdraw(req.id)}>
                                                <XCircle size={14} style={{ marginRight: '4px' }}/> Rút Y/C
                                            </button>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>
            
            <div style={{ marginTop: '16px', color: 'var(--c-muted)', fontSize: '0.9rem' }}>
                Tổng cộng: {totalItems} yêu cầu
            </div>

            {/* Create Modal */}
            {modalOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '500px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>Tạo yêu cầu nghỉ phép</h3>
                            <button onClick={() => setModalOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>
                        
                        {formError && <div style={{ color: 'var(--c-danger)', background: 'var(--c-danger-bg)', padding: '10px', borderRadius: '6px', marginBottom: '16px', fontSize: '0.9rem' }}>{formError}</div>}

                        <form onSubmit={handleCreate} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            <div style={{ padding: '12px', background: 'var(--c-info-bg)', color: 'var(--c-info)', borderRadius: '6px', fontSize: '0.9rem' }}>
                                Lưu ý: Yêu cầu nghỉ sẽ cần được Quản trị viên duyệt. Vui lòng nộp yêu cầu trước ít nhất 1-2 ngày để tránh ảnh hưởng lịch bệnh nhân.
                            </div>
                            <div style={{ display: 'flex', gap: '16px' }}>
                                <div style={{ flex: 1 }}>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Từ thời gian (*)</label>
                                    <input 
                                        type="datetime-local" 
                                        className="form-input" 
                                        required 
                                        value={startDate} 
                                        onChange={e => setStartDate(e.target.value)} 
                                    />
                                </div>
                                <div style={{ flex: 1 }}>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Đến thời gian (*)</label>
                                    <input 
                                        type="datetime-local" 
                                        className="form-input" 
                                        required 
                                        value={endDate} 
                                        onChange={e => setEndDate(e.target.value)} 
                                    />
                                </div>
                            </div>
                            <div>
                                <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Lý do nghỉ (*)</label>
                                <textarea 
                                    className="form-textarea" 
                                    rows={3}
                                    required
                                    value={reason} 
                                    onChange={e => setReason(e.target.value)}
                                    placeholder="Nêu rõ lý do..."
                                />
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px' }}>
                                <button type="button" className="btn-secondary" onClick={() => setModalOpen(false)}>Hủy</button>
                                <button type="submit" className="btn-primary" disabled={formLoading}>
                                    {formLoading ? 'Đang gửi...' : 'Gửi yêu cầu'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
