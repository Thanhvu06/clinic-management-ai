import React from 'react';
import { Link } from 'react-router-dom';
import styles from './MetricStrip.module.css';

export type MetricTone = 'default' | 'attention';

export interface MetricItem {
    label: string;
    value: number | string;
    hint?: string;
    tone?: MetricTone;
    to?: string;
    onClick?: () => void;
    pressed?: boolean;
    ariaLabel?: string;
}

interface MetricStripProps {
    items: MetricItem[];
    ariaLabel?: string;
    className?: string;
}

const needsAttention = (tone: MetricTone | undefined, value: number | string) =>
    tone === 'attention' && typeof value === 'number' && value > 0;

export const MetricCell: React.FC<MetricItem & { className?: string }> = ({
    label,
    value,
    hint,
    tone = 'default',
    to,
    onClick,
    pressed,
    ariaLabel,
    className
}) => {
    const attention = needsAttention(tone, value);
    const content = (
        <>
            <span className={styles.label}>{label}</span>
            <span className={attention ? `${styles.value} ${styles.valueAttention}` : styles.value} data-tone={attention ? 'attention' : 'default'}>
                {value}
            </span>
            {hint && <span className={styles.hint}>{hint}</span>}
        </>
    );
    const classes = [styles.cell, className, to || onClick ? styles.interactive : '', pressed ? styles.pressed : ''].filter(Boolean).join(' ');

    if (to) {
        return <Link to={to} className={classes} aria-label={ariaLabel}>{content}</Link>;
    }
    if (onClick) {
        return (
            <button type="button" className={classes} onClick={onClick} aria-pressed={pressed} aria-label={ariaLabel}>
                {content}
            </button>
        );
    }
    return <div className={classes}>{content}</div>;
};

/** Dải số liệu: một khối viền mảnh, mỗi ô gồm nhãn, giá trị và gợi ý tùy chọn. */
export const MetricStrip: React.FC<MetricStripProps> = ({ items, ariaLabel, className }) => (
    <div className={className ? `${styles.strip} ${className}` : styles.strip} role="group" aria-label={ariaLabel}>
        {items.map(item => <MetricCell key={item.label} {...item} />)}
    </div>
);
