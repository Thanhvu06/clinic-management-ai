import React, { forwardRef } from 'react';
import styles from './forms.module.css';

export interface TextInputProps extends React.InputHTMLAttributes<HTMLInputElement> {
    startIcon?: React.ReactNode;
    icon?: React.ReactNode;
    endIcon?: React.ReactNode;
    isInvalid?: boolean;
    hasError?: boolean;
}

export const TextInput = forwardRef<HTMLInputElement, TextInputProps>(({
    startIcon,
    icon,
    endIcon,
    isInvalid,
    hasError,
    className,
    ...props
}, ref) => {
    const resolvedStartIcon = startIcon || icon;
    const resolvedInvalid = isInvalid || hasError;

    return (
        <div className={styles.inputWrapper}>
            {resolvedStartIcon && <div className={styles.startIcon}>{resolvedStartIcon}</div>}
            <input
                ref={ref}
                className={`
                    ${styles.input}
                    ${resolvedStartIcon ? styles.hasStartIcon : ''}
                    ${endIcon ? styles.hasEndIcon : ''}
                    ${resolvedInvalid ? styles.isInvalid : ''}
                    ${className || ''}
                `}
                {...props}
            />
            {endIcon && <div className={styles.endIcon}>{endIcon}</div>}
        </div>
    );
});

TextInput.displayName = 'TextInput';
