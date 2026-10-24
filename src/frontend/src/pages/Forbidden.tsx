import { RoleHomeButton } from '../components/common/RoleHomeButton';
import React from 'react';
import { ShieldAlert } from 'lucide-react';
import styles from './Forbidden.module.css';

export const Forbidden: React.FC = () => {
    return (
        <div className={styles.stackLayout1}>
            <ShieldAlert size={64} color="var(--cc-color-danger)" className={styles.icon1} />
            <h1 className={styles.heading1}>Bạn không có quyền truy cập</h1>
            <p className={styles.paragraph1}>
                Tài khoản của bạn không được cấp phép để xem nội dung hoặc thực hiện thao tác này.
            </p>
            <RoleHomeButton className={styles.icon2}>
                Quay về trang chủ
            </RoleHomeButton>
        </div>
    );
};
