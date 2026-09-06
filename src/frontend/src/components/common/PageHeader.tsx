import React from 'react';

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
        <div style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            flexWrap: 'wrap',
            gap: '16px',
            marginBottom: '24px'
        }}>
            <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
                    <h1 style={{
                        fontSize: '1.5rem',
                        fontWeight: 700,
                        color: 'var(--c-text-dark)',
                        margin: 0,
                        letterSpacing: '-0.02em'
                    }}>
                        {title}
                    </h1>
                    {badge}
                </div>
                {subtitle && (
                    <p style={{
                        margin: '6px 0 0 0',
                        color: 'var(--c-text-light)',
                        fontSize: '0.9rem'
                    }}>
                        {subtitle}
                    </p>
                )}
            </div>
            {actions && (
                <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    flexWrap: 'wrap'
                }}>
                    {actions}
                </div>
            )}
        </div>
    );
};
