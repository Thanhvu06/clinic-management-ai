import React from 'react';
import { AlertCircle, RefreshCw } from 'lucide-react';

interface InlineErrorProps {
    message: string;
    onRetry?: () => void;
    title?: string;
}

export const InlineError: React.FC<InlineErrorProps> = ({
    message,
    onRetry,
    title = 'Có lỗi xảy ra'
}) => {
    return (
        <div style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: '16px',
            padding: '16px 20px',
            backgroundColor: 'var(--c-danger-bg)',
            border: '1px solid rgba(220, 38, 38, 0.25)',
            borderRadius: 'var(--radius-lg)',
            marginBottom: '20px'
        }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                <AlertCircle size={22} style={{ color: 'var(--c-danger)', flexShrink: 0 }} />
                <div>
                    <div style={{ fontWeight: 600, fontSize: '0.92rem', color: 'var(--c-danger)' }}>
                        {title}
                    </div>
                    <div style={{ fontSize: '0.86rem', color: 'var(--c-text)', marginTop: '2px' }}>
                        {message}
                    </div>
                </div>
            </div>

            {onRetry && (
                <button
                    type="button"
                    className="btn-danger"
                    onClick={onRetry}
                    style={{ fontSize: '0.84rem', padding: '6px 14px', height: '34px' }}
                >
                    <RefreshCw size={14} />
                    <span>Thử lại</span>
                </button>
            )}
        </div>
    );
};
