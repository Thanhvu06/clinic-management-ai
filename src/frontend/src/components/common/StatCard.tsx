import React from 'react';
import { Card } from 'antd';
import { MetricCell } from './MetricStrip';
import styles from './MetricStrip.module.css';

interface StatCardProps {
    title: string;
    value: number | string;
    subtitle?: string;
    /** Giữ để tương thích; không còn hiển thị theo docs/ui/DESIGN.md. */
    icon?: React.ReactNode;
    color?: 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'teal';
    onClick?: () => void;
}

export const StatCard: React.FC<StatCardProps> = ({
    title,
    value,
    subtitle,
    color = 'primary',
    onClick
}) => (
    <Card className={styles.standalone}>
        <MetricCell
            label={title}
            value={value}
            hint={subtitle}
            tone={color === 'warning' || color === 'danger' ? 'attention' : 'default'}
            onClick={onClick}
        />
    </Card>
);
