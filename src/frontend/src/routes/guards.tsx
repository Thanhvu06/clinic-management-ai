import React from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export const ProtectedRoute: React.FC = () => {
    const { isAuthenticated, loading } = useAuth();
    const location = useLocation();

    if (loading) return <div style={{ padding: '20px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải...</div>;

    return isAuthenticated ? <Outlet /> : <Navigate to="/login" state={{ from: location }} replace />;
};

export const PublicRoute: React.FC = () => {
    const { isAuthenticated, user, loading } = useAuth();

    if (loading) return <div style={{ padding: '20px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải...</div>;

    if (isAuthenticated && user) {
        let path = '/patient';
        if (user.role === 'Admin') path = '/admin';
        else if (user.role === 'Doctor') path = '/doctor';
        else if (user.role === 'Receptionist') path = '/reception';
        
        return <Navigate to={path} replace />;
    }

    return <Outlet />;
};

interface RoleRouteProps {
    roles: string[];
}

export const RoleRoute: React.FC<RoleRouteProps> = ({ roles }) => {
    const { isAuthenticated, user, loading } = useAuth();

    if (loading) return <div style={{ padding: '20px', textAlign: 'center', color: 'var(--c-muted)' }}>Đang tải...</div>;

    if (!isAuthenticated) {
        return <Navigate to="/login" replace />;
    }

    if (user && !roles.includes(user.role)) {
        return <Navigate to="/403" replace />;
    }

    return <Outlet />;
};
