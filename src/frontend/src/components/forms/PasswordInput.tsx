import React, { useState, forwardRef } from 'react';
import { Eye, EyeOff, Lock } from 'lucide-react';
import styles from './forms.module.css';

export interface PasswordInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type'> {
    startIcon?: React.ReactNode;
    icon?: React.ReactNode;
    isInvalid?: boolean;
    hasError?: boolean;
}

export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(({
    startIcon = <Lock size={18} />,
    icon,
    isInvalid,
    hasError,
    className,
    ...props
}, ref) => {
    const [showPassword, setShowPassword] = useState(false);
    const resolvedStartIcon = icon || startIcon;
    const resolvedInvalid = isInvalid || hasError;

    return (
        <div className={styles.inputWrapper}>
            {resolvedStartIcon && <div className={styles.startIcon}>{resolvedStartIcon}</div>}
            <input
                ref={ref}
                type={showPassword ? 'text' : 'password'}
                aria-invalid={resolvedInvalid ? 'true' : undefined}
                className={`
                    ${styles.input}
                    ${resolvedStartIcon ? styles.hasStartIcon : ''}
                    ${styles.hasEndIcon}
                    ${resolvedInvalid ? styles.isInvalid : ''}
                    ${className || ''}
                `}
                {...props}
            />
            <div className={styles.endIcon}>
                <button
                    type="button"
                    className={styles.eyeBtn}
                    onClick={() => setShowPassword(!showPassword)}
                    aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
                >
                    {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                </button>
            </div>
        </div>
    );
});

PasswordInput.displayName = 'PasswordInput';
