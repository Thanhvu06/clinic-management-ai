import React from 'react';
import { Flex, Typography } from 'antd';
import styles from './common.module.css';

interface PageHeaderProps {
    title: string;
    subtitle?: string;
    badge?: React.ReactNode;
    actions?: React.ReactNode;
}

export const PageHeader: React.FC<PageHeaderProps> = ({
    title,
    subtitle,
    badge,
    actions
}) => {
    return (
        <Flex justify="space-between" align="flex-start" wrap gap="middle" className={styles.pageHeader}>
            <div>
                <Flex align="center" gap="small" wrap>
                    <Typography.Title level={1} className={styles.pageTitle}>
                        {title}
                    </Typography.Title>
                    {badge}
                </Flex>
                {subtitle && (
                    <Typography.Text type="secondary" className={styles.pageSubtitle}>
                        {subtitle}
                    </Typography.Text>
                )}
            </div>
            {actions && (
                <Flex align="center" gap="small" wrap>
                    {actions}
                </Flex>
            )}
        </Flex>
    );
};
