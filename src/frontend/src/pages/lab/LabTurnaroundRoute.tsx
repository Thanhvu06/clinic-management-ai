import { useAuth } from '../../auth/AuthContext';
import { LabTurnaroundReport } from './LabTurnaroundReport';

export const LabTurnaroundRoute = () => {
    const { user } = useAuth();
    return <LabTurnaroundReport readOnly={user?.role === 'Admin'} />;
};
