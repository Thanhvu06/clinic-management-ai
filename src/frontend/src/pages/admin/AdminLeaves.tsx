import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarDays, User, CheckCircle, XCircle, Eye, X } from 'lucide-react';

interface LeaveRequest {
    id: number;
    doctorId: number;
    doctorName: string;
    startDateTime: string;
    endDateTime: string;
    reason: string;
    status: string;
    adminNote: string | null;
}

export const AdminLeaves: React.FC = () => {
    const [requests, setRequests] = useState<LeaveRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [statusFilter, setStatusFilter] = useState('');
    const [doctors, setDoctors] = useState<any[]>([]);
    const [doctorIdFilter, setDoctorIdFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, req: LeaveRequest | null }>({ isOpen: false, req: null });
    const [adminNote, setAdminNote] = useState('');
    const [actionLoading, setActionLoading] = useState(false);

    useEffect(() => {
        const fetchDoctors = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<any>>('/admin/doctors?isActive=true&pageSize=100');
                if (res.success) setDoctors(res.data.items);
            } catch (e) {}
        };
        fetchDoctors();
    }, []);

    const fetchRequests = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (statusFilter) params.append('status', statusFilter);
            if (doctorIdFilter) params.append('doctorId', doctorIdFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/leave-requests?${params.toString()}`);
            if (res.success && res.data) {
                setRequests(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, [page, statusFilter, doctorIdFilter]);

    const handleProcess = async (action: 'approve' | 'reject') => {
        if (!modal.req) return;
        setActionLoading(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/admin/leave-requests/${modal.req.id}/${action}`, {
                adminNote
            });
            if (res.success) {
                alert(action === 'approve' ? 'Đã duyệt yêu cầu nghỉ.' : 'Đã từ chối yêu cầu nghỉ.');
                setModal({ isOpen: false, req: null });
                fetchRequests();
            }
        } catch (error: any) {
            if (error?.errorCode === 'LEAVE_HAS_AFFECTED_APPOINTMENTS') {
                alert('Bác sĩ đang có lịch hẹn bị ảnh hưởng. Hãy để lễ tân xử lý các lịch này trước khi duyệt nghỉ.');
            } else {
                alert(error?.message || 'Có lỗi xảy ra.');
            }
        } finally {
            setActionLoading(false);
        }
    };

    const formatDate = (dateString: string) => {
        try {
            const date = new Date(dateString);
            return new Intl.DateTimeFormat('vi-VN', { 
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit'
            }).format(date);
        } catch {
            return dateString;
        }
    };

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <span className="badge badge-warning">Chờ xử lý</span>;
            case 'Approved': return <span className="badge badge-success">Đã duyệt</span>;
            case 'Rejected': return <span className="badge badge-danger">Đã từ chối</span>;
            case 'Cancelled': return <span className="badge badge-muted">Đã hủy</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <CalendarDays size={24} /> Quản lý yêu cầu nghỉ
                </h2>
            </div>

            <div className="card-panel" style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ width: '250px' }}>
                        <select className="form-select" value={doctorIdFilter} onChange={e => { setDoctorIdFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả bác sĩ</option>
                            {doctors.map(d => <option key={d.id} value={d.id}>{d.fullName}</option>)}
                        </select>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Pending">Chờ xử lý</option>
                            <option value="Approved">Đã duyệt</option>
                            <option value="Rejected">Đã từ chối</option>
                            <option value="Cancelled">Đã hủy</option>
                        </select>
                    </div>
                </div>
            </div>

            <div className="card-panel" style={{ padding: 0, overflowX: 'auto' }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải dữ liệu...</div>
                ) : (
                    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                        <thead>
                            <tr style={{ background: 'var(--c-bg)', borderBottom: '1px solid var(--c-border)' }}>
                                <th style={{ padding: '16px', textAlign: 'left', fontWeight: 600, color: 'var(--c-text-dark)' }}>Bác sĩ</th>
                                <th style={{ padding: '16px', textAlign: 'left', fontWeight: 600, color: 'var(--c-text-dark)' }}>Thời gian nghỉ</th>
                                <th style={{ padding: '16px', textAlign: 'left', fontWeight: 600, color: 'var(--c-text-dark)' }}>Trạng thái</th>
                                <th style={{ padding: '16px', textAlign: 'right', fontWeight: 600, color: 'var(--c-text-dark)' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {requests.length === 0 ? (
                                <tr>
                                    <td colSpan={4} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy yêu cầu nào.
                                    </td>
                                </tr>
                            ) : requests.map(r => (
                                <tr key={r.id} style={{ borderBottom: '1px solid var(--c-border)' }}>
                                    <td style={{ padding: '16px', fontWeight: 500 }}>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                            <User size={16} color="var(--c-teal)"/> {r.doctorName}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontSize: '0.9rem' }}>Từ: {formatDate(r.startDateTime)}</div>
                                        <div style={{ fontSize: '0.9rem' }}>Đến: {formatDate(r.endDateTime)}</div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        {getStatusBadge(r.status)}
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'right' }}>
                                        <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => { setModal({ isOpen: true, req: r }); setAdminNote(''); }}>
                                            <Eye size={14} style={{ marginRight: '4px' }}/> Chi tiết
                                        </button>
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

            {/* Detail Modal */}
            {modal.isOpen && modal.req && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '500px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>Chi tiết yêu cầu nghỉ</h3>
                            <button onClick={() => setModal({ isOpen: false, req: null })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>
                        
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginBottom: '20px' }}>
                            <div>
                                <span style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Bác sĩ: </span>
                                <strong>{modal.req.doctorName}</strong>
                            </div>
                            <div>
                                <span style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Thời gian: </span>
                                <strong>{formatDate(modal.req.startDateTime)} - {formatDate(modal.req.endDateTime)}</strong>
                            </div>
                            <div>
                                <span style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Trạng thái: </span>
                                {getStatusBadge(modal.req.status)}
                            </div>
                            <div style={{ background: 'var(--c-bg)', padding: '12px', borderRadius: '6px', fontSize: '0.95rem' }}>
                                <span style={{ color: 'var(--c-muted)', display: 'block', marginBottom: '4px' }}>Lý do xin nghỉ:</span>
                                {modal.req.reason}
                            </div>
                            {modal.req.adminNote && (
                                <div style={{ background: 'var(--c-info-bg)', padding: '12px', borderRadius: '6px', fontSize: '0.95rem', border: '1px solid #bfdbfe' }}>
                                    <span style={{ color: 'var(--c-muted)', display: 'block', marginBottom: '4px' }}>Ghi chú quản trị:</span>
                                    {modal.req.adminNote}
                                </div>
                            )}
                        </div>

                        {modal.req.status === 'Pending' && (
                            <>
                                <div style={{ marginBottom: '16px' }}>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Thêm ghi chú xử lý (Tùy chọn)</label>
                                    <textarea 
                                        className="form-textarea" 
                                        rows={2}
                                        value={adminNote} 
                                        onChange={e => setAdminNote(e.target.value)} 
                                        placeholder="Ghi chú thêm về quyết định duyệt/từ chối..."
                                    />
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                                    <button 
                                        className="btn-danger" 
                                        style={{ background: 'white', color: 'var(--c-danger)', border: '1px solid var(--c-danger)' }} 
                                        onClick={() => handleProcess('reject')}
                                        disabled={actionLoading}
                                    >
                                        <XCircle size={18} style={{ marginRight: '4px' }}/> Từ chối
                                    </button>
                                    <button 
                                        className="btn-primary" 
                                        style={{ background: 'var(--c-success)', borderColor: 'var(--c-success)' }} 
                                        onClick={() => handleProcess('approve')}
                                        disabled={actionLoading}
                                    >
                                        <CheckCircle size={18} style={{ marginRight: '4px' }}/> Duyệt yêu cầu
                                    </button>
                                </div>
                            </>
                        )}
                        {modal.req.status !== 'Pending' && (
                            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                                <button className="btn-secondary" onClick={() => setModal({ isOpen: false, req: null })}>Đóng</button>
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
};
