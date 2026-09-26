import React, { createContext, useState, useContext, useEffect, useCallback, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import type { ChatMessage, AiBookingDraft, AiAction } from "../types/ai";

export type { ChatMessage } from "../types/ai";

export type AssistantStatus = "Unchecked" | "Online" | "Degraded" | "Offline";

interface ChatContextType {
    pendingSpecialtyId: number | null;
    setPendingSpecialtyId: (id: number | null) => void;
    activeDraft: AiBookingDraft | null;
    setActiveDraft: React.Dispatch<React.SetStateAction<AiBookingDraft | null>>;
    getBookingContextVersion: () => number;
    messages: ChatMessage[];
    setMessages: React.Dispatch<React.SetStateAction<ChatMessage[]>>;
    addMessage: (message: ChatMessage) => void;
    clearChat: () => void;
    aiAssistantStatus: AssistantStatus;
    setAiAssistantStatus: (status: AssistantStatus) => void;
}

const ChatContext = createContext<ChatContextType>({
    pendingSpecialtyId: null,
    setPendingSpecialtyId: () => {},
    activeDraft: null,
    setActiveDraft: () => {},
    getBookingContextVersion: () => 0,
    messages: [],
    setMessages: () => {},
    addMessage: () => {},
    clearChat: () => {},
    aiAssistantStatus: "Unchecked",
    setAiAssistantStatus: () => {}
});

export function validateActionSchema(action: unknown): action is AiAction {
    if (!action || typeof action !== "object") return false;
    const a = action as Record<string, unknown>;
    if (typeof a.id !== "string" || typeof a.label !== "string" || typeof a.type !== "string") return false;
    if (a.style !== "primary" && a.style !== "secondary" && a.style !== "danger") return false;
    if (typeof a.requiresAuthentication !== "boolean" || typeof a.requiresConfirmation !== "boolean") return false;
    if (a.draftVersion !== undefined) {
        if (typeof a.draftVersion !== "number" || !Number.isInteger(a.draftVersion) || a.draftVersion < 1) return false;
    }
    if (!a.payload || typeof a.payload !== "object") return false;

    const payload = a.payload as Record<string, unknown>;
    if (payload.slotDate !== undefined && payload.slotDate !== null) {
        if (typeof payload.slotDate !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(payload.slotDate)) return false;
    }
    if (payload.startTime !== undefined && payload.startTime !== null) {
        if (typeof payload.startTime !== "string" || !/^\d{2}:\d{2}(:\d{2})?$/.test(payload.startTime)) return false;
    }
    if (payload.endTime !== undefined && payload.endTime !== null) {
        if (typeof payload.endTime !== "string" || !/^\d{2}:\d{2}(:\d{2})?$/.test(payload.endTime)) return false;
    }
    if (payload.targetUrl !== undefined && payload.targetUrl !== null) {
        if (typeof payload.targetUrl !== "string") return false;
        if (payload.targetUrl !== "tel:115" && (!payload.targetUrl.startsWith("/") || payload.targetUrl.startsWith("//"))) return false;
    }
    if (payload.phoneNumber !== undefined && payload.phoneNumber !== null && typeof payload.phoneNumber !== "string") return false;
    if (payload.address !== undefined && payload.address !== null && typeof payload.address !== "string") return false;
    if (payload.facilityName !== undefined && payload.facilityName !== null && typeof payload.facilityName !== "string") return false;
    if (payload.draftVersion !== undefined) {
        if (typeof payload.draftVersion !== "number" || !Number.isInteger(payload.draftVersion) || payload.draftVersion < 1) return false;
    }
    return true;
}

export function validateBookingDraftSchema(item: unknown): item is AiBookingDraft {
    if (!item || typeof item !== "object") return false;
    const draft = item as Record<string, unknown>;
    if (typeof draft.isComplete !== "boolean") return false;
    if (draft.specialtyId !== undefined && draft.specialtyId !== null && typeof draft.specialtyId !== "number") return false;
    if (draft.doctorId !== undefined && draft.doctorId !== null && typeof draft.doctorId !== "number") return false;
    if (draft.slotId !== undefined && draft.slotId !== null && typeof draft.slotId !== "number") return false;
    if (draft.slotDate !== undefined && draft.slotDate !== null) {
        if (typeof draft.slotDate !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(draft.slotDate)) return false;
    }
    if (draft.startTime !== undefined && draft.startTime !== null) {
        if (typeof draft.startTime !== "string" || !/^\d{2}:\d{2}(:\d{2})?$/.test(draft.startTime)) return false;
    }
    if (draft.endTime !== undefined && draft.endTime !== null) {
        if (typeof draft.endTime !== "string" || !/^\d{2}:\d{2}(:\d{2})?$/.test(draft.endTime)) return false;
    }
    if (draft.reason !== undefined && draft.reason !== null) {
        if (typeof draft.reason !== "string" || draft.reason.length > 500) return false;
    }
    if (draft.version !== undefined) {
        if (typeof draft.version !== "number" || !Number.isInteger(draft.version) || draft.version < 1) return false;
    }
    return true;
}

export function validateChatMessageSchema(item: unknown): item is ChatMessage {
    if (!item || typeof item !== "object") return false;
    const msg = item as Record<string, unknown>;
    if (msg.role !== "user" && msg.role !== "model") return false;
    if (typeof msg.content !== "string") return false;
    if (msg.actions !== undefined && msg.actions !== null) {
        if (!Array.isArray(msg.actions)) return false;
        if (!msg.actions.every(validateActionSchema)) return false;
    }
    if (msg.bookingDraft !== undefined && msg.bookingDraft !== null) {
        if (!validateBookingDraftSchema(msg.bookingDraft)) return false;
    }
    if (msg.urgency !== undefined && msg.urgency !== null) {
        if (msg.urgency !== "ROUTINE" && msg.urgency !== "SOON" && msg.urgency !== "EMERGENCY") return false;
    }
    if (msg.assistantStatus !== undefined && msg.assistantStatus !== null) {
        if (msg.assistantStatus !== "Unchecked" && msg.assistantStatus !== "Online" && msg.assistantStatus !== "Degraded" && msg.assistantStatus !== "Offline") return false;
    }
    return true;
}

export const DEFAULT_AI_MESSAGE: ChatMessage = {
    role: "model",
    content: "Chào bạn, tôi là Trợ lý ClinicCare AI. Tôi có thể hỗ trợ giải đáp thông tin sức khỏe tham khảo, gợi ý chuyên khoa, tra cứu bác sĩ và đặt lịch khám trực tiếp qua trò chuyện. Bạn đang cần tư vấn vấn đề gì hôm nay?"
};

const loadStoredMessages = (accountKey: string | null): ChatMessage[] => {
    if (!accountKey) return [];
    try {
        const saved = sessionStorage.getItem(`cliniccare_chat_history_${accountKey}`);
        if (saved) {
            const parsed: unknown = JSON.parse(saved);
            if (Array.isArray(parsed)) {
                const sanitized = parsed.map(msg => {
                    if (msg && typeof msg === "object") {
                        const m = msg as Record<string, unknown>;
                        if (Array.isArray(m.actions)) {
                            m.actions = m.actions.map(act => {
                                if (act && typeof act === "object") {
                                    const a = act as Record<string, unknown>;
                                    if (a.draftVersion === null) delete a.draftVersion;
                                    if (a.payload && typeof a.payload === "object") {
                                        const p = a.payload as Record<string, unknown>;
                                        if (p.draftVersion === null) delete p.draftVersion;
                                    }
                                }
                                return act;
                            });
                        }
                        if (m.bookingDraft && typeof m.bookingDraft === "object") {
                            const bd = m.bookingDraft as Record<string, unknown>;
                            if (bd.version === null || bd.version === undefined || typeof bd.version !== "number" || !Number.isInteger(bd.version) || bd.version < 1) {
                                bd.version = 1;
                            }
                        }
                    }
                    return msg;
                });
                const validMessages = sanitized.filter(validateChatMessageSchema);
                if (validMessages.length > 0) return validMessages;
            }
        }
    } catch (error) {
        console.error("Failed to parse chat history", error);
    }
    return [DEFAULT_AI_MESSAGE];
};

const generateDraftIdentity = (): string =>
    typeof crypto !== "undefined" && crypto.randomUUID
        ? `draft_${crypto.randomUUID()}`
        : `draft_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`;

const loadStoredDraft = (accountKey: string | null): AiBookingDraft | null => {
    if (!accountKey) return null;
    try {
        const saved = sessionStorage.getItem(`cliniccare_booking_draft_${accountKey}`);
        if (saved) {
            const parsed: unknown = JSON.parse(saved);
            if (parsed && typeof parsed === "object") {
                const raw = parsed as Record<string, unknown>;
                if (raw.version === null || raw.version === undefined || typeof raw.version !== "number" || !Number.isInteger(raw.version) || raw.version < 1) {
                    raw.version = 1;
                }
                if (typeof raw.draftId !== "string" || !raw.draftId) {
                    raw.draftId = generateDraftIdentity();
                }
                if (validateBookingDraftSchema(raw)) return raw;
            }
        }
    } catch (error) {
        console.error("Failed to parse booking draft", error);
    }
    return null;
};

export interface PendingBookingAttemptRecord {
    accountKey?: string;
    turnIdentity: string;
    attemptId?: string;
    draftId?: string;
    draftVersion?: number;
    confirmationId?: string;
    sessionId?: string;
    contextSnapshotId?: string;
    payloadFingerprint: string;
    key: string;
    status: "in_flight" | "uncertain" | "succeeded";
    isManualFormAttempt?: boolean;
    createdAtMs?: number;
}

const PENDING_ATTEMPT_TTL_MS = 15 * 60 * 1000;

export const getPendingBookingStorageKey = (accountKey: string | null | undefined): string =>
    `cliniccare_pending_booking_attempt_${accountKey ?? "anon"}`;

export const getLegacyWidgetStorageKey = (accountKey: string | null | undefined): string =>
    `cliniccare_pending_widget_attempt_${accountKey ?? "anon"}`;

export const buildStandardBookingPayloadFingerprint = (
    specialtyId: number | string,
    doctorId: number | string,
    slotDate: string,
    slotId: number | string,
    reason: string,
    revisitRequestId?: number | string | null
): string => `${revisitRequestId ?? "std"}_${specialtyId}_${doctorId}_${slotDate}_${slotId}_${reason.trim()}`;

export function validatePendingBookingAttempt(
    raw: unknown,
    expectedAccountKey?: string | null
): raw is PendingBookingAttemptRecord {
    if (!raw || typeof raw !== "object") return false;
    const r = raw as Record<string, unknown>;
    if (typeof r.key !== "string" || r.key.trim().length === 0) return false;
    if (typeof r.payloadFingerprint !== "string" || r.payloadFingerprint.trim().length === 0) return false;

    const effectiveExpectedAccount = expectedAccountKey ?? "anon";
    const recordAccountKey = typeof r.accountKey === "string" && r.accountKey.trim().length > 0
        ? r.accountKey.trim()
        : (typeof r.attemptId === "string" && r.attemptId.startsWith(`${effectiveExpectedAccount}_`)
            ? effectiveExpectedAccount
            : null);
    if (!recordAccountKey || recordAccountKey !== effectiveExpectedAccount) return false;

    const recordTurnIdentity = typeof r.turnIdentity === "string" && r.turnIdentity.trim().length > 0
        ? r.turnIdentity.trim()
        : (typeof r.attemptId === "string" && r.attemptId.trim().length > 0 ? r.attemptId.trim() : null);
    if (!recordTurnIdentity) return false;

    const status = r.status ?? "in_flight";
    if (status !== "in_flight" && status !== "uncertain" && status !== "succeeded") return false;
    if (r.draftId !== undefined && r.draftId !== null && (typeof r.draftId !== "string" || r.draftId.trim().length === 0)) {
        return false;
    }
    if (r.draftVersion !== undefined && r.draftVersion !== null) {
        if (typeof r.draftVersion !== "number" || !Number.isInteger(r.draftVersion) || r.draftVersion < 1) return false;
    }
    if (r.confirmationId !== undefined && r.confirmationId !== null && (typeof r.confirmationId !== "string" || r.confirmationId.trim().length === 0)) {
        return false;
    }
    if (r.createdAtMs !== undefined && r.createdAtMs !== null) {
        if (typeof r.createdAtMs !== "number" || !Number.isFinite(r.createdAtMs)) return false;
        if (Date.now() - r.createdAtMs > PENDING_ATTEMPT_TTL_MS) return false;
    }

    r.accountKey = recordAccountKey;
    r.turnIdentity = recordTurnIdentity;
    r.status = status;
    return true;
}

export function readPersistedBookingAttempt(accountKey: string | null | undefined): PendingBookingAttemptRecord | null {
    const effectiveAccount = accountKey ?? "anon";
    const keysToCheck = [
        getPendingBookingStorageKey(effectiveAccount),
        getLegacyWidgetStorageKey(effectiveAccount)
    ];

    for (const storageKey of keysToCheck) {
        try {
            const raw = sessionStorage.getItem(storageKey);
            if (!raw) continue;
            const parsed: unknown = JSON.parse(raw);
            if (!validatePendingBookingAttempt(parsed, effectiveAccount)) {
                sessionStorage.removeItem(storageKey);
                continue;
            }
            if (parsed.status === "succeeded") {
                sessionStorage.removeItem(storageKey);
                continue;
            }
            return parsed;
        } catch {
            try {
                sessionStorage.removeItem(storageKey);
            } catch {
                // ignore storage error
            }
        }
    }
    return null;
}

export function writePersistedBookingAttempt(record: PendingBookingAttemptRecord): void {
    const effectiveAccount = record.accountKey || "anon";
    const normalized: PendingBookingAttemptRecord = {
        ...record,
        accountKey: effectiveAccount,
        attemptId: record.attemptId ?? record.turnIdentity,
        createdAtMs: record.createdAtMs ?? Date.now()
    };
    const serialized = JSON.stringify(normalized);
    try {
        sessionStorage.setItem(getPendingBookingStorageKey(effectiveAccount), serialized);
        sessionStorage.setItem(getLegacyWidgetStorageKey(effectiveAccount), serialized);
    } catch {
        // ignore storage error
    }
}

export function removePersistedBookingAttempt(accountKey: string | null | undefined, onlyIfKeyMatches?: string): void {
    const effectiveAccount = accountKey ?? "anon";
    const keysToCheck = [
        getPendingBookingStorageKey(effectiveAccount),
        getLegacyWidgetStorageKey(effectiveAccount)
    ];
    for (const storageKey of keysToCheck) {
        try {
            if (!onlyIfKeyMatches) {
                sessionStorage.removeItem(storageKey);
            } else {
                const raw = sessionStorage.getItem(storageKey);
                if (!raw) continue;
                const parsed = JSON.parse(raw) as { key?: string };
                if (!parsed || typeof parsed !== "object" || parsed.key === onlyIfKeyMatches) {
                    sessionStorage.removeItem(storageKey);
                }
            }
        } catch {
            try {
                sessionStorage.removeItem(storageKey);
            } catch {
                // ignore storage error
            }
        }
    }
}

const AccountBoundChatProvider: React.FC<{
    accountKey: string | null;
    children: React.ReactNode;
}> = ({ accountKey, children }) => {
    const [pendingSpecialtyId, setPendingSpecialtyIdState] = useState<number | null>(null);
    const [activeDraft, setActiveDraftState] = useState<AiBookingDraft | null>(() => loadStoredDraft(accountKey));
    const [messages, setMessagesState] = useState<ChatMessage[]>(() => loadStoredMessages(accountKey));
    const [aiAssistantStatus, setAiAssistantStatus] = useState<AssistantStatus>("Unchecked");
    const bookingContextVersionRef = useRef(0);
    const prevActiveDraftRef = useRef<AiBookingDraft | null>(activeDraft);

    const setActiveDraft = useCallback<React.Dispatch<React.SetStateAction<AiBookingDraft | null>>>((update) => {
        if (!accountKey) return;
        bookingContextVersionRef.current += 1;
        setActiveDraftState(previous => {
            const resolved = typeof update === "function" ? update(previous) : update;
            if (!resolved) {
                removePersistedBookingAttempt(accountKey);
                return null;
            }
            const persistedAttempt = !previous ? readPersistedBookingAttempt(accountKey) : null;
            const nextDraftId = resolved.draftId || previous?.draftId || persistedAttempt?.draftId || generateDraftIdentity();
            if (previous?.draftId && nextDraftId !== previous.draftId) {
                removePersistedBookingAttempt(accountKey);
            }
            return {
                ...resolved,
                draftId: nextDraftId
            };
        });
    }, [accountKey]);

    const setPendingSpecialtyId = useCallback((id: number | null) => {
        if (!accountKey) return;
        setPendingSpecialtyIdState(id);
    }, [accountKey]);

    const setMessages = useCallback<React.Dispatch<React.SetStateAction<ChatMessage[]>>>((update) => {
        if (!accountKey) return;
        setMessagesState(update);
    }, [accountKey]);

    const getBookingContextVersion = useCallback(() => bookingContextVersionRef.current, []);

    useEffect(() => {
        if (!accountKey) return;
        const prevDraft = prevActiveDraftRef.current;
        prevActiveDraftRef.current = activeDraft;
        const draftKey = `cliniccare_booking_draft_${accountKey}`;
        try {
            if (activeDraft) {
                sessionStorage.setItem(draftKey, JSON.stringify(activeDraft));
            } else {
                sessionStorage.removeItem(draftKey);
                // Only clear persisted booking attempt when transitioning from an existing draft to null,
                // never on initial mount/reload when activeDraft starts as null (manual form booking).
                if (prevDraft !== null) {
                    removePersistedBookingAttempt(accountKey);
                }
            }
        } catch (error) {
            console.error("Failed to save booking draft", error);
        }
    }, [activeDraft, accountKey]);

    useEffect(() => {
        if (!accountKey || messages.length === 0) return;
        try {
            sessionStorage.setItem(`cliniccare_chat_history_${accountKey}`, JSON.stringify(messages.slice(-30)));
        } catch (error) {
            console.error("Failed to save chat history", error);
        }
    }, [messages, accountKey]);

    const addMessage = (message: ChatMessage) => {
        if (!accountKey || !validateChatMessageSchema(message)) return;
        setMessagesState(previous => [...previous, message]);
    };

    const clearChat = () => {
        if (!accountKey) return;
        bookingContextVersionRef.current += 1;
        setMessagesState([DEFAULT_AI_MESSAGE]);
        setActiveDraftState(null);
        setPendingSpecialtyIdState(null);
        setAiAssistantStatus("Unchecked");
        sessionStorage.removeItem(`cliniccare_chat_history_${accountKey}`);
        sessionStorage.removeItem(`cliniccare_booking_draft_${accountKey}`);
        sessionStorage.removeItem(`cliniccare_pending_booking_attempt_${accountKey}`);
    };

    return (
        <ChatContext.Provider value={{
            pendingSpecialtyId,
            setPendingSpecialtyId,
            activeDraft,
            setActiveDraft,
            getBookingContextVersion,
            messages,
            setMessages,
            addMessage,
            clearChat,
            aiAssistantStatus,
            setAiAssistantStatus
        }}>
            {children}
        </ChatContext.Provider>
    );
};

export const ChatProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { user } = useAuth();
    const accountKey = user
        ? (user.userId || (user.id !== undefined ? String(user.id) : null))
        : null;

    return (
        <AccountBoundChatProvider key={accountKey ?? "anonymous"} accountKey={accountKey}>
            {children}
        </AccountBoundChatProvider>
    );
};

export const useChatContext = () => useContext(ChatContext);
