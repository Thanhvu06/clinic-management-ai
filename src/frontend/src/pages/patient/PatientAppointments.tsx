import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import {
    CalendarDays, Clock, Stethoscope, User, AlertTriangle,
    XCircle, X, History as HistoryIcon, AlertCircle
} from 'lucide-react';
import { ToastContainer, useToast } from '../../components/Toast';

export const PatientAppointments: React.FC = () => {
    const navigate = useNavigate();
    const { toasts, dismiss, success, warning } = useToast();

    const [appointments, setAppointments] = useState<any[]>([]);
    const [specialtiesMap, setSpecialtiesMap] = useState<Record<number, string>>({});
    const [loading, setLoading] = useState(true);
    const [activeTab, setActiveTab] = useState('All');

    // Cancel modal state
    const [cancelModal, setCancelModal] = useState<{
        isOpen: boolean;
        app: any | null;
        reason: string;
        inlineError: string;
    }>({ isOpen: false, app: null, reason: '', inlineError: '' });
    const [canceling, setCanceling] = useState(false);

    // History modal state
    const [historyModal, setHistoryModal] = useState<{
        isOpen: boolean;
        appCode: string;
        histories: any[];
        loading: boolean;
    }>({ isOpen: false, appCode: '', histories: [], loading: false });

    const fetchData = async () => {
        setLoading(true);
        try {
            const [specRes, appRes] = await Promise.all([
                axiosClient.get<any, ApiResponse<any[]>>('/specialties'),
                axiosClient.get<any, ApiResponse<any>>('/appointments/my?page=1&pageSize=100')
            ]);

            if (specRes.success && specRes.data) {
                const sm: Record<number, string> = {};
                specRes.data.forEach((s: any) => sm[s.id] = s.specialtyName || s.name || '');
                setSpecialtiesMap(sm);
            }

            if (appRes.success && appRes.data?.items) {
                setAppointments(appRes.data.items);
            }
        } catch (_) {}
        finally { setLoading(false); }
    };

    useEffect(() => { fetchData(); }, []);

    const getStatusBadge = (status: string) => {
        switch (status) {
            case 'Pending': return <span className="badge badge-warning">Chờ xác nhận</span>;
            case 'Confirmed': return <span className="badge badge-info">Đã xác nhận</span>;
            case 'PendingCancellation': return <span className="badge badge-warning">Chờ hủy</span>;
            case 'PendingReschedule': return <span className="badge badge-warning">Chờ đổi lịch</span>;
            case 'Completed': return <span className="badge badge-success">Đã hoàn thành</span>;
            case 'Cancelled': return <span className="badge badge-danger">Đã hủy</span>;
            case 'NoShow': return <span className="badge badge-muted">Vắng mặt</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
    };

    const getActionLabel = (action: string) => {
        switch (action) {
            case 'Created': return 'Tạo mới';
            case 'StatusChanged': return 'Cập nhật trạng thái';
            case 'Cancelled': return 'Hủy lịch';
            case 'Rescheduled': return 'Đổi lịch';
            default: return action;
        }
    };

    const handleCancelOpen = (app: any) => {
        setCancelModal({ isOpen: true, app, reason: '', inlineError: '' });
    };

    const handleCancelClose = () => {
        if (canceling) return;
        setCancelModal({ isOpen: false, app: null, reason: '', inlineError: '' });
    };

    const handleCancelSubmit = async () => {
        const { app, reason } = cancelModal;
        if (!app || !reason.trim()) return;
        setCanceling(true);
        setCancelModal(prev => ({ ...prev, inlineError: '' }));
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(
                `/appointments/${app.id}/cancellation-requests`,
                { reason }
            );
            if (res.success) {
                setCancelModal({ isOpen: false, app: null, reason: '', inlineError: '' });
                success('Đã gửi yêu cầu hủy lịch. Lễ tân sẽ xử lý trong thời gian sớm nhất.');
                fetchData();
            }
        } catch (err: any) {
            const code = err?.errorCode;
            if (code === 'ACTIVE_CHANGE_REQUEST_EXISTS') {
                setCancelModal(prev => ({
                    ...prev,
                    inlineError: 'Lịch hẹn này đã có yêu cầu thay đổi đang chờ xử lý. Vui lòng đợi lễ tân xử lý xong trước.'
                }));
                fetchData();
            } else if (code === 'INVALID_APPOINTMENT_STATUS') {
                setCancelModal(prev => ({
                    ...prev,
                    inlineError: 'Trạng thái lịch hiện tại không cho phép gửi yêu cầu hủy.'
                }));
            } else {
                setCancelModal(prev => ({
                    ...prev,
                    inlineError: err?.message || 'Có lỗi xảy ra. Vui lòng thử lại.'
                }));
            }
        } finally {
            setCanceling(false);
        }
    };

    const handleViewHistory = async (app: any) => {
        setHistoryModal({ isOpen: true, appCode: app.appointmentCode, histories: [], loading: true });
        try {
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/appointments/${app.id}/history`);
            if (res.success && res.data) {
                setHistoryModal(prev => ({ ...prev, histories: res.data!, loading: false }));
            } else {
                setHistoryModal(prev => ({ ...prev, loading: false }));
            }
        } catch (_) {
            setHistoryModal(prev => ({ ...prev, loading: false }));
            warning('Không thể tải lịch sử lúc này. Vui lòng thử lại.');
        }
    };

    const tabs = [
        { key: 'All', label: 'Tất cả' },
        { key: 'Pending', label: 'Chờ xác nhận' },
        { key: 'Confirmed', label: 'Đã xác nhận' },
        { key: 'Completed', label: 'Đã hoàn thành' },
        { key: 'Cancelled', label: 'Đã hủy/Vắng mặt' },
    ];

    const filteredAppointments = appointments.filter(app => {
        if (activeTab === 'All') return true;
        if (activeTab === 'Cancelled') return app.status === 'Cancelled' || app.status === 'NoShow';
        return app.status === activeTab;
    });

    const formatDate = (d: string) => {
        try {
            // Handle DateOnly "YYYY-MM-DD"
            const parts = d.split('-');
            return `${parts[2]}/${parts[1]}/${parts[0]}`;
        } catch { return d; }
    };

    const canCancel = (status: string) =>
        status === 'Pending' || status === 'Confirmed';

    return (
        <div style={{ maxWidth: '960px', margin: '0 auto' }}>
            <ToastContainer toasts={toasts} onDismiss={dismiss} />

            <div style={{ marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', margin: 0 }}>Lịch hẹn của tôi</h2>
                <p style={{ color: 'var(--c-muted)', marginTop: '4px', fontSize: '0.95rem' }}>
                    Xem và quản lý các lịch khám của bạn
                </p>
            </div>

            {/* Tabs */}
            <div style={{ display: 'flex', gap: '8px', marginBottom: '20px', overflowX: 'auto', paddingBottom: '4px' }}>
                {tabs.map(tab => (
                    <button
                        key={tab.key}
                        onClick={() => setActiveTab(tab.key)}
                        style={{
                            padding: '7px 16px',
                            borderRadius: '20px',
                            border: '1.5px solid',
                            borderColor: activeTab === tab.key ? 'var(--c-teal)' : 'var(--c-border)',
                            backgroundColor: activeTab === tab.key ? 'var(--c-teal)' : 'white',
                            color: activeTab === tab.key ? 'white' : 'var(--c-text)',
                            cursor: 'pointer',
                            whiteSpace: 'nowrap',
                            fontSize: '0.875rem',
                            fontWeight: activeTab === tab.key ? 600 : 400,
                            transition: 'all 0.15s',
                        }}
                    >{tab.label}</button>
                ))}
            </div>

            {loading ? (
                <div style={{ background: 'white', borderRadius: '12px', border: '1px solid var(--c-border)', padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                    Đang tải lịch hẹn...
                </div>
            ) : filteredAppointments.length === 0 ? (
                <div style={{ background: 'white', padding: '48px 24px', borderRadius: '12px', border: '1px dashed var(--c-border)', textAlign: 'center' }}>
                    <CalendarDays size={40} style={{ color: 'var(--c-muted)', marginBottom: '12px' }} />
                    <h3 style={{ color: 'var(--c-text-dark)', marginBottom: '6px' }}>Chưa có lịch hẹn nào</h3>
                    <p style={{ color: 'var(--c-muted)', fontSize: '0.95rem', marginBottom: '20px' }}>
                        {activeTab === 'All' ? 'Bạn chưa có lịch hẹn nào.' : 'Không có lịch hẹn ở trạng thái này.'}
                    </p>
                    {activeTab === 'All' && (
                        <button className="btn-primary" onClick={() => navigate('/patient/book')}>
                            Đặt lịch khám mới
                        </button>
                    )}
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                    {filteredAppointments.map(app => (
                        <div key={app.id} style={{
                            background: 'white',
                            border: '1px solid var(--c-border)',
                            borderRadius: '12px',
                            overflow: 'hidden',
                            boxShadow: '0 1px 4px rgba(0,0,0,0.05)',
                        }}>
                            {/* Card header: code + status */}
                            <div style={{
                                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                                padding: '12px 18px',
                                background: 'var(--c-bg)',
                                borderBottom: '1px solid var(--c-border)',
                            }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                    <span style={{ fontWeight: 700, color: 'var(--c-navy-dark)', fontSize: '0.95rem', fontFamily: 'monospace' }}>
                                        {app.appointmentCode}
                                    </span>
                                    {app.reason && (
                                        <span style={{ color: 'var(--c-muted)', fontSize: '0.85rem', borderLeft: '1px solid var(--c-border)', paddingLeft: '12px' }}>
                                            {app.reason.length > 50 ? app.reason.slice(0, 50) + '…' : app.reason}
                                        </span>
                                    )}
                                </div>
                                {getStatusBadge(app.status)}
                            </div>

                            {/* Card body: meta info */}
                            <div style={{ padding: '14px 18px', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '10px' }}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--c-text)' }}>
                                    <CalendarDays size={16} style={{ color: 'var(--c-teal)', flexShrink: 0 }} />
                                    <span style={{ fontSize: '0.9rem' }}><strong>Ngày:</strong> {formatDate(app.appointmentDate)}</span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--c-text)' }}>
                                    <Clock size={16} style={{ color: 'var(--c-teal)', flexShrink: 0 }} />
                                    <span style={{ fontSize: '0.9rem' }}><strong>Giờ:</strong> {app.startTime?.substring(0, 5)}</span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--c-text)' }}>
                                    <Stethoscope size={16} style={{ color: 'var(--c-teal)', flexShrink: 0 }} />
                                    <span style={{ fontSize: '0.9rem' }}>
                                        <strong>Chuyên khoa:</strong>{' '}
                                        {app.specialtyName || specialtiesMap[app.specialtyId] || '–'}
                                    </span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--c-text)' }}>
                                    <User size={16} style={{ color: 'var(--c-teal)', flexShrink: 0 }} />
                                    <span style={{ fontSize: '0.9rem' }}>
                                        <strong>Bác sĩ:</strong>{' '}
                                        {app.doctorName || '–'}
                                    </span>
                                </div>
                            </div>

                            {/* Card footer: actions */}
                            <div style={{
                                display: 'flex', justifyContent: 'flex-end', gap: '8px',
                                padding: '10px 18px',
                                borderTop: '1px solid var(--c-border)',
                                background: '#fafbfc',
                            }}>
                                <button
                                    className="btn-secondary"
                                    style={{ padding: '6px 12px', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '5px' }}
                                    onClick={() => handleViewHistory(app)}
                                >
                                    <HistoryIcon size={14} /> Lịch sử
                                </button>
                                {canCancel(app.status) && (
                                    <button
                                        className="btn-danger"
                                        style={{ padding: '6px 12px', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '5px' }}
                                        onClick={() => handleCancelOpen(app)}
                                    >
                                        <XCircle size={14} /> Hủy lịch
                                    </button>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {/* ===================== CANCEL MODAL ===================== */}
            {cancelModal.isOpen && (
                <div style={{
                    position: 'fixed', inset: 0, zIndex: 200,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    backgroundColor: 'rgba(15, 23, 42, 0.45)',
                    backdropFilter: 'blur(2px)',
                    padding: '20px',
                }}>
                    <div style={{
                        background: 'white', borderRadius: '16px',
                        width: '100%', maxWidth: '520px',
                        boxShadow: '0 20px 60px rgba(0,0,0,0.18)',
                        overflow: 'hidden',
                    }}>
                        {/* Modal header */}
                        <div style={{ padding: '20px 24px 16px', borderBottom: '1px solid var(--c-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                <div style={{ width: '36px', height: '36px', borderRadius: '50%', background: '#fef2f2', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                    <AlertTriangle size={18} color="#dc2626" />
                                </div>
                                <h3 style={{ margin: 0, color: 'var(--c-text-dark)', fontSize: '1.05rem' }}>
                                    Xác nhận gửi yêu cầu hủy lịch
                                </h3>
                            </div>
                            <button onClick={handleCancelClose} disabled={canceling} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--c-muted)', padding: '4px' }}>
                                <X size={20} />
                            </button>
                        </div>

                        {/* Modal body */}
                        <div style={{ padding: '20px 24px' }}>
                            {/* Appointment summary */}
                            {cancelModal.app && (
                                <div style={{ background: 'var(--c-bg)', border: '1px solid var(--c-border)', borderRadius: '10px', padding: '14px 16px', marginBottom: '16px', fontSize: '0.9rem', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <span style={{ color: 'var(--c-muted)', minWidth: '90px' }}>Mã lịch:</span>
                                        <strong style={{ fontFamily: 'monospace' }}>{cancelModal.app.appointmentCode}</strong>
                                    </div>
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <span style={{ color: 'var(--c-muted)', minWidth: '90px' }}>Bác sĩ:</span>
                                        <span>{cancelModal.app.doctorName || '–'}</span>
                                    </div>
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <span style={{ color: 'var(--c-muted)', minWidth: '90px' }}>Chuyên khoa:</span>
                                        <span>{cancelModal.app.specialtyName || specialtiesMap[cancelModal.app.specialtyId] || '–'}</span>
                                    </div>
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <span style={{ color: 'var(--c-muted)', minWidth: '90px' }}>Ngày giờ:</span>
                                        <span>{formatDate(cancelModal.app.appointmentDate)} lúc {cancelModal.app.startTime?.substring(0, 5)}</span>
                                    </div>
                                </div>
                            )}

                            {/* Inline error */}
                            {cancelModal.inlineError && (
                                <div style={{
                                    display: 'flex', gap: '10px', alignItems: 'flex-start',
                                    background: '#fef2f2', border: '1px solid #fca5a5',
                                    borderRadius: '8px', padding: '12px 14px',
                                    color: '#991b1b', fontSize: '0.9rem', marginBottom: '14px',
                                }}>
                                    <AlertCircle size={16} style={{ flexShrink: 0, marginTop: '1px' }} />
                                    <span>{cancelModal.inlineError}</span>
                                </div>
                            )}

                            <label style={{ display: 'block', fontWeight: 500, color: 'var(--c-text-dark)', marginBottom: '6px', fontSize: '0.9rem' }}>
                                Lý do hủy lịch <span style={{ color: 'var(--c-danger)' }}>*</span>
                            </label>
                            <textarea
                                className="form-textarea"
                                rows={3}
                                placeholder="Ví dụ: Tôi bị bận đột xuất, không thể đến khám được..."
                                value={cancelModal.reason}
                                onChange={e => setCancelModal(prev => ({ ...prev, reason: e.target.value }))}
                                disabled={canceling}
                                style={{ marginBottom: '0' }}
                            />
                        </div>

                        {/* Modal footer */}
                        <div style={{ padding: '14px 24px 20px', display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                            <button className="btn-secondary" onClick={handleCancelClose} disabled={canceling}>
                                Quay lại
                            </button>
                            <button
                                className="btn-danger"
                                onClick={handleCancelSubmit}
                                disabled={canceling || !cancelModal.reason.trim()}
                                style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                            >
                                {canceling ? (
                                    <>Đang gửi...</>
                                ) : (
                                    <><XCircle size={15} /> Gửi yêu cầu hủy</>
                                )}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* ===================== HISTORY MODAL ===================== */}
            {historyModal.isOpen && (
                <div style={{
                    position: 'fixed', inset: 0, zIndex: 200,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    backgroundColor: 'rgba(15, 23, 42, 0.45)',
                    backdropFilter: 'blur(2px)',
                    padding: '20px',
                }}>
                    <div style={{
                        background: 'white', borderRadius: '16px',
                        width: '100%', maxWidth: '520px', maxHeight: '82vh',
                        display: 'flex', flexDirection: 'column',
                        boxShadow: '0 20px 60px rgba(0,0,0,0.18)',
                        overflow: 'hidden',
                    }}>
                        <div style={{ padding: '18px 24px', borderBottom: '1px solid var(--c-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexShrink: 0 }}>
                            <div>
                                <h3 style={{ margin: 0, fontSize: '1rem' }}>Lịch sử xử lý</h3>
                                <span style={{ fontSize: '0.82rem', color: 'var(--c-muted)', fontFamily: 'monospace' }}>
                                    {historyModal.appCode}
                                </span>
                            </div>
                            <button onClick={() => setHistoryModal({ isOpen: false, appCode: '', histories: [], loading: false })} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--c-muted)' }}>
                                <X size={20} />
                            </button>
                        </div>
                        <div style={{ padding: '20px 24px', overflowY: 'auto', flex: 1 }}>
                            {historyModal.loading ? (
                                <div style={{ color: 'var(--c-muted)', textAlign: 'center', padding: '20px' }}>Đang tải lịch sử...</div>
                            ) : historyModal.histories.length === 0 ? (
                                <div style={{ color: 'var(--c-muted)', textAlign: 'center', padding: '20px' }}>Chưa có lịch sử nào.</div>
                            ) : (
                                <div style={{ position: 'relative' }}>
                                    <div style={{ position: 'absolute', left: '7px', top: '10px', bottom: '10px', width: '2px', background: 'var(--c-border)' }} />
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                                        {historyModal.histories.map((h: any) => (
                                            <div key={h.id} style={{ paddingLeft: '28px', position: 'relative' }}>
                                                <div style={{
                                                    position: 'absolute', left: '2px', top: '4px',
                                                    width: '12px', height: '12px', borderRadius: '50%',
                                                    background: 'var(--c-teal)', border: '2px solid white',
                                                    boxShadow: '0 0 0 2px var(--c-teal)',
                                                }} />
                                                <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginBottom: '3px' }}>
                                                    {h.createdAt?.substring(0, 10).split('-').reverse().join('/')} {h.createdAt?.substring(11, 16)}
                                                </div>
                                                <div style={{ fontWeight: 600, color: 'var(--c-text-dark)', fontSize: '0.9rem', marginBottom: '3px' }}>
                                                    {getActionLabel(h.action)}
                                                </div>
                                                {h.note && (
                                                    <div style={{ fontSize: '0.875rem', color: 'var(--c-text)', marginBottom: '4px' }}>
                                                        {h.note}
                                                    </div>
                                                )}
                                                {(h.oldStatus || h.newStatus) && (
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                                                        {h.oldStatus && getStatusBadge(h.oldStatus)}
                                                        {h.oldStatus && h.newStatus && <span style={{ color: 'var(--c-muted)', fontSize: '0.85rem' }}>→</span>}
                                                        {h.newStatus && getStatusBadge(h.newStatus)}
                                                    </div>
                                                )}
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            )}
                        </div>
                        <div style={{ padding: '14px 24px', borderTop: '1px solid var(--c-border)', display: 'flex', justifyContent: 'flex-end', flexShrink: 0 }}>
                            <button className="btn-secondary" onClick={() => setHistoryModal({ isOpen: false, appCode: '', histories: [], loading: false })}>
                                Đóng
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
