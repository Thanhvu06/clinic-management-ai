import React, { useEffect, useState } from 'react';
import { CheckCircle2, XCircle, AlertTriangle, Info, X } from 'lucide-react';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

export interface ToastItem {
    id: string;
    type: ToastType;
    message: string;
    duration?: number;
}

interface ToastProps {
    toasts: ToastItem[];
    onDismiss: (id: string) => void;
}

const iconMap: Record<ToastType, React.ReactNode> = {
    success: <CheckCircle2 size={18} />,
    error: <XCircle size={18} />,
    warning: <AlertTriangle size={18} />,
    info: <Info size={18} />,
};

const colorMap: Record<ToastType, { bg: string; color: string; border: string }> = {
    success: { bg: '#f0fdf4', color: '#166534', border: '#86efac' },
    error:   { bg: '#fef2f2', color: '#991b1b', border: '#fca5a5' },
    warning: { bg: '#fffbeb', color: '#92400e', border: '#fcd34d' },
    info:    { bg: '#eff6ff', color: '#1e40af', border: '#93c5fd' },
};

const ToastItemComponent: React.FC<{ toast: ToastItem; onDismiss: () => void }> = ({ toast, onDismiss }) => {
    useEffect(() => {
        const t = setTimeout(onDismiss, toast.duration ?? 4000);
        return () => clearTimeout(t);
    }, [toast.id]);

    const c = colorMap[toast.type];
    return (
        <div style={{
            display: 'flex', alignItems: 'flex-start', gap: '10px',
            background: c.bg, color: c.color,
            border: `1px solid ${c.border}`,
            borderRadius: '10px', padding: '12px 16px',
            boxShadow: '0 4px 12px rgba(0,0,0,0.12)',
            minWidth: '280px', maxWidth: '420px',
            animation: 'slideIn 0.2s ease',
        }}>
            <span style={{ flexShrink: 0, marginTop: '1px' }}>{iconMap[toast.type]}</span>
            <span style={{ flex: 1, fontSize: '0.9rem', fontWeight: 500 }}>{toast.message}</span>
            <button onClick={onDismiss} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'inherit', padding: '0', flexShrink: 0, opacity: 0.6 }}>
                <X size={16} />
            </button>
        </div>
    );
};

export const ToastContainer: React.FC<ToastProps> = ({ toasts, onDismiss }) => {
    if (toasts.length === 0) return null;
    return (
        <div style={{
            position: 'fixed', bottom: '24px', right: '24px',
            display: 'flex', flexDirection: 'column', gap: '10px',
            zIndex: 9999,
        }}>
            <style>{`
                @keyframes slideIn {
                    from { opacity: 0; transform: translateY(8px); }
                    to { opacity: 1; transform: translateY(0); }
                }
            `}</style>
            {toasts.map(t => (
                <ToastItemComponent key={t.id} toast={t} onDismiss={() => onDismiss(t.id)} />
            ))}
        </div>
    );
};

// Hook to manage toasts
export const useToast = () => {
    const [toasts, setToasts] = useState<ToastItem[]>([]);

    const addToast = (type: ToastType, message: string, duration?: number) => {
        const id = Math.random().toString(36).slice(2);
        setToasts(prev => [...prev, { id, type, message, duration }]);
    };

    const dismiss = (id: string) => {
        setToasts(prev => prev.filter(t => t.id !== id));
    };

    return {
        toasts,
        dismiss,
        success: (msg: string) => addToast('success', msg),
        error:   (msg: string) => addToast('error', msg),
        warning: (msg: string) => addToast('warning', msg),
        info:    (msg: string) => addToast('info', msg),
    };
};
