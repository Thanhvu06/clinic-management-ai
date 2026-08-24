
import React, { createContext, useState, useContext } from "react";

interface ChatContextType {
    pendingSpecialtyId: number | null;
    setPendingSpecialtyId: (id: number | null) => void;
}

const ChatContext = createContext<ChatContextType>({
    pendingSpecialtyId: null,
    setPendingSpecialtyId: () => {}
});

export const ChatProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [pendingSpecialtyId, setPendingSpecialtyId] = useState<number | null>(null);
    return (
        <ChatContext.Provider value={{ pendingSpecialtyId, setPendingSpecialtyId }}>
            {children}
        </ChatContext.Provider>
    );
};

export const useChatContext = () => useContext(ChatContext);

