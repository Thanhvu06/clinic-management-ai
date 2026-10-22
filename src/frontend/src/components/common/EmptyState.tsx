import React from 'react';
import { Typography } from 'antd';
import styles from './common.module.css';

interface EmptyStateProps {
    /** Không còn hiển thị; giữ để các trang cũ truyền vào vẫn biên dịch được. */
    icon?: React.ReactNode;
    title: string;
    description?: string;
    action?: React.ReactNode;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
    title,
    description,
    action
}) => {
    return (
        <div className={styles.emptyState} data-testid="empty-state">
            <Typography.Text type="secondary" className={styles.emptyTitle}>{title}</Typography.Text>
            {description && (
                <Typography.Text type="secondary" className={styles.emptyDescription}>
                    {description}
                </Typography.Text>
            )}
            {action && (
                <div className={styles.emptyAction}>
                    {action}
                </div>
            )}
        </div>
    );
};
