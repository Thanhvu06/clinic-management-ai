import { formatDateOnly } from '../../utils/formatters';
import styles from './UnifiedCopilotPanel.module.css';

type Row = Record<string, unknown>;
const record = (value: unknown): Row => value && typeof value === 'object' && !Array.isArray(value) ? value as Row : {};
const text = (value: unknown): string => typeof value === 'string' || typeof value === 'number' ? String(value) : '';

export function CopilotWorkspaceReadCard({ type, data }: { type: string; data: unknown }) {
    const source = Array.isArray(data) ? data : record(data).items;
    const items = Array.isArray(source) ? source.map(record) : [];
    if (!items.length) return <p className={styles.typedNotice} role="status">{type === 'doctor_next_patient' ? 'Chưa có bệnh nhân tiếp theo.' : 'Không có bệnh nhân phù hợp.'}</p>;
    return <div className={styles.typedList}>{items.slice(0, type === 'doctor_next_patient' ? 1 : 5).map((item, index) => {
        const appointment = record(item.upcomingAppointment);
        return <article className={styles.typedItem} key={index}>
            <strong>{text(item.fullName ?? item.patientName)}</strong>
            {type === 'reception_patient_lookup' ? <>
                <div className={styles.typedLine}><strong>Mã bệnh án:</strong> {text(item.medicalRecordNumber)}</div>
                <div className={styles.typedLine}><strong>Năm sinh:</strong> {text(item.birthYear) || 'Chưa có'}</div>
                <div className={styles.typedLine}><strong>SĐT:</strong> {text(item.phone)}</div>
                {text(appointment.appointmentCode) && <div className={styles.typedLine}><strong>Lịch hẹn sắp tới:</strong> {text(appointment.appointmentCode)} · {formatDateOnly(text(appointment.appointmentDate))} {text(appointment.startTime).slice(0, 5)}</div>}
            </> : <>
                <div className={styles.typedLine}><strong>Mã lượt:</strong> {text(item.visitCode)}</div>
                <div className={styles.typedLine}><strong>Giờ:</strong> {text(item.startTime)}</div>
                <div className={styles.typedLine}><strong>Lý do khám:</strong> {text(item.reason)}</div>
            </>}
        </article>;
    })}</div>;
}
