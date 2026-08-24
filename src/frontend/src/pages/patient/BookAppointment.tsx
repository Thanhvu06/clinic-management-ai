import React, { useState, useEffect } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import styles from "./BookAppointment.module.css";
import type { ApiResponse } from "../../types";
import { CheckCircle2, User, Clock, Stethoscope, FileText, ArrowRight, ArrowLeft } from "lucide-react";
import { useChatContext } from "../../contexts/ChatContext";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";

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
}

interface Slot {
    slotId: number;
    doctorId: number;
    slotDate: string;
    startTime: string;
    endTime: string;
}

export const BookAppointment: React.FC = () => {
    const navigate = useNavigate();
    const location = useLocation();
    const { pendingSpecialtyId, setPendingSpecialtyId } = useChatContext();
    const { showAlert } = useDialog();

    const [step, setStep] = useState(1);
    const [specialtyId, setSpecialtyId] = useState<number | "">("");
    const [doctorId, setDoctorId] = useState<number | "">("");
    const [slotDate, setSlotDate] = useState<string>("");
    const [slotId, setSlotId] = useState<number | "">("");
    const [reason, setReason] = useState("");

    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [slots, setSlots] = useState<Slot[]>([]);

    const [loadingSpecs, setLoadingSpecs] = useState(true);
    const [loadingSlots, setLoadingSlots] = useState(false);

    const [submitting, setSubmitting] = useState(false);

    useEffect(() => {
        const fetchSpecs = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<Specialty[]>>("/specialties");
                if (res.success && res.data) {
                    setSpecialties(res.data);
                }
            } catch (err) {}
            finally { setLoadingSpecs(false); }
        };
        fetchSpecs();

        const tomorrow = new Date();
        tomorrow.setDate(tomorrow.getDate() + 1);
        setSlotDate(tomorrow.toISOString().split("T")[0]);
    }, []);

    useEffect(() => {
        if (specialties.length === 0) return;

          let targetId: number | null = null;
          let targetDocId: number | null = null;
          if (pendingSpecialtyId) {
              targetId = pendingSpecialtyId;
              setPendingSpecialtyId(null);
          } else {
              const params = new URLSearchParams(location.search);
              const queryId = params.get("specialtyId");
              if (queryId) {
                  targetId = parseInt(queryId, 10);
              }
              const queryDocId = params.get("doctorId");
              if (queryDocId) {
                  targetDocId = parseInt(queryDocId, 10);
              }
          }
          if (targetId && !specialties.find(s => s.id === targetId)) {
              setSpecialties(prev => [...prev, { id: targetId!, specialtyCode: "", specialtyName: "Chuyên khoa đã chọn", description: "", aiEnabled: false }]);
          }
          if (targetId) {
              setSpecialtyId(targetId);
              setStep(2);
          }
          if (targetDocId) {
              setDoctorId(targetDocId);
              setStep(3);
          }
      }, [pendingSpecialtyId, location.search, specialties, setPendingSpecialtyId]);

    useEffect(() => {
        if (!specialtyId) {
            setDoctors([]);
            setDoctorId("");
            return;
        }
        const fetchDoctors = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<any>>(`/specialties/${specialtyId}/doctors`);
                if (res.success && res.data) {
                    const list = Array.isArray(res.data) ? res.data : (res.data.items ?? []);
                    setDoctors(list);
                }
            } catch (err) {}
        };
        fetchDoctors();
    }, [specialtyId]);

    useEffect(() => {
        if (!doctorId || !slotDate) {
            setSlots([]);
            setSlotId("");
            return;
        }
        const fetchSlots = async () => {
            setLoadingSlots(true);
            try {
                const res = await axiosClient.get<any, ApiResponse<Slot[]>>(`/appointments/slots?doctorId=${doctorId}&fromDate=${slotDate}&toDate=${slotDate}`);
                if (res.success && res.data) {
                    setSlots(res.data);
                }
            } catch (err) {}
            finally { setLoadingSlots(false); }
        };
        fetchSlots();
    }, [doctorId, slotDate]);

    const handleConfirm = async () => {
        setSubmitting(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>("/appointments", {
                doctorId,
                specialtyId,
                appointmentSlotId: slotId,
                reason
            });
            if (res.success) {
                showAlert(`Đặt lịch thành công! Mã khám của bạn là #${res.data?.appointmentCode || "N/A"}`, "Thành công", "success");
                navigate("/patient/appointments");
                setStep(1); 
                setSpecialtyId("");
                setDoctorId("");
                setSlotId("");
                setReason("");
            }
        } catch (err: any) {
            const errorCode = err?.response?.data?.errorCode || err?.errorCode;
            let msg = err?.response?.data?.message || err?.message || "Có lỗi xảy ra khi đặt lịch.";
            if (errorCode === "SLOT_ALREADY_BOOKED") {
                msg = "Khung giờ này vừa được đặt. Vui lòng chọn giờ khác.";
                setSlotId("");
                setStep(3);
            } else if (errorCode === "PATIENT_TIME_CONFLICT") {
                msg = "Bạn đã có lịch khám khác trùng giờ này.";
                setStep(3);
            } else if (errorCode === "DOCTOR_NOT_AVAILABLE") {
                msg = "Bác sĩ không khả dụng vào giờ này.";
                setSlotId("");
                setStep(3);
            }
            showAlert(msg, "Lỗi đặt lịch", "error");
        } finally {
            setSubmitting(false);
        }
    };

    const selectedSpec = specialties.find(s => s.id === specialtyId);
    const selectedDoc = doctors.find(d => d.id === doctorId);
    const selectedSlot = slots.find(s => s.slotId === slotId);

    if (loadingSpecs) return <div style={{ color: "var(--c-muted)", padding: "20px" }}>Đang tải thông tin chuyên khoa...</div>;

    const renderStepper = () => (
        <div className={styles.stepperContainer}>
            {[
                { num: 1, label: "Chuyên khoa" },
                { num: 2, label: "Bác sĩ" },
                { num: 3, label: "Thời gian" },
                { num: 4, label: "Xác nhận" }
            ].map((s, idx) => (
                <div key={s.num} className={`${styles.stepItem} ${step >= s.num ? styles.stepItemActive : ""}`}>
                    <div className={styles.stepCircle}>{s.num}</div>
                    <span className={styles.stepLabel}>{s.label}</span>
                    {idx < 3 && <div className={styles.stepLine}></div>}
                </div>
            ))}
        </div>
    );

    return (
        <div>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Đặt lịch khám' }
            ]} />

            <div className={styles.container}>
                <h2 className={styles.pageTitle}>Đặt lịch khám mới</h2>
                
                {renderStepper()}

                <div className={styles.card}>
                    {step === 1 && (
                        <div className={styles.stepContent}>
                            <h3 className={styles.stepTitle}>Chọn chuyên khoa</h3>
                            <div className={styles.selectionGrid}>
                                {specialties.map(s => (
                                    <div 
                                        key={s.id} 
                                        className={`${styles.selectionCard} ${specialtyId === s.id ? styles.selectionCardActive : ""}`}
                                        onClick={() => setSpecialtyId(s.id)}
                                    >
                                        <Stethoscope size={32} className={styles.cardIcon} />
                                        <h4>{s.specialtyName}</h4>
                                        <p>{s.description || "Khám chuyên khoa"}</p>
                                    </div>
                                ))}
                            </div>
                            <div className={styles.stepFooter}>
                                <button className="btn-primary" onClick={() => setStep(2)} disabled={!specialtyId}>
                                    Tiếp tục <ArrowRight size={18} style={{ marginLeft: 8 }}/>
                                </button>
                            </div>
                        </div>
                    )}

                    {step === 2 && (
                        <div className={styles.stepContent}>
                            <h3 className={styles.stepTitle}>Chọn bác sĩ ({selectedSpec?.specialtyName})</h3>
                            {doctors.length === 0 ? (
                                <div className="empty-state">
                                    Chưa có bác sĩ nào thuộc chuyên khoa này. Vui lòng chọn chuyên khoa khác.
                                </div>
                            ) : (
                                <div className={styles.selectionGrid}>
                                    {doctors.map(d => (
                                        <div 
                                            key={d.id} 
                                            className={`${styles.selectionCard} ${doctorId === d.id ? styles.selectionCardActive : ""}`}
                                            onClick={() => setDoctorId(d.id)}
                                        >
                                            <User size={32} className={styles.cardIcon} />
                                            <h4>{d.academicTitle ? d.academicTitle + " " : ""}{d.fullName}</h4>
                                            <p>{d.experienceYears ? `${d.experienceYears} năm kinh nghiệm` : "Bác sĩ chuyên khoa"}</p>
                                        </div>
                                    ))}
                                </div>
                            )}
                            <div className={styles.stepFooter}>
                                <button className="btn-secondary" onClick={() => setStep(1)}>
                                    <ArrowLeft size={18} style={{ marginRight: 8 }}/> Quay lại
                                </button>
                                <button className="btn-primary" onClick={() => setStep(3)} disabled={!doctorId}>
                                    Tiếp tục <ArrowRight size={18} style={{ marginLeft: 8 }}/>
                                </button>
                            </div>
                        </div>
                    )}

                    {step === 3 && (
                        <div className={styles.stepContent}>
                            <h3 className={styles.stepTitle}>Chọn ngày, giờ và ghi chú</h3>
                            
                            <div className="form-group" style={{ maxWidth: 300 }}>
                                <label className="form-label">Ngày khám (*)</label>
                                <input
                                    type="date"
                                    className="form-input"
                                    value={slotDate}
                                    onChange={e => {
                                        setSlotDate(e.target.value);
                                        setSlotId("");
                                    }}
                                    min={new Date().toISOString().split("T")[0]}
                                />
                            </div>

                            <div style={{ marginTop: 24, marginBottom: 24 }}>
                                <label className="form-label">Khung giờ trống (*)</label>
                                {loadingSlots ? (
                                    <div className="empty-state" style={{ padding: 24 }}>Đang tải khung giờ...</div>
                                ) : slots.length === 0 ? (
                                    <div className="empty-state" style={{ padding: 24 }}>
                                        Bác sĩ chưa có lịch trống trong ngày này. Vui lòng chọn ngày khác.
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
                                                {s.startTime.substring(0, 5)}
                                            </button>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div className="form-group">
                                <label className="form-label">Triệu chứng / Ghi chú</label>
                                <textarea
                                    className="form-textarea"
                                    rows={3}
                                    placeholder="Mô tả ngắn gọn triệu chứng hoặc lý do khám..."
                                    value={reason}
                                    onChange={e => setReason(e.target.value)}
                                />
                            </div>

                            <div className={styles.stepFooter}>
                                <button className="btn-secondary" onClick={() => setStep(2)}>
                                    <ArrowLeft size={18} style={{ marginRight: 8 }}/> Quay lại
                                </button>
                                <button className="btn-primary" onClick={() => setStep(4)} disabled={!slotDate || !slotId}>
                                    Tiếp tục <ArrowRight size={18} style={{ marginLeft: 8 }}/>
                                </button>
                            </div>
                        </div>
                    )}

                    {step === 4 && (
                        <div className={styles.stepContent}>
                            <div style={{ display: "flex", alignItems: "center", gap: "12px", marginBottom: "24px", color: "var(--c-primary)" }}>
                                <CheckCircle2 size={32} />
                                <h3 style={{ margin: 0, fontSize: "1.2rem" }}>Xác nhận thông tin đặt lịch</h3>
                            </div>

                            <div className={styles.confirmBox}>
                                <div className={styles.confirmRow}>
                                    <Stethoscope className={styles.confirmIcon} />
                                    <div>
                                        <div className={styles.confirmLabel}>Chuyên khoa</div>
                                        <div className={styles.confirmValue}>{selectedSpec?.specialtyName}</div>
                                    </div>
                                </div>
                                <div className={styles.confirmRow}>
                                    <User className={styles.confirmIcon} />
                                    <div>
                                        <div className={styles.confirmLabel}>Bác sĩ</div>
                                        <div className={styles.confirmValue}>{selectedDoc?.academicTitle ? selectedDoc.academicTitle + " " : ""}{selectedDoc?.fullName}</div>
                                    </div>
                                </div>
                                <div className={styles.confirmRow}>
                                    <Clock className={styles.confirmIcon} />
                                    <div>
                                        <div className={styles.confirmLabel}>Thời gian</div>
                                        <div className={styles.confirmValue}>
                                            {selectedSlot?.startTime.substring(0, 5)} - {selectedSlot?.endTime.substring(0, 5)}<br/>
                                            Ngày {slotDate.split("-").reverse().join("/")}
                                        </div>
                                    </div>
                                </div>
                                {reason && (
                                    <div className={styles.confirmRow}>
                                        <FileText className={styles.confirmIcon} />
                                        <div>
                                            <div className={styles.confirmLabel}>Ghi chú / Triệu chứng</div>
                                            <div className={styles.confirmValue} style={{ fontStyle: 'italic' }}>"{reason}"</div>
                                        </div>
                                    </div>
                                )}
                            </div>

                            <div className={styles.stepFooter}>
                                <button className="btn-secondary" onClick={() => setStep(3)} disabled={submitting}>
                                    <ArrowLeft size={18} style={{ marginRight: 8 }}/> Quay lại
                                </button>
                                <button className="btn-primary" onClick={handleConfirm} disabled={submitting}>
                                    {submitting ? "Đang xử lý..." : "Xác nhận đặt lịch"}
                                </button>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};
