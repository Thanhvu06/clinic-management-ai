import React, { useState, useRef, useEffect } from "react";
import { createPortal } from "react-dom";
import { useNavigate, useLocation } from "react-router-dom";
import { useChatContext } from "../contexts/ChatContext";
import { useAiBookingFlow } from "../hooks/useAiBookingFlow";
import { getRoleDashboardPath } from "../utils/roleRoutes";
import styles from "./MedicalChatWidget.module.css";
import {
    MessageCircle, X, Trash2, Send, AlertTriangle, ArrowRight,
    Minus, Stethoscope, Calendar, Clock, CheckCircle2, Phone,
    FileText, Activity, CreditCard
} from "lucide-react";
import { useAuth } from "../auth/AuthContext";
import type { AiAction, AiChatIntent, AiToolExecutionResult } from "../types/ai";
import SafeMarkdown from "./SafeMarkdown";

const QUICK_PROMPTS: Array<{ label: string; intent?: AiChatIntent }> = [
    { label: "Tôi nên khám chuyên khoa nào?" },
    { label: "Tìm lịch khám sớm nhất.", intent: "FindEarliestAvailableSlot" },
    { label: "Xem lịch hẹn của tôi." },
    { label: "Xem kết quả cận lâm sàng." },
    { label: "Liên hệ lễ tân." }
];

const asRecord = (value: unknown): Record<string, unknown> =>
    value && typeof value === "object" ? value as Record<string, unknown> : {};

const asRecords = (value: unknown): Array<Record<string, unknown>> =>
    Array.isArray(value) ? value.filter(item => item && typeof item === "object") as Array<Record<string, unknown>> : [];

const formatMoney = (value: unknown): string =>
    typeof value === "number" ? `${value.toLocaleString("vi-VN")} VND` : "Chưa công bố";

const GroundedToolData: React.FC<{ result: AiToolExecutionResult }> = ({ result }) => {
    if (result.status !== "completed" || !result.resultType || !result.data) return null;
    const data = asRecord(result.data);
    let rows: Array<{ key: string; text: string }> = [];
    switch (result.resultType) {
        case "specialties":
            rows = asRecords(result.data).map(item => ({ key: String(item.id), text: `${String(item.name ?? "")}${item.code ? ` (${String(item.code)})` : ""}` }));
            break;
        case "doctors":
            rows = asRecords(result.data).map(item => ({ key: String(item.id), text: `${String(item.academicTitle ?? "")} ${String(item.name ?? "")}`.trim() }));
            break;
        case "available_slots":
            rows = asRecords(result.data).map((item, index) => ({ key: String(item.slotId ?? index), text: `${String(item.slotDate ?? "")} · ${String(item.startTime ?? "")} - ${String(item.endTime ?? "")}` }));
            break;
        case "facilities":
            rows = asRecords(result.data).map(item => ({ key: String(item.id), text: `${String(item.name ?? "")} · ${String(item.address ?? item.city ?? "")}` }));
            break;
        case "pricing_catalog":
            rows = [
                ...asRecords(data.consultation).map(item => ({ key: `c-${String(item.specialtyId)}`, text: `${String(item.specialty ?? "")}: ${formatMoney(item.consultationFee)}` })),
                ...asRecords(data.diagnostics).map(item => ({ key: `d-${String(item.serviceId)}`, text: `${String(item.name ?? "")}: ${formatMoney(item.price)}` }))
            ];
            break;
        case "appointments":
            rows = asRecords(data.items).map(item => ({ key: String(item.id ?? item.appointmentId), text: `${String(item.appointmentCode ?? item.id ?? "Lịch hẹn")} · ${String(item.slotDate ?? item.appointmentDate ?? "")}` }));
            break;
        case "appointment_detail":
            rows = [{ key: "detail", text: `${String(data.appointmentCode ?? data.id ?? "Lịch hẹn")} · ${String(data.status ?? "")}` }];
            break;
        case "pending_action":
            rows = [{ key: "pending", text: `Lịch hẹn ${String(data.appointmentCode ?? data.appointmentId ?? "")} · chờ xác nhận` }];
            break;
        case "change_request":
            rows = [{ key: "change", text: `Yêu cầu thay đổi #${String(data.changeRequestId ?? "")} đã được tạo` }];
            break;
    }
    if (rows.length === 0) return null;
    return (
        <div className={styles.specialtyReason} role="list" aria-label="Dữ liệu đã kiểm chứng">
            {rows.slice(0, 10).map(row => <div key={row.key} role="listitem">• {row.text}</div>)}
        </div>
    );
};

