import React from 'react';
import styles from './forms.module.css';

export interface FormFieldProps {
    id?: string;
    htmlFor?: string;
    label?: string;
    required?: boolean;
    optional?: boolean;
    helperText?: string;
    helpText?: string;
    error?: string;
    children: React.ReactNode;
    className?: string;
}

export const FormField: React.FC<FormFieldProps> = ({
    id,
    htmlFor,
    label,
    required,
    optional,
    helperText,
    helpText,
    error,
    children,
    className
}) => {
    const targetId = htmlFor || id;
    const resolvedHelp = error ? undefined : (helperText || helpText);
    const errorId = targetId && error ? `${targetId}-error` : undefined;
    const helpId = targetId && resolvedHelp ? `${targetId}-help` : undefined;

    return (
        <div className={`${styles.formField} ${className || ''}`}>
            {label && (
                <div className={styles.labelRow}>
                    <label htmlFor={targetId} className={styles.label}>
                        {label}
                        {required && <span className={styles.required}>*</span>}
                    </label>
                    {optional && <span className={styles.optionalBadge}>(Không bắt buộc)</span>}
                </div>
            )}
            {children}
            {error ? (
                <div id={errorId} role="alert" className={styles.errorText}>{error}</div>
            ) : resolvedHelp ? (
                <div id={helpId} className={styles.helperText}>{resolvedHelp}</div>
            ) : null}
        </div>
    );
};
