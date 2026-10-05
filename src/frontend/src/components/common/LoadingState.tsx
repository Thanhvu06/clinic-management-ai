import React from 'react';
import { Flex, Spin, Typography } from 'antd';
import styles from './common.module.css';

interface LoadingStateProps {
    message?: string;
    height?: string;
}

export const LoadingState: React.FC<LoadingStateProps> = ({
    message = 'Đang tải dữ liệu...',
    height = '240px'
}) => {
    return (
        // minHeight là giá trị runtime từ props nên truyền qua style (ngoại lệ được ghi trong docs/UI_GUIDELINES.md).
        <Flex vertical align="center" justify="center" gap="small" className={styles.loadingState} style={{ minHeight: height }}>
            <Spin size="large" />
            <Typography.Text type="secondary" className={styles.loadingMessage}>{message}</Typography.Text>
        </Flex>
    );
};
