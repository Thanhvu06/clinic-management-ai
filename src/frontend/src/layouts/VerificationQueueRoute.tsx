import { useAuth } from '../auth/AuthContext';
import { TechnicianVerificationQueue } from '../pages/technician/TechnicianVerificationQueue';

export function VerificationQueueRoute() {
    const { user } = useAuth();
    return <TechnicianVerificationQueue reviewPath={user?.role === 'LabSupervisor' ? '/lab/orders' : '/diagnostics/orders'} />;
}
