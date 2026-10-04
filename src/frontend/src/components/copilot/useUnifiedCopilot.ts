import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import {
    cancelRoleAction,
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
import { aiChatFailureMessage } from '../../api/aiErrorMessages';
import type { AiSuggestionItem } from '../../types/ai';
import { getCopilotRoleConfig, type CopilotRole } from './copilotConfig';
import { useCopilotResource, type CopilotResourceSelection } from './copilotResourceContext';
import { classifyStaffTypedIntent } from './staffTypedIntent';
import { useSuggestionMenuState } from './useSuggestionMenu';

export interface UnifiedCopilotMessage {
    id: string;
    role: 'user' | 'assistant';
    content: string;
    response?: AiCopilotResponse;
    error?: boolean;
    retryText?: string;
    retrySuggestionCode?: string;
}

const makeId = (prefix: string) => `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
const makeSession = () => `sess_${makeId('copilot').replace(/[^A-Za-z0-9_-]/g, '')}`.slice(0, 128);

const getResourceContext = (pathname: string): AiCopilotResourceContext | undefined => {
    const routeId = (pattern: RegExp): number | undefined => {
        const match = pathname.match(pattern);
        const value = match?.[1];
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

const actionErrorDetails = (error: unknown) => {
    const root = asRecord(error);
    const nested = asRecord(root.error);
    const response = asRecord(root.response);
    const responseData = asRecord(response.data);
    const code = [nested.code, root.errorCode, responseData.errorCode, root.code]
        .find(value => typeof value === 'string') as string | undefined;
    const status = [root.status, response.status]
        .find(value => typeof value === 'number') as number | undefined;
    const message = [nested.message, root.message, responseData.message]
        .find(value => typeof value === 'string') as string | undefined;
    return { code, status, message };
};

const terminalActionError = (code?: string, status?: number) =>
    status === 403 || status === 409 || [
        'ACTION_EXPIRED', 'ACTION_CANCELLED', 'ACTION_FAILED_TERMINAL', 'ACTION_ALREADY_COMPLETED',
        'CONCURRENCY_CONFLICT', 'RESOURCE_VERSION_CHANGED', 'ACTION_TOKEN_REFRESH_CONFLICT', 'ACTIVE_ACTION_EXISTS',
        'FORBIDDEN_TOOL', 'FORBIDDEN_CAPABILITY', 'ROLE_MISMATCH', 'FACILITY_SCOPE_DENIED', 'RESOURCE_SCOPE_DENIED'
    ].includes(code ?? '');

const actionErrorMessage = (details: ReturnType<typeof actionErrorDetails>, fallback: string) => {
    if (details.status === 403 || ['FORBIDDEN_TOOL', 'FORBIDDEN_CAPABILITY', 'ROLE_MISMATCH', 'FACILITY_SCOPE_DENIED', 'RESOURCE_SCOPE_DENIED'].includes(details.code ?? ''))
        return 'Bạn không còn quyền hoặc phạm vi cơ sở cho tài nguyên này. Preview đã bị hủy.';
    if (details.status === 409 || ['CONCURRENCY_CONFLICT', 'RESOURCE_VERSION_CHANGED', 'ACTIVE_ACTION_EXISTS', 'ACTION_TOKEN_REFRESH_CONFLICT'].includes(details.code ?? ''))
        return 'Dữ liệu hoặc mã xác nhận đã thay đổi. Preview cũ đã bị hủy; hãy chuẩn bị lại từ dữ liệu mới nhất.';
    return details.message || fallback;
};

const actionCapability = (
    tool: AiCopilotTool,
    resource?: AiCopilotResourceContext,
    selection?: CopilotResourceSelection | null
): ActionCapabilityState => {
    const context = resource ?? {};
    const extraArguments = selection?.actionArguments ?? {};
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
            {
                const hasExactlyOneResource = id(context.visitId) !== id(context.appointmentId);
                const appointmentNeedsDepartment = id(context.appointmentId) && !id(context.departmentId);
                const clinicalIndication = typeof extraArguments.clinicalIndication === 'string' ? extraArguments.clinicalIndication.trim() : '';
                const serviceIds = context.serviceIds?.filter(value => id(value)) ?? [];
                const enabled = hasExactlyOneResource && !appointmentNeedsDepartment && serviceIds.length > 0 && clinicalIndication.length > 0;
                return {
                    tool,
                    enabled,
                    reason: !hasExactlyOneResource
                        ? 'Cần đúng một visit hoặc lịch hẹn đang mở từ hồ sơ bác sĩ.'
                        : appointmentNeedsDepartment
                            ? 'Cần khoa của lịch hẹn từ dữ liệu nghiệp vụ; không tự đoán khoa.'
                        : serviceIds.length === 0
                            ? 'Cần chọn ít nhất một dịch vụ cận lâm sàng trong form chỉ định.'
                            : 'Cần nhập chỉ định lâm sàng trong form trước khi xem preview.',
                    arguments: enabled
                        ? {
                            ...(id(context.visitId)
                                ? { visitId: context.visitId }
                                : { appointmentId: context.appointmentId, ...(id(context.departmentId) ? { departmentId: context.departmentId } : {}) }),
                            clinicalIndication,
                            serviceIds,
                            ...(typeof extraArguments.note === 'string' && extraArguments.note.trim() ? { note: extraArguments.note.trim() } : {})
                        }
                        : undefined
                };
            }
        case 'doctor.prepare_prescription_draft':
            {
                const hasExactlyOneResource = id(context.visitId) !== id(context.appointmentId);
                const appointmentNeedsDepartment = id(context.appointmentId) && !id(context.departmentId);
                const rawItems = Array.isArray(extraArguments.items) ? extraArguments.items : [];
                const items = rawItems
                    .filter(item => item && typeof item === 'object')
                    .map(item => item as Record<string, unknown>)
                    .filter(item => id(typeof item.medicineId === 'number' ? item.medicineId : undefined) &&
                        typeof item.quantity === 'number' && Number.isInteger(item.quantity) && item.quantity > 0 && item.quantity <= 1000)
                    .map(item => ({
                        medicineId: item.medicineId,
                        quantity: item.quantity,
                        ...(typeof item.dosage === 'string' && item.dosage.trim() ? { dosage: item.dosage.trim() } : {}),
                        ...(typeof item.frequency === 'string' && item.frequency.trim() ? { frequency: item.frequency.trim() } : {}),
                        ...(typeof item.durationDays === 'number' && Number.isInteger(item.durationDays) && item.durationDays > 0 ? { durationDays: item.durationDays } : {}),
                        ...(typeof item.instructions === 'string' && item.instructions.trim() ? { instructions: item.instructions.trim() } : {})
                    }));
                const notes = typeof extraArguments.notes === 'string' ? extraArguments.notes.trim() : '';
                const enabled = hasExactlyOneResource && !appointmentNeedsDepartment && items.length > 0;
                return {
                    tool,
                    enabled,
                    reason: !hasExactlyOneResource
                        ? 'Cần đúng một visit hoặc lịch hẹn đang mở từ hồ sơ bác sĩ.'
                        : appointmentNeedsDepartment
                            ? 'Cần khoa của lịch hẹn từ dữ liệu nghiệp vụ; không tự đoán khoa.'
                            : 'Cần ít nhất một thuốc và số lượng hợp lệ trong form đơn thuốc; liều dùng/tần suất nếu đã nhập sẽ được giữ nguyên, AI không tự điền.',
                    arguments: enabled
                        ? {
                            ...(id(context.visitId)
                                ? { visitId: context.visitId }
                                : { appointmentId: context.appointmentId, ...(id(context.departmentId) ? { departmentId: context.departmentId } : {}) }),
                            ...(notes ? { notes } : {}),
                            items
                        }
                        : undefined
                };
            }
        case 'technician.prepare_record_diagnostic_result':
            {
                const resultText = typeof extraArguments.resultText === 'string' ? extraArguments.resultText.trim() : '';
                const enabled = id(context.diagnosticOrderId) && id(context.itemId) && resultText.length > 0;
                return {
                    tool,
                    enabled,
                    reason: !id(context.diagnosticOrderId) || !id(context.itemId)
                        ? 'Cần chọn đúng phiếu và item xét nghiệm trên màn hình nghiệp vụ.'
                        : 'Cần nhập kết quả cho item trước khi xem preview.',
                    arguments: enabled
                        ? {
                            orderId: context.diagnosticOrderId,
                            itemId: context.itemId,
                            resultText,
                            ...(typeof extraArguments.conclusion === 'string' && extraArguments.conclusion.trim() ? { conclusion: extraArguments.conclusion.trim() } : {}),
                            ...(typeof extraArguments.referenceRange === 'string' && extraArguments.referenceRange.trim() ? { referenceRange: extraArguments.referenceRange.trim() } : {}),
                            ...(typeof extraArguments.unit === 'string' && extraArguments.unit.trim() ? { unit: extraArguments.unit.trim() } : {})
                        }
                        : undefined
                };
            }
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
    const resourceSelection = useCopilotResource().selection;
    const routeResourceContext = useMemo(() => getResourceContext(location.pathname), [location.pathname]);
    const resourceContext = resourceSelection?.context ?? routeResourceContext;
    const resourceVersion = resourceSelection?.resourceVersion ?? undefined;
    const resourceIdentityContext = useMemo(() => {
        if (!resourceContext) return null;
        const actionKeys = new Set(['serviceIds', 'departmentId', 'roomId', 'assignedDoctorId']);
        return Object.fromEntries(Object.entries(resourceContext).filter(([key]) => !actionKeys.has(key)));
    }, [resourceContext]);
    const actionResourceContext = useMemo(() => {
        if (!resourceContext) return null;
        return {
            serviceIds: resourceContext.serviceIds ?? null,
            departmentId: resourceContext.departmentId ?? null,
            roomId: resourceContext.roomId ?? null,
            assignedDoctorId: resourceContext.assignedDoctorId ?? null,
            itemId: resourceContext.itemId ?? null
        };
    }, [resourceContext]);
    // A selected resource is the conversation's privacy/ownership boundary.
    // Service/department/room/assigned-doctor inputs and the server row version
    // are a narrower action boundary: changing them must invalidate a
    // preview, but must not erase the conversation while a doctor is typing.
    const resourceKey = useMemo(() => JSON.stringify({
        source: resourceSelection?.source ?? 'route',
        context: resourceIdentityContext
    }), [resourceSelection?.source, resourceIdentityContext]);
    const actionInputKey = useMemo(() => JSON.stringify({
        resource: actionResourceContext,
        actionArguments: resourceSelection?.actionArguments ?? null,
        resourceVersion: resourceVersion ?? null
    }), [actionResourceContext, resourceSelection?.actionArguments, resourceVersion]);
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
    const [actionClockMs, setActionClockMs] = useState(() => Date.now());
    const [actionFeedback, setActionFeedback] = useState<{ status: string; message: string } | null>(null);
    const controllerRef = useRef<AbortController | null>(null);
    const actionControllerRef = useRef<AbortController | null>(null);
    const catalogControllerRef = useRef<AbortController | null>(null);
    const actionKeysRef = useRef(new Map<string, string>());
    const actionRequestNumberRef = useRef(0);
    const actionContextRef = useRef({ identityKey, routeKey, resourceKey, actionInputKey });
    const requestNumberRef = useRef(0);
    const sessionIdRef = useRef(makeSession());
    const conversationIdRef = useRef(makeSession().replace(/^sess_/, 'conv_'));
    const previousIdentityRef = useRef(identityKey);
    const previousRouteRef = useRef(routeKey);
    const previousResourceRef = useRef(resourceKey);
    const previousActionInputRef = useRef(actionInputKey);
    const retryTextRef = useRef<string | null>(null);
    const retrySuggestionCodeRef = useRef<string | null>(null);
    const actionBusyRef = useRef(false);
    const menuState = useSuggestionMenuState({ enabled: open, role, identityKey, currentRoute: location.pathname, resourceContext });
    const menuSuggestions = menuState.chips;
    const typedMenuCodes = useMemo(() => [...menuState.chips, ...menuState.typed].map(item => item.code), [menuState]);

    const reset = useCallback(() => {
        controllerRef.current?.abort();
        actionControllerRef.current?.abort();
        requestNumberRef.current += 1;
        actionRequestNumberRef.current += 1;
        retryTextRef.current = null;
        retrySuggestionCodeRef.current = null;
        sessionIdRef.current = makeSession();
        conversationIdRef.current = makeSession().replace(/^sess_/, 'conv_');
        setLoading(false);
        setInput('');
        setMessages([initialMessage(config.label, config.description)]);
        setPendingAction(null);
        setActionFeedback(null);
        setActionLoading(null);
        actionKeysRef.current.clear();
        actionBusyRef.current = false;
    }, [config]);

    useEffect(() => {
        if (previousIdentityRef.current !== identityKey || previousRouteRef.current !== routeKey || previousResourceRef.current !== resourceKey) {
            previousIdentityRef.current = identityKey;
            previousRouteRef.current = routeKey;
            previousResourceRef.current = resourceKey;
            reset();
        }
        return () => {
            controllerRef.current?.abort();
            actionControllerRef.current?.abort();
            actionRequestNumberRef.current += 1;
        };
    }, [identityKey, resourceKey, routeKey, reset]);

    useEffect(() => {
        actionContextRef.current = { identityKey, routeKey, resourceKey, actionInputKey };
    }, [identityKey, routeKey, resourceKey, actionInputKey]);

    useEffect(() => {
        if (previousActionInputRef.current === actionInputKey) return;
        previousActionInputRef.current = actionInputKey;
        // A changed indication, note, selected service or row version cannot
        // reuse a preview/token prepared for the previous payload. Keep chat
        // messages, input and session; only invalidate the action transaction.
        actionControllerRef.current?.abort();
        actionRequestNumberRef.current += 1;
        actionBusyRef.current = false;
        actionKeysRef.current.clear();
        setActionLoading(null);
        setPendingAction(null);
        setActionFeedback(null);
        setActionClockMs(Date.now());
    }, [actionInputKey]);

    // The expiry is part of the safety boundary, not just display metadata.
    // Wake the UI at the exact boundary, clear the in-memory token, and force
    // a fresh prepare key so an expired action cannot be confirmed or replayed.
    useEffect(() => {
        if (!pendingAction?.expiresAtUtc) return;
        const expiryMs = Date.parse(pendingAction.expiresAtUtc);
        if (!Number.isFinite(expiryMs)) return;
        const actionId = pendingAction.actionId;
        const maxTimerDelay = 2_147_483_647;
        let timer: number;
        const onExpiry = () => {
            const remainingMs = expiryMs - Date.now();
            if (remainingMs > 0) {
                timer = window.setTimeout(onExpiry, Math.min(remainingMs + 1, maxTimerDelay));
                return;
            }
            setActionClockMs(Date.now());
            setPendingAction(current => {
                if (!current || current.actionId !== actionId) return current;
                actionKeysRef.current.delete(current.requestSignature);
                setActionFeedback({ status: 'expired', message: 'Preview đã hết hạn. Hãy chuẩn bị lại trước khi xác nhận.' });
                return { ...current, status: 'expired', confirmationToken: '' };
            });
        };
        timer = window.setTimeout(onExpiry, Math.min(Math.max(0, expiryMs - Date.now()) + 1, maxTimerDelay));
        return () => window.clearTimeout(timer);
    }, [pendingAction?.actionId, pendingAction?.expiresAtUtc]);

    const pendingActionExpired = useMemo(() => {
        if (!pendingAction?.expiresAtUtc) return false;
        const expiryMs = Date.parse(pendingAction.expiresAtUtc);
        return !Number.isFinite(expiryMs) || expiryMs <= actionClockMs;
    }, [actionClockMs, pendingAction]);

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

    // A suggestion button sends its server-owned code; the label is only
    // the visible history text. A typed staff question that clearly matches
    // a code in the current menu is sent the same way, with the typed text as
    // its label. Anything else keeps the original free-text behaviour.
    const submit = useCallback(async (value: string, suggestionCode?: string) => {
        const message = value.trim();
        if (!message || loading || message.length > 500) return;
        const code = suggestionCode ?? classifyStaffTypedIntent(role, message, typedMenuCodes) ?? undefined;
        const requestNumber = ++requestNumberRef.current;
        const controller = new AbortController();
        controllerRef.current?.abort();
        controllerRef.current = controller;
        retryTextRef.current = null;
        retrySuggestionCodeRef.current = null;
        setLoading(true);
        if (!suggestionCode) setInput('');
        setMessages(previous => [...previous, { id: makeId('user'), role: 'user', content: message }]);
        const request: AiCopilotRequest = {
            message,
            conversationId: conversationIdRef.current,
            sessionId: sessionIdRef.current,
            currentRoute: location.pathname,
            resourceContext,
            resourceVersion,
            clientTurnId: makeId('turn'),
            locale: 'vi-VN',
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
            ...(code ? { suggestionCode: code } : {})
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
            retrySuggestionCodeRef.current = code ?? null;
            setMessages(previous => [...previous, { id: makeId('error'), role: 'assistant', error: true, retryText: message, retrySuggestionCode: code, content: aiChatFailureMessage(error) }]);
        } finally {
            if (requestNumber === requestNumberRef.current) setLoading(false);
        }
    }, [loading, location.pathname, resourceContext, resourceVersion, role, typedMenuCodes]);

    const send = useCallback((value = input) => submit(value), [input, submit]);

    const sendSuggestion = useCallback((suggestion: AiSuggestionItem) => submit(suggestion.label, suggestion.code), [submit]);

    const abort = useCallback(() => {
        controllerRef.current?.abort();
        actionControllerRef.current?.abort();
        requestNumberRef.current += 1;
        actionRequestNumberRef.current += 1;
        actionBusyRef.current = false;
        setActionLoading(null);
        setLoading(false);
    }, []);

    const actionCapabilities = useMemo(
        () => catalog.actionTools.map(tool => actionCapability(tool, resourceContext, resourceSelection)),
        [catalog.actionTools, resourceContext, resourceSelection]
    );

    const prepareAction = useCallback(async (capability: ActionCapabilityState) => {
        if (!capability.enabled || !capability.arguments || actionLoading || actionBusyRef.current) return;
        actionBusyRef.current = true;
        const requestId = ++actionRequestNumberRef.current;
        const requestIdentityKey = identityKey;
        const requestRouteKey = routeKey;
        const requestResourceKey = resourceKey;
        const requestActionInputKey = actionInputKey;
        const sessionId = sessionIdRef.current;
        const signature = `${capability.tool.name}:${JSON.stringify(capability.arguments)}`;
        const idempotencyKey = actionKeysRef.current.get(signature) ?? makeId('action');
        actionKeysRef.current.set(signature, idempotencyKey);
        actionControllerRef.current?.abort();
        const controller = new AbortController();
        actionControllerRef.current = controller;
        setPendingAction(null);
        setActionClockMs(Date.now());
        setActionLoading(capability.tool.name);
        setActionFeedback(null);
        const isCurrentRequest = () => !controller.signal.aborted &&
            actionRequestNumberRef.current === requestId &&
            actionContextRef.current.identityKey === requestIdentityKey &&
            actionContextRef.current.routeKey === requestRouteKey &&
            actionContextRef.current.resourceKey === requestResourceKey &&
            actionContextRef.current.actionInputKey === requestActionInputKey &&
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
                setActionClockMs(Date.now());
                setActionFeedback({ status: 'pending_confirmation', message: 'Đã chuẩn bị. Hãy xem lại hậu quả rồi bấm xác nhận rõ ràng.' });
            } else {
                if (result.status !== 'pending_confirmation' || result.error?.retryable === false)
                    actionKeysRef.current.delete(signature);
                setActionFeedback({ status: result.status || 'failed', message: result.error?.message || 'Backend không cấp được preview có cấu trúc để xác nhận.' });
            }
        } catch (error) {
            if (isCurrentRequest()) {
                const details = actionErrorDetails(error);
                if (terminalActionError(details.code, details.status))
                    actionKeysRef.current.delete(signature);
                setActionFeedback({ status: 'failed', message: actionErrorMessage(details, error instanceof Error ? error.message : 'Không thể chuẩn bị thao tác.') });
            }
        } finally {
            if (isCurrentRequest()) {
                setActionLoading(null);
                actionBusyRef.current = false;
            }
        }
    }, [actionInputKey, actionLoading, identityKey, resourceKey, routeKey]);

    const confirmAction = useCallback(async () => {
        const action = pendingAction;
        if (!action || actionLoading || actionBusyRef.current) return;
        const expiresAtMs = action.expiresAtUtc ? Date.parse(action.expiresAtUtc) : NaN;
        if (!Number.isFinite(expiresAtMs) || expiresAtMs <= Date.now() || !action.confirmationToken) {
            actionKeysRef.current.delete(action.requestSignature);
            setActionClockMs(Date.now());
            setPendingAction(current => current?.actionId === action.actionId
                ? { ...current, status: 'expired', confirmationToken: '' }
                : current);
            setActionFeedback({ status: 'expired', message: 'Preview đã hết hạn hoặc không còn token hợp lệ. Hãy chuẩn bị lại.' });
            return;
        }
        actionBusyRef.current = true;
        const requestId = ++actionRequestNumberRef.current;
        const requestIdentityKey = identityKey;
        const requestRouteKey = routeKey;
        const requestResourceKey = resourceKey;
        const requestActionInputKey = actionInputKey;
        const sessionId = sessionIdRef.current;
        const controller = new AbortController();
        actionControllerRef.current?.abort();
        actionControllerRef.current = controller;
        setActionLoading(action.toolName);
        const isCurrentRequest = () => !controller.signal.aborted &&
            actionRequestNumberRef.current === requestId &&
            actionContextRef.current.identityKey === requestIdentityKey &&
            actionContextRef.current.routeKey === requestRouteKey &&
            actionContextRef.current.resourceKey === requestResourceKey &&
            actionContextRef.current.actionInputKey === requestActionInputKey &&
            sessionIdRef.current === sessionId;
        try {
            const result = await confirmRoleAction(action.actionId, { sessionId, concurrencyToken: action.confirmationToken }, controller.signal);
            if (!isCurrentRequest()) return;
            if (result.status === 'completed') {
                setPendingAction(null);
                actionKeysRef.current.delete(action.requestSignature);
                window.dispatchEvent(new Event('cliniccare:copilot-action-completed'));
                setActionFeedback({ status: 'completed', message: result.displayText || 'Thao tác đã hoàn tất và được backend xác nhận.' });
            } else {
                const status = (result.status || '').toLowerCase();
                const terminal = ['expired', 'cancelled', 'failed_terminal'].includes(status) || terminalActionError(result.error?.code);
                if (terminal) {
                    setPendingAction(null);
                    actionKeysRef.current.delete(action.requestSignature);
                }
                setActionFeedback({ status: result.status || 'failed', message: terminal && result.error ? actionErrorMessage({ code: result.error.code, status: undefined, message: result.error.message }, result.error.message || 'Preview không còn hợp lệ.') : result.error?.message || 'Thao tác chưa hoàn tất; dữ liệu vẫn do backend quyết định.' });
            }
        } catch (error) {
            if (isCurrentRequest()) {
                const details = actionErrorDetails(error);
                const terminal = terminalActionError(details.code, details.status);
                if (terminal) {
                    setPendingAction(null);
                    actionKeysRef.current.delete(action.requestSignature);
                }
                setActionFeedback({ status: 'failed', message: actionErrorMessage(details, error instanceof Error ? error.message : 'Không thể xác nhận thao tác.') });
            }
        } finally {
            if (isCurrentRequest()) {
                setActionLoading(null);
                actionBusyRef.current = false;
            }
        }
    }, [actionInputKey, actionLoading, identityKey, pendingAction, resourceKey, routeKey]);

    const cancelAction = useCallback(async () => {
        const action = pendingAction;
        if (!action || actionLoading || actionBusyRef.current) return;
        actionBusyRef.current = true;
        const requestId = ++actionRequestNumberRef.current;
        const requestIdentityKey = identityKey;
        const requestRouteKey = routeKey;
        const requestResourceKey = resourceKey;
        const requestActionInputKey = actionInputKey;
        const sessionId = sessionIdRef.current;
        const controller = new AbortController();
        actionControllerRef.current?.abort();
        actionControllerRef.current = controller;
        setActionLoading(action.toolName);
        const isCurrentRequest = () => !controller.signal.aborted &&
            actionRequestNumberRef.current === requestId &&
            actionContextRef.current.identityKey === requestIdentityKey &&
            actionContextRef.current.routeKey === requestRouteKey &&
            actionContextRef.current.resourceKey === requestResourceKey &&
            actionContextRef.current.actionInputKey === requestActionInputKey &&
            sessionIdRef.current === sessionId;
        try {
            const result = await cancelRoleAction(action.actionId, { sessionId }, controller.signal);
            if (!isCurrentRequest()) return;
            if (result.status === 'cancelled') {
                setPendingAction(null);
                actionKeysRef.current.delete(action.requestSignature);
                setActionFeedback({ status: 'cancelled', message: result.displayText || 'Đã hủy thao tác đang chờ. Không có thay đổi nghiệp vụ nào được thực hiện.' });
            } else {
                const status = (result.status || '').toLowerCase();
                const terminal = ['expired', 'cancelled', 'failed_terminal'].includes(status) || terminalActionError(result.error?.code);
                if (terminal) {
                    setPendingAction(null);
                    actionKeysRef.current.delete(action.requestSignature);
                }
                setActionFeedback({ status: result.status || 'failed', message: result.error?.message || 'Backend chưa thể hủy thao tác đang chờ.' });
            }
        } catch (error) {
            if (isCurrentRequest()) {
                const details = actionErrorDetails(error);
                const terminal = details.status === 404 || details.status === 410 || terminalActionError(details.code, details.status);
                if (terminal) {
                    setPendingAction(null);
                    actionKeysRef.current.delete(action.requestSignature);
                }
                setActionFeedback({ status: 'failed', message: actionErrorMessage(details, 'Không thể hủy thao tác lúc này.') });
            }
        } finally {
            if (isCurrentRequest()) {
                setActionLoading(null);
                actionBusyRef.current = false;
            }
        }
    }, [actionInputKey, actionLoading, identityKey, pendingAction, resourceKey, routeKey]);

    const retry = useCallback(() => {
        const value = retryTextRef.current;
        if (value) void submit(value, retrySuggestionCodeRef.current ?? undefined);
    }, [submit]);

    return {
        user, role, config, open, setOpen, input, setInput, loading, messages, send, sendSuggestion, menuSuggestions, reset, abort, retry,
        resourceContext, resourceSelection, catalogTools: catalog.tools, actionCapabilities, actionLoading,
        pendingAction, pendingActionExpired, actionFeedback, catalogError, prepareAction, confirmAction, cancelAction
    };
};
