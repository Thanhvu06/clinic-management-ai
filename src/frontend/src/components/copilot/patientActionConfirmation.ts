import { useEffect } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import axiosClient from '../../api/axiosClient';
import type { AiToolExecutionResult, ChatMessage } from '../../types/ai';

type SetMessages = Dispatch<SetStateAction<ChatMessage[]>>;

const terminalLabels: Record<string, string> = {
    completed: 'Đã thực hiện',
    cancelled: 'Đã hủy thao tác',
    expired: 'Đã hết hạn',
    rejected: 'Đã từ chối'
};

export const terminalActionLabel = (status: string): string | undefined => terminalLabels[status];

function expiryTime(result: AiToolExecutionResult): number {
    return typeof result.data?.expiresAtUtc === 'string' ? Date.parse(result.data.expiresAtUtc) : NaN;
}

export function patientActionPreviewLabel(result: AiToolExecutionResult): string | undefined {
    if (result.status === 'pending_confirmation') {
        return expiryTime(result) <= Date.now() ? terminalLabels.expired : 'Đang chờ xác nhận';
    }
    return result.resultType === 'pending_action' ? terminalLabels[result.status] : undefined;
}

function retirePreview(messages: ChatMessage[], actionId: string, status: string, displayText?: string): ChatMessage[] {
    return messages.map(message => !message.toolResults?.some(result => result.actionId === actionId && result.status === 'pending_confirmation')
        ? message
        : {
            ...message,
            toolResults: message.toolResults.map(result => result.actionId === actionId && result.status === 'pending_confirmation'
                ? { actionId, status, resultType: 'pending_action', displayText }
                : result)
        });
}

export function usePatientActionExpiry(messages: ChatMessage[], setMessages: SetMessages): void {
    useEffect(() => {
        const pending = messages.flatMap(message => message.toolResults ?? []).filter(result =>
            result.actionId && result.status === 'pending_confirmation' && Number.isFinite(expiryTime(result)));
        if (!pending.length) return;
        const delay = Math.max(0, Math.min(...pending.map(expiryTime)) - Date.now());
        const timeout = window.setTimeout(() => {
            const now = Date.now();
            setMessages(previous => pending.filter(result => expiryTime(result) <= now)
                .reduce((history, result) => retirePreview(history, result.actionId!, 'expired'), previous));
        }, Math.min(delay, 2_147_483_647));
        return () => window.clearTimeout(timeout);
    }, [messages, setMessages]);
}

function terminalStatus(result: AiToolExecutionResult): string | undefined {
    if (['completed', 'cancelled', 'expired'].includes(result.status)) return result.status;
    const code = result.error?.code;
    if (code === 'ACTION_EXPIRED') return 'expired';
    if (code === 'ACTION_CANCELLED') return 'cancelled';
    if (result.error?.retryable === false ||
        ['ACTION_NOT_FOUND', 'ACTION_NOT_CANCELLABLE', 'SESSION_MISMATCH', 'CONCURRENCY_CONFLICT'].includes(code ?? ''))
        return 'rejected';
    return undefined;
}

export async function updatePatientAction(
    mode: 'confirm' | 'cancel', actionId: string, sessionId: string, setMessages: SetMessages, concurrencyToken?: string
): Promise<void> {
    const fallback = mode === 'confirm' ? 'Không thể xác nhận thao tác lúc này. Vui lòng thử lại hoặc mở lại lịch hẹn.'
        : 'Không thể hủy thao tác lúc này. Vui lòng thử lại.';
    let result: AiToolExecutionResult;
    try {
        result = await axiosClient.post<object, AiToolExecutionResult>(`/ai/tool-actions/${encodeURIComponent(actionId)}/${mode}`,
            { sessionId, ...(concurrencyToken ? { concurrencyToken } : {}) }, { suppressForbiddenRedirect: true });
    } catch (error: unknown) {
        const failure = error as { response?: { data?: AiToolExecutionResult }; error?: AiToolExecutionResult['error'] };
        result = failure?.response?.data ?? { status: 'failed', error: failure?.error };
    }

    const status = terminalStatus(result);
    const replay = result.isIdempotentReplay || result.resultType === 'idempotent_replay' || result.data?.status === 'already_completed';
    const content = mode === 'cancel' && status === 'cancelled'
        ? 'Thao tác đang chờ đã được hủy. Bản nháp hội thoại vẫn được giữ nguyên.'
        : result.displayText || result.error?.message || (status === 'completed' ? 'Thao tác đã được thực hiện.' : fallback);
    setMessages(previous => {
        const history = status ? retirePreview(previous, actionId, status, mode === 'cancel' ? result.displayText : undefined) : previous;
        return replay ? history : [...history, {
            role: 'model', content, urgency: 'ROUTINE',
            toolResults: status === 'completed' ? [result] : undefined
        }];
    });
}
