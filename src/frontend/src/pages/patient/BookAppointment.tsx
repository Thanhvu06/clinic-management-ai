import React, { useState, useEffect } from "react";
import { useLocation, Link } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import styles from "./BookAppointment.module.css";
import type { ApiResponse } from "../../types";
import { 
    CheckCircle2, User, Clock, Stethoscope, FileText, 
    ArrowRight, ArrowLeft, Calendar, ShieldCheck, Sparkles, AlertCircle 
} from "lucide-react";
import { useChatContext } from "../../contexts/ChatContext";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";
import { Button, FormField, TextInput, Textarea, FormError } from "../../components/forms";

interface Specialty {
    id: number;
    specialtyCode: string;
    specialtyName: string;
    description: string;
    aiEnabled: boolean;
}

interface Doctor {
    id: number;
    fullName: string;
    academicTitle?: string;
    experienceYears?: number;
    specialtyId?: number;
    specialtyName?: string;
    description?: string;
}

interface Slot {
    slotId: number;
    doctorId: number;
    slotDate: string;
    startTime: string;
    endTime: string;
}

interface BookingSuccessData {
    appointmentId: number;
    appointmentCode: string;
    doctorName: string;
    specialtyName: string;
    slotDate: string;
    startTime: string;
    endTime: string;
    reason?: string;
}

