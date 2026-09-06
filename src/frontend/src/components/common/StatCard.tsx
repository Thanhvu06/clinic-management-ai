import React from 'react';

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
    const colorMap = {
        primary: { bg: 'var(--c-primary-light)', text: 'var(--c-primary)', border: 'rgba(15, 76, 129, 0.2)' },
        success: { bg: 'var(--c-success-bg)', text: 'var(--c-success)', border: 'rgba(5, 150, 105, 0.2)' },
        warning: { bg: 'var(--c-warning-bg)', text: 'var(--c-warning)', border: 'rgba(217, 119, 6, 0.2)' },
        danger: { bg: 'var(--c-danger-bg)', text: 'var(--c-danger)', border: 'rgba(220, 38, 38, 0.2)' },
        info: { bg: 'var(--c-info-bg)', text: 'var(--c-info)', border: 'rgba(2, 132, 199, 0.2)' },
        teal: { bg: 'var(--c-teal-light)', text: 'var(--c-teal)', border: 'rgba(8, 145, 178, 0.2)' }
    };

    const scheme = colorMap[color];

    return (
        <div 
            className="card"
            onClick={onClick}
            style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '20px',
                cursor: onClick ? 'pointer' : 'default',
                transition: 'transform 0.15s ease, box-shadow 0.15s ease',
                height: '100%',
                minHeight: '100px'
            }}
            onMouseEnter={onClick ? (e) => {
                e.currentTarget.style.transform = 'translateY(-2px)';
                e.currentTarget.style.boxShadow = 'var(--shadow-md)';
            } : undefined}
            onMouseLeave={onClick ? (e) => {
                e.currentTarget.style.transform = 'translateY(0)';
                e.currentTarget.style.boxShadow = 'var(--shadow-sm)';
            } : undefined}
        >
            <div>
                <div style={{
                    fontSize: '0.82rem',
                    fontWeight: 600,
                    textTransform: 'uppercase',
                    letterSpacing: '0.04em',
                    color: 'var(--c-text-light)',
                    marginBottom: '6px'
                }}>
                    {title}
                </div>
                <div style={{
                    fontSize: '1.75rem',
                    fontWeight: 700,
                    color: 'var(--c-text-dark)',
                    lineHeight: 1.1
                }}>
                    {value}
                </div>
                {subtitle && (
                    <div style={{
                        fontSize: '0.8rem',
                        color: 'var(--c-text-light)',
                        marginTop: '4px'
                    }}>
                        {subtitle}
                    </div>
                )}
            </div>
            <div style={{
                width: '48px',
                height: '48px',
                borderRadius: '12px',
                backgroundColor: scheme.bg,
                color: scheme.text,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
                border: `1px solid ${scheme.border}`
            }}>
                {icon}
            </div>
        </div>
    );
};
