import React from 'react';
import styles from './forms.module.css';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
    variant?: 'primary' | 'secondary' | 'outline' | 'ghost' | 'danger';
    size?: 'sm' | 'md' | 'lg';
    fullWidth?: boolean;
    loading?: boolean;
    startIcon?: React.ReactNode;
    endIcon?: React.ReactNode;
}

export const Button: React.FC<ButtonProps> = ({
    variant = 'primary',
    size = 'md',
    fullWidth = false,
    loading = false,
    disabled,
    startIcon,
    endIcon,
    children,
    className,
    style,
    ...props
}) => {
    let variantClass = styles.btnPrimary;
    if (variant === 'secondary') variantClass = styles.btnSecondary;
    if (variant === 'outline') variantClass = styles.btnOutline;
    if (variant === 'ghost') variantClass = styles.btnGhost;
    if (variant === 'danger') variantClass = styles.btnDanger;

    const sizeClass = size === 'lg' ? styles.btnLg : (size === 'sm' ? styles.btnSm : '');
    const widthStyle = fullWidth ? { width: '100%', justifyContent: 'center' } : {};

    return (
        <button
            className={`${styles.btn} ${variantClass} ${sizeClass} ${className || ''}`}
            disabled={disabled || loading}
            style={{ ...widthStyle, ...style }}
            {...props}
        >
            {loading ? (
                <div className={styles.spinner} role="status" aria-label="Đang xử lý..." />
            ) : (
                startIcon
            )}
            {children}
            {!loading && endIcon}
        </button>
    );
};
