import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarDays, Stethoscope, CheckCircle, XCircle, AlertCircle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useToast } from '../../components/Toast';

export const PatientRevisit: React.FC = () => {
    const [requests, setRequests] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [actionLoading, setActionLoading] = useState<number | null>(null);
    
    // Modal state
    const [showRejectModal, setShowRejectModal] = useState(false);
    const [rejectingId, setRejectingId] = useState<number | null>(null);
    const [rejectReason, setRejectReason] = useState('');
    const [rejectError, setRejectError] = useState('');

    const navigate = useNavigate();
    const { warning, success } = useToast();

    const fetchRequests = async () => {
        try {
            const res = await axiosClient.get<any, ApiResponse<any[]>>('/revisit-requests/my');
            if (res.success && res.data) {
                setRequests(res.data);
            }
        } catch (error) {
            // Ignore
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, []);

    const handleAccept = () => {
        warning('Vui lòng đặt lịch khám mới theo ngày gợi ý.');
        navigate('/patient/book');
    };

    const openRejectModal = (id: number) => {
        setRejectingId(id);
        setRejectReason('');
        setRejectError('');
        setShowRejectModal(true);
    };

    const closeRejectModal = () => {
        setShowRejectModal(false);
        setRejectingId(null);
    };

    const submitReject = async () => {
        if (!rejectingId) return;

        setActionLoading(rejectingId);
        setRejectError('');
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/revisit-requests/${rejectingId}/reject`, { reason: rejectReason });
            if (res.success) {
                success('Đã từ chối lịch tái khám.');
                fetchRequests();
                closeRejectModal();
            }
        } catch (error: any) {
            setRejectError(error?.message || 'Có lỗi xảy ra khi từ chối.');
        } finally {
            setActionLoading(null);
        }
    };

    const formatDate = (dateString: string) => {
        try {
            const date = new Date(dateString);
            return new Intl.DateTimeFormat('vi-VN').format(date);
        } catch (e) {
            return dateString;
        }
    };

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <span className="badge badge-warning">Chờ phản hồi</span>;
            case 'Accepted': return <span className="badge badge-success">Đã đồng ý</span>;
            case 'Rejected': return <span className="badge badge-danger">Đã từ chối</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
    };

    return (
        <div style={{ maxWidth: '900px', margin: '0 auto' }}>
            <h2 style={{ marginBottom: '24px', color: 'var(--c-navy-dark)' }}>Yêu cầu tái khám</h2>
            
            {loading ? (
                <div style={{ color: 'var(--c-muted)', padding: '20px' }}>Đang tải dữ liệu...</div>
            ) : requests.length === 0 ? (
                <div style={{ background: 'white', padding: '40px', borderRadius: '12px', border: '1px dashed var(--c-border)', textAlign: 'center' }}>
                    <CalendarDays size={48} style={{ color: 'var(--c-muted)', marginBottom: '16px' }} />
                    <h3 style={{ color: 'var(--c-text-dark)', marginBottom: '8px' }}>Không có đề xuất tái khám</h3>
                    <p style={{ color: 'var(--c-muted)' }}>Bạn hiện không có lời mời tái khám nào từ bác sĩ.</p>
                </div>
            ) : (
                <div style={{ display: 'grid', gap: '16px' }}>
                    {requests.map(req => (
                        <div key={req.id} className="card-panel" style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', borderBottom: '1px solid var(--c-border)', paddingBottom: '12px' }}>
                                <div>
                                    <div style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--c-text-dark)' }}>Từ bác sĩ: {req.doctorName}</div>
                                    <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem', marginTop: '4px' }}>
                                        <AlertCircle size={14} style={{ verticalAlign: 'middle', marginRight: '4px' }}/> 
                                        Ghi chú: {req.note || 'Không có ghi chú'}
                                    </div>
                                </div>
                                {getStatusBadge(req.status)}
                            </div>
                            
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <CalendarDays size={18} style={{ color: 'var(--c-teal)' }}/>
                                    <span><strong>Ngày gợi ý:</strong> {formatDate(req.suggestedDate)}</span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Stethoscope size={18} style={{ color: 'var(--c-teal)' }}/>
                                    <span><strong>Khoa:</strong> {req.specialtyName}</span>
                                </div>
                            </div>

                            {req.status === 'Pending' && (
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px', paddingTop: '10px', borderTop: '1px solid var(--c-bg)' }}>
                                    <button 
                                        className="btn-danger" 
                                        style={{ padding: '6px 12px', fontSize: '0.9rem', display: 'flex', alignItems: 'center', gap: '6px', background: 'white', color: 'var(--c-danger)', border: '1px solid var(--c-danger)' }} 
                                        onClick={() => openRejectModal(req.id)}
                                        disabled={actionLoading === req.id}
                                    >
                                        <XCircle size={16} /> Từ chối
                                    </button>
                                    <button 
                                        className="btn-primary" 
                                        style={{ padding: '6px 12px', fontSize: '0.9rem', display: 'flex', alignItems: 'center', gap: '6px' }} 
                                        onClick={handleAccept}
                                        disabled={actionLoading === req.id}
                                    >
                                        <CheckCircle size={16} /> Chọn lịch ngay
                                    </button>
                                </div>
                            )}
                        </div>
                    ))}
                </div>
            )}

            {showRejectModal && (
                <div style={{
                    position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
                    backgroundColor: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(2px)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000
                }}>
                    <div style={{
                        background: 'white', padding: '24px', borderRadius: '12px',
                        width: '100%', maxWidth: '500px', margin: '0 20px',
                        boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '16px' }}>
                            <div style={{ width: '40px', height: '40px', borderRadius: '50%', background: 'var(--c-danger-bg)', color: 'var(--c-danger)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                <AlertCircle size={24} />
                            </div>
                            <h3 style={{ margin: 0, color: 'var(--c-text-dark)', fontSize: '1.2rem' }}>Từ chối tái khám</h3>
                        </div>
                        
                        <p style={{ color: 'var(--c-text)', marginBottom: '16px' }}>Bạn có chắc chắn muốn từ chối đề xuất tái khám này?</p>
                        
                        {rejectError && (
                            <div style={{ background: 'var(--c-danger-bg)', color: 'var(--c-danger)', padding: '12px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.9rem' }}>
                                {rejectError}
                            </div>
                        )}

                        <div style={{ marginBottom: '20px' }}>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500, fontSize: '0.9rem' }}>Lý do từ chối (tùy chọn)</label>
                            <textarea
                                className="form-textarea"
                                rows={3}
                                placeholder="Nhập lý do từ chối..."
                                value={rejectReason}
                                onChange={(e) => setRejectReason(e.target.value)}
                            />
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px' }}>
                            <button className="btn-secondary" onClick={closeRejectModal} disabled={actionLoading === rejectingId}>Quay lại</button>
                            <button className="btn-danger" onClick={submitReject} disabled={actionLoading === rejectingId}>
                                {actionLoading === rejectingId ? 'Đang xử lý...' : 'Xác nhận từ chối'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
