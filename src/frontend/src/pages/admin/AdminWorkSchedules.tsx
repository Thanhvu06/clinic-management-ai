import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { CalendarDays, Plus, Clock, Edit, CheckCircle, XCircle, PlayCircle, X } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';

interface WorkSchedule {
    id: number;
    doctorId: number;
    workDate: string;
    startTime: string;
    endTime: string;
    isActive: boolean;
    slots?: any[]; // The backend doesn't return slots in WorkScheduleDto currently.
}

export const AdminWorkSchedules: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [doctors, setDoctors] = useState<any[]>([]);
    const [selectedDoctorId, setSelectedDoctorId] = useState<number | ''>('');
    const [schedules, setSchedules] = useState<WorkSchedule[]>([]);
    const [loading, setLoading] = useState(false);

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, isEdit: boolean, data: Partial<WorkSchedule> }>({ isOpen: false, isEdit: false, data: {} });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');
    const [actionLoading, setActionLoading] = useState<number | null>(null);

    useEffect(() => {
        const fetchDoctors = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<any>>('/admin/doctors?isActive=true&pageSize=100');
                if (res.success) setDoctors(res.data.items);
            } catch (e) {}
        };
        fetchDoctors();
    }, []);

    useEffect(() => {
        if (!selectedDoctorId) {
            setSchedules([]);
            return;
        }
        fetchSchedules();
    }, [selectedDoctorId]);

    const fetchSchedules = async () => {
        setLoading(true);
        try {
            const res = await axiosClient.get<any, ApiResponse<WorkSchedule[]>>(`/admin/doctors/${selectedDoctorId}/work-schedules`);
            if (res.success && res.data) {
                // Sort by date and time
                const sorted = res.data.sort((a, b) => {
                    const dateDiff = new Date(a.workDate).getTime() - new Date(b.workDate).getTime();
                    if (dateDiff !== 0) return dateDiff;
                    return a.startTime.localeCompare(b.startTime);
                });
                setSchedules(sorted);
            }
        } catch (error) {
            // Error
        } finally {
            setLoading(false);
        }
    };

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');

        if (modal.data.startTime && modal.data.endTime && modal.data.startTime >= modal.data.endTime) {
            setFormError('Giờ bắt đầu phải trước giờ kết thúc.');
            return;
        }

        setFormLoading(true);
        try {
            if (modal.isEdit) {
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/work-schedules/${modal.data.id}`, {
                    workDate: modal.data.workDate,
                    startTime: modal.data.startTime,
                    endTime: modal.data.endTime
                });
                if (res.success) {
                    showAlert('Cập nhật lịch làm việc thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchSchedules();
                }
            } else {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/admin/doctors/${selectedDoctorId}/work-schedules`, {
                    workDate: modal.data.workDate,
                    startTime: modal.data.startTime,
                    endTime: modal.data.endTime
                });
                if (res.success) {
                    showAlert('Tạo lịch làm việc thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchSchedules();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleToggleStatus = async (id: number, currentStatus: boolean) => {
        showConfirm(`Bạn có chắc chắn muốn ${currentStatus ? 'khóa' : 'mở khóa'} lịch làm việc này?`, async () => {
            try {
                const res = await axiosClient.patch<any, ApiResponse<any>>(`/admin/work-schedules/${id}/status`, {
                    isActive: !currentStatus
                });
                if (res.success) {
                    fetchSchedules();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
            }
        });
    };

    const handleGenerateSlots = async (id: number) => {
        showConfirm('Sinh ca khám (slots) cho lịch này? Lịch cũ có thể bị ảnh hưởng nếu đã được đặt.', async () => {
            setActionLoading(id);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/admin/work-schedules/${id}/generate-slots`, {});
                if (res.success) {
                    showAlert('Sinh slot thành công.', 'Thành công', 'success');
                    fetchSchedules();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Có lỗi xảy ra khi sinh slot.', 'Lỗi', 'error');
            } finally {
                setActionLoading(null);
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

    const getDayOfWeek = (dateString: string) => {
        try {
            const days = ['Chủ nhật', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'];
            return days[new Date(dateString).getDay()];
        } catch {
            return '';
        }
    };

    return (
        <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <h2 style={{ color: 'var(--c-navy-dark)', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <CalendarDays size={24} /> Quản lý lịch làm việc
                </h2>
                {selectedDoctorId && (
                    <button className="btn-primary" onClick={() => setModal({ isOpen: true, isEdit: false, data: { workDate: new Date().toISOString().split('T')[0] } })}>
                        <Plus size={18} /> Thêm lịch làm việc
                    </button>
                )}
            </div>

            <div className="card" style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', gap: '16px', alignItems: 'center' }}>
                    <label style={{ fontWeight: 500 }}>Chọn Bác sĩ:</label>
                    <select className="form-select" style={{ maxWidth: '300px' }} value={selectedDoctorId} onChange={e => setSelectedDoctorId(Number(e.target.value))}>
                        <option value="">-- Vui lòng chọn bác sĩ --</option>
                        {doctors.map(d => <option key={d.id} value={d.id}>{d.fullName}</option>)}
                    </select>
                </div>
            </div>

            {selectedDoctorId ? (
                <div className="card table-responsive" style={{ padding: 0 }}>
                    {loading ? (
                        <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải lịch làm việc...</div>
                    ) : (
                        <table className="table" style={{ width: '100%' }}>
                            <thead>
                                <tr>
                                    <th>Ngày</th>
                                    <th>Ca làm việc</th>
                                    <th>Trạng thái</th>
                                    <th style={{ textAlign: 'right' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                                {schedules.length === 0 ? (
                                    <tr>
                                        <td colSpan={4} style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)' }}>
                                            Bác sĩ chưa có lịch làm việc nào.
                                        </td>
                                    </tr>
                                ) : schedules.map(s => (
                                    <tr key={s.id}>
                                        <td>
                                            <div style={{ fontWeight: 600 }}>{formatDate(s.workDate)}</div>
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>{getDayOfWeek(s.workDate)}</div>
                                        </td>
                                        <td>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                <Clock size={16} color="var(--c-teal)"/> 
                                                <span>{s.startTime.substring(0, 5)} - {s.endTime.substring(0, 5)}</span>
                                            </div>
                                            {/* Note: slots array is not provided by backend WorkScheduleDto */}
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                                                Lưu ý: Bạn cần sinh slot để bệnh nhân có thể đặt lịch.
                                            </div>
                                        </td>
                                        <td>
                                            {s.isActive ? 
                                                <span className="badge badge-success">Đang hoạt động</span> : 
                                                <span className="badge badge-danger">Đã khóa</span>
                                            }
                                        </td>
                                        <td style={{ textAlign: 'right' }}>
                                            <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
                                                <button 
                                                    className="btn-primary" 
                                                    style={{ padding: '6px 12px', fontSize: '0.85rem', display: 'flex', alignItems: 'center', gap: '4px' }} 
                                                    onClick={() => handleGenerateSlots(s.id)}
                                                    disabled={actionLoading === s.id}
                                                    title="Tự động chia thời gian thành các khung giờ khám (slots)"
                                                >
                                                    <PlayCircle size={14} /> Sinh Slot
                                                </button>
                                                <button className="btn-secondary" style={{ padding: '6px 12px', fontSize: '0.85rem' }} onClick={() => setModal({ isOpen: true, isEdit: true, data: { ...s } })}>
                                                    <Edit size={14} /> Sửa
                                                </button>
                                                <button 
                                                    className={s.isActive ? "btn-danger" : "btn-secondary"} 
                                                    style={{ padding: '6px 12px', fontSize: '0.85rem' }} 
                                                    onClick={() => handleToggleStatus(s.id, s.isActive)}
                                                >
                                                    {s.isActive ? <XCircle size={14} /> : <CheckCircle size={14} />} {s.isActive ? 'Khóa' : 'Mở khóa'}
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            ) : (
                <div style={{ padding: '40px', textAlign: 'center', color: 'var(--c-muted)', background: 'white', borderRadius: 'var(--radius-lg)', border: '1px dashed var(--c-border)' }}>
                    <CalendarDays size={48} style={{ marginBottom: '16px', opacity: 0.5 }} />
                    <h3>Chưa chọn bác sĩ</h3>
                    <p>Vui lòng chọn một bác sĩ từ danh sách để xem lịch làm việc.</p>
                </div>
            )}

            {/* Form Modal */}
            {modal.isOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '20px' }}>
                    <div style={{ background: 'white', padding: '24px', borderRadius: 'var(--radius-lg)', width: '100%', maxWidth: '400px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                            <h3 style={{ margin: 0 }}>{modal.isEdit ? 'Cập nhật lịch làm việc' : 'Thêm lịch làm việc'}</h3>
                            <button onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })} style={{ background: 'none', border: 'none', cursor: 'pointer' }}><X size={24} color="var(--c-muted)"/></button>
                        </div>
                        
                        {formError && <div style={{ color: 'var(--c-danger)', background: 'var(--c-danger-bg)', padding: '10px', borderRadius: '6px', marginBottom: '16px', fontSize: '0.9rem' }}>{formError}</div>}

                        <form onSubmit={handleFormSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                            <div className="form-group">
                                <label className="form-label">Ngày làm việc (*)</label>
                                <input 
                                    type="date" 
                                    className="form-input" 
                                    required 
                                    value={modal.data.workDate || ''} 
                                    onChange={e => setModal({ ...modal, data: { ...modal.data, workDate: e.target.value } })} 
                                />
                            </div>
                            <div style={{ display: 'flex', gap: '16px' }}>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label className="form-label">Từ giờ (*)</label>
                                    <input 
                                        type="time" 
                                        className="form-input" 
                                        required 
                                        value={modal.data.startTime || ''} 
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, startTime: e.target.value } })} 
                                    />
                                </div>
                                <div className="form-group" style={{ flex: 1 }}>
                                    <label className="form-label">Đến giờ (*)</label>
                                    <input 
                                        type="time" 
                                        className="form-input" 
                                        required 
                                        value={modal.data.endTime || ''} 
                                        onChange={e => setModal({ ...modal, data: { ...modal.data, endTime: e.target.value } })} 
                                    />
                                </div>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '10px' }}>
                                <button type="button" className="btn-secondary" onClick={() => setModal({ isOpen: false, isEdit: false, data: {} })}>Hủy</button>
                                <button type="submit" className="btn-primary" disabled={formLoading}>
                                    {formLoading ? 'Đang lưu...' : 'Xác nhận lưu'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
