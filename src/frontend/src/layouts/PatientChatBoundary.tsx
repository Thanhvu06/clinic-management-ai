import { useAuth } from '../auth/AuthContext';
import { MedicalChatWidget } from '../components/MedicalChatWidget';

export function PatientChatBoundary() {
    const { user, loading } = useAuth();
    if (loading || (user && user.role !== 'Patient')) return null;
    return <MedicalChatWidget />;
}
