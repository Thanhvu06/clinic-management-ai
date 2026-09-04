import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, CalendarDays, Eye, CheckCircle, XCircle, Clock, PlusCircle, RefreshCw, X, Send, Pill, Trash2, Plus } from 'lucide-react';
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

interface ActiveMedicine {
    id: number;
    code: string;
    name: string;
    unit: string;
    stockQuantity: number;
}

interface PrescriptionItemForm {
    medicineId: number;
    quantity: number;
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
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

    // Prescription Form
    const [activeMedicines, setActiveMedicines] = useState<ActiveMedicine[]>([]);
    const [prescriptionItems, setPrescriptionItems] = useState<PrescriptionItemForm[]>([]);
    const [prescriptionNotes, setPrescriptionNotes] = useState('');
    const [currentPrescription, setCurrentPrescription] = useState<any>(null);
    
    // No Show Form
    const [noShowReason, setNoShowReason] = useState('');

    // Revisit Form
    const [revisitDate, setRevisitDate] = useState('');
    const [revisitNote, setRevisitNote] = useState('');

    useEffect(() => {
        axiosClient.get<any, ApiResponse<ActiveMedicine[]>>('/medicines/active')
            .then(res => {
                if (res.success && res.data) setActiveMedicines(res.data);
            })
            .catch(() => {});
    }, []);

