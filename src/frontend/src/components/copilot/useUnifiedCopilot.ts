import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import {
    confirmRoleAction,
    getRoleCopilotCatalog,
    prepareRoleAction,
    sendRoleCopilotMessage,
    type AiCopilotCatalog,
    type AiCopilotRequest,
    type AiCopilotResponse,
    type AiCopilotResourceContext,
    type AiCopilotTool,
    type AiActionPreview
} from '../../api/aiCopilotApi';
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
    const queryId = (name: string): number | undefined => {
        const value = params.get(name);
        const number = value ? Number(value) : NaN;
        return Number.isSafeInteger(number) && number > 0 ? number : undefined;
    };
    const routeId = (pattern: RegExp, queryName: string): number | undefined => {
        const match = pathname.match(pattern);
        const value = match?.[1] ?? params.get(queryName);
        const number = value ? Number(value) : NaN;
        return Number.isSafeInteger(number) && number > 0 ? number : undefined;
    };
    const context: AiCopilotResourceContext = {};
    if (pathname.includes('/appointments/')) context.appointmentId = routeId(/\/appointments\/(\d+)/, 'appointmentId');
    if (pathname.includes('/visits/')) context.visitId = routeId(/\/visits\/(\d+)/, 'visitId');
    if (pathname.includes('/diagnostic-orders/')) context.diagnosticOrderId = routeId(/\/diagnostic-orders\/(\d+)/, 'diagnosticOrderId');
    if (pathname.includes('/diagnostics/orders/')) context.diagnosticOrderId = routeId(/\/diagnostics\/orders\/(\d+)/, 'diagnosticOrderId');
    if (pathname.includes('/prescriptions/')) context.prescriptionId = routeId(/\/prescriptions\/(\d+)/, 'prescriptionId');
    context.encounterId = queryId('encounterId');
    context.departmentId = queryId('departmentId');
    context.roomId = queryId('roomId');
    context.assignedDoctorId = queryId('assignedDoctorId');
    context.existingPatientId = queryId('existingPatientId');
    context.itemId = queryId('itemId');
    const serviceIds = (params.get('serviceIds') ?? '').split(',').map(Number).filter(value => Number.isSafeInteger(value) && value > 0);
    if (serviceIds.length > 0) context.serviceIds = serviceIds;
    return Object.values(context).some(value => value !== undefined) ? context : undefined;
};

export interface UnifiedActionState {
    toolName: string;
    actionId: string;
    status: string;
    expiresAtUtc?: string;
    resourceSummary: string;
    consequence: string;
    confirmationSummary: string;
    previewValidatedAtUtc: string;
    preview: AiActionPreview;
    confirmationToken: string;
    requestSignature: string;
    idempotencyKey: string;
}

export interface ActionCapabilityState {
    tool: AiCopilotTool;
    enabled: boolean;
    reason?: string;
    arguments?: Record<string, unknown>;
}

const asRecord = (value: unknown): Record<string, unknown> => value && typeof value === 'object' ? value as Record<string, unknown> : {};

