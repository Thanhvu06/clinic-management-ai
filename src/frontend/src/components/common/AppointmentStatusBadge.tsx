import { getAppointmentStatusLabel } from '../../utils/appointmentStatusLabels';
import { StatusBadge } from './StatusBadge';

export function AppointmentStatusBadge({ status, size }: { status: string; size?: 'sm' | 'md' }) {
    return <StatusBadge status={status === 'InProgress' ? 'InConsultation' : status} label={getAppointmentStatusLabel(status)} size={size} />;
}