    const fetchAppointments = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments?${params.toString()}`);
            if (res.success && res.data) {
                let items = res.data.items as DoctorAppointment[];
                if (dateFilter === 'today') {
                    const todayStr = new Date().toISOString().split('T')[0];
                    items = items.filter(i => i.appointmentDate.startsWith(todayStr));
                }
                
                items.sort((a, b) => {
                    const dateDiff = new Date(a.appointmentDate).getTime() - new Date(b.appointmentDate).getTime();
                    if (dateDiff !== 0) return dateDiff;
                    return a.startTime.localeCompare(b.startTime);
                });

                setAppointments(items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
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
        setPrescriptionItems([]);
        setPrescriptionNotes('');
        setCurrentPrescription(null);

        try {
            const presRes = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${apt.id}/prescription`);
            if (presRes.success && presRes.data) {
                setCurrentPrescription(presRes.data);
                if (view === 'complete' && presRes.data.items) {
                    setPrescriptionNotes(presRes.data.notes || '');
                    setPrescriptionItems(presRes.data.items.map((i: any) => ({
                        medicineId: i.medicineId,
                        quantity: i.quantity,
                        dosage: i.dosage,
                        frequency: i.frequency,
                        durationDays: i.durationDays,
                        instructions: i.instructions || ''
                    })));
                }
            }
        } catch {
            setCurrentPrescription(null);
        }

        if (view === 'detail') {
            try {
                const res = await axiosClient.get<any, ApiResponse<any>>(`/doctor/appointments/${apt.id}/history`);
                if (res.success && res.data) {
                    setHistory(res.data);
                }
            } catch {
                setHistory([]);
            }
        }
    };

    const handleAddMedicineRow = () => {
        if (activeMedicines.length === 0) return;
        setPrescriptionItems(prev => [
            ...prev,
            {
                medicineId: activeMedicines[0].id,
                quantity: 10,
                dosage: '1 viên',
                frequency: '2 lần / ngày sau ăn',
                durationDays: 5,
                instructions: 'Uống sau bữa ăn'
            }
        ]);
    };

    const handleRemoveMedicineRow = (index: number) => {
        setPrescriptionItems(prev => prev.filter((_, i) => i !== index));
    };

    const handleUpdateMedicineRow = (index: number, field: keyof PrescriptionItemForm, value: any) => {
        setPrescriptionItems(prev => {
            const updated = [...prev];
            updated[index] = { ...updated[index], [field]: value };
            return updated;
        });
    };

    const handleComplete = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!modal.apt) return;
        
        showConfirm('Xác nhận hoàn thành buổi khám và lưu đơn thuốc?', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/complete`, {
                    summary: completeSummary,
                    followUpInstruction: completeInstructions
                });
                if (res.success) {
                    if (prescriptionItems.length > 0) {
                        await axiosClient.post<any, ApiResponse<any>>(`/doctor/appointments/${modal.apt!.id}/prescription`, {
                            notes: prescriptionNotes,
                            items: prescriptionItems
                        });
                    }

                    showAlert('Đã hoàn thành buổi khám và lưu kết quả khám.', 'Thành công', 'success');
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
                                            <CheckCircle size={16} style={{ marginRight: '4px' }}/> Bắt đầu khám & Kê đơn
                                        </button>
                                    </div>
                                )}

                                {currentPrescription && (
                                    <div style={{ marginTop: '20px', borderTop: '1px solid var(--c-border)', paddingTop: '16px' }}>
                                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                            <h4 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--c-navy-dark)' }}>
                                                <Pill size={18} color="var(--c-primary)" /> Đơn thuốc đã kê
                                            </h4>
                                            <span className={currentPrescription.status === 'Dispensed' ? 'badge badge-success' : 'badge badge-warning'}>
                                                {currentPrescription.status === 'Dispensed' ? 'Đã cấp thuốc' : 'Chờ cấp thuốc (Issued)'}
                                            </span>
                                        </div>
                                        {currentPrescription.notes && (
                                            <div style={{ fontSize: '0.9rem', color: 'var(--c-text)', marginBottom: '10px', fontStyle: 'italic' }}>
                                                Ghi chú: {currentPrescription.notes}
                                            </div>
                                        )}
                                        <div style={{ overflowX: 'auto' }}>
                                            <table className="table" style={{ fontSize: '0.875rem' }}>
                                                <thead>
                                                    <tr>
                                                        <th>Tên thuốc</th>
                                                        <th>Số lượng</th>
                                                        <th>Liều dùng</th>
                                                        <th>Tần suất</th>
                                                        <th>Ghi chú</th>
                                                    </tr>
                                                </thead>
                                                <tbody>
                                                    {currentPrescription.items?.map((item: any, idx: number) => (
                                                        <tr key={idx}>
                                                            <td style={{ fontWeight: 600 }}>{item.medicineName} ({item.medicineCode})</td>
                                                            <td>{item.quantity} {item.unit}</td>
                                                            <td>{item.dosage}</td>
                                                            <td>{item.frequency}</td>
                                                            <td>{item.instructions || '-'}</td>
                                                        </tr>
                                                    ))}
                                                </tbody>
                                            </table>
                                        </div>
                                    </div>
                                )}
                            </>
                        )}

                        {modal.view === 'complete' && (
                            <form onSubmit={handleComplete} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                <div style={{ padding: '12px', background: 'var(--c-info-bg)', color: 'var(--c-info)', borderRadius: '6px', fontSize: '0.9rem' }}>
                                    Nhập kết quả khám và kê đơn thuốc cho bệnh nhân. Đơn thuốc sẽ tự động chuyển đến bộ phận Dược sĩ sau khi hoàn thành.
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

                                {/* Prescription Section */}
                                <div style={{ marginTop: '8px', borderTop: '1px dashed var(--c-border)', paddingTop: '16px' }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                        <label style={{ margin: 0, fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px', color: 'var(--c-navy-dark)' }}>
                                            <Pill size={18} color="var(--c-primary)" /> Kê đơn thuốc (Tùy chọn)
                                        </label>
                                        <button
                                            type="button"
                                            className="btn-secondary"
                                            onClick={handleAddMedicineRow}
                                            style={{ fontSize: '0.85rem', padding: '6px 12px', display: 'flex', alignItems: 'center', gap: '4px' }}
                                        >
                                            <Plus size={14} /> Thêm thuốc
                                        </button>
                                    </div>

                                    {prescriptionItems.length > 0 && (
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', marginBottom: '14px' }}>
                                            {prescriptionItems.map((item, idx) => (
                                                <div key={idx} style={{ background: 'var(--c-bg)', padding: '12px', borderRadius: '8px', border: '1px solid var(--c-border)' }}>
                                                    <div style={{ display: 'flex', gap: '8px', alignItems: 'center', marginBottom: '8px', flexWrap: 'wrap' }}>
                                                        <div style={{ flex: '2 1 200px' }}>
                                                            <select
                                                                className="form-select"
                                                                value={item.medicineId}
                                                                onChange={e => handleUpdateMedicineRow(idx, 'medicineId', parseInt(e.target.value, 10))}
                                                            >
                                                                {activeMedicines.map(m => (
                                                                    <option key={m.id} value={m.id}>
                                                                        {m.name} ({m.code}) - Tồn: {m.stockQuantity} {m.unit}
                                                                    </option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                        <div style={{ width: '90px' }}>
                                                            <input
                                                                type="number"
                                                                min={1}
                                                                className="form-input"
                                                                placeholder="SL"
                                                                value={item.quantity}
                                                                onChange={e => handleUpdateMedicineRow(idx, 'quantity', parseInt(e.target.value, 10) || 1)}
                                                            />
                                                        </div>
                                                        <div style={{ flex: '1 1 120px' }}>
                                                            <input
                                                                type="text"
                                                                className="form-input"
                                                                placeholder="Liều dùng (vd: 1 viên)"
                                                                value={item.dosage}
                                                                onChange={e => handleUpdateMedicineRow(idx, 'dosage', e.target.value)}
                                                            />
                                                        </div>
                                                        <button
                                                            type="button"
                                                            onClick={() => handleRemoveMedicineRow(idx)}
                                                            style={{ background: 'none', border: 'none', color: 'var(--c-danger)', cursor: 'pointer', padding: '6px' }}
                                                            title="Xóa thuốc"
                                                        >
                                                            <Trash2 size={16} />
                                                        </button>
                                                    </div>
                                                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                                                        <div style={{ flex: '1 1 180px' }}>
                                                            <input
                                                                type="text"
                                                                className="form-input"
                                                                placeholder="Tần suất (vd: 2 lần/ngày sau ăn)"
                                                                value={item.frequency}
                                                                onChange={e => handleUpdateMedicineRow(idx, 'frequency', e.target.value)}
                                                            />
                                                        </div>
                                                        <div style={{ flex: '2 1 200px' }}>
                                                            <input
                                                                type="text"
                                                                className="form-input"
                                                                placeholder="Hướng dẫn thêm..."
                                                                value={item.instructions || ''}
                                                                onChange={e => handleUpdateMedicineRow(idx, 'instructions', e.target.value)}
                                                            />
                                                        </div>
                                                    </div>
                                                </div>
                                            ))}

                                            <div>
                                                <input
                                                    type="text"
                                                    className="form-input"
                                                    placeholder="Ghi chú đơn thuốc (vd: Uống nhiều nước, kiêng rượu bia...)"
                                                    value={prescriptionNotes}
                                                    onChange={e => setPrescriptionNotes(e.target.value)}
                                                />
                                            </div>
                                        </div>
                                    )}
                                </div>

                                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px', marginTop: '16px' }}>
                                    <button type="button" className="btn-secondary" onClick={() => setModal({ ...modal, view: 'detail' })}>Quay lại</button>
                                    <button type="submit" className="btn-primary" disabled={actionLoading}>
                                        {actionLoading ? 'Đang lưu...' : 'Hoàn thành khám & Lưu đơn thuốc'}
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
