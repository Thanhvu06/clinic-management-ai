import React from 'react';
import { CalendarDays, ClipboardList, FileText, Activity, Package, CreditCard, LayoutDashboard, HeartPulse } from 'lucide-react';
import type { AiSuggestionItem } from '../../types/ai';
import { sanitizeSuggestions } from './useSuggestionMenu';
import styles from './SuggestionChips.module.css';

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
    const items = sanitizeSuggestions(suggestions);
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
        <div className={`${styles.chips} ${variant === 'grid' ? styles.grid : styles.compact}`} role="group" aria-label={ariaLabel} aria-busy={disabled || undefined}>
            {variant === 'grid' && groups.length >= 2 ? groups.map(group => <div className={styles.section} key={group}>
                <h3 className={styles.heading}>{group}</h3>
                <div className={styles.gridButtons}>{items.filter(item => (item.group ?? 'Gợi ý') === group).map(button)}</div>
            </div>) : items.map(button)}
        </div>
    );
};

export default SuggestionChips;
