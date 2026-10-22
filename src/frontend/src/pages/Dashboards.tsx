import React from 'react';
import { Link } from 'react-router-dom';
import styles from './Dashboards.module.css';
import { ShieldPlus } from 'lucide-react';

export { AdminDashboard } from './admin/AdminDashboard';

export const ForbiddenPage: React.FC = () => {
    return (
        <div className={[styles.emptyState, styles.surface1].join(' ')}>
            <ShieldPlus size={64} className={styles.icon1} />
            <h1 className={styles.heading1}>403 - Truy cập bị từ chối</h1>
            <p>Tài khoản của bạn không có quyền xem trang này.</p>
            <Link to="/" className={["btn-primary", styles.icon2].join(' ')}>Quay lại trang chủ</Link>
        </div>
    );
};

export const NotFoundPage: React.FC = () => {
    return (
        <div className={[styles.emptyState, styles.surface1].join(' ')}>
            <h1 className={styles.heading2}>404</h1>
            <p>Trang bạn yêu cầu không tồn tại.</p>
            <Link to="/" className={["btn-primary", styles.icon2].join(' ')}>Quay lại trang chủ</Link>
        </div>
    );
};
