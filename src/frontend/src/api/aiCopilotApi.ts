import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';

export interface AiCopilotCard {
    type: string;
    title: string;
    description?: string | null;
    data?: unknown;
    sources?: Array<{ name: string; kind: string; status?: string }>;
}

export interface AiCopilotTool {
    name: string;
    version: string;
    description: string;
    accessMode: string;
    riskLevel: string;
    confirmation: string;
}

export interface AiCopilotResponse {
    role: string;
    assistantStatus: 'Ready' | 'Clarifying' | 'Online' | 'Degraded' | 'Unavailable' | 'SafetyBlocked';
    providerStatus: 'NotCalled' | 'Online' | 'Degraded' | 'Unavailable' | 'SafetyBlocked';
    assistantMode?: 'Ready' | 'Clarifying' | 'Degraded' | 'Unavailable' | 'SafetyBlocked' | string;
    plannerMode?: string;
    conversationId?: string;
    turnId?: string;
    intent: string;
    message: string;
    clarification?: string | null;
    safetyNotice?: string | null;
    navigationRoute?: string | null;
    suggestedPrompts: string[];
    cards: AiCopilotCard[];
    availableTools: AiCopilotTool[];
    sources?: Array<{ name: string; kind: string; status?: string }>;
}

export interface AiCopilotResourceContext {
    appointmentId?: number;
    visitId?: number;
    encounterId?: number;
    diagnosticOrderId?: number;
    prescriptionId?: number;
    departmentId?: number;
    roomId?: number;
    assignedDoctorId?: number;
    existingPatientId?: number;
    itemId?: number;
    serviceIds?: number[];
}

export interface AiCopilotRequest {
    message: string;
    conversationId?: string;
    sessionId?: string;
    currentRoute?: string;
    resourceContext?: AiCopilotResourceContext;
    resourceVersion?: string;
    clientTurnId?: string;
    locale?: string;
    timezone?: string;
}

export async function sendRoleCopilotMessage(request: AiCopilotRequest | string, signal?: AbortSignal): Promise<AiCopilotResponse> {
    const body: AiCopilotRequest = typeof request === 'string' ? { message: request } : request;
    const response = await axiosClient.post<AiCopilotRequest, ApiResponse<AiCopilotResponse>>('/ai/copilot/chat', body, { signal });
    if (!response.success || !response.data) throw new Error(response.message || 'Không thể kết nối Copilot.');
    return response.data;
}

export interface AiCopilotCatalog {
    tools: AiCopilotTool[];
    actionTools: AiCopilotTool[];
}

export interface AiToolInvocationRequest {
    toolName: string;
    toolVersion: string;
    argumentsJson: string;
    sessionId: string;
    conversationId?: string;
    correlationId?: string;
    idempotencyKey: string;
}

export interface AiRoleActionResult {
    status: string;
    data?: unknown;
    resultType?: string | null;
    displayText?: string | null;
    error?: { code?: string; message?: string; retryable?: boolean } | null;
    requiresConfirmation?: boolean;
    isIdempotentReplay?: boolean;
    actionId?: string | null;
    preview?: AiActionPreview | null;
    retrievedAtUtc?: string;
}

export interface AiActionPreview {
    toolName: string;
    status: 'pending_confirmation' | string;
    resourceType: string;
    resourceId: string;
    resource: AiActionPreviewResource;
    changes: AiActionPreviewChange[];
    resourceVersion?: string | null;
    consequence: string;
    confirmationSummary: string;
    validatedAtUtc: string;
    expiresAtUtc: string;
    sources: Array<{ name: string; kind: string; status?: string }>;
}

export interface AiActionPreviewResource {
    identity: string;
    facility?: string | null;
    department?: string | null;
    subject?: string | null;
    encounter?: string | null;
    currentStatus?: string | null;
}

export interface AiActionPreviewChange {
    kind: string;
    summary: string;
    items: Array<{ label: string; value: string; quantity?: number | null; unit?: string | null }>;
}

export async function getRoleCopilotCatalog(signal?: AbortSignal): Promise<AiCopilotCatalog> {
    const response = await axiosClient.get<unknown, ApiResponse<AiCopilotCatalog>>('/ai/copilot/catalog', { signal });
    if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải danh mục thao tác.');
    return response.data;
}

export async function prepareRoleAction(request: AiToolInvocationRequest, signal?: AbortSignal): Promise<AiRoleActionResult> {
    return axiosClient.post<AiToolInvocationRequest, AiRoleActionResult>('/ai/copilot/actions/prepare', request, { signal });
}

export async function confirmRoleAction(actionId: string, request: { sessionId: string; concurrencyToken: string }, signal?: AbortSignal): Promise<AiRoleActionResult> {
    return axiosClient.post<typeof request, AiRoleActionResult>(`/ai/copilot/actions/${encodeURIComponent(actionId)}/confirm`, request, { signal });
}