const actionCapability = (tool: AiCopilotTool, resource?: AiCopilotResourceContext): ActionCapabilityState => {
    const context = resource ?? {};
    const id = (value: number | undefined) => value !== undefined && Number.isSafeInteger(value) && value > 0;
    switch (tool.name) {
        case 'technician.prepare_start_diagnostic_order':
            return { tool, enabled: id(context.diagnosticOrderId), reason: 'Mở một phiếu chỉ định thực tế để chọn đúng order.', arguments: id(context.diagnosticOrderId) ? { orderId: context.diagnosticOrderId } : undefined };
        case 'technician.prepare_complete_diagnostic_order':
            return { tool, enabled: id(context.diagnosticOrderId), reason: 'Mở một phiếu chỉ định thực tế để chọn đúng order.', arguments: id(context.diagnosticOrderId) ? { orderId: context.diagnosticOrderId } : undefined };
        case 'pharmacist.prepare_reserve_prescription':
            return { tool, enabled: id(context.prescriptionId), reason: 'Mở một đơn thuốc thực tế để chọn đúng prescription.', arguments: id(context.prescriptionId) ? { prescriptionId: context.prescriptionId } : undefined };
        case 'pharmacist.prepare_dispense_prescription':
            return { tool, enabled: id(context.prescriptionId), reason: 'Mở một đơn thuốc thực tế để chọn đúng prescription.', arguments: id(context.prescriptionId) ? { prescriptionId: context.prescriptionId } : undefined };
        case 'reception.prepare_check_in_appointment': {
            const enabled = id(context.appointmentId) && id(context.departmentId);
            return { tool, enabled, reason: 'Cần appointment và department đang được chọn từ dữ liệu thật.', arguments: enabled ? { appointmentId: context.appointmentId, departmentId: context.departmentId, ...(id(context.roomId) ? { roomId: context.roomId } : {}), ...(id(context.assignedDoctorId) ? { assignedDoctorId: context.assignedDoctorId } : {}) } : undefined };
        }
        case 'reception.prepare_create_walk_in':
            return { tool, enabled: false, reason: 'Chỉ hỗ trợ hồ sơ bệnh nhân đã tồn tại; cần chọn patient, department và lý do từ selector domain.' };
        case 'doctor.prepare_diagnostic_order':
            return { tool, enabled: Boolean(context.visitId && context.serviceIds?.length), reason: 'Cần visit được phân công và service IDs do domain selector cung cấp.', arguments: context.visitId && context.serviceIds?.length ? { visitId: context.visitId, serviceIds: context.serviceIds } : undefined };
        case 'doctor.prepare_prescription_draft':
            return { tool, enabled: false, reason: 'Cần các thuốc/liều lượng được chọn từ selector domain của visit; UI không tự đoán item.' };
        case 'technician.prepare_record_diagnostic_result':
            return { tool, enabled: false, reason: 'Cần item xét nghiệm và kết quả được chọn/nhập trong màn hình nghiệp vụ.' };
        default:
            return { tool, enabled: false, reason: 'Thao tác này chưa có selector an toàn trong màn hình hiện tại.' };
    }
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
    const [catalog, setCatalog] = useState<AiCopilotCatalog>({ tools: [], actionTools: [] });
    const [catalogError, setCatalogError] = useState<string | null>(null);
    const [actionLoading, setActionLoading] = useState<string | null>(null);
    const [pendingAction, setPendingAction] = useState<UnifiedActionState | null>(null);
    const [actionFeedback, setActionFeedback] = useState<{ status: string; message: string } | null>(null);
    const controllerRef = useRef<AbortController | null>(null);
    const actionControllerRef = useRef<AbortController | null>(null);
    const catalogControllerRef = useRef<AbortController | null>(null);
    const actionKeysRef = useRef(new Map<string, string>());
    const actionRequestNumberRef = useRef(0);
    const actionContextRef = useRef({ identityKey, routeKey });
    const requestNumberRef = useRef(0);
    const sessionIdRef = useRef(makeSession());
    const conversationIdRef = useRef(makeSession().replace(/^sess_/, 'conv_'));
    const previousIdentityRef = useRef(identityKey);
    const previousRouteRef = useRef(routeKey);
    const retryTextRef = useRef<string | null>(null);

    const reset = useCallback(() => {
        controllerRef.current?.abort();
        actionControllerRef.current?.abort();
        requestNumberRef.current += 1;
        actionRequestNumberRef.current += 1;
        retryTextRef.current = null;
        sessionIdRef.current = makeSession();
        conversationIdRef.current = makeSession().replace(/^sess_/, 'conv_');
        setLoading(false);
        setInput('');
        setMessages([initialMessage(config.label, config.description)]);
        setPendingAction(null);
        setActionFeedback(null);
        setActionLoading(null);
        actionKeysRef.current.clear();
    }, [config]);

    useEffect(() => {
        if (previousIdentityRef.current !== identityKey || previousRouteRef.current !== routeKey) {
            previousIdentityRef.current = identityKey;
            previousRouteRef.current = routeKey;
            reset();
        }
        return () => {
            controllerRef.current?.abort();
            actionControllerRef.current?.abort();
            actionRequestNumberRef.current += 1;
        };
    }, [identityKey, routeKey, reset]);

    useEffect(() => {
        actionContextRef.current = { identityKey, routeKey };
    }, [identityKey, routeKey]);

    useEffect(() => {
        const controller = new AbortController();
        catalogControllerRef.current?.abort();
        catalogControllerRef.current = controller;
        setCatalogError(null);
        if (typeof getRoleCopilotCatalog !== 'function') return () => controller.abort();
        void getRoleCopilotCatalog(controller.signal)
            .then(value => { if (!controller.signal.aborted) setCatalog(value); })
            .catch(error => {
                if (!controller.signal.aborted) {
                    setCatalog({ tools: [], actionTools: [] });
                    setCatalogError(error instanceof Error ? error.message : 'Không thể tải danh mục thao tác.');
                }
            });
        return () => controller.abort();
    }, [identityKey]);

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
        actionControllerRef.current?.abort();
        requestNumberRef.current += 1;
        actionRequestNumberRef.current += 1;
        setActionLoading(null);
        setLoading(false);
    }, []);

    const actionCapabilities = useMemo(
        () => catalog.actionTools.map(tool => actionCapability(tool, getResourceContext(location.pathname, location.search))),
        [catalog.actionTools, location.pathname, location.search]
    );

    const prepareAction = useCallback(async (capability: ActionCapabilityState) => {
        if (!capability.enabled || !capability.arguments || actionLoading) return;
        const requestId = ++actionRequestNumberRef.current;
        const requestIdentityKey = identityKey;
        const requestRouteKey = routeKey;
        const sessionId = sessionIdRef.current;
        const signature = `${capability.tool.name}:${JSON.stringify(capability.arguments)}`;
        const idempotencyKey = actionKeysRef.current.get(signature) ?? makeId('action');
        actionKeysRef.current.set(signature, idempotencyKey);
        actionControllerRef.current?.abort();
        const controller = new AbortController();
        actionControllerRef.current = controller;
        setPendingAction(null);
        setActionLoading(capability.tool.name);
        setActionFeedback(null);
        const isCurrentRequest = () => !controller.signal.aborted &&
            actionRequestNumberRef.current === requestId &&
            actionContextRef.current.identityKey === requestIdentityKey &&
            actionContextRef.current.routeKey === requestRouteKey &&
            sessionIdRef.current === sessionId;
        try {
            const result = await prepareRoleAction({
                toolName: capability.tool.name,
                toolVersion: capability.tool.version,
                argumentsJson: JSON.stringify(capability.arguments),
                sessionId,
                conversationId: conversationIdRef.current,
                correlationId: makeId('correlation'),
                idempotencyKey
            }, controller.signal);
            if (!isCurrentRequest()) return;
            const data = asRecord(result.data);
            const token = typeof data.confirmationToken === 'string' ? data.confirmationToken : '';
            const preview = result.preview;
            const expiresAt = preview?.expiresAtUtc ? new Date(preview.expiresAtUtc).getTime() : NaN;
            const validPreview = Boolean(preview && preview.status === 'pending_confirmation' &&
                preview.toolName === capability.tool.name &&
                preview.resourceType && preview.resourceId &&
                preview.resource?.identity &&
                Array.isArray(preview.changes) && preview.changes.length > 0 &&
                preview.changes.every(change => change.kind && change.summary && Array.isArray(change.items) && change.items.length > 0 &&
                    change.items.every(item => item.label && item.value)) &&
                Array.isArray(preview.sources) && preview.sources.length > 0 &&
                preview.sources.every(source => source.name && source.kind) &&
                preview.consequence && preview.confirmationSummary && preview.validatedAtUtc &&
                Number.isFinite(expiresAt) && expiresAt > Date.now());
            if (result.status === 'pending_confirmation' && result.actionId && token && validPreview) {
                setPendingAction({
                    toolName: capability.tool.name,
                    actionId: result.actionId,
                    status: result.status,
                    expiresAtUtc: preview!.expiresAtUtc,
                    resourceSummary: preview!.resource.identity,
                    consequence: preview!.consequence,
                    confirmationSummary: preview!.confirmationSummary,
                    previewValidatedAtUtc: preview!.validatedAtUtc,
                    preview: preview!,
                    confirmationToken: token,
                    requestSignature: signature,
                    idempotencyKey
                });
                setActionFeedback({ status: 'pending_confirmation', message: 'Đã chuẩn bị. Hãy xem lại hậu quả rồi bấm xác nhận rõ ràng.' });
            } else {
                if (result.status !== 'pending_confirmation' || result.error?.retryable === false)
                    actionKeysRef.current.delete(signature);
                setActionFeedback({ status: result.status || 'failed', message: result.error?.message || 'Backend không cấp được preview có cấu trúc để xác nhận.' });
            }
        } catch (error) {
            if (isCurrentRequest()) {
                const errorRecord = asRecord(error);
                const nestedError = asRecord(errorRecord.error);
                const code = typeof nestedError.code === 'string' ? nestedError.code : '';
                if (['ACTION_EXPIRED', 'ACTION_CANCELLED', 'ACTION_FAILED_TERMINAL', 'ACTION_ALREADY_COMPLETED'].includes(code))
                    actionKeysRef.current.delete(signature);
                setActionFeedback({ status: 'failed', message: typeof nestedError.message === 'string' ? nestedError.message : error instanceof Error ? error.message : 'Không thể chuẩn bị thao tác.' });
            }
        } finally {
            if (isCurrentRequest()) setActionLoading(null);
        }
    }, [actionLoading, identityKey, routeKey]);

    const confirmAction = useCallback(async () => {
        const action = pendingAction;
        if (!action || actionLoading) return;
        const requestId = ++actionRequestNumberRef.current;
        const requestIdentityKey = identityKey;
        const requestRouteKey = routeKey;
        const sessionId = sessionIdRef.current;
        const controller = new AbortController();
        actionControllerRef.current?.abort();
        actionControllerRef.current = controller;
        setActionLoading(action.toolName);
        const isCurrentRequest = () => !controller.signal.aborted &&
            actionRequestNumberRef.current === requestId &&
            actionContextRef.current.identityKey === requestIdentityKey &&
            actionContextRef.current.routeKey === requestRouteKey &&
            sessionIdRef.current === sessionId;
        try {
            const result = await confirmRoleAction(action.actionId, { sessionId, concurrencyToken: action.confirmationToken }, controller.signal);
            if (!isCurrentRequest()) return;
            if (result.status === 'completed') {
                setPendingAction(null);
                actionKeysRef.current.delete(action.requestSignature);
                setActionFeedback({ status: 'completed', message: result.displayText || 'Thao tác đã hoàn tất và được backend xác nhận.' });
            } else {
                const status = (result.status || '').toLowerCase();
                const terminal = ['expired', 'cancelled', 'failed_terminal'].includes(status) ||
                    ['ACTION_EXPIRED', 'ACTION_CANCELLED', 'ACTION_FAILED_TERMINAL'].includes(result.error?.code || '');
                if (terminal) {
                    setPendingAction(null);
                    actionKeysRef.current.delete(action.requestSignature);
                }
                setActionFeedback({ status: result.status || 'failed', message: result.error?.message || 'Thao tác chưa hoàn tất; dữ liệu vẫn do backend quyết định.' });
            }
        } catch (error) {
            if (isCurrentRequest()) {
                const errorRecord = asRecord(error);
                const nestedError = asRecord(errorRecord.error);
                const code = typeof nestedError.code === 'string' ? nestedError.code : '';
                const terminal = ['ACTION_EXPIRED', 'ACTION_CANCELLED', 'ACTION_FAILED_TERMINAL', 'ACTION_ALREADY_COMPLETED'].includes(code);
                if (terminal) {
                    setPendingAction(null);
                    actionKeysRef.current.delete(action.requestSignature);
                }
                setActionFeedback({ status: 'failed', message: typeof nestedError.message === 'string' ? nestedError.message : error instanceof Error ? error.message : 'Không thể xác nhận thao tác.' });
            }
        } finally {
            if (isCurrentRequest()) setActionLoading(null);
        }
    }, [actionLoading, identityKey, pendingAction, routeKey]);

    const retry = useCallback(() => {
        const value = retryTextRef.current;
        if (value) void send(value);
    }, [send]);

    return {
        user, role, config, open, setOpen, input, setInput, loading, messages, send, reset, abort, retry,
        resourceContext: getResourceContext(location.pathname, location.search), catalogTools: catalog.tools, actionCapabilities, actionLoading,
        pendingAction, actionFeedback, catalogError, prepareAction, confirmAction
    };
};
