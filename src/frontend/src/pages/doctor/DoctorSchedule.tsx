import React, { useState, useEffect, useCallback } from 'react';
import { 
    Calendar, ChevronLeft, ChevronRight, Clock, 
    CalendarPlus, AlertTriangle, RefreshCw, X, ShieldAlert 
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorScheduleDayDto, LeavePreviewDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

export const DoctorSchedule: React.FC = () => {
    const { showAlert, showToast } = useDialog();

    // Compute week start (Monday) and week end (Sunday)
    const getMonday = (d: Date) => {
        const date = new Date(d);
        const day = date.getDay();
        const diff = date.getDate() - day + (day === 0 ? -6 : 1);
        return new Date(date.setDate(diff));
    };

    const [currentMonday, setCurrentMonday] = useState<Date>(() => getMonday(new Date()));
    const [scheduleDays, setScheduleDays] = useState<DoctorScheduleDayDto[]>([]);
    const [loading, setLoading] = useState(true);

    // Leave request modal state
    const [isLeaveModalOpen, setIsLeaveModalOpen] = useState(false);
    const [leaveStartDate, setLeaveStartDate] = useState('');
    const [leaveEndDate, setLeaveEndDate] = useState('');
    const [leaveReason, setLeaveReason] = useState('');
    const [leavePreview, setLeavePreview] = useState<LeavePreviewDto | null>(null);
    const [previewLoading, setPreviewLoading] = useState(false);
    const [submittingLeave, setSubmittingLeave] = useState(false);

    const startDateStr = currentMonday.toISOString().split('T')[0];
    const sunday = new Date(currentMonday);
    sunday.setDate(sunday.getDate() + 6);
    const endDateStr = sunday.toISOString().split('T')[0];

    const loadSchedule = useCallback(async () => {
        setLoading(true);
        try {
            const res = await doctorApi.getSchedule(startDateStr, endDateStr);
            if (res.success && res.data) {
                setScheduleDays(res.data);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tải lịch trực của bác sĩ.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    }, [startDateStr, endDateStr, showAlert]);

    useEffect(() => {
        loadSchedule();
    }, [loadSchedule]);

    const handlePrevWeek = () => {
        const d = new Date(currentMonday);
        d.setDate(d.getDate() - 7);
        setCurrentMonday(d);
    };

    const handleNextWeek = () => {
        const d = new Date(currentMonday);
        d.setDate(d.getDate() + 7);
        setCurrentMonday(d);
    };

    const handleTodayWeek = () => {
        setCurrentMonday(getMonday(new Date()));
    };

    // When leave dates change, check preview
    useEffect(() => {
        if (!leaveStartDate || !leaveEndDate) {
            setLeavePreview(null);
            return;
        }

        if (leaveStartDate > leaveEndDate) {
            setLeavePreview(null);
            return;
        }

        const fetchPreview = async () => {
            setPreviewLoading(true);
            try {
                const res = await doctorApi.previewLeave(leaveStartDate, leaveEndDate);
                if (res.success && res.data) {
                    setLeavePreview(res.data);
                }
            } catch (err) {
                setLeavePreview(null);
            } finally {
                setPreviewLoading(false);
            }
        };

        const timer = setTimeout(fetchPreview, 400);
        return () => clearTimeout(timer);
    }, [leaveStartDate, leaveEndDate]);

    const handleSubmitLeave = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!leaveStartDate || !leaveEndDate || !leaveReason.trim()) {
            showAlert('Vui lòng điền đầy đủ ngày bắt đầu, ngày kết thúc và lý do nghỉ phép.', 'Thiếu thông tin', 'warning');
            return;
        }

        setSubmittingLeave(true);
        try {
            const res = await doctorApi.createLeaveRequest({
                startDate: leaveStartDate,
                endDate: leaveEndDate,
                reason: leaveReason.trim()
            });

            if (res.success) {
                showToast('Đã gửi đơn xin nghỉ phép thành công! Ban quản lý sẽ xem xét.', 'success');
                setIsLeaveModalOpen(false);
                setLeaveStartDate('');
                setLeaveEndDate('');
                setLeaveReason('');
                setLeavePreview(null);
                await loadSchedule();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể gửi đơn xin nghỉ phép.', 'Lỗi', 'error');
        } finally {
            setSubmittingLeave(false);
        }
    };

    return (
        <div style={{ padding: '8px 0' }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px', flexWrap: 'wrap', gap: '12px' }}>
                <div>
                    <h1 style={{ fontSize: '1.5rem', fontWeight: 700, color: '#0f172a', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Calendar size={24} style={{ color: '#0284c7' }} />
                        <span>Lịch trực & Ca khám tuần</span>
                    </h1>
                    <p style={{ color: '#64748b', margin: '4px 0 0 0', fontSize: '0.9rem' }}>
                        Xem lịch trực được phân bổ, danh sách các khung giờ và thông tin bệnh nhân đã đặt.
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' }}>
                    <button
                        className="btn-primary"
                        onClick={() => setIsLeaveModalOpen(true)}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', backgroundColor: '#e11d48', cursor: 'pointer' }}
                    >
                        <CalendarPlus size={16} />
                        <span>Đăng ký nghỉ phép</span>
                    </button>
                    <div style={{ display: 'flex', alignItems: 'center', backgroundColor: 'white', border: '1px solid #cbd5e1', borderRadius: '6px', overflow: 'hidden' }}>
                        <button onClick={handlePrevWeek} style={{ border: 'none', background: 'none', padding: '8px 10px', cursor: 'pointer', color: '#475569' }}>
                            <ChevronLeft size={18} />
                        </button>
                        <button onClick={handleTodayWeek} style={{ border: 'none', background: 'none', padding: '8px 12px', cursor: 'pointer', fontSize: '0.85rem', fontWeight: 600, borderLeft: '1px solid #e2e8f0', borderRight: '1px solid #e2e8f0' }}>
                            Hôm nay
                        </button>
                        <button onClick={handleNextWeek} style={{ border: 'none', background: 'none', padding: '8px 10px', cursor: 'pointer', color: '#475569' }}>
                            <ChevronRight size={18} />
                        </button>
                    </div>
                </div>
            </div>

            {/* Week Date Display */}
            <div className="card" style={{ padding: '12px 16px', marginBottom: '20px', backgroundColor: '#f8fafc', borderRadius: '8px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontWeight: 600, color: '#1e293b' }}>
                    Tuần từ {currentMonday.toLocaleDateString('vi-VN')} đến {sunday.toLocaleDateString('vi-VN')}
                </span>
                <button 
                    onClick={loadSchedule} 
                    disabled={loading}
                    style={{ background: 'none', border: 'none', color: '#0284c7', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '4px', fontSize: '0.85rem', fontWeight: 600 }}
                >
                    <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
                    <span>Làm mới</span>
                </button>
            </div>

            {/* Schedule Days View */}
            {loading ? (
                <div style={{ textAlign: 'center', padding: '48px 0', color: '#64748b' }}>
                    <RefreshCw size={28} className="animate-spin" style={{ margin: '0 auto 10px auto' }} />
                    <p>Đang tải lịch trực...</p>
                </div>
            ) : scheduleDays.length === 0 ? (
                <div className="card" style={{ textAlign: 'center', padding: '48px 0', color: '#64748b', borderRadius: '8px' }}>
                    <Calendar size={40} style={{ margin: '0 auto 10px auto', color: '#94a3b8' }} />
                    <p style={{ fontWeight: 600 }}>Không có dữ liệu lịch làm việc cho tuần này.</p>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    {scheduleDays.map(day => (
                        <div key={day.date} className="card" style={{ padding: '16px 20px', borderRadius: '8px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid #e2e8f0', paddingBottom: '10px', marginBottom: '12px' }}>
                                <div>
                                    <span style={{ fontSize: '1.1rem', fontWeight: 700, color: '#0f172a' }}>
                                        {day.dayOfWeekName}
                                    </span>
                                    <span style={{ marginLeft: '8px', color: '#64748b', fontSize: '0.9rem' }}>
                                        ({day.date})
                                    </span>
                                </div>
                                <div>
                                    <span style={{ fontSize: '0.85rem', color: day.shifts.length > 0 ? '#0284c7' : '#94a3b8', fontWeight: 600 }}>
                                        {day.shifts.length > 0 ? `${day.shifts.length} ca trực` : 'Nghỉ trực'}
                                    </span>
                                </div>
                            </div>

                            {day.shifts.length === 0 ? (
                                <div style={{ color: '#94a3b8', fontStyle: 'italic', fontSize: '0.85rem', padding: '6px 0' }}>
                                    Không có ca làm việc nào được phân công.
                                </div>
                            ) : (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
                                    {day.shifts.map(shift => (
                                        <div key={shift.workScheduleId} style={{ backgroundColor: '#f8fafc', padding: '14px', borderRadius: '6px', border: '1px solid #e2e8f0' }}>
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px', flexWrap: 'wrap', gap: '8px' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                    <Clock size={16} style={{ color: '#0284c7' }} />
                                                    <span style={{ fontWeight: 700, color: '#0f172a', fontSize: '0.95rem' }}>
                                                        {shift.shiftName}: {shift.startTime.substring(0, 5)} - {shift.endTime.substring(0, 5)}
                                                    </span>
                                                    <span style={{ backgroundColor: '#e0f2fe', color: '#0369a1', fontSize: '0.75rem', fontWeight: 600, padding: '2px 8px', borderRadius: '4px' }}>
                                                        {shift.room}
                                                    </span>
                                                </div>
                                                <div style={{ fontSize: '0.8rem', color: '#64748b' }}>
                                                    {shift.slots.filter(s => s.isBooked).length}/{shift.slots.length} khung giờ đã được đặt
                                                </div>
                                            </div>

                                            {/* Slots Grid */}
                                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: '8px' }}>
                                                {shift.slots.map(slot => (
                                                    <div 
                                                        key={slot.id} 
                                                        style={{ 
                                                            padding: '8px 10px', 
                                                            borderRadius: '6px', 
                                                            border: slot.isBooked ? '1px solid #bfdbfe' : '1px dashed #cbd5e1',
                                                            backgroundColor: slot.isBooked ? '#eff6ff' : 'white'
                                                        }}
                                                    >
                                                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.8rem', fontWeight: 700, color: slot.isBooked ? '#1e40af' : '#64748b' }}>
                                                            <span>{slot.startTime.substring(0, 5)} - {slot.endTime.substring(0, 5)}</span>
                                                            <span style={{ fontSize: '0.7rem', color: slot.isBooked ? '#15803d' : '#94a3b8' }}>
                                                                {slot.isBooked ? 'Đã đặt' : 'Trống'}
                                                            </span>
                                                        </div>
                                                        {slot.isBooked && (
                                                            <div style={{ marginTop: '4px', fontSize: '0.8rem' }}>
                                                                <div style={{ fontWeight: 600, color: '#0f172a', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                                    {slot.patientName}
                                                                </div>
                                                                <div style={{ color: '#64748b', fontSize: '0.75rem' }}>
                                                                    {slot.patientPhone}
                                                                </div>
                                                            </div>
                                                        )}
                                                    </div>
                                                ))}
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    ))}
                </div>
            )}

            {/* Leave Request Modal with Preview */}
            {isLeaveModalOpen && (
                <div style={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '16px' }}>
                    <div style={{ backgroundColor: 'white', borderRadius: '10px', width: '100%', maxWidth: '540px', padding: '24px', boxShadow: '0 20px 25px -5px rgba(0,0,0,0.1)' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                            <h2 style={{ fontSize: '1.25rem', fontWeight: 700, color: '#0f172a', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <CalendarPlus size={20} style={{ color: '#e11d48' }} />
                                <span>Đăng ký nghỉ phép</span>
                            </h2>
                            <button onClick={() => setIsLeaveModalOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}>
                                <X size={20} />
                            </button>
                        </div>

                        <form onSubmit={handleSubmitLeave}>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '14px' }}>
                                <div>
                                    <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                        Từ ngày *
                                    </label>
                                    <input
                                        type="date"
                                        value={leaveStartDate}
                                        onChange={(e) => setLeaveStartDate(e.target.value)}
                                        required
                                        style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                    />
                                </div>
                                <div>
                                    <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                        Đến ngày *
                                    </label>
                                    <input
                                        type="date"
                                        value={leaveEndDate}
                                        onChange={(e) => setLeaveEndDate(e.target.value)}
                                        required
                                        style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                    />
                                </div>
                            </div>

                            <div style={{ marginBottom: '14px' }}>
                                <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                    Lý do nghỉ phép *
                                </label>
                                <textarea
                                    value={leaveReason}
                                    onChange={(e) => setLeaveReason(e.target.value)}
                                    placeholder="Ghi rõ lý do xin nghỉ để ban quản lý xem xét..."
                                    rows={3}
                                    required
                                    style={{ width: '100%', padding: '8px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.9rem' }}
                                />
                            </div>

                            {/* Leave Preview Impact Banner */}
                            {previewLoading && (
                                <div style={{ fontSize: '0.85rem', color: '#64748b', padding: '10px 0', textAlign: 'center' }}>
                                    <RefreshCw size={14} className="animate-spin" style={{ display: 'inline', marginRight: '6px' }} />
                                    <span>Đang kiểm tra lịch hẹn bị ảnh hưởng...</span>
                                </div>
                            )}

                            {leavePreview && (
                                <div style={{ 
                                    padding: '12px', 
                                    borderRadius: '6px', 
                                    marginBottom: '16px',
                                    backgroundColor: leavePreview.affectedAppointmentCount > 0 ? '#fff1f2' : '#f0fdf4',
                                    border: leavePreview.affectedAppointmentCount > 0 ? '1px solid #fecdd3' : '1px solid #bbf7d0'
                                }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 600, fontSize: '0.9rem', color: leavePreview.affectedAppointmentCount > 0 ? '#be123c' : '#15803d' }}>
                                        {leavePreview.affectedAppointmentCount > 0 ? <AlertTriangle size={18} /> : <ShieldAlert size={18} />}
                                        <span>
                                            {leavePreview.affectedAppointmentCount > 0 
                                                ? `Có ${leavePreview.affectedAppointmentCount} lịch hẹn của bệnh nhân bị ảnh hưởng!` 
                                                : 'Không có lịch hẹn nào của bệnh nhân bị ảnh hưởng.'}
                                        </span>
                                    </div>
                                    {leavePreview.affectedAppointmentCount > 0 && (
                                        <div style={{ marginTop: '8px', maxHeight: '120px', overflowY: 'auto', fontSize: '0.8rem', color: '#881337' }}>
                                            <ul style={{ margin: 0, paddingLeft: '16px' }}>
                                                {leavePreview.affectedAppointments.map(a => (
                                                    <li key={a.id}>
                                                        {a.appointmentDate} ({a.startTime.substring(0, 5)} - {a.endTime.substring(0, 5)}): {a.patientName} ({a.patientPhone})
                                                    </li>
                                                ))}
                                            </ul>
                                            <div style={{ marginTop: '6px', fontStyle: 'italic' }}>
                                                * Nếu được duyệt, bộ phận lễ tân sẽ chủ động liên hệ bệnh nhân để sắp xếp lại ca khám.
                                            </div>
                                        </div>
                                    )}
                                </div>
                            )}

                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '16px' }}>
                                <button
                                    type="button"
                                    onClick={() => setIsLeaveModalOpen(false)}
                                    className="btn-secondary"
                                    style={{ padding: '8px 16px', borderRadius: '6px', cursor: 'pointer' }}
                                >
                                    Hủy
                                </button>
                                <button
                                    type="submit"
                                    disabled={submittingLeave || !leaveStartDate || !leaveEndDate || !leaveReason.trim()}
                                    className="btn-primary"
                                    style={{ padding: '8px 18px', borderRadius: '6px', backgroundColor: '#e11d48', cursor: 'pointer' }}
                                >
                                    {submittingLeave ? 'Đang gửi...' : 'Gửi đơn nghỉ phép'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
};
