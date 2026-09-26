import React from 'react';
import { Loader2 } from 'lucide-react';

interface LoadingStateProps {
    message?: string;
    height?: string;
}

export const LoadingState: React.FC<LoadingStateProps> = ({
    message = 'Đang tải dữ liệu...',
    height = '240px'
}) => {
    return (
        <div style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: height,
            width: '100%',
            gap: '12px',
            color: 'var(--c-text-light)'
        }}>
            <Loader2 size={32} className="animate-spin" style={{ color: 'var(--c-primary)', animation: 'spin 1s linear infinite' }} />
            <span style={{ fontSize: '0.9rem', fontWeight: 500 }}>{message}</span>
            <style>{`
                @keyframes spin {
                    from { transform: rotate(0deg); }
                    to { transform: rotate(360deg); }
                }
            `}</style>
        </div>
    );
};
