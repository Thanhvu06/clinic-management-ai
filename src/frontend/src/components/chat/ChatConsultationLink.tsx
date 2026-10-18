import { Link, type LinkProps } from 'react-router-dom';
import { useChatContext } from '../../contexts/ChatContext';

export function ChatConsultationLink({ onClick, ...props }: LinkProps) {
    const { openChat } = useChatContext();
    return <Link {...props} onClick={event => {
        onClick?.(event);
        if (event.defaultPrevented) return;
        event.preventDefault();
        openChat();
    }} />;
}
