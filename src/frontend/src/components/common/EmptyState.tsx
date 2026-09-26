import React from 'react';
import { Inbox } from 'lucide-react';

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
        <div className="empty-state">
            <div className="empty-state-icon">
                {icon || <Inbox size={48} />}
            </div>
            <h3 style={{ fontSize: '1.05rem', fontWeight: 600 }}>{title}</h3>
            {description && (
                <p style={{
                    fontSize: '0.88rem',
                    color: 'var(--c-text-light)',
                    maxWidth: '400px',
                    margin: '0 auto 16px auto'
                }}>
                    {description}
                </p>
            )}
            {action && (
                <div style={{ marginTop: '12px' }}>
                    {action}
                </div>
            )}
        </div>
    );
};
