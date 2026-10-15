import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { getRoleDashboardPath } from '../utils/roleRoutes';
import styles from './guards.module.css';

export const ProtectedRoute: React.FC = () => {
    const { isAuthenticated, loading } = useAuth();
    const location = useLocation();

    if (loading) return <div className={styles.tone1}>Đang tải...</div>;

    const fullPath = location.pathname + location.search + location.hash;
    const loginTarget = fullPath && fullPath !== '/' ? `/login?returnUrl=${encodeURIComponent(fullPath)}` : '/login';

    return isAuthenticated ? <Outlet /> : <Navigate to={loginTarget} replace />;
};

export const PublicRoute: React.FC = () => {
    const { isAuthenticated, user, loading } = useAuth();

    if (loading) return <div className={styles.tone1}>Đang tải...</div>;

    if (isAuthenticated && user) {
        const path = getRoleDashboardPath(user.role);
        return <Navigate to={path} replace />;
    }

    return <Outlet />;
};

interface RoleRouteProps {
    roles: string[];
}

export const RoleRoute: React.FC<RoleRouteProps> = ({ roles }) => {
    const { isAuthenticated, user, loading } = useAuth();
    const location = useLocation();

    if (loading) return <div className={styles.tone1}>Đang tải...</div>;

    if (!isAuthenticated) {
        const fullPath = location.pathname + location.search + location.hash;
        const loginTarget = fullPath && fullPath !== '/' ? `/login?returnUrl=${encodeURIComponent(fullPath)}` : '/login';
        return <Navigate to={loginTarget} replace />;
    }

    if (user && !roles.includes(user.role)) {
        return <Navigate to="/forbidden" replace />;
    }

    return <Outlet />;
};
