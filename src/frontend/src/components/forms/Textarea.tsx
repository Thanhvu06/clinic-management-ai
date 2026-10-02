import React, { forwardRef } from 'react';
import styles from './forms.module.css';

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
    isInvalid?: boolean;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(({
    isInvalid,
    className,
    ...props
}, ref) => {
    return (
        <textarea
            ref={ref}
            aria-invalid={isInvalid ? 'true' : undefined}
            className={`
                ${styles.textarea}
                ${isInvalid ? styles.isInvalid : ''}
                ${className || ''}
            `}
            {...props}
        />
    );
});

Textarea.displayName = 'Textarea';
