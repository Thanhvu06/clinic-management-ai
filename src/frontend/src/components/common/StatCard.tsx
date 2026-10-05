import React from 'react';
import { Card, Flex, Typography } from 'antd';
import styles from './common.module.css';

interface StatCardProps {
    title: string;
    value: number | string;
    subtitle?: string;
    icon: React.ReactNode;
    color?: 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'teal';
    onClick?: () => void;
}

export const StatCard: React.FC<StatCardProps> = ({
    title,
    value,
    subtitle,
    icon,
    color = 'primary',
    onClick
}) => {
    return (
        <Card
            hoverable={!!onClick}
            onClick={onClick}
            className={`${styles.statCard} ${onClick ? styles.statCardClickable : ''}`}
        >
            <Flex align="center" justify="space-between" gap="middle">
                <div>
                    <Typography.Text type="secondary" className={styles.statTitle}>
                        {title}
                    </Typography.Text>
                    <div className={styles.statValue}>
                        {value}
                    </div>
                    {subtitle && (
                        <Typography.Text type="secondary" className={styles.statSubtitle}>
                            {subtitle}
                        </Typography.Text>
                    )}
                </div>
                <div className={`${styles.statIcon} ${styles[`statIcon_${color}`]}`}>
                    {icon}
                </div>
            </Flex>
        </Card>
    );
};
