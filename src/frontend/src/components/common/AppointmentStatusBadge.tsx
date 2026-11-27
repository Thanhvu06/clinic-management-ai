import { appointmentStatusLabels, getAppointmentStatusLabel } from '../../utils/appointmentStatusLabels';
import { StatusBadge } from './StatusBadge';
import styles from './AppointmentStatusBadge.module.css';

const toneClasses = {
    Pending: styles.amber,
    Confirmed: styles.blue,
    CheckedIn: styles.teal,
    InConsultation: styles.purple,
    InProgress: styles.purple,
    Completed: styles.green,
    Cancelled: styles.gray,
    NoShow: styles.red,
    PendingReschedule: styles.orange,
    PendingCancellation: styles.deepOrange,
} satisfies Record<keyof typeof appointmentStatusLabels, string>;

export function AppointmentStatusBadge({ status, label, size }: { status: string; label?: string; size?: 'sm' | 'md' }) {
    const toneClass = Object.hasOwn(toneClasses, status) ? toneClasses[status as keyof typeof toneClasses] : undefined;
    return <StatusBadge status={status} label={label ?? getAppointmentStatusLabel(status)} size={size} toneClass={toneClass} />;
}
