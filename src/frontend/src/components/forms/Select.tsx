import React, { forwardRef } from 'react';
import styles from './forms.module.css';

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
    isInvalid?: boolean;
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(({
    isInvalid,
    className,
    children,
    ...props
}, ref) => {
    return (
        <select
            ref={ref}
            className={`
                ${styles.select}
                ${isInvalid ? styles.isInvalid : ''}
                ${className || ''}
            `}
            {...props}
        >
            {children}
        </select>
    );
});

Select.displayName = 'Select';
