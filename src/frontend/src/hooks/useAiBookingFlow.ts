import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
    useChatContext,
    buildStandardBookingPayloadFingerprint,
    readPersistedBookingAttempt,
    writePersistedBookingAttempt,
    removePersistedBookingAttempt,
    type PendingBookingAttemptRecord
} from "../contexts/ChatContext";
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
    ReviewBookingAction,
    AiToolExecutionResult
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
    confirmationId?: string;
    contextSnapshotId?: string;
    sessionId?: string;
    draftId?: string;
    draftVersion?: number;
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

const generateSessionIdentity = (): string =>
    typeof crypto !== "undefined" && crypto.randomUUID
        ? `sess_${crypto.randomUUID()}`
        : `sess_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;

const sessionStorageKey = (accountKey: string | null | undefined): string =>
    `cliniccare_ai_session_${accountKey ?? "anon"}`;

const loadOrCreateSessionIdentity = (accountKey: string | null | undefined): string => {
    try {
        const persisted = sessionStorage.getItem(sessionStorageKey(accountKey));
        if (persisted && /^sess_[A-Za-z0-9_-]{1,123}$/.test(persisted)) return persisted;
    } catch {
        // Storage can be unavailable in privacy mode; an in-memory session is still safe.
    }

    const generated = generateSessionIdentity();
    try {
        sessionStorage.setItem(sessionStorageKey(accountKey), generated);
    } catch {
        // Keep the generated identity in memory when storage is unavailable.
    }
    return generated;
};

const persistSessionIdentity = (accountKey: string | null | undefined, sessionId: string): void => {
    if (!/^sess_[A-Za-z0-9_-]{1,123}$/.test(sessionId)) return;
    try {
        sessionStorage.setItem(sessionStorageKey(accountKey), sessionId);
    } catch {
        // Best effort only; the backend remains authoritative.
    }
};

const rotateSessionIdentity = (accountKey: string | null | undefined): string => {
    const next = generateSessionIdentity();
    persistSessionIdentity(accountKey, next);
    return next;
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
    const prevAccountKeyRef = useRef(accountKey);
    const activeDraftRef = useRef(activeDraft);
    const prevDraftRef = useRef<AiBookingDraft | null>(activeDraft);
    const sessionIdRef = useRef<string>("");
    const lastConfirmationAttemptRef = useRef<PendingBookingAttemptRecord | null>(
        readPersistedBookingAttempt(accountKey)
    );
    const isSubmittingBookingRef = useRef(false);
    const lastKnownGeminiStatusRef = useRef<"Unchecked" | "Healthy" | "Degraded">("Unchecked");
    const draftCancelledAtRef = useRef<number>(0);
    const contextSnapshotIdRef = useRef<string | undefined>(undefined);

    useEffect(() => {
        const prevDraft = prevDraftRef.current;
        prevDraftRef.current = activeDraft;
        activeDraftRef.current = activeDraft;

        if (prevDraft !== null && activeDraft === null) {
            activeBookingSubmitIdRef.current += 1;
            isSubmittingBookingRef.current = false;
            setSubmittingBooking(false);
            contextSnapshotIdRef.current = undefined;
            lastConfirmationAttemptRef.current = null;
            removePersistedBookingAttempt(accountKeyRef.current);
            return;
        }

        if (prevDraft !== null && activeDraft !== null) {
            const isDraftReplacedOrMutated =
                (Boolean(prevDraft.draftId) && Boolean(activeDraft.draftId) && prevDraft.draftId !== activeDraft.draftId) ||
                (prevDraft.specialtyId !== undefined && activeDraft.specialtyId !== prevDraft.specialtyId) ||
                (prevDraft.doctorId !== undefined && activeDraft.doctorId !== prevDraft.doctorId) ||
                (prevDraft.slotId !== undefined && activeDraft.slotId !== prevDraft.slotId) ||
                (prevDraft.slotDate !== undefined && activeDraft.slotDate !== prevDraft.slotDate) ||
                (prevDraft.startTime !== undefined && activeDraft.startTime !== prevDraft.startTime) ||
                (Boolean(prevDraft.reason?.trim()) && (activeDraft.reason || "").trim() !== (prevDraft.reason || "").trim()) ||
                (prevDraft.version !== undefined && activeDraft.version !== undefined && activeDraft.version !== prevDraft.version);

            if (isDraftReplacedOrMutated) {
                activeBookingSubmitIdRef.current += 1;
                isSubmittingBookingRef.current = false;
                setSubmittingBooking(false);
                contextSnapshotIdRef.current = undefined;
                lastConfirmationAttemptRef.current = null;
                removePersistedBookingAttempt(accountKeyRef.current);
                return;
            }
        }

        if (activeDraft && !lastConfirmationAttemptRef.current) {
            const restored = readPersistedBookingAttempt(accountKeyRef.current);
            if (restored && (!activeDraft.draftId || !restored.draftId || restored.draftId === activeDraft.draftId)) {
                lastConfirmationAttemptRef.current = restored;
            }
        }
    }, [activeDraft]);

    useEffect(() => {
        const prevAccountKey = prevAccountKeyRef.current;
        prevAccountKeyRef.current = accountKey;
        accountKeyRef.current = accountKey;

        if (prevAccountKey !== accountKey) {
            activeBookingSubmitIdRef.current += 1;
            isSubmittingBookingRef.current = false;
            setSubmittingBooking(false);
            contextSnapshotIdRef.current = undefined;
            lastConfirmationAttemptRef.current = readPersistedBookingAttempt(accountKey);
            sessionIdRef.current = loadOrCreateSessionIdentity(accountKey);
        } else {
            if (!lastConfirmationAttemptRef.current) {
                lastConfirmationAttemptRef.current = readPersistedBookingAttempt(accountKey);
            }
            if (!sessionIdRef.current) {
                sessionIdRef.current = loadOrCreateSessionIdentity(accountKey);
            }
        }
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
                    persistSessionIdentity(accountKeyRef.current, data.sessionId);
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
                    clarificationPrompt: data.clarificationPrompt,
                    toolResults: data.toolResults
                };

                if (data.dialogueOutcome === "DraftCancelled" || data.dialogueOutcome === "SessionRejected") {
                    draftCancelledAtRef.current = Date.now();
                    contextSnapshotIdRef.current = undefined;
                    sessionIdRef.current = rotateSessionIdentity(accountKeyRef.current);
                    lastConfirmationAttemptRef.current = null;
                    removePersistedBookingAttempt(accountKeyRef.current);
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
                                sessionId: data.sessionId,
                                contextSnapshotId: data.contextSnapshotId,
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
                                sessionId: data.sessionId ?? incomingDraft.sessionId,
                                contextSnapshotId: data.contextSnapshotId ?? incomingDraft.contextSnapshotId,
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

            if (errorCode === "SESSION_EXPIRED" || errorCode === "DRAFT_CANCELLED") {
                draftCancelledAtRef.current = Date.now();
                contextSnapshotIdRef.current = undefined;
                sessionIdRef.current = rotateSessionIdentity(accountKeyRef.current);
                lastConfirmationAttemptRef.current = null;
                removePersistedBookingAttempt(accountKeyRef.current);
                setActiveDraft(null);
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Phiên hoặc bản nháp cũ đã hết hiệu lực. Tôi đã tạo phiên mới; vui lòng chọn lại thông tin đặt lịch.",
                    urgency: "ROUTINE",
                    assistantStatus: "Offline"
                }]);
                return;
            }

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

                const effectiveConfirmationId = activeDraft?.confirmationId || action.payload.confirmationId;
                if (!effectiveConfirmationId) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Chưa có mã xác nhận đặt lịch hợp lệ từ hệ thống. Vui lòng tải lại thông tin lịch khám mới nhất trước khi xác nhận.",
                        urgency: "ROUTINE"
                    }]);
                    break;
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
                const actionSessionId = action.payload.sessionId ?? sessionIdRef.current;
                const actionDraftId = action.payload.draftId ?? activeDraft.draftId;
                const actionDraftVersion = action.payload.draftVersion ?? activeDraft.version;
                const actionSnapshotId = action.payload.contextSnapshotId ?? contextSnapshotIdRef.current;
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
                
                const effectiveAccountKey = bookingAccountKey ?? "anon";
                const currentAttemptId = `${effectiveAccountKey}_${bookingDraftIdAtStart ?? "draft"}_${actionConfirmationId || `${action.payload.slotId}_v${activeDraft.version}`}`;
                const payloadFingerprint = buildStandardBookingPayloadFingerprint(
                    action.payload.specialtyId,
                    action.payload.doctorId,
                    action.payload.slotDate,
                    action.payload.slotId,
                    actionReason
                );
                const legacyWidgetFingerprint = `${action.payload.specialtyId}_${action.payload.doctorId}_${action.payload.slotId}_${action.payload.slotDate}_${action.payload.startTime}_${actionReason}`;

                const doesCandidateMatchTurn = (candidate: PendingBookingAttemptRecord | null): candidate is PendingBookingAttemptRecord => {
                    if (!candidate) return false;
                    if (candidate.accountKey !== effectiveAccountKey) return false;
                    if (candidate.status === "succeeded") return false;
                    if (candidate.payloadFingerprint !== payloadFingerprint && candidate.payloadFingerprint !== legacyWidgetFingerprint) {
                        return false;
                    }
                    if (actionConfirmationId) {
                        if (candidate.confirmationId !== actionConfirmationId) return false;
                    } else if (candidate.confirmationId) {
                        return false;
                    }
                    if (actionSessionId && candidate.sessionId !== actionSessionId) {
                        return false;
                    }
                    if (actionDraftId && candidate.draftId !== actionDraftId) {
                        return false;
                    }
                    if (actionDraftVersion !== undefined && candidate.draftVersion !== actionDraftVersion) {
                        return false;
                    }
                    if (actionSnapshotId && candidate.contextSnapshotId !== actionSnapshotId) {
                        return false;
                    }
                    if (!candidate.draftId && candidate.attemptId && candidate.attemptId !== currentAttemptId) {
                        return false;
                    }
                    return true;
                };

                let idempotencyKey = "";

                try {
                    const bookPayload: CreateAppointmentPayload = {
                        doctorId: action.payload.doctorId,
                        specialtyId: action.payload.specialtyId,
                        appointmentSlotId: action.payload.slotId,
                        reason: actionReason
                    };
                    // A server-issued confirmation carries the complete AI
                    // contract. Legacy/manual actions without one remain a
                    // regular manual booking request and never invent IDs.
                    if (actionConfirmationId) {
                        bookPayload.confirmationId = actionConfirmationId;
                        bookPayload.contextSnapshotId = actionSnapshotId;
                        bookPayload.sessionId = actionSessionId;
                        bookPayload.draftId = actionDraftId;
                        bookPayload.draftVersion = actionDraftVersion;
                    }
                    const inMemoryCandidate = lastConfirmationAttemptRef.current;
                    const persistedCandidate = !doesCandidateMatchTurn(inMemoryCandidate)
                        ? readPersistedBookingAttempt(effectiveAccountKey)
                        : null;

                    if (doesCandidateMatchTurn(inMemoryCandidate)) {
                        idempotencyKey = inMemoryCandidate.key;
                    } else if (doesCandidateMatchTurn(persistedCandidate)) {
                        idempotencyKey = persistedCandidate.key;
                        lastConfirmationAttemptRef.current = persistedCandidate;
                    } else {
                        idempotencyKey = typeof crypto !== "undefined" && crypto.randomUUID
                            ? crypto.randomUUID()
                            : `chat_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;
                    }

                    const attemptRecord: PendingBookingAttemptRecord = {
                        accountKey: effectiveAccountKey,
                        turnIdentity: currentAttemptId,
                        attemptId: currentAttemptId,
                        draftId: bookingDraftIdAtStart,
                        draftVersion: activeDraft.version,
                        confirmationId: actionConfirmationId,
                        sessionId: actionSessionId,
                        contextSnapshotId: actionSnapshotId,
                        payloadFingerprint,
                        key: idempotencyKey,
                        status: "in_flight"
                    };
                    lastConfirmationAttemptRef.current = attemptRecord;
                    writePersistedBookingAttempt(attemptRecord);

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
                        }
                        removePersistedBookingAttempt(effectiveAccountKey, idempotencyKey);
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
                    const apiErr = err as {
                        response?: { status?: number; data?: { errorCode?: string; message?: string } };
                        errorCode?: string;
                        message?: string;
                    };
                    const errorCode = apiErr?.response?.data?.errorCode || apiErr?.errorCode;
                    const status = apiErr?.response?.status;
                    const isDeterministicRejection =
                        errorCode === "SLOT_ALREADY_BOOKED" ||
                        errorCode === "PATIENT_TIME_CONFLICT" ||
                        errorCode === "DOCTOR_NOT_AVAILABLE" ||
                        status === 400 ||
                        status === 409 ||
                        status === 422;

                    if (isDeterministicRejection) {
                        if (lastConfirmationAttemptRef.current?.key === idempotencyKey) {
                            lastConfirmationAttemptRef.current = null;
                        }
                        removePersistedBookingAttempt(effectiveAccountKey, idempotencyKey);
                    } else if (lastConfirmationAttemptRef.current?.key === idempotencyKey) {
                        const uncertainRecord: PendingBookingAttemptRecord = {
                            ...lastConfirmationAttemptRef.current,
                            status: "uncertain"
                        };
                        lastConfirmationAttemptRef.current = uncertainRecord;
                        writePersistedBookingAttempt(uncertainRecord);
                    }

                    if (errorCode === "SLOT_ALREADY_BOOKED") {
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

    const confirmToolAction = async (actionId: string, concurrencyToken?: string): Promise<void> => {
        if (!actionId || loading || submittingBooking) return;
        try {
            const result = await axiosClient.post<{
                sessionId: string;
                concurrencyToken?: string;
            }, AiToolExecutionResult>(`/ai/tool-actions/${actionId}/confirm`, {
                sessionId: sessionIdRef.current,
                concurrencyToken
            });
            setMessages(previous => [...previous, {
                role: "model",
                content: result.status === "completed"
                    ? "Thao tác đã được xác nhận và gửi đến hệ thống ClinicCare."
                    : result.error?.message || "ClinicCare chưa thể hoàn tất thao tác này.",
                urgency: "ROUTINE",
                toolResults: [result],
                assistantStatus: result.status === "completed" ? "Online" : "Degraded"
            }]);
        } catch {
            setErrorMsg("Không thể xác nhận thao tác lúc này. Vui lòng thử lại hoặc mở lại lịch hẹn.");
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
        confirmToolAction,
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
