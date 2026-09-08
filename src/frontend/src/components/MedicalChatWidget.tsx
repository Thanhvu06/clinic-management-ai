import React, { useState, useRef, useEffect } from "react";
import { createPortal } from "react-dom";
import { useNavigate } from "react-router-dom";
import { useChatContext } from "../contexts/ChatContext";
import axiosClient from "../api/axiosClient";
import styles from "./MedicalChatWidget.module.css";
import {
    MessageCircle, X, Trash2, Send, AlertTriangle, ArrowRight,
    Minus, Stethoscope, Calendar, Clock, CheckCircle2, Phone,
    FileText, Activity, CreditCard
} from "lucide-react";
import type { ApiResponse } from "../types";
import type { ChatMessage, AiChatResponse, AiAction } from "../types/ai";
import { useAuth } from "../auth/AuthContext";

const QUICK_PROMPTS = [
    "Tôi nên khám chuyên khoa nào?",
    "Tìm lịch khám sớm nhất.",
    "Xem lịch hẹn của tôi.",
    "Xem kết quả cận lâm sàng.",
    "Liên hệ lễ tân."
];

const PatientMedicalChatWidget: React.FC = () => {
    const [isOpen, setIsOpen] = useState(false);
    const { messages, setMessages, clearChat, setPendingSpecialtyId } = useChatContext();
    const [input, setInput] = useState("");
    const [loading, setLoading] = useState(false);
    const [submittingBooking, setSubmittingBooking] = useState(false);
    const [errorMsg, setErrorMsg] = useState("");

    const messagesEndRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();

    useEffect(() => {
        if (isOpen) {
            messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
        }
    }, [messages, isOpen]);

    useEffect(() => {
        const handleEsc = (e: KeyboardEvent) => {
            if (e.key === "Escape" && isOpen) {
                setIsOpen(false);
            }
        };
        window.addEventListener("keydown", handleEsc);
        return () => window.removeEventListener("keydown", handleEsc);
    }, [isOpen]);

    const handleSendMessage = async (
        textToSend: string,
        pendingPayload?: { specialtyId?: number; doctorId?: number; slotId?: number; slotDate?: string },
        prefixMessage?: ChatMessage
    ) => {
        const trimmed = textToSend.trim();
        if (!trimmed || loading) return;
        if (trimmed.length > 500) {
            setErrorMsg("Tin nhắn quá dài (tối đa 500 ký tự).");
            return;
        }

        const userMsg: ChatMessage = { role: "user", content: trimmed };
        const newMessages = prefixMessage
            ? [...messages, prefixMessage, userMsg]
            : [...messages, userMsg];
        const historyMessages = newMessages
            .slice(-9, -1)
            .map(m => ({ role: m.role, content: m.content }));

        setMessages(newMessages);
        setInput("");
        setErrorMsg("");
        setLoading(true);

        try {
            const requestBody = {
                message: trimmed,
                context: historyMessages,
                pendingSpecialtyId: pendingPayload?.specialtyId,
                pendingDoctorId: pendingPayload?.doctorId,
                pendingSlotId: pendingPayload?.slotId,
                pendingSlotDate: pendingPayload?.slotDate
            };

            const res = await axiosClient.post<any, ApiResponse<AiChatResponse>>("/ai/chat", requestBody);

            if (res.success && res.data) {
                const data = res.data;
                const aiMsg: ChatMessage = {
                    role: "model",
                    content: data.message || data.reply || "",
                    urgency: data.urgency,
                    safetyNotice: data.safetyNotice,
                    suggestions: data.specialtySuggestions || data.suggestedSpecialties || [],
                    actions: data.actions || [],
                    bookingDraft: data.bookingDraft,
                    missingFields: data.missingFields || []
                };
                setMessages(prev => [...prev, aiMsg]);
            } else {
                throw new Error("Invalid response");
            }
        } catch (err: any) {
            if (err?.errorCode === "TOO_MANY_REQUESTS" || err?.message?.includes("quá nhiều")) {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút.",
                    urgency: "ROUTINE"
                }]);
            } else {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Xin lỗi, hệ thống AI đang bận hoặc gặp sự cố kết nối. Bạn có thể chọn chuyên khoa và đặt lịch trực tiếp qua trang Đặt lịch khám.",
                    urgency: "ROUTINE",
                    actions: [
                        {
                            id: "act-fallback-book",
                            type: "StartBooking",
                            label: "Mở trang Đặt lịch khám",
                            style: "primary",
                            requiresAuthentication: false,
                            requiresConfirmation: false,
                            payload: { targetUrl: "/patient/book" }
                        }
                    ]
                }]);
            }
        } finally {
            setLoading(false);
        }
    };

    const handleActionClick = async (action: AiAction) => {
        const payload = action.payload;

        // Navigation Actions
        if (payload.targetUrl) {
            if (payload.targetUrl.startsWith("tel:")) {
                window.location.href = payload.targetUrl;
                return;
            }
            navigate(payload.targetUrl);
            setIsOpen(false);
            return;
        }

        // View Specialty
        if (action.type === "ViewSpecialty" && payload.specialtyId) {
            navigate(`/patient/book?specialtyId=${payload.specialtyId}`);
            setIsOpen(false);
            return;
        }

        // Select Doctor
        if (action.type === "SelectDoctor" && payload.doctorId) {
            await handleSendMessage(
                `Tôi muốn đặt khám với bác sĩ ${payload.doctorName || ""}`,
                { specialtyId: payload.specialtyId, doctorId: payload.doctorId, slotDate: payload.slotDate }
            );
            return;
        }

        // Select Slot
        if (action.type === "SelectSlot" && payload.slotId) {
            await handleSendMessage(
                `Tôi chọn khung giờ ${payload.startTime} ngày ${payload.slotDate}`,
                { specialtyId: payload.specialtyId, doctorId: payload.doctorId, slotId: payload.slotId, slotDate: payload.slotDate }
            );
            return;
        }

        // Confirm Booking
        if (action.type === "ConfirmBooking" && payload.slotId && payload.doctorId && payload.specialtyId) {
            if (submittingBooking) return;
            setSubmittingBooking(true);
            try {
                const bookRes = await axiosClient.post<any, ApiResponse<any>>("/appointments", {
                    doctorId: payload.doctorId,
                    specialtyId: payload.specialtyId,
                    appointmentSlotId: payload.slotId,
                    reason: payload.reason || "Đặt lịch qua Trợ lý ClinicCare AI"
                });

                if (bookRes.success && bookRes.data) {
                    const apt = bookRes.data;
                    const successMsg: ChatMessage = {
                        role: "model",
                        content: `🎉 Đặt lịch khám thành công!\n- Mã cuộc hẹn: ${apt.appointmentCode || apt.id}\n- Bác sĩ: ${payload.doctorName}\n- Thời gian: ${payload.startTime} ngày ${payload.slotDate}\n\nBạn có thể theo dõi cuộc hẹn tại danh sách lịch khám.`,
                        actions: [
                            {
                                id: "act-view-created-apt",
                                type: "ViewMyAppointments",
                                label: "Xem lịch hẹn của tôi",
                                style: "primary",
                                requiresAuthentication: true,
                                requiresConfirmation: false,
                                payload: { targetUrl: "/patient/appointments" }
                            }
                        ]
                    };
                    setMessages(prev => [...prev, successMsg]);
                }
            } catch (err: any) {
                const errorCode = err?.response?.data?.errorCode || err?.errorCode;
                if (errorCode === "SLOT_ALREADY_BOOKED") {
                    const conflictNotice: ChatMessage = {
                        role: "model",
                        content: "⚠️ Rất tiếc, khung giờ này vừa có bệnh nhân khác đặt trước. Bạn vui lòng chọn một khung giờ khác nhé.",
                        urgency: "ROUTINE"
                    };
                    await handleSendMessage(
                        `Xem các lịch trống khác của bác sĩ ${payload.doctorName}`,
                        { specialtyId: payload.specialtyId, doctorId: payload.doctorId, slotDate: payload.slotDate },
                        conflictNotice
                    );
                } else {
                    const errorNotice = err?.response?.data?.message || err?.message || "Đặt lịch không thành công. Vui lòng thử lại.";
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: `⚠️ ${errorNotice}`,
                        urgency: "ROUTINE"
                    }]);
                }
            } finally {
                setSubmittingBooking(false);
            }
            return;
        }

        // Manual Specialty Selection
        if (action.type === "ManualSpecialtySelection") {
            navigate("/patient/book");
            setIsOpen(false);
        }
    };

    const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
        if (e.nativeEvent.isComposing) return;
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            e.stopPropagation();
            handleSendMessage(input);
        }
    };

    const widgetContent = (
        <div className={styles.widgetContainer}>
            {!isOpen && (
                <button
                    className={styles.launcher}
                    onClick={() => setIsOpen(true)}
                    aria-label="Mở Trợ lý ClinicCare AI"
                    type="button"
                >
                    <MessageCircle size={28} />
                </button>
            )}

            {isOpen && (
                <div className={styles.chatWindow} role="dialog" aria-label="Cửa sổ trò chuyện ClinicCare AI">
                    <div className={styles.header}>
                        <div className={styles.headerTitle}>
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
                                onClick={() => setIsOpen(false)}
                                title="Thu nhỏ"
                                aria-label="Thu nhỏ"
                            >
                                <Minus size={18} />
                            </button>
                            <button
                                type="button"
                                className={styles.iconBtn}
                                onClick={() => setIsOpen(false)}
                                title="Đóng"
                                aria-label="Đóng"
                            >
                                <X size={18} />
                            </button>
                        </div>
                    </div>

                    <div className={styles.messageArea}>
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
                                                        <span className={styles.summaryValue}>{msg.bookingDraft.slotDate}</span>
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
                            className={styles.textarea}
                            placeholder="Mô tả triệu chứng hoặc đặt câu hỏi..."
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            onKeyDown={handleKeyDown}
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