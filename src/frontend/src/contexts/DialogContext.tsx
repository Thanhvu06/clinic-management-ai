import React, { createContext, useContext, useState, useCallback } from 'react';
import type { ReactNode } from 'react';
import { CheckCircle, AlertTriangle, Info, XCircle } from 'lucide-react';
import styles from './DialogContext.module.css';

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
            case 'success': return <CheckCircle size={48} className={["", styles.icon1].join(' ')} />;
            case 'error': return <XCircle size={48} className={["", styles.icon2].join(' ')} />;
            case 'warning': return <AlertTriangle size={48} className={["", styles.icon3].join(' ')} />;
            case 'info': return <Info size={48} className={["", styles.icon4].join(' ')} />;
        }
    };

    return (
        <DialogContext.Provider value={{ showAlert, showConfirm, showToast }}>
            {children}
            
            {dialog.isOpen && (
                <div className={["dialog-overlay", styles.overlay1].join(' ')} role="dialog" aria-modal="true">
                    <div className={["dialog-content", styles.stackLayout1].join(' ')}>
                        <div className={styles.spacing1}>
                            {renderIcon(dialog.type)}
                        </div>
                        <h3 className={styles.heading1}>{dialog.title}</h3>
                        <p className={styles.paragraph1}>{dialog.message}</p>
                        
                        <div className={styles.flexLayout1}>
                            {dialog.isConfirm ? (
                                <>
                                    <button className={["btn-secondary", styles.button1].join(' ')} onClick={closeDialog}>
                                        {dialog.cancelText || 'Hủy'}
                                    </button>
                                    <button className={["btn-primary", styles.button2].join(' ')} onClick={handleConfirm}>
                                        {dialog.confirmText || 'Xác nhận'}
                                    </button>
                                </>
                            ) : (
                                <button className={["btn-primary", styles.button3].join(' ')} onClick={closeDialog}>
                                    Đóng
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            )}

            <div className={styles.overlay2}>
                {toasts.map(toast => (
                    <div key={toast.id} className={[styles.flexLayout2, (toast.type === 'error' ? styles.state1 : styles.state2)].join(' ')}>
                        {toast.message}
                    </div>
                ))}
            </div>
        </DialogContext.Provider>
    );
};
