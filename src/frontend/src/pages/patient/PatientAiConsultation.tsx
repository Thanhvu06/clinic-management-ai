import { BookingSummaryCard } from "../../components/BookingSummaryCard";
import { BookingSuccessCard } from "../../components/BookingSuccessCard";
import { BookingWizard } from "../../components/BookingWizard";
import { BookingActionChoices } from "../../components/BookingActionChoices";
import { ProviderStatus } from "../../components/copilot/ProviderStatus";
import { patientSuggestions, isAdditionalCopy } from "../../components/copilot/patientPresentation";
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
import { useAuth } from "../../auth/AuthContext";
import { useLocation } from "react-router-dom";
import { useSuggestionMenu } from "../../components/copilot/useSuggestionMenu";
import { SuggestionChips } from "../../components/copilot/SuggestionChips";
import { renderCopilotCardData } from "../../components/copilot/copilotDataRenderers";
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
        activeDraft,
        clearChat,
        handleSendMessage,
        handleSuggestion,
        wizard,
        handleWizardStep,
        handleActionClick,
        formatVietnameseDate
    } = useAiBookingFlow();

    const { user, identityVersion } = useAuth();
    const location = useLocation();
    const suggestionMenu = useSuggestionMenu({ enabled: true, role: user?.role, identityKey: `${user?.userId}:${identityVersion}`, currentRoute: location.pathname });
    const latestAssistantIndex = messages.map(message => message.role).lastIndexOf("model");
    const emptySuggestions = messages.length <= 1;
    const activeSuggestions = patientSuggestions(messages[latestAssistantIndex], suggestionMenu, emptySuggestions);

    useEffect(() => {
        if (!wizard) messagesEndRef.current?.scrollIntoView({ behavior: "instant", block: 'end' });
    }, [messages, loading, wizard]);

    const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
        if (e.nativeEvent.isComposing) return;
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            e.stopPropagation();
            handleSendMessage(input);
        }
    };

    const reviewIndex = wizard?.step === 'review' ? messages.findLastIndex(message => message.bookingDraft?.isComplete) : -1;
    const renderMessage = (msg: typeof messages[number], idx: number) => (
                        <div key={idx} data-review={wizard?.step === "review" && idx === latestAssistantIndex && msg.bookingDraft?.isComplete || undefined} className={`${styles.messageRow} ${msg.role === "user" ? styles.rowUser : styles.rowModel}`}>
                            <div data-chat-bubble className={`${styles.bubble} ${msg.role === "user" ? styles.bubbleUser : styles.bubbleModel}`}>
                                {idx !== reviewIndex && !msg.bookingResult && <div>{msg.content}</div>}
                                {msg.bookingResult && <BookingSuccessCard result={msg.bookingResult} onAction={action => void handleActionClick(action)} busy={submittingBooking} />}

                                {msg.copilotCards?.map((card, cardIndex) => <article className={styles.bookingSummaryCard} key={`${card.type}-${cardIndex}`}><h4>{card.title}</h4>{isAdditionalCopy(card.description, msg.content) && <p>{card.description}</p>}{renderCopilotCardData(card)}</article>)}

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

                                {msg.bookingDraft && msg.bookingDraft.isComplete && msg.urgency !== "EMERGENCY" && (
    <BookingSummaryCard draft={msg.bookingDraft} formatVietnameseDate={formatVietnameseDate} />
)}

{/* Action Buttons */}
                                {msg.actions && msg.actions.length > 0 && msg.urgency !== "EMERGENCY" && (
                                    <BookingActionChoices actions={msg.actions} latest={idx === latestAssistantIndex} renderAction={act => {
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
                                                    disabled={submittingBooking}
                                                    data-stale={isStale ? "true" : undefined}
                                                    aria-disabled={isStale ? "true" : undefined}
                                                    title={isStale ? "Lựa chọn đã cũ" : undefined}
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
                                                    {isStale && <span className={styles.staleTag}> (Lựa chọn đã cũ)</span>}
                                                </button>
                                            );
                                        }} />
                                )}
                            </div>
                        </div>

    );

    return (
        <div className={styles.consultationPage}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Tư vấn & Trợ lý AI' }
            ]} />

            <div className={styles.consultationPanel} data-chat-panel>
                <div className={styles.header}>
                    <div className={styles.headerTitle}>
                        <Stethoscope size={24} color="var(--chat-accent)" />
                        Trợ lý ClinicCare
                                            </div>
                    <button
                        type="button"
                        onClick={clearChat}
                        title="Xóa lịch sử và bắt đầu lại"
                        className={styles.iconBtn}
                    >
                        <Trash2 size={16} /> Làm mới
                    </button>
                </div>

                <ProviderStatus state={wizard ? 'NotCalled' : messages[latestAssistantIndex]?.providerState} error={errorMsg || (messages[latestAssistantIndex]?.assistantStatus === 'Offline' ? messages[latestAssistantIndex]?.content : undefined)} detail={messages[latestAssistantIndex]?.executionMode} />

                <div className={styles.messageArea} aria-live="polite" data-chat-messages>
                    {/* Welcome Disclaimer */}
                    <div className={styles.welcomeContainer}>
                        <div className={styles.disclaimerBadge}>
                            <AlertTriangle size={18} style={{ flexShrink: 0, marginTop: 1 }} />
                            <span>Trợ lý hỗ trợ định hướng chuyên khoa và đặt lịch khám. Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán y khoa.</span>
                        </div>

                        {emptySuggestions && !activeSuggestions?.length && (
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

                    {messages.map((msg, idx) => idx === reviewIndex ? null : renderMessage(msg, idx))}

                    <BookingWizard state={wizard} busy={loading || submittingBooking} onStep={handleWizardStep} reviewContent={reviewIndex >= 0 ? renderMessage(messages[reviewIndex], reviewIndex) : undefined} />
                    {loading && (
                        <div className={`${styles.messageRow} ${styles.rowModel}`}>
                            <div className={`${styles.bubble} ${styles.bubbleModel}`}>
                                <div style={{ display: "flex", gap: "6px", padding: "4px" }}>
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "var(--chat-accent)", animation: "pulse 1.4s infinite" }} />
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "var(--chat-accent)", animation: "pulse 1.4s infinite 0.2s" }} />
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "var(--chat-accent)", animation: "pulse 1.4s infinite 0.4s" }} />
                                </div>
                            </div>
                        </div>
                    )}
                    <div ref={messagesEndRef} />
                </div>

                <div className={styles.composer} data-chat-composer>
                    <SuggestionChips variant={emptySuggestions && !wizard ? "grid" : "compact"} suggestions={activeSuggestions} disabled={loading || submittingBooking} onSelect={suggestion => void handleSuggestion(suggestion)} ariaLabel={emptySuggestions ? "Tra cứu nhanh dữ liệu của bạn" : "Gợi ý tiếp theo"} />
                    <div className={styles.inputArea}>
                        <textarea
                            className={styles.textarea}
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
                            className={styles.sendBtn}
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
