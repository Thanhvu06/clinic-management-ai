import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, CalendarDays, Eye, CheckCircle, XCircle, Clock, PlusCircle, RefreshCw, X, Send } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface DoctorAppointment {
    id: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    patientGender: string;
    patientDob: string | null;
    specialtyId: number;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string | null;
    status: string;
}

export const DoctorAppointments: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [appointments, setAppointments] = useState<DoctorAppointment[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');
    const [dateFilter, setDateFilter] = useState(''); // 'today' or ''

    // Modals
    const [modal, setModal] = useState<{ isOpen: boolean, apt: DoctorAppointment | null, view: 'detail' | 'complete' | 'revisit' | 'noshow' }>({ isOpen: false, apt: null, view: 'detail' });
    const [actionLoading, setActionLoading] = useState(false);
    
    const [history, setHistory] = useState<any[]>([]);
    
    // Complete Form
    const [completeSummary, setCompleteSummary] = useState('');
    const [completeInstructions, setCompleteInstructions] = useState('');
    
    // No Show Form
    const [noShowReason, setNoShowReason] = useState('');

    // Revisit Form
    const [revisitDate, setRevisitDate] = useState('');
    const [revisitNote, setRevisitNote] = useState('');

    const fetchAppointments = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);
            
            // The backend doesn't seem to have a date filter explicit for today in DoctorAppointmentController (only search, status). 
            // So we might have to filter client-side if needed, but let's send it if the backend supports it later.

            const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments?${params.toString()}`);
            if (res.success && res.data) {
                let items = res.data.items as DoctorAppointment[];
                if (dateFilter === 'today') {
                    const todayStr = new Date().toISOString().split('T')[0];
                    items = items.filter(i => i.appointmentDate.startsWith(todayStr));
                }
                
                // Sort by date/time
                items.sort((a, b) => {
                    const dateDiff = new Date(a.appointmentDate).getTime() - new Date(b.appointmentDate).getTime();
                    if (dateDiff !== 0) return dateDiff;
                    return a.startTime.localeCompare(b.startTime);
                });

                setAppointments(items);
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
    }, [page, statusFilter, dateFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchAppointments();
    };

    const openModal = async (apt: DoctorAppointment, view: 'detail' | 'complete' | 'revisit' | 'noshow') => {
        setModal({ isOpen: true, apt, view });
        setCompleteSummary('');
        setCompleteInstructions('');
        setNoShowReason('');
        setRevisitDate('');
        setRevisitNote('');

        if (view === 'detail') {
            try {
                // Doctor may not have access to history, but we try (if admin/patient only, this will fail)
                const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${apt.id}/history`);
                if (res.success && res.data) {
                    setHistory(res.data);
                }
            } catch (error: any) {
                setHistory([]);
            }
        }
    };

    const handleComplete = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!modal.apt) return;
        
        showConfirm('Xác nhận hoàn thành buổi khám? Thông tin này chỉ là tóm tắt kết quả khám trong phạm vi hệ thống.', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/complete`, {
                    summary: completeSummary,
                    followUpInstruction: completeInstructions
                });
                if (res.success) {
                    showAlert('Đã hoàn thành buổi khám.', 'Thành công', 'success');
                    setModal({ ...modal, view: 'detail' });
                    fetchAppointments();
                }
            } catch (error: any) {
                if (error?.errorCode === 'INVALID_APPOINTMENT_STATUS') showAlert('Trạng thái lịch hẹn không còn hợp lệ (đã được xử lý trước đó).', 'Lỗi', 'error');
                else showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
                fetchAppointments();
            } finally {
                setActionLoading(false);
            }
        });
    };

    const handleNoShow = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!modal.apt) return;
        
        showConfirm('Xác nhận đánh dấu bệnh nhân vắng mặt? Hành động này sẽ khóa lịch khám và không thể hoàn tác.', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/noshow`, {
                    reason: noShowReason
                });
                if (res.success) {
                    showAlert('Đã đánh dấu vắng mặt.', 'Thành công', 'success');
                    setModal({ isOpen: false, apt: null, view: 'detail' });
                    fetchAppointments();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            } finally {
                setActionLoading(false);
            }
        });
    };

    const handleRevisit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!modal.apt) return;
        
        showConfirm('Gửi đề xuất tái khám đến bệnh nhân?', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/revisit-requests`, {
                    suggestedDate: revisitDate,
                    note: revisitNote
                });
                if (res.success) {
                    showAlert('Đề xuất tái khám đã gửi đến bệnh nhân.', 'Thành công', 'success');
                    setModal({ isOpen: false, apt: null, view: 'detail' });
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            } finally {
                setActionLoading(false);
            }
        });
    };

    const formatDate = (dateString: string) => {
        try { return new Intl.DateTimeFormat('vi-VN').format(new Date(dateString)); } 
        catch { return dateString; }
    };

    const translateStatus = (status: string) => {
        switch (status) {
            case 'Pending': return 'Chờ xác nhận';
            case 'Confirmed': return 'Đã xác nhận (Chờ khám)';
            case 'Completed': return 'Đã khám xong';
            case 'Cancelled': return 'Đã hủy';
            case 'NoShow': return 'Vắng mặt';
            default: return status;
        }
    };

    const getStatusBadge = (status: string) => {
        switch(status) {
            case 'Pending': return <span className="badge badge-warning">{translateStatus(status)}</span>;
            case 'Confirmed': return <span className="badge badge-info">{translateStatus(status)}</span>;
            case 'Completed': return <span className="badge badge-success">{translateStatus(status)}</span>;
            case 'Cancelled': return <span className="badge badge-danger">{translateStatus(status)}</span>;
            case 'NoShow': return <span className="badge badge-default">{translateStatus(status)}</span>;
            default: return <span className="badge badge-default">{status}</span>;
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <CalendarDays size={24} /> Lịch khám của tôi
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
                                placeholder="Tìm theo mã lịch..." 
                                style={{ paddingLeft: '40px' }}
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </div>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={dateFilter} onChange={e => { setDateFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả ngày</option>
                            <option value="today">Hôm nay</option>
                        </select>
                    </div>
                    <div style={{ width: '200px' }}>
                        <select className="form-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPage(1); }}>
                            <option value="">Tất cả trạng thái</option>
                            <option value="Confirmed">Đã xác nhận (Chờ khám)</option>
                            <option value="Completed">Đã khám xong</option>
                            <option value="NoShow">Vắng mặt</option>
                        </select>
                    </div>
                    <button type="submit" className="btn-secondary">Tìm kiếm</button>
                </form>
            </div>

            <div className="card table-responsive" style={{ padding: 0 }}>
                {loading ? (
                    <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải dữ liệu...</div>
                ) : (
                    <table className="table">
                        <thead>
                            <tr>
                                <th>Giờ khám</th>
                                <th>Bệnh nhân</th>
                                <th>Lý do khám</th>
                                <th>Trạng thái</th>
                                <th style={{ textAlign: 'right' }}>Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            {appointments.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                        Không tìm thấy lịch khám phù hợp.
                                    </td>
                                </tr>
                            ) : appointments.map(apt => (
                                <tr key={apt.id} style={{ borderBottom: '1px solid var(--c-border)' }}>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 600, display: 'flex', alignItems: 'center', gap: '4px', color: 'var(--c-navy-dark)' }}>
                                            <Clock size={14}/> {apt.startTime.substring(0,5)}
                                        </div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                                            {formatDate(apt.appointmentDate)}
                                        </div>
                                        <div style={{ fontSize: '0.75rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                                            #{apt.appointmentCode}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontWeight: 500 }}>{apt.patientName}</div>
                                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>{apt.patientGender} | {apt.patientDob ? formatDate(apt.patientDob) : 'N/A'}</div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-text)', maxWidth: '250px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            {apt.reason || <span style={{ color: 'var(--c-muted)' }}>Không có ghi chú</span>}
                                        </div>
                                    </td>
                                    <td style={{ padding: '16px' }}>
                                        {getStatusBadge(apt.status)}
                                    </td>
                                    <td style={{ padding: '16px', textAlign: 'right' }}>
                                        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', flexWrap: 'wrap' }}>
                                            <button className="btn-secondary" style={{ padding: '4px 10px', fontSize: '0.85rem' }} onClick={() => openModal(apt, 'detail')}>
                                                <Eye size={14} style={{ marginRight: '4px' }}/> Chi tiết
                                            </button>
                                            {apt.status === 'Confirmed' && (
                                                <button className="btn-primary" style={{ padding: '4px 10px', fontSize: '0.85rem' }} onClick={() => openModal(apt, 'complete')}>
                                                    <CheckCircle size={14} style={{ marginRight: '4px' }}/> Khám
                                                </button>
                                            )}
                                            {apt.status === 'Completed' && (
                                                <button className="btn-secondary" style={{ padding: '4px 10px', fontSize: '0.85rem', color: 'var(--c-navy)' }} onClick={() => openModal(apt, 'revisit')}>
                                                    <PlusCircle size={14} style={{ marginRight: '4px' }}/> Tái khám
                                                </button>
                                            )}
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}
            </div>

            <div style={{ marginTop: '16px', color: 'var(--c-muted)', fontSize: '0.9rem' }}>
                Tổng cộng: {totalItems} lịch khám {dateFilter === 'today' ? 'trong ngày hôm nay' : ''}
            </div>

            {/* Modals */}
            {modal.isOpen && modal.apt && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: '12px', width: '100%', maxWidth: '600px', maxHeight: '90vh', overflowY: 'auto' }}>
                        
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>
                                {modal.view === 'detail' && 'Chi tiết lịch khám'}
                                {modal.view === 'complete' && 'Tóm tắt kết quả khám'}
                                {modal.view === 'noshow' && 'Xác nhận vắng mặt'}
                                {modal.view === 'revisit' && 'Đề xuất tái khám'}
                            </h3>
                            <button onClick={() => setModal({ isOpen: false, apt: null, view: 'detail' })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>

                        {/* Common Patient Info Header */}
                        <div style={{ background: 'var(--c-bg)', padding: '16px', borderRadius: '8px', marginBottom: '20px', display: 'flex', gap: '16px', alignItems: 'center' }}>
                            <div style={{ width: '48px', height: '48px', borderRadius: '50%', background: 'var(--c-navy)', color: 'white', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 'bold', fontSize: '1.2rem' }}>
                                {modal.apt.patientName.charAt(0)}
                            </div>
                            <div>
                                <div style={{ fontWeight: 600, fontSize: '1.1rem' }}>{modal.apt.patientName}</div>
                                <div style={{ fontSize: '0.9rem', color: 'var(--c-muted)' }}>
                                    {modal.apt.patientGender} | Sinh: {modal.apt.patientDob ? formatDate(modal.apt.patientDob) : 'N/A'} | Lịch hẹn: {formatDate(modal.apt.appointmentDate)} ({modal.apt.startTime.substring(0,5)})
                                </div>
                            </div>
                        </div>

                        {modal.view === 'detail' && (
                            <>
                                <div style={{ marginBottom: '20px' }}>
                                    <div style={{ fontWeight: 500, marginBottom: '8px' }}>Triệu chứng / Lý do khám:</div>
                                    <div style={{ background: 'var(--c-bg)', padding: '12px', borderRadius: '6px', fontSize: '0.95rem' }}>
                                        {modal.apt.reason || <span style={{ color: 'var(--c-muted)' }}>Không có</span>}
                                    </div>
                                </div>
                                <div style={{ marginBottom: '20px' }}>
                                    <div style={{ fontWeight: 500, marginBottom: '8px', display: 'flex', justifyContent: 'space-between' }}>
                                        <span>Trạng thái hiện tại</span>
                                        {getStatusBadge(modal.apt.status)}
                                    </div>
                                    {history.length > 0 && (
                                        <div style={{ borderLeft: '2px solid var(--c-border)', marginLeft: '8px', paddingLeft: '16px', display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' }}>
                                            {history.map(h => (
                                                <div key={h.id} style={{ position: 'relative' }}>
                                                    <div style={{ position: 'absolute', left: '-21px', top: '2px', width: '10px', height: '10px', borderRadius: '50%', background: 'var(--c-teal)' }}></div>
                                                    <div style={{ fontWeight: 500, fontSize: '0.9rem' }}>{h.action}</div>
                                                    {h.note && <div style={{ fontSize: '0.85rem', color: 'var(--c-text)', marginTop: '4px' }}>{h.note}</div>}
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                                {modal.apt.status === 'Confirmed' && (
                                    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '24px' }}>
                                        <button className="btn-secondary" style={{ color: 'var(--c-danger)', borderColor: 'var(--c-border)' }} onClick={() => setModal({ ...modal, view: 'noshow' })}>
                                            <XCircle size={16} style={{ marginRight: '4px' }}/> Đánh dấu vắng mặt
                                        </button>
                                        <button className="btn-primary" onClick={() => setModal({ ...modal, view: 'complete' })}>
                                            <CheckCircle size={16} style={{ marginRight: '4px' }}/> Bắt đầu khám
                                        </button>
                                    </div>
                                )}
                            </>
                        )}

                        {modal.view === 'complete' && (
                            <form onSubmit={handleComplete} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                <div style={{ padding: '12px', background: 'var(--c-info-bg)', color: 'var(--c-info)', borderRadius: '6px', fontSize: '0.9rem' }}>
                                    Thông tin này chỉ là tóm tắt kết quả khám trong phạm vi hệ thống để bệnh nhân theo dõi.
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Tóm tắt kết quả khám (*)</label>
                                    <textarea 
                                        className="form-textarea" 
                                        rows={4} 
                                        required 
                                        value={completeSummary} 
                                        onChange={e => setCompleteSummary(e.target.value)}
                                        placeholder="Ghi nhận triệu chứng, chẩn đoán sơ bộ..."
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Hướng dẫn theo dõi (Tùy chọn)</label>
                                    <textarea 
                                        className="form-textarea" 
                                        rows={2} 
                                        value={completeInstructions} 
                                        onChange={e => setCompleteInstructions(e.target.value)}
                                        placeholder="Nhắc nhở dùng thuốc, kiêng cữ..."
                                    />
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '16px' }}>
                                    <button type="button" className="btn-secondary" onClick={() => setModal({ ...modal, view: 'detail' })}>Quay lại</button>
                                    <button type="submit" className="btn-primary" disabled={actionLoading}>
                                        {actionLoading ? 'Đang lưu...' : 'Hoàn thành khám'}
                                    </button>
                                </div>
                            </form>
                        )}

                        {modal.view === 'noshow' && (
                            <form onSubmit={handleNoShow} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                <div style={{ padding: '12px', background: 'var(--c-warning-bg)', color: 'var(--c-warning)', borderRadius: '6px', fontSize: '0.9rem' }}>
                                    Hành động này sẽ hủy buổi khám vì bệnh nhân không đến. Vui lòng xác nhận chắc chắn vì không thể hoàn tác.
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Ghi chú / Lý do (Tùy chọn)</label>
                                    <textarea 
                                        className="form-textarea" 
                                        rows={2} 
                                        value={noShowReason} 
                                        onChange={e => setNoShowReason(e.target.value)}
                                        placeholder="Ví dụ: Đã gọi điện 3 lần không bắt máy..."
                                    />
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '16px' }}>
                                    <button type="button" className="btn-secondary" onClick={() => setModal({ ...modal, view: 'detail' })}>Hủy</button>
                                    <button type="submit" className="btn-danger" disabled={actionLoading}>
                                        {actionLoading ? 'Đang xử lý...' : 'Xác nhận vắng mặt'}
                                    </button>
                                </div>
                            </form>
                        )}

                        {modal.view === 'revisit' && (
                            <form onSubmit={handleRevisit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Ngày hẹn tái khám đề nghị (*)</label>
                                    <input 
                                        type="date" 
                                        className="form-input" 
                                        required 
                                        value={revisitDate} 
                                        onChange={e => setRevisitDate(e.target.value)}
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Tin nhắn / Lý do tái khám (Tùy chọn)</label>
                                    <textarea 
                                        className="form-textarea" 
                                        rows={3} 
                                        value={revisitNote} 
                                        onChange={e => setRevisitNote(e.target.value)}
                                        placeholder="Ví dụ: Tái khám sau khi uống hết thuốc..."
                                    />
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '16px' }}>
                                    <button type="button" className="btn-secondary" onClick={() => setModal({ ...modal, view: 'detail' })}>Hủy</button>
                                    <button type="submit" className="btn-primary" disabled={actionLoading}>
                                        <Send size={16} style={{ marginRight: '6px' }}/> Gửi đề xuất
                                    </button>
                                </div>
                            </form>
                        )}

                    </div>
                </div>
            )}
        </div>
    );
};