export const BookAppointment: React.FC = () => {
    const location = useLocation();
    const { pendingSpecialtyId, setPendingSpecialtyId } = useChatContext();
    const { showAlert } = useDialog();

    const [step, setStep] = useState<1 | 2 | 3 | 4>(1);
    const [specialtyId, setSpecialtyId] = useState<number | "">("");
    const [doctorId, setDoctorId] = useState<number | "">("");
    const [slotDate, setSlotDate] = useState<string>(() => {
        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        return tomorrow.toISOString().split("T")[0];
    });
    const [slotId, setSlotId] = useState<number | "">("");
    const [reason, setReason] = useState("");

    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [slots, setSlots] = useState<Slot[]>([]);

    const [loadingSpecs, setLoadingSpecs] = useState(true);
    const [loadingDocs, setLoadingDocs] = useState(false);
    const [loadingSlots, setLoadingSlots] = useState(false);

    const [submitting, setSubmitting] = useState(false);
    const [bookingError, setBookingError] = useState<string | null>(null);
    const [successBooking, setSuccessBooking] = useState<BookingSuccessData | null>(null);

    // 1. Fetch Specialties
    useEffect(() => {
        const fetchSpecs = async () => {
            try {
                setLoadingSpecs(true);
                const res = await axiosClient.get<any, ApiResponse<Specialty[]>>("/specialties");
                if (res.success && res.data) {
                    setSpecialties(res.data);
                }
            } catch (err) {
                console.error("Failed to load specialties", err);
            } finally {
                setLoadingSpecs(false);
            }
        };
        fetchSpecs();
    }, []);

    // 2. Parse URL parameters and AI context safely
    useEffect(() => {
        if (specialties.length === 0) return;

        let targetSpecId: number | null = null;
        let targetDocId: number | null = null;

        if (pendingSpecialtyId) {
            targetSpecId = pendingSpecialtyId;
            setPendingSpecialtyId(null);
        } else {
            const params = new URLSearchParams(location.search);
            const querySpecId = params.get("specialtyId");
            if (querySpecId) {
                targetSpecId = parseInt(querySpecId, 10);
            }
            const queryDocId = params.get("doctorId");
            if (queryDocId) {
                targetDocId = parseInt(queryDocId, 10);
            }
        }

        if (targetSpecId && !isNaN(targetSpecId)) {
            setSpecialtyId(targetSpecId);
        }

        if (targetDocId && !isNaN(targetDocId)) {
            setDoctorId(targetDocId);
        }
    }, [specialties, pendingSpecialtyId, location.search, setPendingSpecialtyId]);

    // 3. Fetch Doctors when specialtyId changes (or all active doctors if query has doctorId)
    useEffect(() => {
        if (!specialtyId) {
            setDoctors([]);
            return;
        }

        const fetchDoctors = async () => {
            try {
                setLoadingDocs(true);
                const res = await axiosClient.get<any, ApiResponse<any>>(`/specialties/${specialtyId}/doctors`);
                if (res.success && res.data) {
                    const list = Array.isArray(res.data) ? res.data : (res.data.items ?? []);
                    setDoctors(list);
                    
                    // If current doctorId is not in the specialty's doctor list, reset doctorId
                    if (doctorId && !list.some((d: Doctor) => d.id === doctorId)) {
                        setDoctorId("");
                        setSlotId("");
                    }
                }
            } catch (err) {
                console.error("Failed to fetch doctors", err);
            } finally {
                setLoadingDocs(false);
            }
        };

        fetchDoctors();
    }, [specialtyId]);

    // 4. Fetch Slots when doctorId and slotDate are set
    useEffect(() => {
        if (!doctorId || !slotDate) {
            setSlots([]);
            setSlotId("");
            return;
        }

        const fetchSlots = async () => {
            try {
                setLoadingSlots(true);
                const res = await axiosClient.get<any, ApiResponse<Slot[]>>(
                    `/appointments/slots?doctorId=${doctorId}&fromDate=${slotDate}&toDate=${slotDate}`
                );
                if (res.success && res.data) {
                    setSlots(res.data);
                } else {
                    setSlots([]);
                }
            } catch (err) {
                console.error("Failed to fetch slots", err);
                setSlots([]);
            } finally {
                setLoadingSlots(false);
            }
        };

        fetchSlots();
    }, [doctorId, slotDate]);

    // Handler when selecting specialty: discards doctor & slot
    const handleSelectSpecialty = (id: number) => {
        if (specialtyId !== id) {
            setSpecialtyId(id);
            setDoctorId("");
            setSlotId("");
            setSlots([]);
        }
    };

    // Handler when selecting doctor: discards slot
    const handleSelectDoctor = (id: number) => {
        if (doctorId !== id) {
            setDoctorId(id);
            setSlotId("");
            setSlots([]);
        }
    };

    // Confirm booking submit
    const handleConfirm = async () => {
        if (submitting || !specialtyId || !doctorId || !slotId) return;
        setSubmitting(true);
        setBookingError(null);

        try {
            const res = await axiosClient.post<any, ApiResponse<any>>("/appointments", {
                doctorId,
                specialtyId,
                appointmentSlotId: slotId,
                reason: reason.trim() ? reason.trim() : null
            });

            if (res.success && res.data) {
                setSuccessBooking({
                    appointmentId: res.data.id || res.data.appointmentId,
                    appointmentCode: res.data.appointmentCode,
                    doctorName: res.data.doctorName || selectedDoc?.fullName || "",
                    specialtyName: res.data.specialtyName || selectedSpec?.specialtyName || "",
                    slotDate,
                    startTime: (res.data.startTime || selectedSlot?.startTime || "").substring(0, 5),
                    endTime: (res.data.endTime || selectedSlot?.endTime || "").substring(0, 5),
                    reason: res.data.reason || (reason.trim() ? reason.trim() : undefined)
                });
            }
        } catch (err: any) {
            const errorCode = err?.response?.data?.errorCode || err?.errorCode;
            let msg = err?.response?.data?.message || err?.message || "Có lỗi xảy ra khi đặt lịch.";
            if (errorCode === "SLOT_ALREADY_BOOKED") {
                msg = "Khung giờ này vừa được đặt bởi người khác. Vui lòng chọn giờ khác.";
                setSlotId("");
                setStep(3);
            } else if (errorCode === "PATIENT_TIME_CONFLICT") {
                msg = "Bạn đã có một lịch khám khác trùng vào khung giờ này.";
                setStep(3);
            } else if (errorCode === "DOCTOR_NOT_AVAILABLE") {
                msg = "Bác sĩ không có ca trực hoặc nghỉ phép vào giờ này.";
                setSlotId("");
                setStep(3);
            }
            setBookingError(msg);
            showAlert(msg, "Thông báo đặt lịch", "error");
        } finally {
            setSubmitting(false);
        }
    };

    const selectedSpec = specialties.find(s => s.id === specialtyId);
    const selectedDoc = doctors.find(d => d.id === doctorId);
    const selectedSlot = slots.find(s => s.slotId === slotId);

    // Success Screen
    if (successBooking) {
        return (
            <div className={styles.pageWrapper}>
                <div style={{ maxWidth: '680px', margin: '20px auto', background: '#ffffff', borderRadius: '16px', border: '1px solid var(--c-border)', padding: '40px', textAlign: 'center', boxShadow: 'var(--shadow-md)' }}>
                    <div style={{ width: '72px', height: '72px', borderRadius: '50%', background: '#ecfdf5', color: '#10b981', display: 'flex', alignItems: 'center', justifyContent: 'center', margin: '0 auto 20px auto' }}>
                        <CheckCircle2 size={44} />
                    </div>

                    <h1 style={{ fontSize: '1.65rem', fontWeight: 800, color: 'var(--c-navy)', margin: '0 0 8px 0' }}>
                        Đặt lịch khám thành công!
                    </h1>
                    <p style={{ color: 'var(--c-text-muted)', fontSize: '0.95rem', margin: '0 0 24px 0' }}>
                        Lịch hẹn của bạn đã được ghi nhận vào hệ thống phòng khám ClinicCare.
                    </p>

                    <div style={{ background: '#f8fafc', border: '1px solid var(--c-border-light)', borderRadius: '12px', padding: '24px', textAlign: 'left', marginBottom: '28px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingBottom: '12px', borderBottom: '1px solid #e2e8f0', marginBottom: '12px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Mã lịch hẹn:</span>
                            <strong style={{ fontSize: '1.2rem', color: 'var(--c-primary)', letterSpacing: '0.05em' }}>
                                #{successBooking.appointmentCode}
                            </strong>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Chuyên khoa:</span>
                            <span style={{ fontWeight: 600, color: 'var(--c-navy)' }}>{successBooking.specialtyName}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Bác sĩ khám:</span>
                            <span style={{ fontWeight: 600, color: 'var(--c-navy)' }}>{successBooking.doctorName}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Thời gian hẹn:</span>
                            <strong style={{ color: 'var(--c-primary)' }}>
                                {successBooking.startTime} - {successBooking.endTime} ({successBooking.slotDate})
                            </strong>
                        </div>
                        {successBooking.reason && (
                            <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                                <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Lý do khám:</span>
                                <span style={{ fontStyle: 'italic', maxWidth: '300px', textAlign: 'right' }}>"{successBooking.reason}"</span>
                            </div>
                        )}
                    </div>

                    <div style={{ background: '#eff6ff', border: '1px solid #bfdbfe', borderRadius: '8px', padding: '14px', marginBottom: '28px', textAlign: 'left', fontSize: '0.85rem', color: '#1e40af' }}>
                        <Clock size={16} style={{ display: 'inline', marginRight: '6px', verticalAlign: 'middle' }} />
                        <strong>Lưu ý:</strong> Vui lòng có mặt tại quầy lễ tân trước giờ hẹn 10-15 phút và cung cấp mã lịch hẹn <strong>#{successBooking.appointmentCode}</strong> để được tiếp đón ưu tiên.
                    </div>

                    <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', flexWrap: 'wrap' }}>
                        <Link to="/patient/appointments" className="btn btn-primary" style={{ textDecoration: 'none', padding: '12px 24px' }}>
                            Xem lịch hẹn của tôi
                        </Link>
                        <button 
                            type="button" 
                            className="btn btn-secondary"
                            onClick={() => {
                                setSuccessBooking(null);
                                setStep(1);
                                setSpecialtyId("");
                                setDoctorId("");
                                setSlotId("");
                                setReason("");
                            }}
                            style={{ padding: '12px 24px' }}
                        >
                            Đặt thêm lịch hẹn khác
                        </button>
                    </div>
                </div>
            </div>
        );
    }

    if (loadingSpecs) {
        return (
            <div className={styles.pageWrapper}>
                <div style={{ padding: '40px', background: '#ffffff', borderRadius: '12px', border: '1px solid var(--c-border)', textAlign: 'center' }}>
                    Đang tải dữ liệu phòng khám...
                </div>
            </div>
        );
    }

    return (
        <div className={styles.pageWrapper}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/' },
                { label: 'Đặt lịch khám' }
            ]} />

            <div className={styles.pageHeader}>
                <h1 className={styles.pageTitle}>Đặt lịch khám Chuyên khoa</h1>
                <p className={styles.pageSubtitle}>
                    Chọn chuyên khoa, bác sĩ và khung giờ 30 phút thuận tiện để được tiếp đón chu đáo không phải chờ đợi.
                </p>
            </div>

            {/* Stepper Progress Bar */}
            <div className={styles.stepperContainer} role="progressbar" aria-valuenow={step} aria-valuemin={1} aria-valuemax={4}>
                {[
                    { num: 1, label: "1. Chuyên khoa" },
                    { num: 2, label: "2. Bác sĩ" },
                    { num: 3, label: "3. Thời gian" },
                    { num: 4, label: "4. Xác nhận" }
                ].map((s, idx) => (
                    <div key={s.num} className={`${styles.stepItem} ${step >= s.num ? styles.stepItemActive : ""}`}>
                        <div className={styles.stepCircle}>
                            {step > s.num ? <CheckCircle2 size={20} /> : s.num}
                        </div>
                        <span className={styles.stepLabel}>{s.label}</span>
                        {idx < 3 && <div className={styles.stepLine}></div>}
                    </div>
                ))}
            </div>

            {/* 2-Column Responsive Layout */}
            <div className={styles.bookingLayout}>
                {/* Left Column: Current Wizard Step */}
                <div className={styles.mainCard}>
                    {bookingError && (
                        <div style={{ marginBottom: '20px' }}>
                            <FormError message={bookingError} />
                        </div>
                    )}

                    {/* Step 1: Chọn Chuyên khoa */}
                    {step === 1 && (
                        <div>
                            <h2 className={styles.stepTitle}>Bước 1: Chọn chuyên khoa khám</h2>
                            <p className={styles.stepSubtitle}>
                                Vui lòng chọn một chuyên khoa phù hợp với tình trạng sức khỏe hiện tại của bạn.
                            </p>

                            <div className={styles.selectionGrid}>
                                {specialties.map(s => (
                                    <div 
                                        key={s.id} 
                                        className={`${styles.selectionCard} ${specialtyId === s.id ? styles.selectionCardActive : ""}`}
                                        onClick={() => handleSelectSpecialty(s.id)}
                                        role="button"
                                        tabIndex={0}
                                        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') handleSelectSpecialty(s.id); }}
                                    >
                                        <Stethoscope size={30} className={styles.cardIcon} />
                                        <h4>{s.specialtyName}</h4>
                                        <p>{s.description || "Khám và điều trị chuyên sâu"}</p>
                                        {s.aiEnabled && (
                                            <span style={{ fontSize: '0.75rem', color: 'var(--c-primary)', fontWeight: 600, marginTop: '8px', display: 'flex', alignItems: 'center', gap: '3px' }}>
                                                <Sparkles size={12} /> Hỗ trợ AI
                                            </span>
                                        )}
                                    </div>
                                ))}
                            </div>

                            <div className={styles.stepFooter}>
                                <span style={{ fontSize: '0.875rem', color: 'var(--c-text-muted)' }}>
                                    Chưa biết nên chọn khoa nào? <Link to="/patient/ai-consultation" style={{ color: 'var(--c-primary)', fontWeight: 600 }}>Tư vấn cùng AI &rarr;</Link>
                                </span>
                                <Button 
                                    variant="primary" 
                                    size="lg" 
                                    onClick={() => setStep(2)} 
                                    disabled={!specialtyId}
                                >
                                    Tiếp tục: Chọn bác sĩ <ArrowRight size={18} />
                                </Button>
                            </div>
                        </div>
                    )}

                    {/* Step 2: Chọn Bác sĩ */}
                    {step === 2 && (
                        <div>
                            <h2 className={styles.stepTitle}>
                                Bước 2: Chọn bác sĩ ({selectedSpec?.specialtyName})
                            </h2>
                            <p className={styles.stepSubtitle}>
                                Chọn bác sĩ mà bạn muốn trực tiếp thăm khám và tư vấn.
                            </p>

                            {loadingDocs ? (
                                <div style={{ padding: '36px', textAlign: 'center', color: 'var(--c-text-muted)' }}>
                                    Đang tải danh sách bác sĩ thuộc chuyên khoa...
                                </div>
                            ) : doctors.length === 0 ? (
                                <div style={{ padding: '36px', background: '#f8fafc', borderRadius: '12px', textAlign: 'center' }}>
                                    <AlertCircle size={36} color="var(--c-text-muted)" style={{ marginBottom: '8px' }} />
                                    <p style={{ color: 'var(--c-text)', fontWeight: 600, margin: '0 0 6px 0' }}>
                                        Chưa có bác sĩ thuộc chuyên khoa này
                                    </p>
                                    <p style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem', margin: '0 0 16px 0' }}>
                                        Vui lòng quay lại chọn chuyên khoa khác hoặc liên hệ hotline phòng khám.
                                    </p>
                                    <Button variant="secondary" size="md" onClick={() => setStep(1)}>
                                        <ArrowLeft size={16} /> Chọn lại chuyên khoa
                                    </Button>
                                </div>
                            ) : (
                                <div className={styles.selectionGrid}>
                                    {doctors.map(d => (
                                        <div 
                                            key={d.id} 
                                            className={`${styles.selectionCard} ${doctorId === d.id ? styles.selectionCardActive : ""}`}
                                            onClick={() => handleSelectDoctor(d.id)}
                                            role="button"
                                            tabIndex={0}
                                            onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') handleSelectDoctor(d.id); }}
                                        >
                                            <div style={{ width: '48px', height: '48px', borderRadius: '50%', background: '#e0f2fe', color: 'var(--c-primary)', display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: '10px' }}>
                                                <User size={26} />
                                            </div>
                                            <h4>{d.academicTitle ? d.academicTitle + ". " : ""}{d.fullName}</h4>
                                            <p>{d.experienceYears ? `${d.experienceYears} năm kinh nghiệm` : "Bác sĩ chuyên khoa"}</p>
                                        </div>
                                    ))}
                                </div>
                            )}

                            <div className={styles.stepFooter}>
                                <Button variant="secondary" size="md" onClick={() => setStep(1)}>
                                    <ArrowLeft size={16} /> Quay lại
                                </Button>
                                <Button 
                                    variant="primary" 
                                    size="lg" 
                                    onClick={() => setStep(3)} 
                                    disabled={!doctorId}
                                >
                                    Tiếp tục: Chọn giờ khám <ArrowRight size={18} />
                                </Button>
                            </div>
                        </div>
                    )}

                    {/* Step 3: Chọn Ngày, Giờ & Triệu chứng */}
                    {step === 3 && (
                        <div>
                            <h2 className={styles.stepTitle}>
                                Bước 3: Chọn ngày & giờ khám
                            </h2>
                            <p className={styles.stepSubtitle}>
                                Lịch trống của {selectedDoc?.academicTitle ? selectedDoc.academicTitle + ". " : ""}{selectedDoc?.fullName}
                            </p>

                            <div style={{ maxWidth: '320px', marginBottom: '24px' }}>
                                <FormField id="slotDate" label="Chọn ngày khám" required>
                                    <TextInput
                                        id="slotDate"
                                        type="date"
                                        min={new Date().toISOString().split("T")[0]}
                                        value={slotDate}
                                        onChange={(e) => {
                                            setSlotDate(e.target.value);
                                            setSlotId("");
                                        }}
                                    />
                                </FormField>
                            </div>

                            <div style={{ marginBottom: '28px' }}>
                                <label style={{ display: 'block', fontSize: '0.9rem', fontWeight: 600, color: 'var(--c-navy)', marginBottom: '8px' }}>
                                    Khung giờ khám còn trống (*)
                                </label>
                                {loadingSlots ? (
                                    <div style={{ padding: '24px', background: '#f8fafc', borderRadius: '8px', textAlign: 'center', color: 'var(--c-text-muted)' }}>
                                        Đang kiểm tra lịch trống của bác sĩ...
                                    </div>
                                ) : slots.length === 0 ? (
                                    <div style={{ padding: '24px', background: '#f8fafc', borderRadius: '8px', textAlign: 'center', color: 'var(--c-text-muted)' }}>
                                        Bác sĩ chưa có lịch trống trong ngày {slotDate}. Quý khách vui lòng chọn một ngày khám khác.
                                    </div>
                                ) : (
                                    <div className={styles.slotGrid}>
                                        {slots.map(s => (
                                            <button
                                                key={s.slotId}
                                                type="button"
                                                className={`${styles.slotBtn} ${slotId === s.slotId ? styles.slotBtnActive : ""}`}
                                                onClick={() => setSlotId(s.slotId)}
                                            >
                                                <Clock size={14} />
                                                {s.startTime.substring(0, 5)}
                                            </button>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div style={{ marginBottom: '20px' }}>
                                <FormField 
                                    id="reason" 
                                    label="Triệu chứng hoặc lý do thăm khám (Không bắt buộc)"
                                    helpText="Giúp bác sĩ chuẩn bị hồ sơ và hiểu sơ bộ tình trạng sức khỏe trước khi bạn đến."
                                >
                                    <Textarea
                                        id="reason"
                                        placeholder="Ví dụ: Đau đầu kéo dài 3 ngày, ho sốt nhẹ, khám định kỳ..."
                                        value={reason}
                                        onChange={(e) => setReason(e.target.value)}
                                        rows={3}
                                    />
                                </FormField>
                            </div>

                            <div className={styles.stepFooter}>
                                <Button variant="secondary" size="md" onClick={() => setStep(2)}>
                                    <ArrowLeft size={16} /> Quay lại
                                </Button>
                                <Button 
                                    variant="primary" 
                                    size="lg" 
                                    onClick={() => setStep(4)} 
                                    disabled={!slotDate || !slotId}
                                >
                                    Tiếp tục: Xác nhận <ArrowRight size={18} />
                                </Button>
                            </div>
                        </div>
                    )}

                    {/* Step 4: Xác nhận & Đặt hẹn */}
                    {step === 4 && (
                        <div>
                            <h2 className={styles.stepTitle}>
                                Bước 4: Kiểm tra & Xác nhận thông tin
                            </h2>
                            <p className={styles.stepSubtitle}>
                                Vui lòng kiểm tra lại toàn bộ chi tiết lịch hẹn trước khi gửi yêu cầu đặt khám.
                            </p>

                            <div style={{ background: '#f8fafc', border: '1px solid var(--c-border-light)', borderRadius: '12px', padding: '24px', marginBottom: '24px' }}>
                                <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start', marginBottom: '16px', paddingBottom: '16px', borderBottom: '1px solid #e2e8f0' }}>
                                    <Stethoscope size={22} color="var(--c-primary)" style={{ marginTop: '2px', flexShrink: 0 }} />
                                    <div>
                                        <span style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)', display: 'block' }}>CHUYÊN KHOA</span>
                                        <span style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--c-navy)' }}>{selectedSpec?.specialtyName}</span>
                                    </div>
                                </div>

                                <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start', marginBottom: '16px', paddingBottom: '16px', borderBottom: '1px solid #e2e8f0' }}>
                                    <User size={22} color="var(--c-primary)" style={{ marginTop: '2px', flexShrink: 0 }} />
                                    <div>
                                        <span style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)', display: 'block' }}>BÁC SĨ PHỤ TRÁCH</span>
                                        <span style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--c-navy)' }}>
                                            {selectedDoc?.academicTitle ? selectedDoc.academicTitle + ". " : ""}{selectedDoc?.fullName}
                                        </span>
                                    </div>
                                </div>

                                <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start', marginBottom: '16px', paddingBottom: '16px', borderBottom: '1px solid #e2e8f0' }}>
                                    <Clock size={22} color="var(--c-primary)" style={{ marginTop: '2px', flexShrink: 0 }} />
                                    <div>
                                        <span style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)', display: 'block' }}>THỜI GIAN KHÁM TIÊU CHUẨN</span>
                                        <span style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--c-navy)' }}>
                                            {selectedSlot?.startTime.substring(0, 5)} - {selectedSlot?.endTime.substring(0, 5)}
                                        </span>
                                        <div style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)' }}>
                                            Ngày {slotDate.split("-").reverse().join("/")}
                                        </div>
                                    </div>
                                </div>

                                {reason && (
                                    <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-start' }}>
                                        <FileText size={22} color="var(--c-primary)" style={{ marginTop: '2px', flexShrink: 0 }} />
                                        <div>
                                            <span style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)', display: 'block' }}>TRIỆU CHỨNG / GHI CHÚ</span>
                                            <span style={{ fontSize: '0.95rem', color: 'var(--c-text)', fontStyle: 'italic' }}>
                                                "{reason}"
                                            </span>
                                        </div>
                                    </div>
                                )}
                            </div>

                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#047857', background: '#ecfdf5', padding: '12px 16px', borderRadius: '8px', fontSize: '0.875rem', marginBottom: '24px' }}>
                                <ShieldCheck size={18} style={{ flexShrink: 0 }} />
                                <span>Phòng khám cam kết giữ đúng lịch hẹn và bảo mật thông tin hồ sơ y tế.</span>
                            </div>

                            <div className={styles.stepFooter}>
                                <Button variant="secondary" size="md" onClick={() => setStep(3)} disabled={submitting}>
                                    <ArrowLeft size={16} /> Quay lại
                                </Button>
                                <Button 
                                    variant="primary" 
                                    size="lg" 
                                    onClick={handleConfirm} 
                                    loading={submitting}
                                    disabled={submitting}
                                >
                                    Xác nhận & Đặt lịch
                                </Button>
                            </div>
                        </div>
                    )}
                </div>

                {/* Right Column: Sticky Appointment Summary */}
                <aside className={styles.summaryCard}>
                    <h3 className={styles.summaryTitle}>
                        <FileText size={18} color="var(--c-primary)" /> Tóm tắt lịch khám
                    </h3>

                    <div className={styles.summaryList}>
                        <div className={styles.summaryItem}>
                            <div className={styles.summaryItemIcon}>
                                <Stethoscope size={18} />
                            </div>
                            <div>
                                <div className={styles.summaryItemLabel}>Chuyên khoa</div>
                                <div className={selectedSpec ? styles.summaryItemValue : styles.summaryItemEmpty}>
                                    {selectedSpec?.specialtyName || "Chưa chọn"}
                                </div>
                            </div>
                        </div>

                        <div className={styles.summaryItem}>
                            <div className={styles.summaryItemIcon}>
                                <User size={18} />
                            </div>
                            <div>
                                <div className={styles.summaryItemLabel}>Bác sĩ</div>
                                <div className={selectedDoc ? styles.summaryItemValue : styles.summaryItemEmpty}>
                                    {selectedDoc ? `${selectedDoc.academicTitle ? selectedDoc.academicTitle + ". " : ""}${selectedDoc.fullName}` : "Chưa chọn"}
                                </div>
                            </div>
                        </div>

                        <div className={styles.summaryItem}>
                            <div className={styles.summaryItemIcon}>
                                <Calendar size={18} />
                            </div>
                            <div>
                                <div className={styles.summaryItemLabel}>Ngày khám</div>
                                <div className={slotDate ? styles.summaryItemValue : styles.summaryItemEmpty}>
                                    {slotDate ? slotDate.split("-").reverse().join("/") : "Chưa chọn"}
                                </div>
                            </div>
                        </div>

                        <div className={styles.summaryItem}>
                            <div className={styles.summaryItemIcon}>
                                <Clock size={18} />
                            </div>
                            <div>
                                <div className={styles.summaryItemLabel}>Khung giờ (30 phút)</div>
                                <div className={selectedSlot ? styles.summaryItemValue : styles.summaryItemEmpty}>
                                    {selectedSlot ? `${selectedSlot.startTime.substring(0, 5)} - ${selectedSlot.endTime.substring(0, 5)}` : "Chưa chọn"}
                                </div>
                            </div>
                        </div>
                    </div>

                    <div style={{ background: '#f8fafc', borderRadius: '8px', padding: '14px', border: '1px solid var(--c-border-light)', fontSize: '0.85rem', color: 'var(--c-text-muted)', lineHeight: 1.5 }}>
                        <div style={{ fontWeight: 600, color: 'var(--c-navy)', marginBottom: '4px' }}>Hỗ trợ đặt hẹn</div>
                        Hotline: <strong style={{ color: 'var(--c-primary)' }}>1900 1234</strong> (07:00 - 19:00 hàng ngày)
                    </div>
                </aside>
            </div>
        </div>
    );
};
