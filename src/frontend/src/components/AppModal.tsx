import React, { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';
import { X } from 'lucide-react';

interface AppModalProps {
    isOpen: boolean;
    onClose: () => void;
    title: string;
    children: ReactNode;
    actions?: ReactNode;
    maxWidth?: string;
}

export const AppModal: React.FC<AppModalProps> = ({
    isOpen,
    onClose,
    title,
    children,
    actions,
    maxWidth = '520px'
}) => {
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (isOpen) {
            document.body.style.overflow = 'hidden';
            setTimeout(() => closeRef.current?.focus(), 50);
        } else {
            document.body.style.overflow = '';
        }
        return () => { document.body.style.overflow = ''; };
    }, [isOpen]);

    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && isOpen) onClose();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    return (
        <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="app-modal-title"
            style={{
                position: 'fixed', inset: 0,
                backgroundColor: 'rgba(15, 23, 42, 0.55)',
                backdropFilter: 'blur(4px)',
                display: 'flex', justifyContent: 'center', alignItems: 'center',
                zIndex: 1000, padding: '16px'
            }}
        >
            <div style={{
                background: 'white',
                borderRadius: '12px',
                width: '100%',
                maxWidth,
                maxHeight: '90vh',
                display: 'flex',
                flexDirection: 'column',
                boxShadow: '0 25px 50px -12px rgba(0,0,0,0.25)',
                overflow: 'hidden'
            }}>
                <div style={{
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                    padding: '20px 24px',
                    borderBottom: '1px solid var(--c-border)',
                    flexShrink: 0
                }}>
                    <h3 id="app-modal-title" style={{ margin: 0, fontSize: '1.1rem', color: 'var(--c-navy)', fontWeight: 600 }}>
                        {title}
                    </h3>
                    <button
                        ref={closeRef}
                        onClick={onClose}
                        style={{
                            background: 'none', border: 'none', cursor: 'pointer',
                            color: 'var(--c-text-light)', padding: '4px', borderRadius: '6px',
                            display: 'flex', alignItems: 'center'
                        }}
                        aria-label="Dong"
                    >
                        <X size={20} />
                    </button>
                </div>
                <div style={{ padding: '24px', overflowY: 'auto', flex: 1 }}>
                    {children}
                </div>
                {actions && (
                    <div style={{
                        display: 'flex', justifyContent: 'flex-end', gap: '12px',
                        padding: '16px 24px',
                        borderTop: '1px solid var(--c-border)',
                        flexShrink: 0
                    }}>
                        {actions}
                    </div>
                )}
            </div>
        </div>
    );
};
