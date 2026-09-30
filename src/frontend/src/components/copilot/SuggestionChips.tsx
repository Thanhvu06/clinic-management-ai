import React from 'react';
import type { AiSuggestionItem } from '../../types/ai';
import { sanitizeSuggestions } from './useSuggestionMenu';
import styles from './SuggestionChips.module.css';

interface SuggestionChipsProps {
    suggestions?: readonly AiSuggestionItem[] | null;
    disabled?: boolean;
    onSelect: (suggestion: AiSuggestionItem) => void;
    ariaLabel?: string;
}

/**
 * Role suggestion buttons. Selecting one sends only its server-owned code;
 * the label is what the conversation history shows.
 */
export const SuggestionChips: React.FC<SuggestionChipsProps> = ({ suggestions, disabled = false, onSelect, ariaLabel = 'Gợi ý tra cứu nhanh' }) => {
    const items = sanitizeSuggestions(suggestions);
    if (items.length === 0) return null;
    return (
        <div className={styles.chips} role="group" aria-label={ariaLabel} aria-busy={disabled || undefined}>
            {items.map(item => (
                <button
                    key={item.code}
                    type="button"
                    className={styles.chip}
                    disabled={disabled}
                    aria-label={`Gợi ý: ${item.label}`}
                    data-suggestion-code={item.code}
                    onClick={() => { if (!disabled) onSelect(item); }}
                >
                    {item.label}
                </button>
            ))}
        </div>
    );
};

export default SuggestionChips;
