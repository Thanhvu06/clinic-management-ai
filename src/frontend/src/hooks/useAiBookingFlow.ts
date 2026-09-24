import { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { useChatContext } from "../contexts/ChatContext";
import { useAuth } from "../auth/AuthContext";
import axiosClient from "../api/axiosClient";
import type { ApiResponse } from "../types";
import type {
    ChatMessage,
    AiChatResponse,
    AiAction,
    AiBookingDraft,
    AiChatIntent,
    ConfirmBookingAction,
    ReviewBookingAction
} from "../types/ai";

export function isBookingConfirmationAction(action: AiAction): action is ConfirmBookingAction | ReviewBookingAction {
    return (action.type === "ConfirmBooking" || action.type === "ReviewBooking") &&
           action.payload !== undefined &&
           typeof action.payload === "object";
}


export interface AiChatRequestPayload {
    message: string;
    context: Array<{ role: "user" | "model"; content: string }>;
    intent?: AiChatIntent;
    pendingSpecialtyId?: number;
    pendingDoctorId?: number;
    pendingSlotId?: number;
    pendingSlotDate?: string;
    reason?: string;
    draftVersion?: number;
    displayedDoctorIds?: number[];
    displayedSlotIds?: number[];
    contextSnapshotId?: string;
    sessionId?: string;
    draftId?: string;
}

export interface CreateAppointmentPayload {
    doctorId: number;
    specialtyId: number;
    appointmentSlotId: number;
    reason: string;
}

export interface AppointmentEntityDto {
    id: number;
    appointmentCode: string;
    patientId: number;
    doctorId: number;
    doctorName?: string;
    specialtyId: number;
    specialtyName?: string;
    appointmentSlotId: number;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string;
    status: string;
}

export const isSafeClientRoute = (url?: string): boolean => {
    if (!url || typeof url !== "string") return false;
    if (url === "tel:115") return true;
    if (!url.startsWith("/") || url.startsWith("//")) return false;
    if (url.includes("://") || url.includes(":") || url.includes("\\") || /\s/.test(url)) return false;

    const [path] = url.split(/[?#]/);
    const allowedPathPrefixes = [
        "/patient/book",
        "/patient/appointments",
        "/patient/diagnostic-results",
        "/patient/prescriptions",
        "/patient/invoices",
        "/doctors"
    ];

    return allowedPathPrefixes.some(prefix => path === prefix || path.startsWith(prefix + "/"));
};

export const formatVietnameseDate = (dateStr?: string): string => {
    if (!dateStr) return "";
    try {
        const parts = dateStr.split("-");
        if (parts.length === 3) {
            return `${parts[2]}/${parts[1]}/${parts[0]}`;
        }
        const d = new Date(dateStr);
        if (!isNaN(d.getTime())) {
            const day = String(d.getDate()).padStart(2, "0");
            const month = String(d.getMonth() + 1).padStart(2, "0");
            const year = d.getFullYear();
            return `${day}/${month}/${year}`;
        }
    } catch {
        // fallback
    }
    return dateStr;
};

interface SendMessageOptions {
    intent?: AiChatIntent;
    specialtyId?: number;
    pendingSpecialtyId?: number;
    doctorId?: number;
    pendingDoctorId?: number;
    slotId?: number;
    pendingSlotId?: number;
    slotDate?: string;
    pendingSlotDate?: string;
    reason?: string;
    draftVersion?: number;
    contextSnapshotId?: string;
    sessionId?: string;
    draftId?: string;
}

export const useAiBookingFlow = (onNavigate?: () => void) => {
    const {
        messages,
        setMessages,
        clearChat,
        setPendingSpecialtyId,
        activeDraft,
        setActiveDraft,
        getBookingContextVersion,
        aiAssistantStatus,
        setAiAssistantStatus
    } = useChatContext();
    const { user } = useAuth();

    const [input, setInput] = useState("");
    const [loading, setLoading] = useState(false);
    const [submittingBooking, setSubmittingBooking] = useState(false);
    const [errorMsg, setErrorMsg] = useState("");
    const navigate = useNavigate();
    const activeRequestIdRef = useRef(0);
    const activeBookingSubmitIdRef = useRef(0);
    const activeRequestControllerRef = useRef<AbortController | null>(null);
    const accountKey = user ? (user.userId || (user.id !== undefined ? String(user.id) : null)) : null;
    const accountKeyRef = useRef(accountKey);
    const activeDraftRef = useRef(activeDraft);
    const sessionIdRef = useRef<string>("");
    const lastConfirmationAttemptRef = useRef<{ attemptId: string; payloadFingerprint: string; key: string; status?: string } | null>(null);
    
    // Initialize from sessionStorage on mount to survive remount
    const widgetStorageKey = useMemo(
        () => `cliniccare_pending_widget_attempt_${accountKey ?? "anon"}`,
        [accountKey]
    );
    useEffect(() => {
        try {
            const saved = sessionStorage.getItem(widgetStorageKey);
            if (saved && !lastConfirmationAttemptRef.current) {
                const parsed = JSON.parse(saved) as { attemptId: string; payloadFingerprint: string; key: string; status?: string };
                if (parsed?.key && parsed?.attemptId) {
                    lastConfirmationAttemptRef.current = parsed;
                }
            }
        } catch {
            // ignore storage error
        }
    }, [widgetStorageKey]);
    const isSubmittingBookingRef = useRef(false);
    const lastKnownGeminiStatusRef = useRef<"Unchecked" | "Healthy" | "Degraded">("Unchecked");
    const draftCancelledAtRef = useRef<number>(0);
    const contextSnapshotIdRef = useRef<string | undefined>(undefined);

    useEffect(() => {
        activeDraftRef.current = activeDraft;
        if (!activeDraft) {
            lastConfirmationAttemptRef.current = null;
        }
    }, [activeDraft]);

    useEffect(() => {
        accountKeyRef.current = accountKey;
        lastConfirmationAttemptRef.current = null;
        contextSnapshotIdRef.current = undefined;
        sessionIdRef.current = typeof crypto !== "undefined" && crypto.randomUUID
            ? `sess_${crypto.randomUUID()}`
            : `sess_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;
        return () => {
            activeRequestControllerRef.current?.abort();
        };
    }, [accountKey]);

    const handleSendMessage = async (
        textToSend: string,
        pendingPayload?: SendMessageOptions,
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

        const historyMessages: Array<{ role: "user" | "model"; content: string }> = newMessages
            .slice(-9, -1)
            .map(m => ({ role: m.role, content: m.content }));

        setMessages(newMessages);
        setInput("");
        setErrorMsg("");
        setLoading(true);
        const requestSentAt = Date.now();
        const requestId = ++activeRequestIdRef.current;
        const bookingContextVersion = getBookingContextVersion();
        const requesterAccountKey = accountKeyRef.current;
        const requestController = new AbortController();
        activeRequestControllerRef.current?.abort();
        activeRequestControllerRef.current = requestController;

        const isCurrentRequest = () =>
            requestId === activeRequestIdRef.current &&
            requesterAccountKey === accountKeyRef.current &&
            bookingContextVersion === getBookingContextVersion();

        try {
            const preservedReason = pendingPayload?.reason || activeDraft?.reason;

            // Extract displayed doctor and slot IDs from the last assistant message
            const lastModelMsg = [...messages].reverse().find(m => m.role === "model");
            const displayedDoctorIds = lastModelMsg?.actions
                ?.filter(a => a.type === "SelectDoctor" && a.payload && "doctorId" in a.payload && a.payload.doctorId)
                ?.map(a => Number((a.payload as { doctorId: number }).doctorId))
                ?.filter(id => !isNaN(id) && id > 0) || [];

            const displayedSlotIds = lastModelMsg?.actions
                ?.filter(a => a.type === "SelectSlot" && a.payload && "slotId" in a.payload && a.payload.slotId)
                ?.map(a => Number((a.payload as { slotId: number }).slotId))
                ?.filter(id => !isNaN(id) && id > 0) || [];

            const requestBody: AiChatRequestPayload = {
                message: trimmed,
                context: historyMessages,
                intent: pendingPayload?.intent,
                pendingSpecialtyId: pendingPayload?.pendingSpecialtyId ?? pendingPayload?.specialtyId ?? activeDraft?.specialtyId,
                pendingDoctorId: pendingPayload?.pendingDoctorId ?? pendingPayload?.doctorId ?? activeDraft?.doctorId,
                pendingSlotId: pendingPayload?.pendingSlotId ?? pendingPayload?.slotId ?? activeDraft?.slotId,
                pendingSlotDate: pendingPayload?.pendingSlotDate ?? pendingPayload?.slotDate ?? activeDraft?.slotDate,
                reason: preservedReason,
                draftVersion: pendingPayload?.draftVersion ?? activeDraft?.version,
                displayedDoctorIds: displayedDoctorIds.length > 0 ? displayedDoctorIds : undefined,
                displayedSlotIds: displayedSlotIds.length > 0 ? displayedSlotIds : undefined,
                contextSnapshotId: pendingPayload?.contextSnapshotId ?? contextSnapshotIdRef.current,
                sessionId: pendingPayload?.sessionId ?? sessionIdRef.current,
                draftId: pendingPayload?.draftId ?? activeDraft?.draftId
            };

            const res = await axiosClient.post<AiChatRequestPayload, ApiResponse<AiChatResponse>>(
                "/ai/chat",
                requestBody,
                { signal: requestController.signal }
            );

            if (!isCurrentRequest()) return;

            if (res.success && res.data) {
                const data = res.data;
                if (data.sessionId) {
                    sessionIdRef.current = data.sessionId;
                }
                if (data.contextSnapshotId) {
                    contextSnapshotIdRef.current = data.contextSnapshotId;
                }

                if (data.providerStatus === "Healthy") {
                    lastKnownGeminiStatusRef.current = "Healthy";
                } else if (
                    data.assistantStatus === "Degraded" ||
                    data.providerStatus === "Unavailable" ||
                    data.providerStatus === "Degraded" ||
                    data.providerStatus === "FallbackToLocal" ||
                    data.providerStatus === "Error"
                ) {
                    lastKnownGeminiStatusRef.current = "Degraded";
                }

                let effectiveStatus: "Unchecked" | "Online" | "Degraded" | "Offline";
                if (data.providerStatus === "Healthy") {
                    effectiveStatus = "Online";
                } else if (
                    data.assistantStatus === "Degraded" ||
                    data.providerStatus === "Unavailable" ||
                    data.providerStatus === "Degraded" ||
                    data.providerStatus === "FallbackToLocal" ||
                    data.providerStatus === "Error"
                ) {
                    effectiveStatus = "Degraded";
                } else if (data.providerStatus === "NotCalled" || !data.providerStatus) {
                    if (lastKnownGeminiStatusRef.current === "Degraded") {
                        effectiveStatus = "Degraded";
                    } else if (lastKnownGeminiStatusRef.current === "Healthy") {
                        effectiveStatus = "Online";
                    } else {
                        effectiveStatus = "Unchecked";
                    }
                } else {
                    effectiveStatus = data.assistantStatus || "Unchecked";
                }
                setAiAssistantStatus(effectiveStatus);

                const aiMsg: ChatMessage = {
                    role: "model",
                    content: data.message || data.reply || "",
                    urgency: data.urgency,
                    safetyNotice: data.safetyNotice,
                    suggestions: data.specialtySuggestions || data.suggestedSpecialties || [],
                    actions: data.actions || [],
                    bookingDraft: data.bookingDraft,
                    missingFields: data.missingFields || [],
                    assistantStatus: effectiveStatus,
                    primaryIntent: data.primaryIntent,
                    dialogueOutcome: data.dialogueOutcome,
                    clarificationPrompt: data.clarificationPrompt
                };

                if (data.dialogueOutcome === "DraftCancelled") {
                    draftCancelledAtRef.current = Date.now();
                    setActiveDraft(null);
                } else if (draftCancelledAtRef.current > requestSentAt) {
                    // Stale response received after draft was cancelled, do not resurrect draft
                } else {
                    let fallbackDraft: AiBookingDraft | undefined = undefined;
                    const fallbackDraftAction = data.actions?.find(isBookingConfirmationAction);
                    if (fallbackDraftAction && fallbackDraftAction.payload) {
                        const p = fallbackDraftAction.payload;
                        const specId = p.specialtyId;
                        const docId = p.doctorId;
                        const sId = p.slotId;
                        const sDate = p.slotDate;
                        const sTime = p.startTime;
                        const eTime = p.endTime;
                        const draftReason = p.reason || preservedReason || activeDraft?.reason;

                        if (specId && docId && sId && sDate && sTime && eTime) {
                            const isComplete = isValidBookingReason(draftReason);
                            fallbackDraft = {
                                specialtyId: specId,
                                specialtyName: p.specialtyName,
                                doctorId: docId,
                                doctorName: p.doctorName,
                                slotId: sId,
                                slotDate: sDate,
                                startTime: sTime,
                                endTime: eTime,
                                reason: draftReason,
                                isComplete,
                                version: activeDraft?.version ?? fallbackDraftAction.draftVersion ?? p.draftVersion ?? 1,
                                confirmationId: p.confirmationId
                            };
                        }
                    }

                    const incomingDraft: AiBookingDraft | undefined = data.bookingDraft || fallbackDraft;

                    if (incomingDraft) {
                        const currentVersion = (typeof activeDraft?.version === "number" && Number.isInteger(activeDraft.version) && activeDraft.version >= 1)
                            ? activeDraft.version
                            : undefined;
                        const hasIncomingVersion = typeof incomingDraft.version === "number" && Number.isInteger(incomingDraft.version) && incomingDraft.version >= 1;
                        const incomingVersion = hasIncomingVersion ? incomingDraft.version : undefined;

                        if (currentVersion !== undefined && incomingVersion !== undefined && incomingVersion < currentVersion) {
                            // Stale response received out-of-order, do not overwrite newer draft
                        } else {
                            const isSubstantiveChange = Boolean(
                                activeDraft && (
                                    incomingDraft.specialtyId !== activeDraft.specialtyId ||
                                    incomingDraft.doctorId !== activeDraft.doctorId ||
                                    incomingDraft.slotId !== activeDraft.slotId ||
                                    incomingDraft.slotDate !== activeDraft.slotDate ||
                                    incomingDraft.startTime !== activeDraft.startTime ||
                                    (incomingDraft.reason || "").trim() !== (activeDraft.reason || "").trim() ||
                                    (incomingVersion !== undefined && currentVersion !== undefined && incomingVersion > currentVersion)
                                )
                            );
                            const mergedDraft: AiBookingDraft = {
                                ...incomingDraft,
                                reason: incomingDraft.reason || preservedReason || activeDraft?.reason,
                                version: incomingVersion ?? currentVersion,
                                confirmationId: incomingDraft.confirmationId ?? (isSubstantiveChange ? undefined : activeDraft?.confirmationId)
                            };
                            draftCancelledAtRef.current = 0;
                            setActiveDraft(mergedDraft);
                            aiMsg.bookingDraft = mergedDraft;
                        }
                    }
                }


                setMessages(prev => [...prev, aiMsg]);
            } else {
                throw new Error("Invalid response");
            }
        } catch (err: unknown) {
            if (requestController.signal.aborted || !isCurrentRequest()) return;
            setAiAssistantStatus("Offline");
            const apiErr = err as { errorCode?: string; message?: string; response?: { data?: { errorCode?: string; message?: string } } };
            const errorCode = apiErr?.response?.data?.errorCode || apiErr?.errorCode;
            const message = apiErr?.response?.data?.message || apiErr?.message || "";

            if (errorCode === "TOO_MANY_REQUESTS" || message.includes("quá nhiều")) {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút.",
                    urgency: "ROUTINE",
                    assistantStatus: "Offline"
                }]);
            } else {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Xin lỗi, hệ thống AI đang bận hoặc gặp sự cố kết nối. Bạn có thể chọn chuyên khoa và đặt lịch trực tiếp qua trang Đặt lịch khám.",
                    urgency: "ROUTINE",
                    assistantStatus: "Offline",
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
            if (requestId === activeRequestIdRef.current) {
                setLoading(false);
                activeRequestControllerRef.current = null;
            }
        }
    };

    const handleActionClick = async (action: AiAction): Promise<void> => {
        const INTERACTIVE_BOOKING_ACTIONS = [
            "SelectDoctor",
            "SelectSlot",
            "ViewAvailableSlots",
            "ChangePreferredDate"
        ];

        const isInteractiveBookingAction = INTERACTIVE_BOOKING_ACTIONS.includes(action.type);

        const rawActionVersion =
            action.draftVersion ??
            (action.payload as Record<string, unknown>)?.draftVersion;

        const hasValidActionVersion =
            typeof rawActionVersion === "number" &&
            Number.isInteger(rawActionVersion) &&
            rawActionVersion >= 1;

        const actionVersion = hasValidActionVersion ? rawActionVersion : undefined;

        const hasValidDraftVersion =
            typeof activeDraft?.version === "number" &&
            Number.isInteger(activeDraft.version) &&
            activeDraft.version >= 1;

        if (isInteractiveBookingAction) {
            if (!activeDraft && draftCancelledAtRef.current > 0) {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Bản nháp đặt lịch trước đó đã bị hủy. Vui lòng bắt đầu yêu cầu đặt lịch mới.",
                    urgency: "ROUTINE"
                }]);
                return;
            }

            if (actionVersion !== undefined && activeDraft?.version !== undefined && actionVersion !== activeDraft.version) {
                const actVer = actionVersion;
                const draftVer = activeDraft.version;
                const warningContent = actVer < draftVer
                    ? `Thao tác này thuộc phiên bản thảo lịch cũ (v${actVer}). Thông tin lịch khám hiện tại đã được cập nhật sang phiên bản mới hơn (v${draftVer}). Vui lòng thao tác trên các nút mới nhất.`
                    : `Thao tác này không khớp với phiên bản thảo lịch hiện tại (v${actVer} so với v${draftVer}). Vui lòng thao tác trên các lựa chọn mới nhất.`;

                setMessages(prev => {
                    const lastMsg = prev[prev.length - 1];
                    if (lastMsg?.role === "model" && lastMsg.content === warningContent) {
                        return prev;
                    }
                    return [...prev, {
                        role: "model",
                        content: warningContent,
                        urgency: "ROUTINE",
                        actions: activeDraft?.specialtyId ? [
                            {
                                id: `act-reload-draft-${Date.now()}`,
                                type: "ViewAvailableSlots",
                                label: "Tải lại lựa chọn hiện tại",
                                style: "secondary",
                                requiresAuthentication: false,
                                requiresConfirmation: false,
                                draftVersion: activeDraft.version,
                                payload: {
                                    specialtyId: activeDraft.specialtyId,
                                    doctorId: activeDraft.doctorId,
                                    slotDate: activeDraft.slotDate,
                                    draftVersion: activeDraft.version
                                }
                            }
                        ] : []
                    }];
                });
                return;
            }

            if (!hasValidActionVersion && hasValidDraftVersion) {
                const legacyWarning = "Lựa chọn này được tạo từ phiên trò chuyện cũ hoặc chưa được đồng bộ phiên bản. Tôi đã tạo lựa chọn mới nhất để bạn tiếp tục.";
                setMessages(prev => {
                    const lastMsg = prev[prev.length - 1];
                    if (lastMsg?.role === "model" && lastMsg.content === legacyWarning) {
                        return prev;
                    }
                    return [...prev, {
                        role: "model",
                        content: legacyWarning,
                        urgency: "ROUTINE",
                        actions: activeDraft?.specialtyId ? [
                            {
                                id: `act-reload-draft-${Date.now()}`,
                                type: "ViewAvailableSlots",
                                label: "Tải lại lựa chọn hiện tại",
                                style: "secondary",
                                requiresAuthentication: false,
                                requiresConfirmation: false,
                                draftVersion: activeDraft.version,
                                payload: {
                                    specialtyId: activeDraft.specialtyId,
                                    doctorId: activeDraft.doctorId,
                                    slotDate: activeDraft.slotDate,
                                    draftVersion: activeDraft.version
                                }
                            }
                        ] : []
                    }];
                });
                return;
            }
        }

        switch (action.type) {
            case "ViewSpecialty": {
                if (action.payload.specialtyId) {
                    setPendingSpecialtyId(action.payload.specialtyId);
                    navigate(`/patient/book?specialtyId=${action.payload.specialtyId}`);
                    onNavigate?.();
                } else if (action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)) {
                    navigate(action.payload.targetUrl);
                    onNavigate?.();
                }
                break;
            }

            case "ViewDoctors": {
                const targetUrl = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : (action.payload.specialtyId
                        ? `/doctors?specialtyId=${action.payload.specialtyId}`
                        : "/doctors");
                navigate(targetUrl);
                onNavigate?.();
                break;
            }

            case "ViewAvailableSlots": {
                const specId = action.payload.specialtyId ?? activeDraft?.specialtyId;
                const docId = action.payload.doctorId ?? activeDraft?.doctorId;
                const slotDate = action.payload.slotDate ?? activeDraft?.slotDate;

                await handleSendMessage(
                    `Xem lịch trống khả dụng`,
                    {
                        pendingSpecialtyId: specId,
                        pendingDoctorId: docId,
                        pendingSlotDate: slotDate,
                        reason: activeDraft?.reason,
                        draftVersion: actionVersion ?? activeDraft?.version
                    }
                );
                break;
            }

            case "StartBooking": {
                const url = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : (action.payload.specialtyId
                        ? `/patient/book?specialtyId=${action.payload.specialtyId}`
                        : "/patient/book");
                navigate(url);
                onNavigate?.();
                break;
            }

            case "SelectDoctor": {
                const nextDraft: AiBookingDraft = {
                    ...activeDraft,
                    specialtyId: action.payload.specialtyId,
                    specialtyName: action.payload.specialtyName || activeDraft?.specialtyName,
                    doctorId: action.payload.doctorId,
                    doctorName: action.payload.doctorName,
                    slotId: undefined,
                    startTime: undefined,
                    endTime: undefined,
                    isComplete: false,
                    reason: activeDraft?.reason,
                    version: activeDraft?.version
                };
                setActiveDraft(nextDraft);

                await handleSendMessage(
                    `Tôi muốn đặt khám với bác sĩ ${action.payload.doctorName || ""}`,
                    {
                        pendingSpecialtyId: action.payload.specialtyId,
                        pendingDoctorId: action.payload.doctorId,
                        pendingSlotDate: action.payload.slotDate || activeDraft?.slotDate,
                        reason: activeDraft?.reason,
                        draftVersion: activeDraft?.version
                    }
                );
                break;
            }

            case "SelectSlot": {
                const nextDraft: AiBookingDraft = {
                    ...activeDraft,
                    specialtyId: action.payload.specialtyId ?? activeDraft?.specialtyId,
                    specialtyName: action.payload.specialtyName ?? activeDraft?.specialtyName,
                    doctorId: action.payload.doctorId ?? activeDraft?.doctorId,
                    doctorName: action.payload.doctorName ?? activeDraft?.doctorName,
                    slotId: action.payload.slotId,
                    slotDate: action.payload.slotDate,
                    startTime: action.payload.startTime,
                    endTime: action.payload.endTime,
                    reason: action.payload.reason || activeDraft?.reason,
                    isComplete: isValidBookingReason(action.payload.reason || activeDraft?.reason),
                    version: activeDraft?.version
                };
                setActiveDraft(nextDraft);

                await handleSendMessage(
                    `Tôi chọn khung giờ ${action.payload.startTime} ngày ${formatVietnameseDate(action.payload.slotDate)}`,
                    {
                        pendingSpecialtyId: nextDraft.specialtyId,
                        pendingDoctorId: nextDraft.doctorId,
                        pendingSlotId: action.payload.slotId,
                        pendingSlotDate: action.payload.slotDate,
                        reason: nextDraft.reason,
                        draftVersion: activeDraft?.version
                    }
                );
                break;
            }

            case "ReviewBooking": {
                if (!activeDraft && draftCancelledAtRef.current > 0) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Bản nháp đặt lịch trước đó đã bị hủy. Vui lòng bắt đầu yêu cầu đặt lịch mới.",
                        urgency: "ROUTINE"
                    }]);
                    break;
                }

                const specId = action.payload.specialtyId || activeDraft?.specialtyId;
                const docId = action.payload.doctorId || activeDraft?.doctorId;
                const slotId = action.payload.slotId || activeDraft?.slotId;
                const slotDate = action.payload.slotDate || activeDraft?.slotDate;
                const startTime = action.payload.startTime || activeDraft?.startTime;
                const endTime = action.payload.endTime || activeDraft?.endTime;
                const specName = action.payload.specialtyName || activeDraft?.specialtyName;
                const docName = action.payload.doctorName || activeDraft?.doctorName;
                const reason = action.payload.reason?.trim() || activeDraft?.reason?.trim() || "";
                const currentVersion = activeDraft?.version ?? 1;

                if (!specId || !docId || !slotId || !slotDate || !startTime || !endTime || !isValidBookingReason(reason)) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Thông tin đặt lịch chưa đầy đủ (thiếu chuyên khoa, bác sĩ, khung giờ hoặc lý do khám). Vui lòng chọn đầy đủ thông tin trước khi xác nhận.",
                        urgency: "ROUTINE"
                    }]);
                    break;
                }

                if (actionVersion !== undefined && activeDraft?.version !== undefined && actionVersion !== activeDraft.version) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: `Thông tin xác nhận lịch khám thuộc phiên bản cũ (v${actionVersion}). Phiên bản hiện tại là v${activeDraft.version}. Vui lòng kiểm tra lại thông tin mới nhất trước khi xác nhận.`,
                        urgency: "ROUTINE"
                    }]);
                    break;
                }

                if (action.payload.confirmationId && activeDraft?.confirmationId && action.payload.confirmationId !== activeDraft.confirmationId) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Yêu cầu xác nhận này không còn hiệu lực do bản nháp đã được cập nhật hoặc làm mới. Vui lòng xác nhận trên lựa chọn mới nhất.",
                        urgency: "ROUTINE"
                    }]);
                    break;
                }

                const effectiveConfirmationId = activeDraft?.confirmationId || action.payload.confirmationId || `conf_v${currentVersion}_${slotId}`;
                if (activeDraft && !activeDraft.confirmationId) {
                    setActiveDraft(prev => prev ? { ...prev, confirmationId: effectiveConfirmationId } : prev);
                }

                const formattedDate = formatVietnameseDate(slotDate);
                const reviewMsg: ChatMessage = {
                    role: "model",
                    content: `📋 **Thông tin xác nhận lịch hẹn:**\n- **Chuyên khoa:** ${specName || "Chuyên khoa"}\n- **Bác sĩ:** ${docName || "Bác sĩ"}\n- **Thời gian:** ${startTime} ngày ${formattedDate}\n- **Lý do khám:** ${reason}\n\nBạn vui lòng xác nhận để hoàn tất đặt lịch.`,
                    urgency: "ROUTINE",
                    actions: [
                        {
                            id: "act-confirm-from-review",
                            type: "ConfirmBooking",
                            label: "Xác nhận đặt lịch",
                            style: "primary",
                            requiresAuthentication: true,
                            requiresConfirmation: true,
                            draftVersion: currentVersion,
                            payload: {
                                confirmationId: effectiveConfirmationId,
                                specialtyId: specId,
                                specialtyName: specName,
                                doctorId: docId,
                                doctorName: docName,
                                slotId: slotId,
                                slotDate: slotDate,
                                startTime: startTime,
                                endTime: endTime || "",
                                reason: reason,
                                draftVersion: currentVersion
                            }
                        }
                    ]
                };
                setMessages(prev => [...prev, reviewMsg]);
                break;
            }

            case "ConfirmBooking": {
                if (submittingBooking || isSubmittingBookingRef.current) return;
                if (!action.requiresConfirmation) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Yêu cầu xác nhận đặt lịch không hợp lệ. Vui lòng xem lại thông tin trước khi thử lại.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                const actionReason = (action.payload.reason || "").trim();
                const draftReason = (activeDraft?.reason || "").trim();

                if (!isValidBookingReason(actionReason)) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Lý do khám phải từ 10 đến 500 ký tự. Vui lòng nhập lý do khám hoặc mô tả triệu chứng trước khi xác nhận đặt lịch.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                if (!activeDraft || !activeDraft.specialtyId || !activeDraft.doctorId || !activeDraft.slotId || !activeDraft.slotDate || !activeDraft.startTime) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Bản nháp đặt lịch không tồn tại hoặc chưa hoàn chỉnh. Vui lòng chọn lại thông tin khám để tiếp tục.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                if (actionVersion !== undefined && activeDraft.version !== undefined && actionVersion !== activeDraft.version) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: `Thông tin xác nhận lịch khám thuộc phiên bản cũ (v${actionVersion}). Phiên bản hiện tại là v${activeDraft.version}. Vui lòng kiểm tra lại thông tin mới nhất trước khi xác nhận.`,
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                if (activeDraft.version !== undefined && actionVersion === undefined) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: `Thông tin xác nhận lịch khám thuộc phiên bản cũ (vkhông xác định). Phiên bản hiện tại là v${activeDraft.version}. Vui lòng kiểm tra lại thông tin mới nhất trước khi xác nhận.`,
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                const actionConfirmationId = action.payload.confirmationId;
                if (
                    (activeDraft.confirmationId && (!actionConfirmationId || actionConfirmationId !== activeDraft.confirmationId)) ||
                    (!activeDraft.confirmationId && Boolean(actionConfirmationId))
                ) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Yêu cầu xác nhận này không còn hiệu lực do bản nháp đã được cập nhật hoặc làm mới. Vui lòng xác nhận trên lựa chọn mới nhất.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                const isSpecialtyMatch = action.payload.specialtyId === activeDraft.specialtyId;
                const isDoctorMatch = action.payload.doctorId === activeDraft.doctorId;
                const isSlotMatch = action.payload.slotId === activeDraft.slotId;
                const isDateMatch = action.payload.slotDate === activeDraft.slotDate;
                const isStartTimeMatch = action.payload.startTime === activeDraft.startTime;
                const isEndTimeMatch = !activeDraft.endTime || !action.payload.endTime || action.payload.endTime === activeDraft.endTime;
                const isReasonMatch = actionReason === draftReason;

                if (!isSpecialtyMatch || !isDoctorMatch || !isSlotMatch || !isDateMatch || !isStartTimeMatch || !isEndTimeMatch || !isReasonMatch) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Thông tin xác nhận không khớp với thông tin bản nháp hiện tại (chuyên khoa, bác sĩ, ngày khám, khung giờ hoặc lý do khám đã thay đổi). Vui lòng xác nhận trên lựa chọn mới nhất.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                const bookingSubmitId = ++activeBookingSubmitIdRef.current;
                const bookingAccountKey = accountKeyRef.current;
                const bookingContextVersionAtStart = getBookingContextVersion();
                const bookingDraftIdAtStart = activeDraft.draftId;
                const isBookingAttemptStillCurrent = () =>
                    bookingSubmitId === activeBookingSubmitIdRef.current &&
                    bookingAccountKey === accountKeyRef.current &&
                    bookingContextVersionAtStart === getBookingContextVersion() &&
                    activeDraftRef.current?.draftId === bookingDraftIdAtStart;

                isSubmittingBookingRef.current = true;
                setSubmittingBooking(true);
                
                const currentAttemptId = `${bookingAccountKey ?? "anon"}_${bookingDraftIdAtStart ?? "draft"}_${actionConfirmationId || `${action.payload.slotId}_v${activeDraft.version}`}`;
                const payloadFingerprint = `${action.payload.specialtyId}_${action.payload.doctorId}_${action.payload.slotId}_${action.payload.slotDate}_${action.payload.startTime}_${actionReason}`;
                let idempotencyKey: string;

                try {
                    const bookPayload: CreateAppointmentPayload = {
                        doctorId: action.payload.doctorId,
                        specialtyId: action.payload.specialtyId,
                        appointmentSlotId: action.payload.slotId,
                        reason: actionReason
                    };
                    if (
                        lastConfirmationAttemptRef.current &&
                        lastConfirmationAttemptRef.current.attemptId === currentAttemptId &&
                        lastConfirmationAttemptRef.current.payloadFingerprint === payloadFingerprint
                    ) {
                        idempotencyKey = lastConfirmationAttemptRef.current.key;
                    } else {
                        idempotencyKey = typeof crypto !== "undefined" && crypto.randomUUID
                            ? crypto.randomUUID()
                            : `chat_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;
                        lastConfirmationAttemptRef.current = {
                            attemptId: currentAttemptId,
                            payloadFingerprint,
                            key: idempotencyKey
                        };
                        try {
                            sessionStorage.setItem(widgetStorageKey, JSON.stringify(lastConfirmationAttemptRef.current));
                        } catch {
                            // ignore storage error
                        }
                    }

                    const bookRes = await axiosClient.post<CreateAppointmentPayload, ApiResponse<AppointmentEntityDto>>(
                        "/appointments",
                        bookPayload,
                        {
                            headers: {
                                "Idempotency-Key": idempotencyKey
                            }
                        }
                    );

                    if (bookRes.success && bookRes.data) {
                        if (lastConfirmationAttemptRef.current?.key === idempotencyKey) {
                            lastConfirmationAttemptRef.current = null;
                            try {
                                sessionStorage.removeItem(widgetStorageKey);
                            } catch {
                                // ignore storage error
                            }
                        }
                        if (!isBookingAttemptStillCurrent()) {
                            return;
                        }
                        const apt = bookRes.data;
                        const formattedDate = formatVietnameseDate(action.payload.slotDate);
                        const finalReason = apt.reason || actionReason;
                        const finalDocName = apt.doctorName || action.payload.doctorName || "Bác sĩ phụ trách";
                        const finalTime = apt.startTime || action.payload.startTime;
                        const successMsg: ChatMessage = {
                            role: "model",
                            content: `🎉 **Đặt lịch khám thành công!**\n- **Mã cuộc hẹn:** ${apt.appointmentCode || apt.id}\n- **Bác sĩ:** ${finalDocName}\n- **Thời gian:** ${finalTime} ngày ${formattedDate}\n- **Lý do khám:** ${finalReason}\n\nCuộc hẹn của bạn đã được lưu vào hệ thống phòng khám.`,
                            actions: [
                                {
                                    id: "act-view-created-apt",
                                    type: "ViewMyAppointments",
                                    label: "Xem danh sách lịch hẹn của tôi",
                                    style: "primary",
                                    requiresAuthentication: true,
                                    requiresConfirmation: false,
                                    payload: { targetUrl: "/patient/appointments" }
                                }
                            ]
                        };
                        setMessages(prev => [...prev, successMsg]);
                        draftCancelledAtRef.current = Date.now();
                        setActiveDraft(null);
                    }
                } catch (err: unknown) {
                    if (!isBookingAttemptStillCurrent()) {
                        return;
                    }
                    const apiErr = err as { response?: { data?: { errorCode?: string; message?: string } }; errorCode?: string; message?: string };
                    const errorCode = apiErr?.response?.data?.errorCode || apiErr?.errorCode;

                    if (errorCode === "SLOT_ALREADY_BOOKED") {
                        lastConfirmationAttemptRef.current = null;
                        try {
                            sessionStorage.removeItem(widgetStorageKey);
                        } catch {
                            // ignore storage error
                        }
                        const conflictNotice: ChatMessage = {
                            role: "model",
                            content: "⚠️ **Khung giờ này vừa có bệnh nhân khác đặt trước.** Khung giờ đã được cập nhật, thông tin triệu chứng của bạn vẫn được lưu giữ. Vui lòng chọn khung giờ khác bên dưới:",
                            urgency: "ROUTINE"
                        };

                        await handleSendMessage(
                            `Xem các lịch trống khác của bác sĩ ${action.payload.doctorName || ""}`,
                            {
                                intent: "FindEarliestAvailableSlot",
                                pendingSpecialtyId: action.payload.specialtyId,
                                pendingDoctorId: action.payload.doctorId,
                                pendingSlotDate: action.payload.slotDate,
                                reason: actionReason,
                                draftVersion: activeDraft?.version
                            },
                            conflictNotice
                        );
                    } else {
                        // Network/unknown error: preserve key as uncertain for retry
                        if (lastConfirmationAttemptRef.current?.payloadFingerprint === payloadFingerprint) {
                            lastConfirmationAttemptRef.current = {
                                ...lastConfirmationAttemptRef.current,
                                status: "uncertain"
                            };
                            try {
                                sessionStorage.setItem(widgetStorageKey, JSON.stringify(lastConfirmationAttemptRef.current));
                            } catch {
                                // ignore storage error
                            }
                        }
                        const errorNotice = apiErr?.response?.data?.message || apiErr?.message || "Đặt lịch không thành công. Vui lòng thử lại.";
                        setMessages(prev => [...prev, {
                            role: "model",
                            content: `⚠️ ${errorNotice}`,
                            urgency: "ROUTINE"
                        }]);
                    }
                } finally {
                    if (bookingSubmitId === activeBookingSubmitIdRef.current) {
                        isSubmittingBookingRef.current = false;
                        setSubmittingBooking(false);
                    }
                }
                break;
            }


            case "ChangePreferredDate": {
                const nextDate = action.payload.slotDate;
                const formattedNext = formatVietnameseDate(nextDate);
                const preservedReason = action.payload.reason || activeDraft?.reason;

                if (activeDraft) {
                    setActiveDraft({
                        ...activeDraft,
                        slotDate: nextDate,
                        slotId: undefined,
                        startTime: undefined,
                        endTime: undefined,
                        isComplete: false,
                        reason: preservedReason,
                        version: actionVersion ?? activeDraft.version
                    });
                }

                await handleSendMessage(
                    `Tôi muốn xem lịch khám vào ngày ${formattedNext}`,
                    {
                        pendingSpecialtyId: action.payload.specialtyId ?? activeDraft?.specialtyId,
                        pendingDoctorId: action.payload.doctorId ?? activeDraft?.doctorId,
                        pendingSlotDate: nextDate,
                        reason: preservedReason,
                        draftVersion: actionVersion ?? activeDraft?.version
                    }
                );
                break;
            }

            case "ViewMyAppointments":
            case "OpenAppointmentDetail":
            case "RequestReschedule":
            case "RequestCancellation": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/appointments";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ViewDiagnosticResults": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/diagnostic-results";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ViewPrescriptions": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/prescriptions";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ViewBills": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/invoices";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ContactReception": {
                const phone = action.payload.phoneNumber?.trim();
                const address = action.payload.address?.trim();
                const facilityName = action.payload.facilityName?.trim();
                const reason = action.payload.reason?.trim();
                
                let content: string;
                if (phone && phone !== "Chưa cấu hình") {
                    content = `📞 **Thông tin Quầy Tiếp Đón & Lễ Tân${facilityName ? ` - ${facilityName}` : ""}:**\n- **Hotline hỗ trợ:** [${phone}](tel:${phone.replace(/\s+/g, "")})${address ? `\n- **Địa chỉ:** ${address}` : ""}${reason ? `\n- **Ghi chú:** ${reason}` : ""}\n\nNếu cần hỗ trợ thêm, bạn có thể liên hệ số điện thoại trên.`;
                } else {
                    content = `📞 **Thông tin Quầy Tiếp Đón & Lễ Tân${facilityName ? ` - ${facilityName}` : ""}:**\nThông tin liên hệ lễ tân chưa được cấu hình trong hệ thống.${address ? `\n- **Địa chỉ cơ sở:** ${address}` : ""}`;
                }

                const receptionMsg: ChatMessage = {
                    role: "model",
                    content,
                    urgency: "ROUTINE"
                };
                setMessages(prev => [...prev, receptionMsg]);
                break;
            }

            case "ManualSpecialtySelection": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/book";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "CallEmergency": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "tel:115";
                window.location.href = target;
                break;
            }

            default: {
                const _exhaustiveCheck: never = action;
                console.warn("Unhandled action type:", _exhaustiveCheck);
                break;
            }
        }
    };

    return {
        input,
        setInput,
        loading,
        submittingBooking,
        errorMsg,
        setErrorMsg,
        messages,
        activeDraft,
        clearChat,
        handleSendMessage,
        handleActionClick,
        formatVietnameseDate,
        aiAssistantStatus
    };
};

export const isValidBookingReason = (reason?: string): boolean => {
    const normalized = reason?.trim() ?? "";
    if (normalized.length < 10 || normalized.length > 500) return false;
    const lower = normalized.toLowerCase();
    if (lower === "ok" || lower === "chốt" || lower.startsWith("chốt ") || lower.startsWith("đồng ý") || lower.startsWith("tôi chọn")) {
        return false;
    }
    return true;
};
