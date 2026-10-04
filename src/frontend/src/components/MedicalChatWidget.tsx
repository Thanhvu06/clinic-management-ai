import { BookingSuccessCard } from "./BookingSuccessCard";
import { providerStateTone } from "./copilot/copilotConfig";
import { DEFAULT_AI_MESSAGE } from "../contexts/ChatContext";
import { BookingSummaryCard } from "./BookingSummaryCard";
import { BookingWizard } from "./BookingWizard";
import { BookingActionChoices } from "./BookingActionChoices";
import { ProviderStatus } from "./copilot/ProviderStatus";
import { patientSuggestions, isAdditionalCopy } from "./copilot/patientPresentation";
import React, { useState, useRef, useEffect } from "react";
import { createPortal } from "react-dom";
import { useNavigate, useLocation } from "react-router-dom";
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
import { aiToolErrorMessage } from '../api/aiErrorMessages';
import { SuggestionChips } from "./copilot/SuggestionChips";
import { useSuggestionMenu } from "./copilot/useSuggestionMenu";
import { renderCopilotCardData } from "./copilot/copilotDataRenderers";

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
    const [historyExpanded, setHistoryExpanded] = useState(false);
    const [hasNewMessages, setHasNewMessages] = useState(false);
    const messageAreaRef = useRef<HTMLDivElement>(null);
    const nearBottomRef = useRef(true);
    const wizardHistoryLimitRef = useRef<number | null>(null);
    const previousMessagesRef = useRef<typeof messages>([]);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const location = useLocation();
    const { user } = useAuth();

    const {
        input,
        setInput,
        loading,
        submittingBooking,
        errorMsg, retryLastRequest, canRetry, retryAfterSeconds,
        messages,
        activeDraft,
        clearChat,
        handleSendMessage,
        handleSuggestion,
        wizard,
        handleWizardStep,
        handleActionClick,
        confirmToolAction,
        cancelToolAction,
        formatVietnameseDate
    } = useAiBookingFlow(() => setIsOpen(false));
    const suggestionMenu = useSuggestionMenu({
        enabled: isOpen,
        role: user?.role,
        identityKey: user?.userId ?? (user?.id !== undefined ? String(user.id) : "anonymous"),
        currentRoute: location.pathname
    });
    const latestAssistantIndex = messages.map(message => message.role).lastIndexOf("model");
    const conversationEmpty = messages.length === 0 || messages.length === 1 && messages[0].content === DEFAULT_AI_MESSAGE.content;
    const emptySuggestions = messages.length <= 1;
    const activeSuggestions = patientSuggestions(messages[latestAssistantIndex], suggestionMenu, emptySuggestions);
    const suggestionsBusy = loading || submittingBooking || executingActionId !== null;
    const onSuggestion = (suggestion: Parameters<typeof handleSuggestion>[0]) => {
        if (suggestionsBusy) return;
        void handleSuggestion(suggestion);
    };

    const scrollToLatest = () => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'instant', block: 'end' });
        nearBottomRef.current = true;
        setHasNewMessages(false);
    };
    useEffect(() => {
        if (isOpen) { scrollToLatest(); inputRef.current?.focus(); }
    }, [isOpen]);
    useEffect(() => {
        const previous = previousMessagesRef.current;
        previousMessagesRef.current = messages;
        if (!isOpen || messages === previous || wizard) return;
        const ownMessage = messages.slice(previous.length).some(message => message.role === 'user');
        if (nearBottomRef.current || ownMessage) scrollToLatest();
        else if (messages.length > previous.length) setHasNewMessages(true);
        if (ownMessage) inputRef.current?.focus();
    }, [messages, isOpen, wizard]);

    useEffect(() => {
        if (!wizard) wizardHistoryLimitRef.current = null;
        else if (wizardHistoryLimitRef.current === null) wizardHistoryLimitRef.current = messages.length;
    }, [wizard, messages.length]);

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
            if (!loading && !submittingBooking) void handleSendMessage(input);
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

    const reviewIndex = wizard?.step === 'review' ? messages.findLastIndex(message => message.bookingDraft?.isComplete) : -1;
    const retryButton = () => canRetry && <button type="button" className={styles.textLink} disabled={loading || submittingBooking || retryAfterSeconds > 0} onClick={() => void retryLastRequest()}>{retryAfterSeconds > 0 ? `Thử lại sau ${retryAfterSeconds} giây` : 'Thử lại'}</button>;
    const isStaleAction = (act: AiAction) => {
        if (!['SelectDoctor', 'SelectSlot', 'ConfirmBooking', 'ReviewBooking', 'ChangePreferredDate'].includes(act.type)) return false;
        const payload = act.payload as Record<string, unknown>;
        const version = act.draftVersion ?? payload.draftVersion;
        return (['ConfirmBooking', 'ReviewBooking'].includes(act.type) && !activeDraft) ||
            (act.type === 'ConfirmBooking' && Boolean(payload.confirmationId) && Boolean(activeDraft?.confirmationId) && payload.confirmationId !== activeDraft?.confirmationId) ||
            (typeof version === 'number' && typeof activeDraft?.version === 'number' && version < activeDraft.version) ||
            (version === undefined && (activeDraft?.version ?? 1) > 1);
    };
    const history = messages.map((msg, idx) => ({ msg, idx })).filter(({ msg, idx }) => idx !== reviewIndex && !(idx === 0 && msg.content === DEFAULT_AI_MESSAGE.content));
    const wizardHistoryLimit = wizardHistoryLimitRef.current ?? messages.length;
    const priorHistory = wizard ? history.filter(({ idx }) => idx < (reviewIndex >= 0 ? reviewIndex : wizardHistoryLimit)) : history;
    const hiddenCount = wizard ? priorHistory.length : history.length > 6 ? Math.max(0, history.length - 4) : 0;
    const visibleHistory = historyExpanded ? priorHistory : wizard ? [] : history.slice(hiddenCount);
    const latestMessage = messages[latestAssistantIndex];
    const announcement = (errorMsg || latestMessage?.content || '').replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/[*_#`>]/g, '').slice(0, 200);
    const renderMessage = (msg: typeof messages[number], idx: number) => (
                            <div
                                key={idx}
                                data-review={wizard?.step === "review" && idx === latestAssistantIndex && msg.bookingDraft?.isComplete || undefined}
                                className={`${styles.messageRow} ${msg.role === "user" ? styles.rowUser : styles.rowModel}`}
                            >
                                <div data-chat-bubble className={`${styles.bubble} ${msg.role === "user" ? styles.bubbleUser : styles.bubbleModel}`}>
                                    {!msg.bookingResult && <SafeMarkdown content={idx === reviewIndex ? "" : msg.content} />}

                                    {/* Emergency Card */}
                                    {msg.urgency === "EMERGENCY" && (
                                        <div className={styles.emergencyCard}>
                                            <div className={styles.emergencyHeader}>
                                                <AlertTriangle size={20} />
                                                <span>CẢNH BÁO NGUY HIỂM</span>
                                            </div>
                                            {isAdditionalCopy(msg.safetyNotice || "Dấu hiệu bạn mô tả có thể là tình huống khẩn cấp. Hãy gọi 115 hoặc đến cơ sở y tế gần nhất ngay lập tức.", msg.content) && <div>{msg.safetyNotice || "Dấu hiệu bạn mô tả có thể là tình huống khẩn cấp. Hãy gọi 115 hoặc đến cơ sở y tế gần nhất ngay lập tức."}</div>}
                                            <a href="tel:115" className={styles.call115Btn}>
                                                <Phone size={18} />
                                                Gọi Cấp cứu 115 ngay
                                            </a>
                                        </div>
                                    )}

                                    {msg.toolResults?.map((toolResult, toolIndex) => toolResult.status === 'completed'
                                        ? <GroundedToolData key={toolIndex} result={toolResult} />
                                        : toolResult.status === 'failed' ? <p key={toolIndex} className={styles.toolError}>{aiToolErrorMessage(toolResult.error?.code, toolResult.error?.message)}</p> : toolResult.displayText && isAdditionalCopy(toolResult.displayText, msg.content) ? <p key={toolIndex}>{toolResult.displayText}</p> : null)}

                                    {msg.copilotCards?.map((card, cardIndex) => (
                                        <div key={`${card.type}-${cardIndex}`} className={styles.cardContainer}>
                                            <div className={styles.cardData} aria-label="Dữ liệu từ hệ thống ClinicCare">
                                                <h4 className={styles.bookingSummaryTitle}>
                                                    <CheckCircle2 size={18} color={card.data === null || card.data === undefined ? "#b91c1c" : "var(--chat-accent)"} />
                                                    {card.title}
                                                </h4>
                                                {isAdditionalCopy(card.description, msg.content) && <p className={styles.specialtyReason}>{card.description}</p>}
                                                {renderCopilotCardData(card)}
                                            </div>
                                        </div>
                                    ))}

                                    {msg.suggestions && msg.suggestions.length > 0 && msg.urgency !== 'EMERGENCY' && <div className={styles.cardContainer}>
                                        {msg.suggestions.map(s => <button type="button" key={s.specialtyId} className={styles.specialtyChoice}
                                            aria-label={`Xem lịch khám khoa ${s.specialtyName}`} disabled={loading || submittingBooking}
                                            onClick={() => void handleSendMessage(`Tìm lịch khám khoa ${s.specialtyName}`, { specialtyId: s.specialtyId })}>
                                            <span><strong>{s.specialtyName}</strong><small>{s.reason}</small></span><ArrowRight size={18} aria-hidden="true" />
                                        </button>)}
                                        <p className={styles.specialtyReason}>Chạm vào một khoa để xem lịch khám.</p>
                                    </div>}
                                    {msg.bookingResult && <BookingSuccessCard result={msg.bookingResult} onAction={act => void onActionClick(act)} busy={submittingBooking} />}

                                    {!msg.toolResults?.some(result => result.status === "pending_confirmation") && msg.bookingDraft && msg.bookingDraft.isComplete && msg.urgency !== "EMERGENCY" && (
    <BookingSummaryCard draft={msg.bookingDraft} formatVietnameseDate={formatVietnameseDate} />
)}

{msg.toolResults?.map((toolResult, toolIndex) => toolResult.status === 'pending_confirmation' && msg.toolResults?.findIndex(result => result.status === 'pending_confirmation') === toolIndex
                                                ? <div key={toolIndex} className={msg.urgency === 'EMERGENCY' ? styles.cardData : styles.bookingSummaryCard} role="status" aria-label="Trạng thái thao tác AI">
                                                    <h4 className={styles.bookingSummaryTitle}>Đang chờ xác nhận</h4>
                                                    <p className={styles.specialtyReason}>Thao tác ghi chưa được thực hiện. Hãy kiểm tra thông tin và xác nhận trong luồng lịch hẹn.</p>
                                                {toolResult.status === "pending_confirmation" && toolResult.actionId && (<>
                                                    <button
                                                        type="button"
                                                        className={`${styles.actionBtn} ${styles.btnPrimary}`}
                                                        disabled={
                                                            executingActionId === toolResult.actionId ||
                                                            typeof toolResult.data?.concurrencyToken !== "string" ||
                                                            toolResult.data.concurrencyToken.length === 0
                                                        }
                                                        onClick={() => {
                                                            const token = typeof toolResult.data?.concurrencyToken === "string"
                                                                ? toolResult.data.concurrencyToken
                                                                : undefined;
                                                            if (!token) return;
                                                            setExecutingActionId(toolResult.actionId || null);
                                                            void confirmToolAction(toolResult.actionId || "", token).finally(() => setExecutingActionId(null));
                                                        }}
                                                    >
                                                        {executingActionId === toolResult.actionId
                                                            ? "Đang xác nhận..."
                                                            : typeof toolResult.data?.concurrencyToken !== "string"
                                                                ? "Thiếu mã xác nhận"
                                                                : "Xác nhận thực hiện"}
                                                    </button>
                                                    <button
                                                        type="button"
                                                        className={`${styles.actionBtn} ${styles.btnSecondary}`}
                                                        disabled={executingActionId === toolResult.actionId}
                                                        onClick={() => {
                                                            setExecutingActionId(toolResult.actionId || null);
                                                            void cancelToolAction(toolResult.actionId || "").finally(() => setExecutingActionId(null));
                                                        }}
                                                    >
                                                        {executingActionId === toolResult.actionId ? "Đang hủy..." : "Hủy thao tác"}
                                                    </button>
                                                </>)}
                                                </div> : null)}

{/* Action Buttons */}
                                    {!msg.bookingResult && !msg.toolResults?.some(result => result.status === "pending_confirmation") && msg.actions && msg.actions.length > 0 && msg.urgency !== "EMERGENCY" && (
                                        <BookingActionChoices actions={msg.actions} latest={idx === latestAssistantIndex} isStale={isStaleAction} renderAction={act => {
                                                const isPrimary = act.style === "primary";
                                                const isDanger = act.style === "danger";

                                                const isStale = isStaleAction(act);

                                                const btnClass = `${
                                                    isDanger
                                                        ? `${styles.actionBtn} ${styles.btnDanger}`
                                                        : isPrimary
                                                            ? `${styles.actionBtn} ${styles.btnPrimary}`
                                                            : `${styles.actionBtn} ${styles.btnSecondary}`
                                                }`;

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

                                                    </button>
                                                );
                                            }} />
                                    )}
                                    {msg.isError && idx === latestAssistantIndex && retryButton()}
                                </div>
                            </div>

    );

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
                    data-chat-panel
                    role="dialog"
                    aria-modal="true"
                    aria-labelledby="cliniccare-chat-title"
                >
                    <div className={styles.header}>
                        <div className={styles.headerTitle} id="cliniccare-chat-title">
                            <Stethoscope size={22} />
                            <span>ClinicCare AI</span>
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

                    <ProviderStatus state={wizard ? 'NotCalled' : messages[latestAssistantIndex]?.providerState} error={errorMsg || (messages[latestAssistantIndex]?.assistantStatus === 'Offline' ? messages[latestAssistantIndex]?.content : undefined)} detail={messages[latestAssistantIndex]?.executionMode} />

                    {(latestMessage?.fallbackActive || providerStateTone(latestMessage?.providerState) === 'amber' || /quá nhiều|giới hạn|429|phản hồi quá lâu|chờ.*giây/i.test(errorMsg)) && <p className={styles.localHint}>Trợ lý đang ở chế độ nội bộ nên chỉ hiểu một số câu. Bạn dùng các gợi ý bên dưới để chắc chắn nhất.</p>}
                    <div className={styles.visuallyHidden} aria-live="polite" aria-atomic="true" data-chat-announcement>{announcement && <span key={announcement} role="note" aria-label={announcement} />}</div>
                    <div ref={messageAreaRef} className={styles.messageArea} data-chat-messages onScroll={() => {
                        const area = messageAreaRef.current;
                        if (area) { nearBottomRef.current = area.scrollHeight - area.scrollTop - area.clientHeight <= 80; if (nearBottomRef.current) setHasNewMessages(false); }
                    }}>
                        {!wizard && <div className={conversationEmpty ? styles.welcomeContainer : styles.welcomeLine}>
                            {conversationEmpty ? <><p>Xin chào{user?.fullName ? `, ${user.fullName}` : ''}! Mình có thể giúp bạn đặt lịch khám và tra cứu lịch hẹn, đơn thuốc, kết quả xét nghiệm, hóa đơn.</p>
                            <span>Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.</span></> : <p>Xin chào{user?.fullName ? `, ${user.fullName}` : ''}! Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.</p>}
                            {emptySuggestions && !activeSuggestions?.length && <div className={styles.quickPrompts}>{QUICK_PROMPTS.map(qp => <button key={qp.label} type="button" className={styles.quickPromptChip} disabled={suggestionsBusy} onClick={() => void handleSendMessage(qp.label, { intent: qp.intent })}>{qp.label}</button>)}</div>}
                        </div>}
                        {hiddenCount > 0 && <button type="button" className={styles.textLink} onClick={() => setHistoryExpanded(value => !value)}>{historyExpanded ? 'Thu gọn' : `Xem ${hiddenCount} tin nhắn trước`}</button>}
                        {visibleHistory.map(({ msg, idx }) => renderMessage(msg, idx))}
                    <BookingWizard state={wizard} busy={loading || submittingBooking} onStep={handleWizardStep} reviewContent={reviewIndex >= 0 ? <>{renderMessage(messages[reviewIndex], reviewIndex)}{history.filter(({ idx }) => idx > reviewIndex).map(({ msg, idx }) => renderMessage(msg, idx))}</> : undefined} />
                        {loading && (
                            <div className={`${styles.messageRow} ${styles.rowModel}`}>
                                <div className={`${styles.bubble} ${styles.bubbleModel}`}>
                                    <div role="status" aria-label="Trợ lý đang trả lời" style={{ display: "flex", gap: "5px", padding: "6px", alignItems: "center" }}>
                                        <span>Trợ lý đang trả lời…</span>
                                        <div style={{ width: "7px", height: "7px", borderRadius: "50%", backgroundColor: "var(--chat-accent)", animation: "pulse 1.4s infinite" }} />
                                        <div style={{ width: "7px", height: "7px", borderRadius: "50%", backgroundColor: "var(--chat-accent)", animation: "pulse 1.4s infinite 0.2s" }} />
                                        <div style={{ width: "7px", height: "7px", borderRadius: "50%", backgroundColor: "var(--chat-accent)", animation: "pulse 1.4s infinite 0.4s" }} />
                                    </div>
                                </div>
                            </div>
                        )}
                        {wizard && reviewIndex < 0 && history.filter(({ idx }) => idx >= wizardHistoryLimit).map(({ msg, idx }) => renderMessage(msg, idx))}
                        <div ref={messagesEndRef} />
                    </div>

                    {hasNewMessages && <button type="button" className={styles.newMessages} onClick={scrollToLatest}>Có tin nhắn mới</button>}
                    <div className={styles.composer} data-chat-composer>
                        <SuggestionChips variant={emptySuggestions && !wizard ? "grid" : "compact"} suggestions={activeSuggestions} disabled={suggestionsBusy} onSelect={onSuggestion} ariaLabel={emptySuggestions ? "Tra cứu nhanh dữ liệu của bạn" : "Gợi ý tiếp theo"} />
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
                            disabled={!input.trim() || loading || submittingBooking}
                            aria-label="Gửi tin nhắn"
                        >
                            <Send size={18} />
                        </button>
                    </div>
                    {errorMsg && <div className={styles.errorText}>{errorMsg}{retryButton()}</div>}
                    </div>
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
                        <div style={{ width: 48, height: 48, borderRadius: "50%", backgroundColor: "#ccfbf1", display: "flex", alignItems: "center", justifyContent: "center", color: "var(--chat-accent)" }}>
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
