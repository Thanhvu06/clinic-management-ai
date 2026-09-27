import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { sendRoleCopilotMessage, type AiCopilotRequest, type AiCopilotResponse, type AiCopilotResourceContext } from '../../api/aiCopilotApi';
import { getCopilotRoleConfig, type CopilotRole } from './copilotConfig';

export interface UnifiedCopilotMessage {
    id: string;
    role: 'user' | 'assistant';
    content: string;
    response?: AiCopilotResponse;
    error?: boolean;
    retryText?: string;
}

const makeId = (prefix: string) => `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
const makeSession = () => `sess_${makeId('copilot').replace(/[^A-Za-z0-9_-]/g, '')}`.slice(0, 128);

const getResourceContext = (pathname: string, search: string): AiCopilotResourceContext | undefined => {
    const params = new URLSearchParams(search);
    const routeId = (pattern: RegExp): number | undefined => {
        const match = pathname.match(pattern);
        const value = match?.[1] ?? params.get('appointmentId') ?? params.get('visitId') ?? params.get('orderId') ?? params.get('prescriptionId');
        const number = value ? Number(value) : NaN;
        return Number.isSafeInteger(number) && number > 0 ? number : undefined;
    };
    const context: AiCopilotResourceContext = {};
    if (pathname.includes('/appointments/')) context.appointmentId = routeId(/\/appointments\/(\d+)/);
    if (pathname.includes('/visits/')) context.visitId = routeId(/\/visits\/(\d+)/);
    if (pathname.includes('/diagnostic-orders/')) context.diagnosticOrderId = routeId(/\/diagnostic-orders\/(\d+)/);
    if (pathname.includes('/diagnostics/orders/')) context.diagnosticOrderId = routeId(/\/diagnostics\/orders\/(\d+)/);
    if (pathname.includes('/prescriptions/')) context.prescriptionId = routeId(/\/prescriptions\/(\d+)/);
    return Object.values(context).some(value => value !== undefined) ? context : undefined;
};

const initialMessage = (label: string, description: string): UnifiedCopilotMessage => ({
    id: makeId('welcome'), role: 'assistant', content: `${label} sẵn sàng. ${description}`
});

export const useUnifiedCopilot = () => {
    const { user, identityVersion } = useAuth();
    const location = useLocation();
    const role = (user?.role || 'Patient') as CopilotRole;
    const config = useMemo(() => getCopilotRoleConfig(role), [role]);
    const identityKey = `${user?.userId ?? 'anonymous'}:${role}:${identityVersion}`;
    const routeKey = `${location.pathname}${location.search}`;
    const [open, setOpen] = useState(false);
    const [input, setInput] = useState('');
    const [loading, setLoading] = useState(false);
    const [messages, setMessages] = useState<UnifiedCopilotMessage[]>(() => [initialMessage(config.label, config.description)]);
    const controllerRef = useRef<AbortController | null>(null);
    const requestNumberRef = useRef(0);
    const sessionIdRef = useRef(makeSession());
    const conversationIdRef = useRef(makeSession().replace(/^sess_/, 'conv_'));
    const previousIdentityRef = useRef(identityKey);
    const previousRouteRef = useRef(routeKey);
    const retryTextRef = useRef<string | null>(null);

    const reset = useCallback(() => {
        controllerRef.current?.abort();
        requestNumberRef.current += 1;
        retryTextRef.current = null;
        sessionIdRef.current = makeSession();
        conversationIdRef.current = makeSession().replace(/^sess_/, 'conv_');
        setLoading(false);
        setInput('');
        setMessages([initialMessage(config.label, config.description)]);
    }, [config]);

    useEffect(() => {
        if (previousIdentityRef.current !== identityKey || previousRouteRef.current !== routeKey) {
            previousIdentityRef.current = identityKey;
            previousRouteRef.current = routeKey;
            reset();
        }
        return () => controllerRef.current?.abort();
    }, [identityKey, routeKey, reset]);

    const send = useCallback(async (value = input) => {
        const message = value.trim();
        if (!message || loading || message.length > 500) return;
        const requestNumber = ++requestNumberRef.current;
        const controller = new AbortController();
        controllerRef.current?.abort();
        controllerRef.current = controller;
        retryTextRef.current = null;
        setLoading(true);
        setInput('');
        setMessages(previous => [...previous, { id: makeId('user'), role: 'user', content: message }]);
        const request: AiCopilotRequest = {
            message,
            conversationId: conversationIdRef.current,
            sessionId: sessionIdRef.current,
            currentRoute: location.pathname,
            resourceContext: getResourceContext(location.pathname, location.search),
            clientTurnId: makeId('turn'),
            locale: 'vi-VN',
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone
        };

        try {
            const response = await sendRoleCopilotMessage(request, controller.signal);
            if (controller.signal.aborted || requestNumber !== requestNumberRef.current) return;
            if (response.conversationId) conversationIdRef.current = response.conversationId;
            if (response.conversationId && response.conversationId !== conversationIdRef.current) conversationIdRef.current = response.conversationId;
            setMessages(previous => [...previous, { id: response.turnId || makeId('assistant'), role: 'assistant', content: response.message, response }]);
        } catch (error) {
            if (controller.signal.aborted || requestNumber !== requestNumberRef.current) return;
            retryTextRef.current = message;
            const detail = error && typeof error === 'object' && 'message' in error ? String((error as { message?: unknown }).message ?? '') : '';
            setMessages(previous => [...previous, { id: makeId('error'), role: 'assistant', error: true, retryText: message, content: detail || 'Không thể kết nối Copilot. Bạn có thể thử lại.' }]);
        } finally {
            if (requestNumber === requestNumberRef.current) setLoading(false);
        }
    }, [input, loading, location.pathname, location.search]);

    const abort = useCallback(() => {
        controllerRef.current?.abort();
        requestNumberRef.current += 1;
        setLoading(false);
    }, []);

    const retry = useCallback(() => {
        const value = retryTextRef.current;
        if (value) void send(value);
    }, [send]);

    return { user, role, config, open, setOpen, input, setInput, loading, messages, send, reset, abort, retry, resourceContext: getResourceContext(location.pathname, location.search) };
};