const PatientMedicalChatWidget: React.FC = () => {
    const [isOpen, setIsOpen] = useState(false);
    const [executingActionId, setExecutingActionId] = useState<string | null>(null);
    const launcherRef = useRef<HTMLButtonElement>(null);
    const chatWindowRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLTextAreaElement>(null);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();
    const { setPendingSpecialtyId, aiAssistantStatus } = useChatContext();

    const {
        input,
        setInput,
        loading,
        submittingBooking,
        errorMsg,
        messages,
        activeDraft,
        clearChat,
        handleSendMessage,
        handleActionClick,
        confirmToolAction,
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
                            <span className={`${styles.statusPill} ${
                                aiAssistantStatus === "Degraded" || aiAssistantStatus === "Unchecked"
                                    ? styles.statusPillDegraded
                                    : aiAssistantStatus === "Offline"
                                    ? styles.statusPillOffline
                                    : styles.statusPillOnline
                            }`}>
                                <span className={`${styles.statusDot} ${
                                    aiAssistantStatus === "Degraded" || aiAssistantStatus === "Unchecked"
                                        ? styles.statusDotDegraded
                                        : aiAssistantStatus === "Offline"
                                        ? styles.statusDotOffline
                                        : styles.statusDotOnline
                                }`} />
                                {aiAssistantStatus === "Degraded"
                                    ? "Chế độ rút gọn"
                                    : aiAssistantStatus === "Offline"
                                    ? "Ngoại tuyến"
                                    : aiAssistantStatus === "Unchecked"
                                    ? "Chưa kiểm tra AI"
                                    : "Trực tuyến"}
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
                                    <SafeMarkdown content={msg.content} />

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

                                    {msg.toolResults?.map((toolResult, toolIndex) => (
                                        <div key={`${toolResult.actionId ?? "tool"}-${toolIndex}`} className={styles.cardContainer}>
                                            <div className={styles.bookingSummaryCard} role="status" aria-label="Trạng thái thao tác AI">
                                                <h4 className={styles.bookingSummaryTitle}>
                                                    <CheckCircle2 size={18} color={toolResult.status === "failed" ? "#b91c1c" : "#0d9488"} />
                                                    {toolResult.status === "pending_confirmation" ? "Đang chờ xác nhận" : toolResult.status === "completed" ? "Dữ liệu từ hệ thống ClinicCare" : "Không thể thực hiện thao tác"}
                                                </h4>
                                                <p className={styles.specialtyReason}>
                                                    {toolResult.status === "pending_confirmation"
                                                        ? "Thao tác ghi chưa được thực hiện. Hãy kiểm tra thông tin và xác nhận trong luồng lịch hẹn."
                                                        : toolResult.error?.message || toolResult.displayText || "Kết quả được trả về từ dịch vụ ClinicCare đã kiểm chứng."}
                                                </p>
                                                <GroundedToolData result={toolResult} />
                                                {toolResult.status === "pending_confirmation" && toolResult.actionId && (
                                                    <button
                                                        type="button"
                                                        className={`${styles.actionBtn} ${styles.btnPrimary}`}
                                                        disabled={executingActionId === toolResult.actionId}
                                                        onClick={() => {
                                                            setExecutingActionId(toolResult.actionId || null);
                                                            const token = typeof toolResult.data?.concurrencyToken === "string" ? toolResult.data.concurrencyToken : undefined;
                                                            void confirmToolAction(toolResult.actionId || "", token).finally(() => setExecutingActionId(null));
                                                        }}
                                                    >
                                                        {executingActionId === toolResult.actionId ? "Đang xác nhận..." : "Xác nhận thực hiện"}
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                    ))}

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

                                                const isBooking = ["SelectDoctor", "SelectSlot", "ConfirmBooking", "ReviewBooking", "ChangePreferredDate"].includes(act.type);
                                                const rawActVersion = act.draftVersion ?? (act.payload as Record<string, unknown>)?.draftVersion;
                                                const hasValidActVer = typeof rawActVersion === "number" && Number.isInteger(rawActVersion) && rawActVersion >= 1;
                                                const activeVer = (typeof activeDraft?.version === "number" && Number.isInteger(activeDraft.version) && activeDraft.version >= 1)
                                                    ? activeDraft.version
                                                    : null;

                                                const actConfirmationId = (act.payload as Record<string, unknown>)?.confirmationId;
                                                const isStale = isBooking && (
                                                    (["ConfirmBooking", "ReviewBooking"].includes(act.type) && !activeDraft) ||
                                                    (act.type === "ConfirmBooking" && Boolean(actConfirmationId) && Boolean(activeDraft?.confirmationId) && actConfirmationId !== activeDraft?.confirmationId) ||
                                                    (hasValidActVer && activeVer !== null && (rawActVersion as number) < activeVer) ||
                                                    (!hasValidActVer && activeVer !== null && activeVer > 1)
                                                );

                                                const btnClass = `${
                                                    isDanger
                                                        ? `${styles.actionBtn} ${styles.btnDanger}`
                                                        : isPrimary
                                                            ? `${styles.actionBtn} ${styles.btnPrimary}`
                                                            : `${styles.actionBtn} ${styles.btnSecondary}`
                                                } ${isStale ? styles.btnStale : ""}`;

                                                return (
                                                    <button
                                                        key={act.id}
                                                        type="button"
                                                        className={btnClass}
                                                        disabled={submittingBooking || executingActionId !== null}
                                                        data-stale={isStale ? "true" : undefined}
                                                        aria-disabled={isStale ? "true" : undefined}
                                                        title={isStale ? "Lựa chọn đã cũ" : undefined}
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
                                                        {isStale && <span className={styles.staleTag}> (Lựa chọn đã cũ)</span>}
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

const GuestMedicalChatNoticeWidget: React.FC = () => {
    const [isOpen, setIsOpen] = useState(false);
    const navigate = useNavigate();
    const location = useLocation();

    const widgetContent = (
        <div className={styles.widgetContainer}>
            {!isOpen && (
                <button
                    type="button"
                    className={styles.launcher}
                    onClick={() => setIsOpen(true)}
                    aria-label="Mở Trợ lý ClinicCare AI"
                >
                    <MessageCircle size={28} />
                </button>
            )}

            {isOpen && (
                <div className={styles.chatWindow} role="dialog" aria-modal="true" aria-label="Trợ lý ClinicCare AI">
                    <div className={styles.header}>
                        <div className={styles.headerTitle}>
                            <Stethoscope size={22} />
                            <span>ClinicCare AI</span>
                        </div>
                        <div className={styles.headerActions}>
                            <button
                                type="button"
                                className={styles.iconBtn}
                                onClick={() => setIsOpen(false)}
                                aria-label="Đóng cửa sổ chat"
                            >
                                <X size={20} />
                            </button>
                        </div>
                    </div>

                    <div className={styles.noticeCard}>
                        <div style={{ width: 48, height: 48, borderRadius: "50%", backgroundColor: "#ccfbf1", display: "flex", alignItems: "center", justifyContent: "center", color: "#0d9488" }}>
                            <Stethoscope size={28} />
                        </div>
                        <h3 className={styles.noticeTitle}>Chào mừng bạn đến với ClinicCare AI</h3>
                        <p className={styles.noticeText}>
                            Trợ lý y tế thông minh hỗ trợ giải đáp thắc mắc sức khỏe và đặt lịch khám tiện lợi cho Bệnh nhân.
                        </p>
                        <p className={styles.noticeText} style={{ fontSize: "0.85rem", color: "#94a3b8" }}>
                            Vui lòng đăng nhập tài khoản Bệnh nhân để bắt đầu trò chuyện và đặt lịch khám bệnh trực tiếp.
                        </p>
                        <button
                            type="button"
                            className={styles.noticeBtnPrimary}
                            onClick={() => navigate(`/login?returnUrl=${encodeURIComponent(location.pathname)}`)}
                        >
                            Đăng nhập tài khoản Bệnh nhân
                        </button>
                        <button
                            type="button"
                            className={styles.noticeBtnSecondary}
                            onClick={() => navigate('/register')}
                        >
                            Đăng ký tài khoản mới
                        </button>
                    </div>
                </div>
            )}
        </div>
    );

    return createPortal(widgetContent, document.body);
};

const StaffMedicalChatNoticeWidget: React.FC<{ user: { fullName?: string; role: string } }> = ({ user }) => {
    const [isOpen, setIsOpen] = useState(false);
    const navigate = useNavigate();

    const widgetContent = (
        <div className={styles.widgetContainer}>
            {!isOpen && (
                <button
                    type="button"
                    className={styles.launcher}
                    onClick={() => setIsOpen(true)}
                    aria-label="Mở thông tin Trợ lý ClinicCare AI"
                >
                    <MessageCircle size={28} />
                </button>
            )}

            {isOpen && (
                <div className={styles.chatWindow} role="dialog" aria-modal="true" aria-label="Thông tin Trợ lý ClinicCare AI">
                    <div className={styles.header}>
                        <div className={styles.headerTitle}>
                            <Stethoscope size={22} />
                            <span>ClinicCare AI</span>
                        </div>
                        <div className={styles.headerActions}>
                            <button
                                type="button"
                                className={styles.iconBtn}
                                onClick={() => setIsOpen(false)}
                                aria-label="Đóng cửa sổ"
                            >
                                <X size={20} />
                            </button>
                        </div>
                    </div>

                    <div className={styles.noticeCard}>
                        <div style={{ width: 48, height: 48, borderRadius: "50%", backgroundColor: "#e0f2fe", display: "flex", alignItems: "center", justifyContent: "center", color: "#0284c7" }}>
                            <Stethoscope size={28} />
                        </div>
                        <h3 className={styles.noticeTitle}>Trợ lý AI dành riêng cho Bệnh nhân</h3>
                        <p className={styles.noticeText}>
                            Bạn đang đăng nhập bằng tài khoản nhân viên y tế: <strong>{user.fullName || user.role}</strong> (Vai trò: {user.role}).
                        </p>
                        <p className={styles.noticeText} style={{ fontSize: "0.85rem", color: "#94a3b8" }}>
                            Khu vực tư vấn và đặt lịch khám AI trực tuyến phục vụ người bệnh. Nhân viên y tế vui lòng thao tác trên Không gian làm việc chuyên môn.
                        </p>
                        <button
                            type="button"
                            className={styles.noticeBtnPrimary}
                            onClick={() => navigate(getRoleDashboardPath(user.role))}
                        >
                            Về Không gian làm việc ({user.role})
                        </button>
                        <button
                            type="button"
                            className={styles.noticeBtnSecondary}
                            onClick={() => setIsOpen(false)}
                        >
                            Đóng thông báo
                        </button>
                    </div>
                </div>
            )}
        </div>
    );

    return createPortal(widgetContent, document.body);
};

export const MedicalChatWidget: React.FC = () => {
    const { user, isAuthenticated } = useAuth();
    const location = useLocation();

    // Do not show floating widget on staff dashboard workspaces (doctor, reception, admin, pharmacy, diagnostics)
    const isStaffWorkspace = location.pathname.startsWith('/doctor') ||
                             location.pathname.startsWith('/reception') ||
                             location.pathname.startsWith('/admin') ||
                             location.pathname.startsWith('/pharmacy') ||
                             location.pathname.startsWith('/diagnostics');

    if (isStaffWorkspace) return null;

    if (isAuthenticated && user?.role === 'Patient') {
        return <PatientMedicalChatWidget />;
    }

    if (isAuthenticated && user && user.role !== 'Patient') {
        return <StaffMedicalChatNoticeWidget user={user} />;
    }

    return <GuestMedicalChatNoticeWidget />;
};
