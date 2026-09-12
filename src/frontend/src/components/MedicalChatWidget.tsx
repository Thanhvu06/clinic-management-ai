import React, { useState, useRef, useEffect } from "react";
import { createPortal } from "react-dom";
import { useNavigate } from "react-router-dom";
import { useChatContext } from "../contexts/ChatContext";
import { useAiBookingFlow } from "../hooks/useAiBookingFlow";
import styles from "./MedicalChatWidget.module.css";
import {
    MessageCircle, X, Trash2, Send, AlertTriangle, ArrowRight,
    Minus, Stethoscope, Calendar, Clock, CheckCircle2, Phone,
    FileText, Activity, CreditCard
} from "lucide-react";
import { useAuth } from "../auth/AuthContext";
import type { AiAction, AiChatIntent } from "../types/ai";

const QUICK_PROMPTS: Array<{ label: string; intent?: AiChatIntent }> = [
    { label: "Tôi nên khám chuyên khoa nào?" },
    { label: "Tìm lịch khám sớm nhất.", intent: "FindEarliestAvailableSlot" },
    { label: "Xem lịch hẹn của tôi." },
    { label: "Xem kết quả cận lâm sàng." },
    { label: "Liên hệ lễ tân." }
];

const PatientMedicalChatWidget: React.FC = () => {
    const [isOpen, setIsOpen] = useState(false);
    const [executingActionId, setExecutingActionId] = useState<string | null>(null);
    const launcherRef = useRef<HTMLButtonElement>(null);
    const chatWindowRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLTextAreaElement>(null);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();
    const { setPendingSpecialtyId } = useChatContext();

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
    } = useAiBookingFlow(() => setIsOpen(false));

    useEffect(() => {
        if (isOpen) {
            messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
            requestAnimationFrame(() => {
                inputRef.current?.focus();
            });
        }
    }, [messages, isOpen]);

    // Accessible keyboard handling: Escape and Tab Focus Trap
    useEffect(() => {
        if (!isOpen) return;

        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") {
                setIsOpen(false);
                setTimeout(() => {
                    launcherRef.current?.focus();
                }, 50);
                return;
            }

            if (e.key === "Tab") {
                const container = chatWindowRef.current;
                if (!container) return;

                const focusableElements = container.querySelectorAll<HTMLElement>(
                    'button:not([disabled]), textarea:not([disabled]), input:not([disabled]), a[href]:not([disabled]), [tabindex]:not([tabindex="-1"])'
                );
                if (focusableElements.length === 0) return;

                const firstElement = focusableElements[0];
                const lastElement = focusableElements[focusableElements.length - 1];

                if (e.shiftKey) {
                    if (document.activeElement === firstElement) {
                        e.preventDefault();
                        lastElement.focus();
                    }
                } else {
                    if (document.activeElement === lastElement) {
                        e.preventDefault();
                        firstElement.focus();
                    }
                }
            }
        };

        window.addEventListener("keydown", handleKeyDown);
        return () => window.removeEventListener("keydown", handleKeyDown);
    }, [isOpen]);

    const handleTextareaKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
        if (e.nativeEvent.isComposing) return;
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            e.stopPropagation();
            handleSendMessage(input);
        }
    };

    const onActionClick = async (act: AiAction) => {
        if (executingActionId || submittingBooking) return;
        setExecutingActionId(act.id);
        try {
            await handleActionClick(act);
        } finally {
            setExecutingActionId(null);
        }
    };

    const widgetContent = (
        <div className={styles.widgetContainer}>
            {!isOpen && (
                <button
                    ref={launcherRef}
                    className={styles.launcher}
                    onClick={() => setIsOpen(true)}
                    aria-label="Mở Trợ lý ClinicCare AI"
                    type="button"
                >
                    <MessageCircle size={28} />
                </button>
            )}

            {isOpen && (
                <div
                    ref={chatWindowRef}
                    className={styles.chatWindow}
                    role="dialog"
                    aria-modal="true"
                    aria-labelledby="cliniccare-chat-title"
                >
                    <div className={styles.header}>
                        <div className={styles.headerTitle} id="cliniccare-chat-title">
                            <Stethoscope size={22} />
                            <span>ClinicCare AI</span>
                            <span className={styles.statusPill}>
                                <span className={styles.statusDot} />
                                Trực tuyến
                            </span>
                        </div>
                        <div className={styles.headerActions}>
                            <button
                                type="button"
                                className={styles.iconBtn}
                                onClick={clearChat}
                                title="Bắt đầu cuộc trò chuyện mới"
                                aria-label="Làm mới cuộc trò chuyện"
                            >
                                <Trash2 size={17} />
                            </button>
                            <button
                                type="button"
                                className={styles.iconBtn}
                                onClick={() => {
                                    setIsOpen(false);
                                    setTimeout(() => launcherRef.current?.focus(), 50);
                                }}
                                title="Thu nhỏ"
                                aria-label="Thu nhỏ"
                            >
                                <Minus size={18} />
                            </button>
                            <button
                                type="button"
                                className={styles.iconBtn}
                                onClick={() => {
                                    setIsOpen(false);
                                    setTimeout(() => launcherRef.current?.focus(), 50);
                                }}
                                title="Đóng"
                                aria-label="Đóng"
                            >
                                <X size={18} />
                            </button>
                        </div>
                    </div>

                    <div className={styles.messageArea} aria-live="polite">
                        {/* Welcome Disclaimer on top */}
                        <div className={styles.welcomeContainer}>
                            <div className={styles.disclaimerBadge}>
                                <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: 1 }} />
                                <span>Trợ lý hỗ trợ định hướng chuyên khoa và đặt lịch khám. Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.</span>
                            </div>

                            {messages.length <= 1 && (
                                <>
                                    <p className={styles.quickPromptsTitle}>Gợi ý câu hỏi nhanh:</p>
                                    <div className={styles.quickPrompts}>
                                        {QUICK_PROMPTS.map((qp) => (
                                            <button
                                                key={qp.label}
                                                type="button"
                                                className={styles.quickPromptChip}
                                                onClick={() => handleSendMessage(qp.label, { intent: qp.intent })}
                                            >
                                                {qp.label}
                                            </button>
                                        ))}
                                    </div>
                                </>
                            )}
                        </div>

                        {messages.map((msg, idx) => (
                            <div
                                key={idx}
                                className={`${styles.messageRow} ${msg.role === "user" ? styles.rowUser : styles.rowModel}`}
                            >
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
                                                    <div style={{ display: "flex", gap: "6px" }}>
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
                                                                setIsOpen(false);
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
                                                        disabled={submittingBooking || executingActionId !== null}
                                                        onClick={() => onActionClick(act)}
                                                    >
                                                        {act.type === "ConfirmBooking" && <CheckCircle2 size={16} />}
                                                        {act.type === "SelectSlot" && <Clock size={16} />}
                                                        {act.type === "SelectDoctor" && <Stethoscope size={16} />}
                                                        {act.type === "ViewMyAppointments" && <Calendar size={16} />}
                                                        {act.type === "ViewDiagnosticResults" && <Activity size={16} />}
                                                        {act.type === "ViewPrescriptions" && <FileText size={16} />}
                                                        {act.type === "ViewBills" && <CreditCard size={16} />}
                                                        {executingActionId === act.id ? "Đang xử lý..." : (submittingBooking && act.type === "ConfirmBooking" ? "Đang xử lý..." : act.label)}
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
                                    <div style={{ display: "flex", gap: "5px", padding: "6px" }}>
                                        <div style={{ width: "7px", height: "7px", borderRadius: "50%", backgroundColor: "#0d9488", animation: "pulse 1.4s infinite" }} />
                                        <div style={{ width: "7px", height: "7px", borderRadius: "50%", backgroundColor: "#0d9488", animation: "pulse 1.4s infinite 0.2s" }} />
                                        <div style={{ width: "7px", height: "7px", borderRadius: "50%", backgroundColor: "#0d9488", animation: "pulse 1.4s infinite 0.4s" }} />
                                    </div>
                                </div>
                            </div>
                        )}
                        <div ref={messagesEndRef} />
                    </div>

                    <div className={styles.inputArea}>
                        <textarea
                            ref={inputRef}
                            className={styles.textarea}
                            placeholder="Mô tả triệu chứng hoặc đặt câu hỏi..."
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            onKeyDown={handleTextareaKeyDown}
                            rows={1}
                            maxLength={500}
                            aria-label="Nội dung tin nhắn gửi tới ClinicCare AI"
                        />
                        <button
                            type="button"
                            className={styles.sendBtn}
                            onClick={() => handleSendMessage(input)}
                            disabled={!input.trim() || loading}
                            aria-label="Gửi tin nhắn"
                        >
                            <Send size={18} />
                        </button>
                    </div>
                    {errorMsg && <div className={styles.errorText}>{errorMsg}</div>}
                </div>
            )}

            <style>{`
                @keyframes pulse {
                    0%, 100% { opacity: 0.3; transform: scale(0.8); }
                    50% { opacity: 1; transform: scale(1.2); }
                }
            `}</style>
        </div>
    );

    return createPortal(widgetContent, document.body);
};

export const MedicalChatWidget: React.FC = () => {
    const { user } = useAuth();
    if (user?.role !== "Patient") return null;
    return <PatientMedicalChatWidget />;
};
