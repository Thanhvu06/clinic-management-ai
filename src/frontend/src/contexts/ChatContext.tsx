import React, { createContext, useState, useContext, useEffect, useCallback, useRef } from "react";
import { useAuth } from "../auth/AuthContext";
import type { ChatMessage, AiBookingDraft, AiAction } from "../types/ai";

export type { ChatMessage } from "../types/ai";

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
    clearChat: () => {}
});

export function validateActionSchema(action: unknown): action is AiAction {
    if (!action || typeof action !== "object") return false;
    const a = action as Record<string, unknown>;
    if (typeof a.id !== "string" || typeof a.label !== "string" || typeof a.type !== "string") return false;
    if (a.style !== "primary" && a.style !== "secondary" && a.style !== "danger") return false;
    if (typeof a.requiresAuthentication !== "boolean" || typeof a.requiresConfirmation !== "boolean") return false;
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
                const validMessages = parsed.filter(validateChatMessageSchema);
                if (validMessages.length > 0) return validMessages;
            }
        }
    } catch (error) {
        console.error("Failed to parse chat history", error);
    }
    return [DEFAULT_AI_MESSAGE];
};

const loadStoredDraft = (accountKey: string | null): AiBookingDraft | null => {
    if (!accountKey) return null;
    try {
        const saved = sessionStorage.getItem(`cliniccare_booking_draft_${accountKey}`);
        if (saved) {
            const parsed: unknown = JSON.parse(saved);
            if (validateBookingDraftSchema(parsed)) return parsed;
        }
    } catch (error) {
        console.error("Failed to parse booking draft", error);
    }
    return null;
};

const AccountBoundChatProvider: React.FC<{
    accountKey: string | null;
    children: React.ReactNode;
}> = ({ accountKey, children }) => {
    const [pendingSpecialtyId, setPendingSpecialtyIdState] = useState<number | null>(null);
    const [activeDraft, setActiveDraftState] = useState<AiBookingDraft | null>(() => loadStoredDraft(accountKey));
    const [messages, setMessagesState] = useState<ChatMessage[]>(() => loadStoredMessages(accountKey));
    const bookingContextVersionRef = useRef(0);

    const setActiveDraft = useCallback<React.Dispatch<React.SetStateAction<AiBookingDraft | null>>>((update) => {
        if (!accountKey) return;
        bookingContextVersionRef.current += 1;
        setActiveDraftState(update);
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
        const draftKey = `cliniccare_booking_draft_${accountKey}`;
        try {
            if (activeDraft) {
                sessionStorage.setItem(draftKey, JSON.stringify(activeDraft));
            } else {
                sessionStorage.removeItem(draftKey);
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
        sessionStorage.removeItem(`cliniccare_chat_history_${accountKey}`);
        sessionStorage.removeItem(`cliniccare_booking_draft_${accountKey}`);
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
            clearChat
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
