import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useChatContext } from '../../contexts/ChatContext';

const consultationPath = '/patient/ai-consultation';
const PreviousChatRoute = createContext('/');

export function ChatRouteHistory({ children }: { children: ReactNode }) {
    const location = useLocation();
    const [previous, setPrevious] = useState(location);
    if (location.key !== previous.key && location.pathname !== consultationPath) {
        setPrevious(location);
    }
    const destination = previous.pathname === consultationPath
        ? '/'
        : `${previous.pathname}${previous.search}${previous.hash}`;
    return <PreviousChatRoute.Provider value={destination}>{children}</PreviousChatRoute.Provider>;
}

export function ChatConsultationRedirect() {
    const { openChat } = useChatContext();
    const navigate = useNavigate();
    const previous = useContext(PreviousChatRoute);
    const redirected = useRef(false);

    useEffect(() => {
        if (redirected.current) return;
        redirected.current = true;
        openChat();
        void navigate(previous, { replace: true });
    }, [navigate, openChat, previous]);

    return null;
}
