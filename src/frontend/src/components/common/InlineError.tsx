import React from 'react';
import { Alert, Button } from 'antd';
import { RefreshCw } from 'lucide-react';
import styles from './common.module.css';

interface InlineErrorProps {
    message: string;
    onRetry?: () => void;
    title?: string;
}

export const InlineError: React.FC<InlineErrorProps> = ({
    message,
    onRetry,
    title = 'Có lỗi xảy ra'
}) => {
    return (
        <Alert
            type="error"
            showIcon
            className={styles.inlineError}
            title={title}
            description={message}
            action={onRetry && (
                <Button danger type="primary" onClick={onRetry} icon={<RefreshCw size={14} />}>
                    Thử lại
                </Button>
            )}
        />
    );
};
