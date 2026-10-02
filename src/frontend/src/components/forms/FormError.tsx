import React from 'react';
import { AlertCircle } from 'lucide-react';
import styles from './forms.module.css';

interface FormErrorProps {
    message?: string | null;
    className?: string;
}

export const FormError: React.FC<FormErrorProps> = ({ message, className }) => {
    if (!message) return null;
    return (
        <div className={`${styles.formErrorBanner} ${className || ''}`} role="alert">
            <AlertCircle size={18} />
            <div>{message}</div>
        </div>
    );
};
