import React, { useRef, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useChatContext } from "../../contexts/ChatContext";
import { useAiBookingFlow } from "../../hooks/useAiBookingFlow";
import styles from "../../components/MedicalChatWidget.module.css";
import {
    Trash2, Send, AlertTriangle, ArrowRight,
    Stethoscope, Calendar, Clock, CheckCircle2, Phone,
    FileText, Activity, CreditCard
} from "lucide-react";
import { Breadcrumb } from "../../components/Breadcrumb";

const QUICK_PROMPTS = [
    "Tôi nên khám chuyên khoa nào?",
    "Tìm lịch khám sớm nhất.",
    "Xem lịch hẹn của tôi.",
    "Xem kết quả cận lâm sàng.",
    "Liên hệ lễ tân."
];

export const PatientAiConsultation: React.FC = () => {
    const { setPendingSpecialtyId } = useChatContext();
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();

    const {
        input,
        setInput,
        loading,
        submittingBooking,
        errorMsg,
        messages,
        clearChat,
        handleSendMessage,
        handleActionClick,
        formatVietnameseDate
    } = useAiBookingFlow();

    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }, [messages, loading]);

    const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
        if (e.nativeEvent.isComposing) return;
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            e.stopPropagation();
            handleSendMessage(input);
        }
    };

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto', display: 'flex', flexDirection: 'column', height: 'calc(100vh - 120px)' }}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Tư vấn & Trợ lý AI' }
            ]} />

            <div className="card" style={{ flex: 1, display: 'flex', flexDirection: 'column', padding: 0, overflow: 'hidden' }}>
                <div style={{ padding: '16px 24px', backgroundColor: '#0f172a', color: 'white', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', fontSize: '1.2rem', fontWeight: 600 }}>
                        <Stethoscope size={24} color="#0d9488" />
                        ClinicCare AI Action Assistant
                        <span className={styles.statusPill}>
                            <span className={styles.statusDot} />
                            Trực tuyến
                        </span>
                    </div>
                    <button
                        type="button"
                        onClick={clearChat}
                        title="Xóa lịch sử và bắt đầu lại"
                        style={{ background: 'rgba(255,255,255,0.15)', border: 'none', color: 'white', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.85rem' }}
                    >
                        <Trash2 size={16} /> Làm mới
                    </button>
                </div>

                <div style={{ flex: 1, overflowY: 'auto', padding: '24px', backgroundColor: '#f8fafc', display: 'flex', flexDirection: 'column', gap: '16px' }} aria-live="polite">
                    {/* Welcome Disclaimer */}
                    <div className={styles.welcomeContainer}>
                        <div className={styles.disclaimerBadge}>
                            <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: 1 }} />
                            <span>Trợ lý hỗ trợ định hướng chuyên khoa và đặt lịch khám. Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.</span>
                        </div>

                        {messages.length <= 1 && (
                            <>
                                <p className={styles.quickPromptsTitle}>Gợi ý câu hỏi nhanh:</p>
                                <div className={styles.quickPrompts}>
                                    {QUICK_PROMPTS.map((qp, idx) => (
                                        <button
                                            key={idx}
                                            type="button"
                                            className={styles.quickPromptChip}
                                            onClick={() => handleSendMessage(qp)}
                                        >
                                            {qp}
                                        </button>
                                    ))}
                                </div>
                            </>
                        )}
                    </div>

                    {messages.map((msg, idx) => (
                        <div key={idx} className={`${styles.messageRow} ${msg.role === "user" ? styles.rowUser : styles.rowModel}`}>
                            <div className={`${styles.bubble} ${msg.role === "user" ? styles.bubbleUser : styles.bubbleModel}`}>
                                <div>{msg.content}</div>

                                {/* Emergency Card */}
                                {msg.urgency === "EMERGENCY" && (
                                    <div className={styles.emergencyCard}>
                                        <div className={styles.emergencyHeader}>
                                            <AlertTriangle size={20} />
                                            <span>CẢNH BÁO NGUY HIỂM</span>
                                        </div>
                                        <div>{msg.safetyNotice || "Dấu hiệu bạn mô tả có thể là tình huống khẩn cấp. Hãy gọi 115 hoặc đến cơ sở y tế gần nhất ngay lập tức."}</div>
                                        <a href="tel:115" className={styles.call115Btn}>
                                            <Phone size={18} />
                                            Gọi Cấp cứu 115 ngay
                                        </a>
                                    </div>
                                )}

                                {/* Specialty Suggestions */}
                                {msg.suggestions && msg.suggestions.length > 0 && msg.urgency !== "EMERGENCY" && (
                                    <div className={styles.cardContainer}>
                                        {msg.suggestions.map(s => (
                                            <div key={s.specialtyId} className={styles.specialtyCard}>
                                                <div className={styles.specialtyHeader}>
                                                    <h4 className={styles.specialtyTitle}>{s.specialtyName}</h4>
                                                    <span className={styles.fitBadge}>Phù hợp tham khảo</span>
                                                </div>
                                                <p className={styles.specialtyReason}>{s.reason}</p>
                                                <div style={{ display: "flex", gap: "8px" }}>
                                                    <button
                                                        type="button"
                                                        className={`${styles.actionBtn} ${styles.btnPrimary}`}
                                                        style={{ flex: 1 }}
                                                        onClick={() => handleSendMessage(`Tìm lịch khám khoa ${s.specialtyName}`, { specialtyId: s.specialtyId })}
                                                    >
                                                        Xem lịch khám <ArrowRight size={14} />
                                                    </button>
                                                    <button
                                                        type="button"
                                                        className={`${styles.actionBtn} ${styles.btnSecondary}`}
                                                        onClick={() => {
                                                            setPendingSpecialtyId(s.specialtyId);
                                                            navigate(`/patient/book?specialtyId=${s.specialtyId}`);
                                                        }}
                                                    >
                                                        Chi tiết khoa
                                                    </button>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}

                                {/* Booking Draft Summary Card */}
                                {msg.bookingDraft && msg.bookingDraft.isComplete && msg.urgency !== "EMERGENCY" && (
                                    <div className={styles.cardContainer}>
                                        <div className={styles.bookingSummaryCard}>
                                            <h4 className={styles.bookingSummaryTitle}>
                                                <Calendar size={18} color="#0d9488" />
                                                Tóm tắt thông tin đặt lịch
                                            </h4>
                                            <div className={styles.summaryTable}>
                                                <div className={styles.summaryRow}>
                                                    <span className={styles.summaryLabel}>Chuyên khoa:</span>
                                                    <span className={styles.summaryValue}>{msg.bookingDraft.specialtyName}</span>
                                                </div>
                                                <div className={styles.summaryRow}>
                                                    <span className={styles.summaryLabel}>Bác sĩ:</span>
                                                    <span className={styles.summaryValue}>{msg.bookingDraft.doctorName}</span>
                                                </div>
                                                <div className={styles.summaryRow}>
                                                    <span className={styles.summaryLabel}>Ngày khám:</span>
                                                    <span className={styles.summaryValue}>{formatVietnameseDate(msg.bookingDraft.slotDate)}</span>
                                                </div>
                                                <div className={styles.summaryRow}>
                                                    <span className={styles.summaryLabel}>Khung giờ:</span>
                                                    <span className={styles.summaryValue}>{msg.bookingDraft.startTime} - {msg.bookingDraft.endTime}</span>
                                                </div>
                                                <div className={styles.summaryRow}>
                                                    <span className={styles.summaryLabel}>Lý do khám:</span>
                                                    <span className={styles.summaryValue}>{msg.bookingDraft.reason}</span>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                )}

                                {/* Action Buttons */}
                                {msg.actions && msg.actions.length > 0 && msg.urgency !== "EMERGENCY" && (
                                    <div className={styles.cardContainer}>
                                        {msg.actions.map(act => {
                                            const isPrimary = act.style === "primary";
                                            const isDanger = act.style === "danger";
                                            const btnClass = isDanger
                                                ? `${styles.actionBtn} ${styles.btnDanger}`
                                                : isPrimary
                                                    ? `${styles.actionBtn} ${styles.btnPrimary}`
                                                    : `${styles.actionBtn} ${styles.btnSecondary}`;

                                            return (
                                                <button
                                                    key={act.id}
                                                    type="button"
                                                    className={btnClass}
                                                    disabled={submittingBooking}
                                                    onClick={() => handleActionClick(act)}
                                                >
                                                    {act.type === "ConfirmBooking" && <CheckCircle2 size={16} />}
                                                    {act.type === "SelectSlot" && <Clock size={16} />}
                                                    {act.type === "SelectDoctor" && <Stethoscope size={16} />}
                                                    {act.type === "ViewMyAppointments" && <Calendar size={16} />}
                                                    {act.type === "ViewDiagnosticResults" && <Activity size={16} />}
                                                    {act.type === "ViewPrescriptions" && <FileText size={16} />}
                                                    {act.type === "ViewBills" && <CreditCard size={16} />}
                                                    {submittingBooking && act.type === "ConfirmBooking" ? "Đang xử lý..." : act.label}
                                                </button>
                                            );
                                        })}
                                    </div>
                                )}
                            </div>
                        </div>
                    ))}

                    {loading && (
                        <div className={`${styles.messageRow} ${styles.rowModel}`}>
                            <div className={`${styles.bubble} ${styles.bubbleModel}`}>
                                <div style={{ display: "flex", gap: "6px", padding: "4px" }}>
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "#0d9488", animation: "pulse 1.4s infinite" }} />
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "#0d9488", animation: "pulse 1.4s infinite 0.2s" }} />
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "#0d9488", animation: "pulse 1.4s infinite 0.4s" }} />
                                </div>
                            </div>
                        </div>
                    )}
                    <div ref={messagesEndRef} />
                </div>

                <div style={{ padding: '20px 24px', backgroundColor: 'white', borderTop: '1px solid var(--c-border)' }}>
                    <div style={{ display: 'flex', gap: '12px', alignItems: 'center', backgroundColor: '#f1f5f9', padding: '8px 16px', borderRadius: '24px', border: '1px solid var(--c-border-light)' }}>
                        <textarea
                            style={{ flex: 1, border: 'none', background: 'transparent', outline: 'none', resize: 'none', padding: '8px 0', fontSize: '1rem', color: 'var(--c-text)', maxHeight: '120px', minHeight: '24px' }}
                            placeholder="Mô tả triệu chứng hoặc câu hỏi (VD: Tôi bị đau ngực âm ỉ, khó thở)..."
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            onKeyDown={handleKeyDown}
                            rows={1}
                            maxLength={500}
                            aria-label="Nội dung tin nhắn tư vấn AI"
                        />
                        <button
                            type="button"
                            onClick={() => handleSendMessage(input)}
                            disabled={!input.trim() || loading}
                            style={{ width: '40px', height: '40px', borderRadius: '50%', backgroundColor: input.trim() && !loading ? '#0d9488' : 'var(--c-border)', color: 'white', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', cursor: input.trim() && !loading ? 'pointer' : 'not-allowed', transition: 'background-color 0.2s' }}
                            aria-label="Gửi tin nhắn"
                        >
                            <Send size={18} />
                        </button>
                    </div>
                    {errorMsg && <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '8px', paddingLeft: '16px' }}>{errorMsg}</div>}
                </div>
            </div>

            <style>{`
                @keyframes pulse {
                    0%, 100% { opacity: 0.3; transform: scale(0.8); }
                    50% { opacity: 1; transform: scale(1.2); }
                }
            `}</style>
        </div>
    );
};

