import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarDays, Clock, Stethoscope, User, AlertCircle, XCircle, X, History as HistoryIcon } from 'lucide-react';

export const PatientAppointments: React.FC = () => {
    const [appointments, setAppointments] = useState<any[]>([]);
    const [specialtiesMap, setSpecialtiesMap] = useState<Record<number, string>>({});
    const [doctorsMap, setDoctorsMap] = useState<Record<number, string>>({});
    const [loading, setLoading] = useState(true);
    const [activeTab, setActiveTab] = useState('All');
    
    // Cancellation Modal
    const [cancelModal, setCancelModal] = useState<{ isOpen: boolean, appId: number | null, reason: string }>({ isOpen: false, appId: null, reason: '' });
    const [canceling, setCanceling] = useState(false);
    
    // History Modal
    const [historyModal, setHistoryModal] = useState<{ isOpen: boolean, histories: any[], loading: boolean }>({ isOpen: false, histories: [], loading: false });

    const fetchData = async () => {
        setLoading(true);
        try {
            const [specRes, appRes] = await Promise.all([
                axiosClient.get<any, ApiResponse<any[]>>('/specialties'),
                axiosClient.get<any, ApiResponse<any>>('/appointments/my?page=1&pageSize=100')
            ]);

            if (specRes.success && specRes.data) {
                const sm: Record<number, string> = {};
                specRes.data.forEach(s => sm[s.id] = s.name);
                setSpecialtiesMap(sm);
            }

            if (appRes.success && appRes.data?.items) {
                setAppointments(appRes.data.items);
                
                const doctorIds = [...new Set(appRes.data.items.map((a: any) => a.doctorId))];
                const dm: Record<number, string> = {};
                for (const dId of doctorIds as number[]) {
                    try {
                        const dRes = await axiosClient.get<any, ApiResponse<any>>(`/doctors/${dId}`);
                        if (dRes.success && dRes.data) {
                            dm[dId] = dRes.data.fullName;
                        }
                    } catch(e) {}
                }
                setDoctorsMap(dm);
            }
        } catch (error) {} finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchData();
    }, []);

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <span className="badge badge-warning">Chờ xác nhận</span>;
            case 'Confirmed': return <span className="badge badge-info">Đã xác nhận</span>;
            case 'Completed': return <span className="badge badge-success">Đã hoàn thành</span>;
            case 'Cancelled': return <span className="badge badge-danger">Đã hủy</span>;
            case 'NoShow': return <span className="badge badge-muted">Vắng mặt</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
    };

    const getActionName = (action: string) => {
        switch(action) {
            case 'Created': return 'Tạo mới';
            case 'StatusChanged': return 'Cập nhật trạng thái';
            case 'Cancelled': return 'Hủy lịch';
            case 'Rescheduled': return 'Đổi lịch';
            default: return action;
        }
    };

    const handleCancelSubmit = async () => {
        if (!cancelModal.appId || !cancelModal.reason) return;
        setCanceling(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/appointments/${cancelModal.appId}/cancellation-requests`, {
                reason: cancelModal.reason
            });
            if (res.success) {
                alert('Yêu cầu hủy lịch đã được gửi, vui lòng chờ lễ tân xử lý.');
                setCancelModal({ isOpen: false, appId: null, reason: '' });
                // We should ideally fetch data again, but it might just be a request pending. We won't see it immediately in GET /my unless it changes status.
                // Re-fetch to be safe
                fetchData();
            }
        } catch (error: any) {
            const msg = error?.errorCode === 'ACTIVE_CHANGE_REQUEST_EXISTS' ? 'Đã có yêu cầu đang xử lý cho lịch này.' :
                        error?.errorCode === 'INVALID_APPOINTMENT_STATUS' ? 'Trạng thái lịch hiện tại không cho phép hủy.' : 
                        error?.message || 'Lỗi khi gửi yêu cầu hủy.';
            alert(msg);
        } finally {
            setCanceling(false);
        }
    };

    const handleViewHistory = async (appId: number) => {
        setHistoryModal({ isOpen: true, histories: [], loading: true });
        try {
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/appointments/${appId}/history`);
            if (res.success && res.data) {
                setHistoryModal({ isOpen: true, histories: res.data, loading: false });
            }
        } catch (error) {
            setHistoryModal({ isOpen: false, histories: [], loading: false });
            alert('Không thể xem lịch sử lúc này.');
        }
    };

    const filteredAppointments = appointments.filter(app => {
        if (activeTab === 'All') return true;
        if (activeTab === 'Pending') return app.status === 'Pending';
        if (activeTab === 'Confirmed') return app.status === 'Confirmed';
        if (activeTab === 'Completed') return app.status === 'Completed';
        if (activeTab === 'Cancelled') return app.status === 'Cancelled' || app.status === 'NoShow';
        return true;
    });

    const formatDate = (dateString: string) => {
        try {
            const date = new Date(dateString);
            return new Intl.DateTimeFormat('vi-VN').format(date);
        } catch (e) {
            return dateString;
        }
    };

    return (
        <div style={{ maxWidth: '900px', margin: '0 auto' }}>
            <h2 style={{ marginBottom: '24px', color: 'var(--c-navy-dark)' }}>Lịch hẹn của tôi</h2>
            
            <div style={{ display: 'flex', gap: '10px', marginBottom: '20px', overflowX: 'auto', paddingBottom: '10px' }}>
                {['All', 'Pending', 'Confirmed', 'Completed', 'Cancelled'].map(tab => {
                    const label = tab === 'All' ? 'Tất cả' :
                                  tab === 'Pending' ? 'Chờ xác nhận' :
                                  tab === 'Confirmed' ? 'Đã xác nhận' :
                                  tab === 'Completed' ? 'Đã hoàn thành' : 'Đã hủy/Vắng mặt';
                    return (
                        <button 
                            key={tab}
                            onClick={() => setActiveTab(tab)}
                            style={{
                                padding: '8px 16px',
                                borderRadius: '20px',
                                border: '1px solid',
                                borderColor: activeTab === tab ? 'var(--c-teal)' : 'var(--c-border)',
                                backgroundColor: activeTab === tab ? 'var(--c-teal)' : 'white',
                                color: activeTab === tab ? 'white' : 'var(--c-text)',
                                cursor: 'pointer',
                                whiteSpace: 'nowrap'
                            }}
                        >
                            {label}
                        </button>
                    )
                })}
            </div>

            {loading ? (
                <div style={{ color: 'var(--c-muted)', padding: '20px' }}>Đang tải...</div>
            ) : filteredAppointments.length === 0 ? (
                <div style={{ background: 'white', padding: '40px', borderRadius: '12px', border: '1px dashed var(--c-border)', textAlign: 'center' }}>
                    <CalendarDays size={48} style={{ color: 'var(--c-muted)', marginBottom: '16px' }} />
                    <h3 style={{ color: 'var(--c-text-dark)', marginBottom: '8px' }}>Chưa có lịch hẹn nào</h3>
                    <p style={{ color: 'var(--c-muted)' }}>Bạn chưa có lịch hẹn nào ở trạng thái này.</p>
                </div>
            ) : (
                <div style={{ display: 'grid', gap: '16px' }}>
                    {filteredAppointments.map(app => (
                        <div key={app.id} className="card-panel" style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', borderBottom: '1px solid var(--c-border)', paddingBottom: '12px' }}>
                                <div>
                                    <div style={{ fontSize: '1.1rem', fontWeight: 600, color: 'var(--c-text-dark)' }}>Mã lịch: {app.appointmentCode}</div>
                                    <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem', marginTop: '4px' }}>
                                        <AlertCircle size={14} style={{ verticalAlign: 'middle', marginRight: '4px' }}/> 
                                        Lý do: {app.reason}
                                    </div>
                                </div>
                                {getStatusBadge(app.status)}
                            </div>
                            
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <CalendarDays size={18} style={{ color: 'var(--c-teal)' }}/>
                                    <span><strong>Ngày:</strong> {formatDate(app.appointmentDate)}</span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Clock size={18} style={{ color: 'var(--c-teal)' }}/>
                                    <span><strong>Giờ:</strong> {app.startTime.substring(0, 5)}</span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Stethoscope size={18} style={{ color: 'var(--c-teal)' }}/>
                                    <span><strong>Khoa:</strong> {specialtiesMap[app.specialtyId] || `ID: ${app.specialtyId}`}</span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <User size={18} style={{ color: 'var(--c-teal)' }}/>
                                    <span><strong>Bác sĩ:</strong> {doctorsMap[app.doctorId] || `ID: ${app.doctorId}`}</span>
                                </div>
                            </div>

                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px', paddingTop: '10px', borderTop: '1px solid var(--c-bg)' }}>
                                <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.9rem' }} onClick={() => handleViewHistory(app.id)}>
                                    <HistoryIcon size={16} /> Lịch sử
                                </button>
                                {(app.status === 'Pending' || app.status === 'Confirmed') && (
                                    <>
                                        <button className="btn-danger" style={{ padding: '6px 12px', fontSize: '0.9rem', display: 'flex', alignItems: 'center', gap: '6px' }} onClick={() => setCancelModal({ isOpen: true, appId: app.id, reason: '' })}>
                                            <XCircle size={16} /> Hủy lịch
                                        </button>
                                    </>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {/* Cancel Modal */}
            {cancelModal.isOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '400px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                            <h3 style={{ margin: 0 }}>Xác nhận hủy lịch</h3>
                            <button onClick={() => setCancelModal({ ...cancelModal, isOpen: false })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={20} /></button>
                        </div>
                        <p style={{ marginBottom: '16px', fontSize: '0.95rem' }}>Vui lòng cho biết lý do bạn muốn hủy lịch này:</p>
                        <textarea 
                            className="form-textarea" 
                            rows={3} 
                            placeholder="Nhập lý do hủy..."
                            value={cancelModal.reason}
                            onChange={(e) => setCancelModal({ ...cancelModal, reason: e.target.value })}
                            style={{ marginBottom: '16px' }}
                        />
                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                            <button className="btn-secondary" onClick={() => setCancelModal({ ...cancelModal, isOpen: false })}>Đóng</button>
                            <button className="btn-danger" onClick={handleCancelSubmit} disabled={canceling || !cancelModal.reason.trim()}>
                                {canceling ? 'Đang gửi...' : 'Gửi yêu cầu hủy'}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* History Modal */}
            {historyModal.isOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '500px', maxHeight: '80vh', overflowY: 'auto' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                            <h3 style={{ margin: 0 }}>Lịch sử xử lý</h3>
                            <button onClick={() => setHistoryModal({ isOpen: false, histories: [], loading: false })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={20} /></button>
                        </div>
                        {historyModal.loading ? (
                            <div>Đang tải lịch sử...</div>
                        ) : historyModal.histories.length === 0 ? (
                            <div>Chưa có lịch sử nào.</div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                {historyModal.histories.map((h: any) => (
                                    <div key={h.id} style={{ borderLeft: '2px solid var(--c-border)', paddingLeft: '16px', paddingBottom: '16px', position: 'relative' }}>
                                        <div style={{ position: 'absolute', left: '-5px', top: '0', width: '8px', height: '8px', borderRadius: '50%', background: 'var(--c-teal)' }}></div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginBottom: '4px' }}>
                                            {formatDate(h.createdAt.substring(0, 10))} {h.createdAt.substring(11, 16)}
                                        </div>
                                        <div style={{ fontWeight: 600, color: 'var(--c-text-dark)', marginBottom: '4px' }}>
                                            {getActionName(h.action)}
                                        </div>
                                        {h.note && <div style={{ fontSize: '0.9rem' }}>Ghi chú: {h.note}</div>}
                                        {(h.oldStatus || h.newStatus) && (
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                                                {h.oldStatus && getStatusBadge(h.oldStatus)} {h.newStatus && <>→ {getStatusBadge(h.newStatus)}</>}
                                            </div>
                                        )}
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
};
