import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { CalendarDays, ClipboardList, FileText, Activity, Package, CreditCard, LayoutDashboard, HeartPulse } from 'lucide-react';
import type { AiSuggestionItem } from '../../types/ai';
import { sanitizeSuggestions } from './useSuggestionMenu';
import styles from './SuggestionChips.module.css';
import './chatTokens.css';

interface SuggestionChipsProps {
    suggestions?: readonly AiSuggestionItem[] | null;
    disabled?: boolean;
    onSelect: (suggestion: AiSuggestionItem) => void;
    ariaLabel?: string;
    variant?: 'grid' | 'compact';
}

const suggestionIcon = (code: string) => {
    if (code.includes('inventory')) return Package;
    if (code.includes('payment') || code.includes('bills')) return CreditCard;
    if (code.includes('dashboard')) return LayoutDashboard;
    if (code.includes('health')) return HeartPulse;
    if (code.includes('diagnostic') || code.includes('worklist')) return Activity;
    if (code.includes('booking') || code.includes('appointments')) return CalendarDays;
    if (code.includes('queue')) return ClipboardList;
    return FileText;
};

/**
 * Role suggestion buttons. Selecting one sends only its server-owned code;
 * the label is what the conversation history shows.
 */
export const SuggestionChips: React.FC<SuggestionChipsProps> = ({ suggestions, disabled = false, onSelect, ariaLabel = 'Gợi ý tra cứu nhanh', variant = 'compact' }) => {
    const container = useRef<HTMLDivElement>(null);
    const [horizontal, setHorizontal] = useState(false);
    const drag = useRef<{ pointerId: number; startX: number; startScroll: number; moved: boolean } | null>(null);
    const suppressClick = useRef(false);
    const items = sanitizeSuggestions(suggestions);
    const signature = items.map(item => item.code).join(',');
    useLayoutEffect(() => {
        const element = container.current;
        if (!element || variant !== 'compact') return;
        const measure = () => {
            element.dataset.horizontal = 'false';
            const exceeds = element.scrollHeight > 96;
            element.dataset.horizontal = String(exceeds);
            setHorizontal(exceeds);
        };
        measure();
        if (typeof ResizeObserver === 'undefined') return;
        const observer = new ResizeObserver(measure);
        observer.observe(element);
        return () => observer.disconnect();
    }, [signature, variant]);
    useEffect(() => {
        const element = container.current;
        if (!element || !horizontal) return;
        const onWheel = (event: WheelEvent) => {
            if (event.ctrlKey || event.deltaY === 0 || Math.abs(event.deltaX) > Math.abs(event.deltaY)) return;
            const maximum = element.scrollWidth - element.clientWidth;
            const next = Math.max(0, Math.min(maximum, element.scrollLeft + event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? element.clientWidth : 1)));
            if (next === element.scrollLeft) return;
            event.preventDefault();
            element.scrollLeft = next;
        };
        element.addEventListener('wheel', onWheel, { passive: false });
        return () => element.removeEventListener('wheel', onWheel);
    }, [horizontal]);

    const startDrag = (event: React.PointerEvent<HTMLDivElement>) => {
        if (!horizontal || event.pointerType !== 'mouse' || event.button !== 0) return;
        suppressClick.current = false;
        drag.current = { pointerId: event.pointerId, startX: event.clientX, startScroll: event.currentTarget.scrollLeft, moved: false };
    };
    const moveDrag = (event: React.PointerEvent<HTMLDivElement>) => {
        const state = drag.current;
        if (!horizontal || event.pointerType !== 'mouse' || !state || state.pointerId !== event.pointerId) return;
        const delta = state.startX - event.clientX;
        if (!state.moved && Math.abs(delta) < 5) return;
        if (!state.moved) event.currentTarget.setPointerCapture?.(event.pointerId);
        state.moved = true;
        suppressClick.current = true;
        event.preventDefault();
        event.currentTarget.scrollLeft = Math.max(0, Math.min(event.currentTarget.scrollWidth - event.currentTarget.clientWidth, state.startScroll + delta));
    };
    const endDrag = (event: React.PointerEvent<HTMLDivElement>) => {
        if (drag.current?.pointerId !== event.pointerId) return;
        if (event.currentTarget.hasPointerCapture?.(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
        drag.current = null;
    };
    if (items.length === 0) return null;
    const groups = Array.from(new Set(items.map(item => item.group ?? 'Gợi ý')));
    const button = (item: AiSuggestionItem) => {
        const Icon = suggestionIcon(item.code);
        return (
                <button
                    key={item.code}
                    type="button"
                    className={styles.chip}
                    disabled={disabled}
                    aria-label={`Gợi ý: ${item.label}`}
                    data-suggestion-code={item.code}
                    onClick={() => { if (!disabled) onSelect(item); }}
                >
                    <Icon size={16} aria-hidden="true" /><span>{item.label}</span>
                </button>
        );
    };
    return (
        <div ref={container} data-suggestion-strip data-horizontal={horizontal} className={`${styles.chips} ${variant === 'grid' ? styles.grid : styles.compact}`} role="group" aria-label={ariaLabel} aria-busy={disabled || undefined}
            onPointerDown={startDrag} onPointerMove={moveDrag} onPointerUp={endDrag} onPointerCancel={endDrag} onLostPointerCapture={endDrag}
            onClickCapture={event => {
                if (!horizontal || !suppressClick.current) return;
                suppressClick.current = false;
                event.preventDefault();
                event.stopPropagation();
            }}>
            {variant === 'grid' && groups.length >= 2 ? groups.map(group => <div className={styles.section} key={group}>
                <h3 className={styles.heading}>{group}</h3>
                <div className={styles.gridButtons}>{items.filter(item => (item.group ?? 'Gợi ý') === group).map(button)}</div>
            </div>) : items.map(button)}
        </div>
    );
};

export default SuggestionChips;
