import React, { createContext, useState, useContext, useEffect } from "react";
import { useAuth } from "../auth/AuthContext";

export interface ChatMessage {
    role: "user" | "model";
    content: string;
    urgency?: string;
    suggestions?: any[];
}

interface ChatContextType {
    pendingSpecialtyId: number | null;
    setPendingSpecialtyId: (id: number | null) => void;
    messages: ChatMessage[];
    setMessages: React.Dispatch<React.SetStateAction<ChatMessage[]>>;
    addMessage: (message: ChatMessage) => void;
}

const ChatContext = createContext<ChatContextType>({
    pendingSpecialtyId: null,
    setPendingSpecialtyId: () => {},
    messages: [],
    setMessages: () => {},
    addMessage: () => {}
});

export const ChatProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { user, loading } = useAuth();
    const [pendingSpecialtyId, setPendingSpecialtyId] = useState<number | null>(null);
    const [messages, setMessages] = useState<ChatMessage[]>([]);
    
    const defaultMessage: ChatMessage = {
        role: "model",
        content: "Chào bạn, tôi là trợ lý y tế AI của ClinicCare. Tôi có thể hỗ trợ thông tin sức khỏe tham khảo và gợi ý chuyên khoa phù hợp. Bạn đang gặp triệu chứng hoặc cần tư vấn về vấn đề sức khỏe nào ạ?"
    };

    // Load messages when user changes
    useEffect(() => {
        if (loading) return;
        if (!user) {
            setMessages([]);
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
        
        setMessages([defaultMessage]);
    }, [user, loading]);

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

    return (
        <ChatContext.Provider value={{ pendingSpecialtyId, setPendingSpecialtyId, messages, setMessages, addMessage }}>
            {children}
        </ChatContext.Provider>
    );
};

export const useChatContext = () => useContext(ChatContext);

