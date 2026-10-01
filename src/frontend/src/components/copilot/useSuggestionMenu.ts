import { useEffect, useMemo, useState } from 'react';
import { getCopilotSuggestions } from '../../api/aiSuggestionApi';
import type { AiSuggestionItem, AiSuggestionMenuRequest } from '../../types/ai';

export const MAX_SUGGESTION_CHIPS = 6;
const SUGGESTION_CODE = /^[a-z]+(\.[a-z_]+)+$/;
const SUGGESTION_ROLES = new Set(['Patient', 'Doctor', 'Receptionist', 'DiagnosticTechnician', 'Pharmacist', 'Admin']);

/**
 * Defence in depth for server-issued (or session-restored) chips: keep only
 * well-formed codes with a label, drop duplicates and cap the list.
 */
export const sanitizeSuggestions = (items: readonly AiSuggestionItem[] | null | undefined): AiSuggestionItem[] => {
    if (!Array.isArray(items)) return [];
    const seen = new Set<string>();
    const result: AiSuggestionItem[] = [];
    for (const item of items) {
        if (!item || typeof item.code !== 'string' || typeof item.label !== 'string') continue;
        const label = item.label.trim();
        if (item.code.length > 64 || !SUGGESTION_CODE.test(item.code) || !label || seen.has(item.code)) continue;
        seen.add(item.code);
        const group = typeof item.group === 'string' ? item.group.trim().slice(0, 40) : undefined;
        result.push({ code: item.code, label, ...(group ? { group } : {}) });
        if (result.length >= MAX_SUGGESTION_CHIPS) break;
    }
    return result;
};

type ResourceHint = AiSuggestionMenuRequest['resourceContext'];

const positiveId = (value: unknown): number | undefined =>
    typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : undefined;

/** Only the resource identifiers the server resolver understands are sent. */
const pickResource = (resource: Record<string, unknown> | null | undefined): ResourceHint => {
    if (!resource) return undefined;
    const picked: NonNullable<ResourceHint> = {
        appointmentId: positiveId(resource.appointmentId),
        visitId: positiveId(resource.visitId),
        encounterId: positiveId(resource.encounterId),
        diagnosticOrderId: positiveId(resource.diagnosticOrderId),
        prescriptionId: positiveId(resource.prescriptionId)
    };
    return Object.values(picked).some(value => value !== undefined) ? picked : undefined;
};

export interface SuggestionMenuOptions {
    enabled: boolean;
    role?: string | null;
    identityKey: string;
    currentRoute: string;
    resourceContext?: object | null;
}

/** Loads the initial suggestion menu when a chat opens or its route/resource changes. */
export const useSuggestionMenu = ({ enabled, role, identityKey, currentRoute, resourceContext }: SuggestionMenuOptions): AiSuggestionItem[] => {
    const [menu, setMenu] = useState<{ key: string; items: AiSuggestionItem[] }>({ key: '', items: [] });
    const resourceKey = JSON.stringify(pickResource(resourceContext as Record<string, unknown> | null | undefined) ?? null);
    const active = enabled && SUGGESTION_ROLES.has(role ?? '');
    const requestKey = useMemo(() => JSON.stringify([identityKey, role, currentRoute, resourceKey]), [identityKey, role, currentRoute, resourceKey]);

    useEffect(() => {
        if (!active) return;
        const controller = new AbortController();
        const parsed = JSON.parse(resourceKey) as ResourceHint | null;
        void getCopilotSuggestions({ currentRoute, ...(parsed ? { resourceContext: parsed } : {}) }, controller.signal)
            .then(result => { if (!controller.signal.aborted) setMenu({ key: requestKey, items: sanitizeSuggestions(result?.suggestions) }); })
            .catch(() => { if (!controller.signal.aborted) setMenu({ key: requestKey, items: [] }); });
        return () => controller.abort();
    }, [active, currentRoute, requestKey, resourceKey]);

    // A menu fetched for another identity/route/resource is never shown.
    return active && menu.key === requestKey ? menu.items : [];
};
