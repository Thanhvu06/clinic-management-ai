import React from 'react';
import { Empty, Typography } from 'antd';
import { Inbox } from 'lucide-react';
import styles from './common.module.css';

interface EmptyStateProps {
    icon?: React.ReactNode;
    title: string;
    description?: string;
    action?: React.ReactNode;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
    icon,
    title,
    description,
    action
}) => {
    return (
        <Empty
            className={styles.emptyState}
            image={icon || <Inbox size={48} />}
            description={
                <>
                    <Typography.Title level={3} className={styles.emptyTitle}>{title}</Typography.Title>
                    {description && (
                        <Typography.Text type="secondary" className={styles.emptyDescription}>
                            {description}
                        </Typography.Text>
                    )}
                </>
            }
        >
            {action && (
                <div className={styles.emptyAction}>
                    {action}
                </div>
            )}
        </Empty>
    );
};
