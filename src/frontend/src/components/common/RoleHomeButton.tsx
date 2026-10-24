import { Button } from 'antd';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { getRoleDashboardPath } from '../../utils/roleRoutes';

export function RoleHomeButton({ children, className }: { children: React.ReactNode; className?: string }) {
    const { user } = useAuth();
    const navigate = useNavigate();
    return <Button type="primary" className={className} onClick={() => navigate(user ? getRoleDashboardPath(user.role) : '/')}>{children}</Button>;
}
