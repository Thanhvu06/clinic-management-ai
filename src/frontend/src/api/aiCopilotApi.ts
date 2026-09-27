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
