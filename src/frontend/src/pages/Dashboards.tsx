import { RoleHomeButton } from '../components/common/RoleHomeButton';
import React from 'react';
import styles from './Dashboards.module.css';
import { ShieldPlus, FileQuestion } from 'lucide-react';

export { AdminDashboard } from './admin/AdminDashboard';

export const ForbiddenPage: React.FC = () => {
    return (
        <div className={[styles.emptyState, styles.surface1].join(' ')}>
            <ShieldPlus size={64} className={styles.icon1} />
            <h1 className={styles.heading1}>403 - Truy cập bị từ chối</h1>
            <p>Tài khoản của bạn không có quyền xem trang này.</p>
            <RoleHomeButton className={styles.icon2}>Quay lại trang chủ</RoleHomeButton>
        </div>
    );
};

export const NotFoundPage: React.FC = () => {
    return (
        <div className={[styles.emptyState, styles.surface1].join(' ')}>
            <FileQuestion size={64} className={styles.icon1} aria-hidden="true" />
            <h1 className={styles.heading2}>404</h1>
            <p>Trang bạn yêu cầu không tồn tại.</p>
            <RoleHomeButton className={styles.icon2}>Quay lại trang chủ</RoleHomeButton>
        </div>
    );
};
