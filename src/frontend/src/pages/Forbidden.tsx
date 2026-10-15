import React from 'react';
import { Link } from 'react-router-dom';
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
            <Link to="/" className={["btn-primary", styles.icon2].join(' ')}>
                Quay về trang chủ
            </Link>
        </div>
    );
};
