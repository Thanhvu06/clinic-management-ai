import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, CalendarDays, Eye, X, Clock, RefreshCw } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface ReceptionAppointment {
    id: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    doctorId: number;
    doctorName: string;
    specialtyId: number;
    specialtyName: string;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string | null;
    status: string;
}

export const ReceptionAppointments: React.FC = () => {
    const [appointments, setAppointments] = useState<ReceptionAppointment[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Detail Modal
    const [modal, setModal] = useState<{ isOpen: boolean, apt: ReceptionAppointment | null }>({ isOpen: false, apt: null });
    const [actionLoading, setActionLoading] = useState(false);
    const [history, setHistory] = useState<any[]>([]);
    const [historyLoading, setHistoryLoading] = useState(false);
    const [historyError, setHistoryError] = useState('');

    const fetchAppointments = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments?${params.toString()}`);
            if (res.success && res.data) {
                setAppointments(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error handling
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchAppointments();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchAppointments();
    };

    const fetchHistory = async (id: number) => {
        setHistoryLoading(true);
        setHistoryError('');
        setHistory([]);
        try {
            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments/${id}/history`);
            if (res.success && res.data) {
                setHistory(res.data);
            }
        } catch (error: any) {
            console.error("Lỗi tải lịch sử:", error);
            setHistoryError('Không thể tải lịch sử.');
        } finally {
            setHistoryLoading(false);
        }
    };

    const openDetail = (apt: ReceptionAppointment) => {
        setModal({ isOpen: true, apt });
        fetchHistory(apt.id);
    };

    const { showAlert, showConfirm } = useDialog();

    const handleConfirm = async () => {
        const apt = modal.apt;
        if (!apt) return;
        
        showConfirm('Xác nhận lịch hẹn này hợp lệ và đã sẵn sàng cho bác sĩ?', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/reception/appointments/${apt.id}/confirm`, {});
                if (res.success) {
                    showAlert('Đã xác nhận lịch hẹn.', 'Thành công', 'success');
                    // Update local state temporarily for fast UI or refetch all
                    setModal({ ...modal, apt: { ...apt, status: 'Confirmed' } });
                    fetchHistory(apt.id);
                    fetchAppointments();
                }
            } catch (error: any) {
                if (error?.errorCode === 'INVALID_APPOINTMENT_STATUS') {
                    showAlert('Trạng thái lịch hẹn không hợp lệ (có thể đã được người khác xử lý). Dữ liệu sẽ được làm mới.', 'Thông báo', 'warning');
                    fetchAppointments();
                    setModal({ isOpen: false, apt: null });
                } else {
                    showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
                }
            } finally {
                setActionLoading(false);
            }
        });
    };

    const formatDate = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN').format(new Date(dateString));
        } catch {
            return dateString;
        }
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

    const translateStatus = (status: string) => {
        switch (status) {
            case 'Pending': return 'Chờ xác nhận';
            case 'Confirmed': return 'Đã xác nhận';
            case 'Completed': return 'Đã hoàn thành';
            case 'Cancelled': return 'Đã hủy';
            case 'NoShow': return 'Không đến khám';
            default: return status;
        }
    };

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <span className="badge badge-warning">{translateStatus(status)}</span>;
            case 'Confirmed': return <span className="badge badge-info">{translateStatus(status)}</span>;
            case 'Completed': return <span className="badge badge-success">{translateStatus(status)}</span>;
            case 'Cancelled': return <span className="badge badge-danger">{translateStatus(status)}</span>;
            case 'NoShow': return <span className="badge badge-muted">{translateStatus(status)}</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
    };

    const translateAction = (action: string) => {
        switch (action) {
            case 'Created': return 'Tạo lịch hẹn';
            case 'Confirmed': return 'Xác nhận lịch';
            case 'Cancelled': return 'Hủy lịch';
            case 'Rescheduled': return 'Đổi lịch';
            case 'Completed': return 'Hoàn thành khám';
            default: return action;
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <CalendarDays size={24} /> Quản lý lịch hẹn
                </h2>
                <button className="btn-secondary" onClick={() => fetchAppointments()} style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                    <RefreshCw size={14} /> Làm mới
                </button>
            </div>

            <div className="card" style={{ marginBottom: '24px' }}>
                <form onSubmit={handleSearchSubmit} style={{ display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
                    <div style={{ flex: '1 1 250px' }}>
                        <div style={{ position: 'relative' }}>
                            <Search size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                            <input 
                                type="text" 
                                className="form-input" 
                                placeholder="Tìm theo mã lịch, tên, SĐT..." 
                                style={{ paddingLeft: '40px' }}
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Pending">Chờ xác nhận</option>
                            <option value="Confirmed">Đã xác nhận</option>
                            <option value="Completed">Đã hoàn thành</option>
                            <option value="Cancelled">Đã hủy</option>
                        </select>
                    </div>
                    <button type="submit" className="btn-secondary">Tìm kiếm</button>
                </form>
            </div>

            <div className="card" style={{ padding: 0 }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải dữ liệu...</div>
                ) : (
                    <div className="table-responsive">
                        <table className="table">
                            <thead>
                                <tr>
                                    <th>Lịch khám</th>
                                    <th>Bệnh nhân</th>
                                    <th>Bác sĩ & Chuyên khoa</th>
                                    <th>Trạng thái</th>
                                    <th style={{ textAlign: 'right' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                            {appointments.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy lịch hẹn nào.
                                    </td>
                                </tr>
                            ) : appointments.map(apt => (
                                <tr key={apt.id} style={{ borderBottom: '1px solid var(--c-border)' }}>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>{apt.appointmentCode}</div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px' }}>
                                            <Clock size={12}/> {formatDate(apt.appointmentDate)} {apt.startTime.substring(0,5)}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 500 }}>{apt.patientName}</div>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-muted)' }}>{apt.patientPhone}</div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 500 }}>{apt.doctorName}</div>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-muted)' }}>{apt.specialtyName}</div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        {getStatusBadge(apt.status)}
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'right' }}>
                                        <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => openDetail(apt)}>
                                            <Eye size={14} style={{ marginRight: '4px' }}/> Chi tiết
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
                Tổng cộng: {totalItems} lịch hẹn
            </div>

            {/* Detail Modal */}
            {modal.isOpen && modal.apt && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '600px', maxHeight: '90vh', overflowY: 'auto' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                Chi tiết lịch hẹn <span style={{ color: 'var(--c-teal)' }}>#{modal.apt.appointmentCode}</span>
                            </h3>
                            <button onClick={() => setModal({ isOpen: false, apt: null })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '24px' }}>
                            <div style={{ background: 'var(--c-bg)', padding: '16px', borderRadius: '8px' }}>
                                <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginBottom: '4px' }}>Bệnh nhân</div>
                                <div style={{ fontWeight: 600 }}>{modal.apt.patientName}</div>
                                <div>{modal.apt.patientPhone}</div>
                            </div>
                            <div style={{ background: 'var(--c-bg)', padding: '16px', borderRadius: '8px' }}>
                                <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginBottom: '4px' }}>Bác sĩ & Chuyên khoa</div>
                                <div style={{ fontWeight: 600 }}>{modal.apt.doctorName}</div>
                                <div>{modal.apt.specialtyName}</div>
                            </div>
                            <div style={{ background: 'var(--c-bg)', padding: '16px', borderRadius: '8px', gridColumn: '1 / -1' }}>
                                <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginBottom: '4px' }}>Thời gian khám</div>
                                <div style={{ fontWeight: 600, display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    <Clock size={16} color="var(--c-teal)"/> 
                                    {formatDate(modal.apt.appointmentDate)} ({modal.apt.startTime.substring(0,5)} - {modal.apt.endTime.substring(0,5)})
                                </div>
                            </div>
                        </div>

                        <div style={{ marginBottom: '24px' }}>
                            <div style={{ fontWeight: 500, marginBottom: '8px' }}>Lý do khám:</div>
                            <div style={{ background: 'var(--c-bg)', padding: '12px', borderRadius: '6px', fontSize: '0.95rem' }}>
                                {modal.apt.reason || <span style={{ color: 'var(--c-muted)' }}>Không có ghi chú</span>}
                            </div>
                        </div>

                        <div style={{ marginBottom: '24px' }}>
                            <div style={{ fontWeight: 500, marginBottom: '12px', display: 'flex', justifyContent: 'space-between' }}>
                                <span>Tiến trình xử lý</span>
                                {getStatusBadge(modal.apt.status)}
                            </div>
                            
                            {historyLoading ? (
                                <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Đang tải lịch sử...</div>
                            ) : historyError ? (
                                <div style={{ color: 'var(--c-warning)', fontSize: '0.9rem', background: 'var(--c-warning-bg)', padding: '12px', borderRadius: '6px' }}>{historyError}</div>
                            ) : history.length === 0 ? (
                                <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Không có dữ liệu lịch sử.</div>
                            ) : (
                                <div style={{ borderLeft: '2px solid var(--c-border)', marginLeft: '8px', paddingLeft: '16px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                    {history.map(h => (
                                        <div key={h.id} style={{ position: 'relative' }}>
                                            <div style={{ position: 'absolute', left: '-21px', top: '2px', width: '10px', height: '10px', borderRadius: '50%', background: 'var(--c-teal)' }}></div>
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>{formatDateTime(h.createdAt)}</div>
                                            <div style={{ fontWeight: 500 }}>{translateAction(h.action)}</div>
                                            {h.note && <div style={{ fontSize: '0.9rem', marginTop: '4px', background: 'var(--c-bg)', padding: '6px 10px', borderRadius: '4px' }}>{h.note}</div>}
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>

                        {modal.apt.status === 'Pending' && (
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', paddingTop: '16px', borderTop: '1px solid var(--c-border)' }}>
                                <button className="btn-secondary" onClick={() => setModal({ isOpen: false, apt: null })}>Đóng</button>
                                <button 
                                    className="btn-primary" 
                                    onClick={handleConfirm}
                                    disabled={actionLoading}
                                >
                                    {actionLoading ? 'Đang xử lý...' : 'Xác nhận lịch hẹn'}
                                </button>
                            </div>
                        )}
                        {modal.apt.status !== 'Pending' && (
                            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                                <button className="btn-secondary" onClick={() => setModal({ isOpen: false, apt: null })}>Đóng</button>
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
};
