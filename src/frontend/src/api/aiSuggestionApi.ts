import axiosClient from './axiosClient';
import type { ApiResponse } from '../types';
import type { AiSuggestionMenu, AiSuggestionMenuRequest } from '../types/ai';

/** Initial suggestion buttons for the signed-in role and the verified resource. */
export async function getCopilotSuggestions(request: AiSuggestionMenuRequest, signal?: AbortSignal): Promise<AiSuggestionMenu> {
    const response = await axiosClient.post<AiSuggestionMenuRequest, ApiResponse<AiSuggestionMenu>>('/ai/copilot/suggestions', request, { signal, suppressForbiddenRedirect: true });
    if (!response?.success || !response.data) throw new Error(response?.message || 'Không thể tải gợi ý.');
    return response.data;
}
