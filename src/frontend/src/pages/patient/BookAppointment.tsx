import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import styles from './BookAppointment.module.css';
import type { ApiResponse } from '../../types';
import { Sparkles, CalendarDays, AlertTriangle, CheckCircle2, User, Clock, Stethoscope, FileText, ArrowRight } from 'lucide-react';

interface Specialty {
    id: number;
    name: string;
    description: string;
}

interface Doctor {
    id: number;
    fullName: string;
    specialtyName: string;
}

interface Slot {
    id: number;
    slotDate: string;
    startTime: string;
    endTime: string;
    isBooked: boolean;
}

export const BookAppointment: React.FC = () => {
    const navigate = useNavigate();
    
    const [step, setStep] = useState(1);
    const [symptomDescription, setSymptomDescription] = useState('');
    const [specialtyId, setSpecialtyId] = useState<number | ''>('');
    const [doctorId, setDoctorId] = useState<number | ''>('');
    const [slotDate, setSlotDate] = useState<string>('');
    const [slotId, setSlotId] = useState<number | ''>('');
    const [reason, setReason] = useState('');

    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [slots, setSlots] = useState<Slot[]>([]);
    const [aiSuggestions, setAiSuggestions] = useState<any[]>([]);


    const [loadingSlots, setLoadingSlots] = useState(false);
    const [loadingAi, setLoadingAi] = useState(false);
    const [submitting, setSubmitting] = useState(false);

    const [aiMessage, setAiMessage] = useState('');
    const [appointmentCode, setAppointmentCode] = useState('');
    const [error, setError] = useState('');

    useEffect(() => {
        const fetchSpecialties = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<Specialty[]>>('/specialties');
                if (res.success && res.data) {
                    setSpecialties(res.data);
                }
            } catch (err) {}
        };
        fetchSpecialties();
        
        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        setSlotDate(tomorrow.toISOString().split('T')[0]);
    }, []);

    useEffect(() => {
        if (!specialtyId) {
            setDoctors([]);
            setDoctorId('');
            return;
        }
        const fetchDoctors = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<Doctor[]>>(`/specialties/${specialtyId}/doctors`);
                if (res.success && res.data) {
                    setDoctors(res.data);
                }
            } catch (err) {}
        };
        fetchDoctors();
    }, [specialtyId]);

    useEffect(() => {
        if (!doctorId || !slotDate) {
            setSlots([]);
            setSlotId('');
            return;
        }
        const fetchSlots = async () => {
            setLoadingSlots(true);
            try {
                const res = await axiosClient.get<any, ApiResponse<Slot[]>>(`/doctors/${doctorId}/available-slots?fromDate=${slotDate}&toDate=${slotDate}&specialtyId=${specialtyId}`);
                if (res.success && res.data) {
                    setSlots(res.data);
                }
            } catch (err) {} finally {
                setLoadingSlots(false);
            }
        };
        fetchSlots();
    }, [doctorId, slotDate]);

    const handleAiSuggest = async () => {
        if (!symptomDescription || symptomDescription.length < 10) {
            alert('Vui lòng mô tả chi tiết hơn (ít nhất 10 ký tự).');
            return;
        }
        setLoadingAi(true);
        setAiMessage('');
        setAiSuggestions([]);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/ai/specialty-suggestions', { symptomDescription });
            if (res.success && res.data) {
                if (res.data.outcome === 'SUCCESS') {
                    setAiSuggestions(res.data.suggestions);
                    setAiMessage(res.data.disclaimer);
                } else {
                    setAiMessage('Không thể gợi ý. Vui lòng tự chọn chuyên khoa bên dưới.');
                }
            }
        } catch (err) {
            setAiMessage('Tính năng gợi ý tạm thời không khả dụng.');
        } finally {
            setLoadingAi(false);
        }
    };

    const handleProceedToConfirm = () => {
        if (!specialtyId || !doctorId || !slotId || !reason) {
            setError('Vui lòng điền đầy đủ các thông tin bắt buộc (*).');
            window.scrollTo(0, 0);
            return;
        }
        setError('');
        setStep(2);
        window.scrollTo(0, 0);
    };

    const handleSubmit = async () => {
        setSubmitting(true);
        setError('');
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/appointments', {
                doctorId,
                specialtyId,
                appointmentSlotId: slotId,
                reason
            });
            if (res.success) {
                setAppointmentCode(res.data?.appointmentCode || 'N/A');
                setStep(3);
                window.scrollTo(0, 0);
            }
        } catch (err: any) {
            const errorCode = err?.errorCode;
            if (errorCode === 'SLOT_ALREADY_BOOKED') {
                setError('Khung giờ này vừa được đặt. Vui lòng chọn giờ khác.');
                setSlotId('');
                setStep(1);
            } else if (errorCode === 'PATIENT_TIME_CONFLICT') {
                setError('Bạn đã có lịch khám khác trùng giờ này.');
                setStep(1);
            } else if (errorCode === 'DOCTOR_NOT_AVAILABLE') {
                setError('Bác sĩ không khả dụng vào giờ này.');
                setSlotId('');
                setStep(1);
            } else {
                setError(err?.message || 'Có lỗi xảy ra khi đặt lịch.');
            }
            window.scrollTo(0, 0);
        } finally {
            setSubmitting(false);
        }
    };

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <h2>Đặt lịch khám mới</h2>
                <div className={styles.stepper}>
                    <div className={`${styles.step} ${step >= 1 ? styles.stepActive : ''}`}>
                        <div className={styles.stepNumber}>1</div>
                        <span>Chọn lịch khám</span>
                    </div>
                    <div className={styles.stepLine} />
                    <div className={`${styles.step} ${step >= 2 ? styles.stepActive : ''}`}>
                        <div className={styles.stepNumber}>2</div>
                        <span>Xác nhận thông tin</span>
                    </div>
                    <div className={styles.stepLine} />
                    <div className={`${styles.step} ${step >= 3 ? styles.stepActive : ''}`}>
                        <div className={styles.stepNumber}>3</div>
                        <span>Hoàn tất</span>
                    </div>
                </div>
            </div>

            {error && (
                <div className={styles.alertError}>
                    <AlertTriangle size={24} />
                    <span>{error}</span>
                </div>
            )}

            {step === 1 && (
                <div className={styles.card}>
                    <div className={styles.section}>
                        <h3 className={styles.sectionHeader}><Sparkles size={24} /> Phân tích triệu chứng bằng AI</h3>
                        <div className={styles.warningBox}>
                            <AlertTriangle size={20} style={{ flexShrink: 0 }} />
                            <span>Vui lòng chỉ mô tả tình trạng sức khỏe. Không nhập tên, số điện thoại, email hay thông tin nhận dạng cá nhân.</span>
                        </div>
                        <textarea 
                            className="form-textarea"
                            value={symptomDescription}
                            onChange={(e) => setSymptomDescription(e.target.value)}
                            placeholder="Ví dụ: Tôi bị đau đầu dai dẳng kéo dài 3 ngày nay, kèm theo buồn nôn..."
                            rows={3}
                        />
                        <button className="btn-primary" onClick={handleAiSuggest} disabled={loadingAi} style={{ marginTop: '10px' }}>
                            <Sparkles size={18} /> {loadingAi ? 'Đang phân tích...' : 'Gợi ý chuyên khoa'}
                        </button>
                        
                        {aiMessage && (
                            <div className={styles.aiSuggestBox}>
                                <div className={styles.aiBadge}><Sparkles size={12}/> Tham khảo AI</div>
                                <p style={{ fontSize: '0.9rem' }}>{aiMessage}</p>
                                
                                {aiSuggestions.length > 0 && (
                                    <div className={styles.aiSuggestionsGrid}>
                                        {aiSuggestions.map(s => (
                                            <button key={s.specialtyId} className={styles.aiCard} onClick={() => {
                                                setSpecialtyId(s.specialtyId);
                                                setReason(symptomDescription); // Auto fill reason
                                            }}>
                                                <div className={styles.aiCardTitle}>{s.specialtyName}</div>
                                                <div className={styles.aiCardReason}>{s.reason}</div>
                                            </button>
                                        ))}
                                    </div>
                                )}
                            </div>
                        )}
                    </div>

                    <div className={styles.section} style={{ marginBottom: 0 }}>
                        <h3 className={styles.sectionHeader}><CalendarDays size={24} /> Chi tiết lịch hẹn</h3>
                        
                        <div className={styles.formGrid}>
                            <div className={styles.formGroup}>
                                <label>Chuyên khoa (*)</label>
                                <select className="form-select" value={specialtyId} onChange={(e) => setSpecialtyId(Number(e.target.value))}>
                                    <option value="">-- Chọn chuyên khoa --</option>
                                    {specialties.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
                                </select>
                            </div>

                            <div className={styles.formGroup}>
                                <label>Bác sĩ (*)</label>
                                <select className="form-select" value={doctorId} onChange={(e) => setDoctorId(Number(e.target.value))} disabled={!specialtyId}>
                                    <option value="">-- Vui lòng chọn bác sĩ --</option>
                                    {doctors.map(d => <option key={d.id} value={d.id}>{d.fullName}</option>)}
                                </select>
                            </div>

                            <div className={styles.formGroup}>
                                <label>Ngày khám (*)</label>
                                <input type="date" className="form-input" value={slotDate} onChange={(e) => setSlotDate(e.target.value)} min={new Date().toISOString().split('T')[0]} />
                            </div>

                            <div className={styles.formGroup}>
                                <label>Lý do khám thực tế (*)</label>
                                <input type="text" className="form-input" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Nhập tóm tắt triệu chứng..." />
                            </div>
                        </div>

                        <div className={styles.formGroup} style={{ marginTop: '10px' }}>
                            <label>Khung giờ khám (*)</label>
                            {loadingSlots ? <div style={{ color: 'var(--c-muted)' }}>Đang tải ca khám...</div> : (
                                <div className={styles.slotsContainer}>
                                    {!doctorId ? (
                                        <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Vui lòng chọn bác sĩ và ngày khám.</div>
                                    ) : slots.length === 0 ? (
                                        <div style={{ color: 'var(--c-muted)', fontSize: '0.9rem' }}>Không có khung giờ trống trong ngày này.</div>
                                    ) : (
                                        slots.map(s => (
                                            <button 
                                                key={s.id}
                                                className={`${styles.slotBtn} ${slotId === s.id ? styles.slotSelected : ''}`}
                                                onClick={() => setSlotId(s.id)}
                                            >
                                                {s.startTime.substring(0, 5)}
                                            </button>
                                        ))
                                    )}
                                </div>
                            )}
                        </div>

                        <div className={styles.actions}>
                            <button className="btn-primary" onClick={handleProceedToConfirm}>
                                Tiếp tục <ArrowRight size={18} />
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {step === 2 && (
                <div className={styles.card}>
                    <h3 className={styles.sectionHeader}><FileText size={24} /> Xác nhận phiếu đặt lịch</h3>
                    
                    <div className={styles.summaryTicket}>
                        <div className={styles.summaryRow}>
                            <span className={styles.summaryLabel}><Stethoscope size={16} style={{ verticalAlign: 'middle', marginRight: '5px' }}/> Chuyên khoa</span>
                            <span className={styles.summaryValue}>{specialties.find(s => s.id === specialtyId)?.name}</span>
                        </div>
                        <div className={styles.summaryRow}>
                            <span className={styles.summaryLabel}><User size={16} style={{ verticalAlign: 'middle', marginRight: '5px' }}/> Bác sĩ phụ trách</span>
                            <span className={styles.summaryValue}>{doctors.find(d => d.id === doctorId)?.fullName}</span>
                        </div>
                        <div className={styles.summaryRow}>
                            <span className={styles.summaryLabel}><CalendarDays size={16} style={{ verticalAlign: 'middle', marginRight: '5px' }}/> Ngày khám</span>
                            <span className={styles.summaryValue}>{slotDate}</span>
                        </div>
                        <div className={styles.summaryRow}>
                            <span className={styles.summaryLabel}><Clock size={16} style={{ verticalAlign: 'middle', marginRight: '5px' }}/> Giờ khám</span>
                            <span className={styles.summaryValue}>{slots.find(s => s.id === slotId)?.startTime.substring(0, 5)}</span>
                        </div>
                        <div className={styles.summaryRow}>
                            <span className={styles.summaryLabel}><FileText size={16} style={{ verticalAlign: 'middle', marginRight: '5px' }}/> Lý do khám</span>
                            <span className={styles.summaryValue}>{reason}</span>
                        </div>
                    </div>

                    <div className={styles.warningBox}>
                        <AlertTriangle size={20} style={{ flexShrink: 0 }} />
                        <span>Lịch hẹn sẽ ở trạng thái <strong>Chờ xác nhận</strong> sau khi gửi. Bộ phận lễ tân sẽ xử lý yêu cầu của bạn sớm nhất.</span>
                    </div>

                    <div className={styles.actions}>
                        <button className="btn-secondary" onClick={() => setStep(1)}>Quay lại sửa</button>
                        <button className="btn-primary" onClick={handleSubmit} disabled={submitting}>
                            {submitting ? 'Đang gửi yêu cầu...' : 'Hoàn tất đặt lịch'}
                        </button>
                    </div>
                </div>
            )}

            {step === 3 && (
                <div className={styles.card}>
                    <div className={styles.successState}>
                        <div className={styles.successIcon}>
                            <CheckCircle2 size={48} />
                        </div>
                        <h2 style={{ color: 'var(--c-text-dark)', marginBottom: '10px' }}>Đặt lịch thành công!</h2>
                        <p style={{ color: 'var(--c-muted)', fontSize: '1.1rem' }}>Yêu cầu đặt lịch của bạn đã được ghi nhận.</p>
                        
                        <div className={styles.appointmentCode}>
                            {appointmentCode}
                        </div>
                        <p style={{ marginBottom: '30px' }}>Trạng thái: <span className="badge badge-warning">Chờ xác nhận</span></p>

                        <div style={{ display: 'flex', gap: '15px', justifyContent: 'center' }}>
                            <button className="btn-primary" onClick={() => navigate('/patient/appointments')}>
                                Xem lịch hẹn của tôi
                            </button>
                            <button className="btn-secondary" onClick={() => {
                                setStep(1); setSlotId(''); setReason('');
                            }}>
                                Đặt lịch mới
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
