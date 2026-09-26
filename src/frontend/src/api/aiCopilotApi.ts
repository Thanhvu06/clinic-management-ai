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
    assistantStatus: 'Online' | 'Degraded' | 'Unavailable' | 'SafetyBlocked';
    providerStatus: 'NotCalled' | 'Online' | 'Degraded' | 'Unavailable' | 'SafetyBlocked';
    intent: string;
    message: string;
    safetyNotice?: string | null;
    navigationRoute?: string | null;
    suggestedPrompts: string[];
    cards: AiCopilotCard[];
    availableTools: AiCopilotTool[];
}

export async function sendRoleCopilotMessage(message: string): Promise<AiCopilotResponse> {
    const response = await axiosClient.post<unknown, ApiResponse<AiCopilotResponse>>('/ai/copilot/chat', { message });
    if (!response.success || !response.data) throw new Error(response.message || 'Không thể kết nối Copilot.');
    return response.data;
}
