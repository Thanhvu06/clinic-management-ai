import React, { createContext, useContext, useState, useCallback } from 'react';
import type { ReactNode } from 'react';
import { CheckCircle, AlertTriangle, Info, XCircle } from 'lucide-react';

type DialogType = 'success' | 'error' | 'warning' | 'info';

interface Toast {
    id: string;
    message: string;
    type: DialogType;
}

interface DialogState {
    isOpen: boolean;
    title: string;
    message: string;
    type: DialogType;
    onConfirm?: () => void;
    onCancel?: () => void;
    confirmText?: string;
    cancelText?: string;
    isConfirm: boolean;
}

interface DialogContextProps {
    showAlert: (message: string, title?: string, type?: DialogType) => void;
    showConfirm: (message: string, onConfirm: () => void, title?: string) => void;
    showToast: (message: string, type?: DialogType) => void;
}

const DialogContext = createContext<DialogContextProps | undefined>(undefined);

export const useDialog = () => {
    const context = useContext(DialogContext);
    if (!context) throw new Error('useDialog must be used within DialogProvider');
    return context;
};

export const DialogProvider: React.FC< { children: ReactNode }> = ({ children }) => {
    const [dialog, setDialog] = useState<DialogState>({
        isOpen: false,
        title: '',
        message: '',
        type: 'info',
        isConfirm: false
    });
    
    const [toasts, setToasts] = useState<Toast[]>([]);

    const showAlert = useCallback((message: string, title: string = 'Thông báo', type: DialogType = 'info') => {
        setDialog({ isOpen: true, title, message, type, isConfirm: false });
    }, []);

    const showConfirm = useCallback((message: string, onConfirm: () => void, title: string = 'Xác nhận') => {
        setDialog({
            isOpen: true,
            title,
            message,
            type: 'warning',
            isConfirm: true,
            onConfirm,
            confirmText: 'Xác nhận',
            cancelText: 'Hủy'
        });
    }, []);

    const showToast = useCallback((message: string, type: DialogType = 'success') => {
        const id = Math.random().toString(36).substr(2, 9);
        setToasts(prev => [...prev, { id, message, type }]);
        setTimeout(() => {
            setToasts(prev => prev.filter(t => t.id !== id));
        }, 3000);
    }, []);

    const closeDialog = () => {
        setDialog(prev => ({ ...prev, isOpen: false }));
    };

    const handleConfirm = () => {
        if (dialog.onConfirm) dialog.onConfirm();
        closeDialog();
    };

    const renderIcon = (type: DialogType) => {
        switch (type) {
            case 'success': return <CheckCircle size={48} className="" style={{ color: 'var(--c-success)' }} />;
            case 'error': return <XCircle size={48} className="" style={{ color: 'var(--c-danger)' }} />;
            case 'warning': return <AlertTriangle size={48} className="" style={{ color: 'var(--c-warning)' }} />;
            case 'info': return <Info size={48} className="" style={{ color: 'var(--c-info)' }} />;
        }
    };

    return (
        <DialogContext.Provider value={{ showAlert, showConfirm, showToast }}>
            {children}
            
            {dialog.isOpen && (
                <div className="dialog-overlay" role="dialog" aria-modal="true" style={{
                    position: 'fixed', top: 0, left: 0, width: '100%', height: '100%',
                    backgroundColor: 'rgba(15, 23, 42, 0.6)', backdropFilter: 'blur(4px)',
                    display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 9999
                }}>
                    <div className="dialog-content" style={{
                        background: 'white', padding: '24px', borderRadius: '12px',
                        width: '90%', maxWidth: '400px', boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1)',
                        display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center'
                    }}>
                        <div style={{ marginBottom: '16px' }}>
                            {renderIcon(dialog.type)}
                        </div>
                        <h3 style={{ margin: '0 0 12px 0', fontSize: '1.25rem', color: 'var(--c-navy)' }}>{dialog.title}</h3>
                        <p style={{ margin: '0 0 24px 0', color: 'var(--c-text)', fontSize: '0.95rem' }}>{dialog.message}</p>
                        
                        <div style={{ display: 'flex', gap: '12px', width: '100%', justifyContent: 'center' }}>
                            {dialog.isConfirm ? (
                                <>
                                    <button className="btn-secondary" style={{ flex: 1 }} onClick={closeDialog}>
                                        {dialog.cancelText || 'Hủy'}
                                    </button>
                                    <button className="btn-primary" style={{ flex: 1, backgroundColor: 'var(--c-danger)', borderColor: 'var(--c-danger)' }} onClick={handleConfirm}>
                                        {dialog.confirmText || 'Xác nhận'}
                                    </button>
                                </>
                            ) : (
                                <button className="btn-primary" style={{ minWidth: '120px' }} onClick={closeDialog}>
                                    Đóng
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            )}

            <div style={{
                position: 'fixed', bottom: '24px', right: '24px', display: 'flex', flexDirection: 'column', gap: '8px', zIndex: 9999
            }}>
                {toasts.map(toast => (
                    <div key={toast.id} style={{
                        background: toast.type === 'error' ? 'var(--c-danger)' : 'var(--c-navy)',
                        color: 'white', padding: '12px 20px', borderRadius: '8px',
                        boxShadow: '0 10px 15px -3px rgba(0, 0, 0, 0.1)',
                        display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.9rem'
                    }}>
                        {toast.message}
                    </div>
                ))}
            </div>
        </DialogContext.Provider>
    );
};
