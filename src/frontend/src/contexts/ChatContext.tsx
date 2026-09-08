import React, { createContext, useState, useContext, useEffect } from "react";
import { useAuth } from "../auth/AuthContext";
import type { ChatMessage, AiBookingDraft, AiAction } from "../types/ai";

export type { ChatMessage } from "../types/ai";

interface ChatContextType {
    pendingSpecialtyId: number | null;
    setPendingSpecialtyId: (id: number | null) => void;
    activeDraft: AiBookingDraft | null;
    setActiveDraft: (draft: AiBookingDraft | null) => void;
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

export const ChatProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { user, loading } = useAuth();
    const currentUserId = user?.id ?? null;
    const [boundUserId, setBoundUserId] = useState<number | null>(currentUserId);
    const [pendingSpecialtyId, setPendingSpecialtyId] = useState<number | null>(null);
    const [activeDraft, setActiveDraft] = useState<AiBookingDraft | null>(null);
    const [messages, setMessages] = useState<ChatMessage[]>(currentUserId ? [DEFAULT_AI_MESSAGE] : []);

    // Zero-frame leakage: If user changed since last render, reset synchronously during render
    if (currentUserId !== boundUserId) {
        setBoundUserId(currentUserId);
        setPendingSpecialtyId(null);
        setActiveDraft(null);
        setMessages(currentUserId ? [DEFAULT_AI_MESSAGE] : []);
    }

    // Load messages and draft when user changes with race-condition protection
    useEffect(() => {
        let active = true;

        if (loading) return;

        if (!user) {
            setMessages([]);
            setActiveDraft(null);
            setPendingSpecialtyId(null);
            return;
        }

        let loadedMessages: ChatMessage[] | null = null;
        let loadedDraft: AiBookingDraft | null = null;

        // 1. Load chat history
        const key = `cliniccare_chat_history_${user.id}`;
        try {
            const saved = sessionStorage.getItem(key);
            if (saved) {
                const parsed = JSON.parse(saved);
                if (Array.isArray(parsed)) {
                    const validMessages = parsed.filter(validateChatMessageSchema);
                    if (validMessages.length > 0) {
                        loadedMessages = validMessages;
                    }
                }
            }
        } catch (e) {
            console.error("Failed to parse chat history", e);
        }

        // 2. Load draft independently
        const draftKey = `cliniccare_booking_draft_${user.id}`;
        try {
            const savedDraft = sessionStorage.getItem(draftKey);
            if (savedDraft) {
                const parsedDraft = JSON.parse(savedDraft);
                if (validateBookingDraftSchema(parsedDraft)) {
                    loadedDraft = parsedDraft;
                }
            }
        } catch (e) {
            console.error("Failed to parse booking draft", e);
        }

        if (!active) return;

        if (loadedMessages && loadedMessages.length > 0) {
            setMessages(loadedMessages);
        } else {
            setMessages([DEFAULT_AI_MESSAGE]);
        }
        setActiveDraft(loadedDraft);

        return () => {
            active = false;
        };
    }, [user?.id, loading]);

    // Save activeDraft when it changes
    useEffect(() => {
        if (!user || loading || user.id !== boundUserId) return;
        const draftKey = `cliniccare_booking_draft_${user.id}`;
        try {
            if (activeDraft) {
                sessionStorage.setItem(draftKey, JSON.stringify(activeDraft));
            } else {
                sessionStorage.removeItem(draftKey);
            }
        } catch (e) {
            console.error("Failed to save booking draft", e);
        }
    }, [activeDraft, user?.id, boundUserId, loading]);

    // Save messages when they change
    useEffect(() => {
        if (!user || loading || user.id !== boundUserId || messages.length === 0) return;

        const key = `cliniccare_chat_history_${user.id}`;
        try {
            // Keep last 30 messages to avoid quota issues
            const messagesToSave = messages.slice(-30);
            sessionStorage.setItem(key, JSON.stringify(messagesToSave));
        } catch (e) {
            console.error("Failed to save chat history", e);
        }
    }, [messages, user?.id, boundUserId, loading]);

    const addMessage = (message: ChatMessage) => {
        if (!user || user.id !== boundUserId) return;
        if (!validateChatMessageSchema(message)) return;
        setMessages(prev => [...prev, message]);
    };

    const clearChat = () => {
        setMessages(user ? [DEFAULT_AI_MESSAGE] : []);
        setActiveDraft(null);
        setPendingSpecialtyId(null);
        if (user) {
            const key = `cliniccare_chat_history_${user.id}`;
            const draftKey = `cliniccare_booking_draft_${user.id}`;
            sessionStorage.removeItem(key);
            sessionStorage.removeItem(draftKey);
        }
    };

    // Loaded-user guard: Provider value ensures that if user is not authenticated or boundUserId hasn't matched,
    // we never expose another user's messages or draft
    const isUserActive = !!user && user.id === boundUserId;
    const safeMessages = isUserActive ? messages : (user ? [DEFAULT_AI_MESSAGE] : []);
    const safeDraft = isUserActive ? activeDraft : null;
    const safePendingSpecialtyId = isUserActive ? pendingSpecialtyId : null;

    return (
        <ChatContext.Provider value={{
            pendingSpecialtyId: safePendingSpecialtyId,
            setPendingSpecialtyId,
            activeDraft: safeDraft,
            setActiveDraft,
            messages: safeMessages,
            setMessages,
            addMessage,
            clearChat
        }}>
            {children}
        </ChatContext.Provider>
    );
};

export const useChatContext = () => useContext(ChatContext);

