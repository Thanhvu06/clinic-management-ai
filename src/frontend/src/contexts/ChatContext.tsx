import React, { createContext, useState, useContext, useEffect } from "react";
import { useAuth } from "../auth/AuthContext";
import type { ChatMessage, AiBookingDraft } from "../types/ai";

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

export const ChatProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { user, loading } = useAuth();
    const [pendingSpecialtyId, setPendingSpecialtyId] = useState<number | null>(null);
    const [activeDraft, setActiveDraft] = useState<AiBookingDraft | null>(null);
    const [messages, setMessages] = useState<ChatMessage[]>([]);

    const defaultMessage: ChatMessage = {
        role: "model",
        content: "Chào bạn, tôi là Trợ lý ClinicCare AI. Tôi có thể hỗ trợ giải đáp thông tin sức khỏe tham khảo, gợi ý chuyên khoa, tra cứu bác sĩ và đặt lịch khám trực tiếp qua trò chuyện. Bạn đang cần tư vấn vấn đề gì hôm nay?"
    };

    // Load messages when user changes
    useEffect(() => {
        if (loading) return;
        if (!user) {
            setMessages([]);
            setActiveDraft(null);
            return;
        }

        const key = `cliniccare_chat_history_${user.id}`;
        try {
            const saved = sessionStorage.getItem(key);
            if (saved) {
                const parsed = JSON.parse(saved);
                if (Array.isArray(parsed) && parsed.length > 0) {
                    const validMessages = parsed.filter(m => m && (m.role === 'user' || m.role === 'model') && typeof m.content === 'string');
                    if (validMessages.length > 0) {
                        setMessages(validMessages);
                        return;
                    }
                }
            }
        } catch (e) {
            console.error("Failed to parse chat history", e);
        }

        const draftKey = `cliniccare_booking_draft_${user.id}`;
        try {
            const savedDraft = sessionStorage.getItem(draftKey);
            if (savedDraft) {
                const parsedDraft = JSON.parse(savedDraft);
                if (parsedDraft && typeof parsedDraft === "object") {
                    setActiveDraft(parsedDraft);
                }
            }
        } catch (e) {
            console.error("Failed to parse booking draft", e);
        }

        setMessages([defaultMessage]);
    }, [user, loading]);

    // Save activeDraft when it changes
    useEffect(() => {
        if (!user || loading) return;
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
    }, [activeDraft, user, loading]);

    // Save messages when they change
    useEffect(() => {
        if (!user || loading || messages.length === 0) return;

        const key = `cliniccare_chat_history_${user.id}`;
        try {
            // Keep last 30 messages to avoid quota issues
            const messagesToSave = messages.slice(-30);
            sessionStorage.setItem(key, JSON.stringify(messagesToSave));
        } catch (e) {
            console.error("Failed to save chat history", e);
        }
    }, [messages, user, loading]);

    const addMessage = (message: ChatMessage) => {
        setMessages(prev => [...prev, message]);
    };

    const clearChat = () => {
        setMessages([defaultMessage]);
        setActiveDraft(null);
        if (user) {
            const key = `cliniccare_chat_history_${user.id}`;
            const draftKey = `cliniccare_booking_draft_${user.id}`;
            sessionStorage.removeItem(key);
            sessionStorage.removeItem(draftKey);
        }
    };

    return (
        <ChatContext.Provider value={{
            pendingSpecialtyId,
            setPendingSpecialtyId,
            activeDraft,
            setActiveDraft,
            messages,
            setMessages,
            addMessage,
            clearChat
        }}>
            {children}
        </ChatContext.Provider>
    );
};

export const useChatContext = () => useContext(ChatContext);
