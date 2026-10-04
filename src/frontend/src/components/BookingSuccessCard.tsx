import { CheckCircle2 } from 'lucide-react';
import type { AiAction, ChatMessage } from '../types/ai';
import { formatVietnameseDate } from '../hooks/useAiBookingFlow';
import styles from './MedicalChatWidget.module.css';

export const BookingSuccessCard = ({ result, onAction, busy = false }: {
    result: NonNullable<ChatMessage['bookingResult']>; onAction: (action: AiAction) => void; busy?: boolean;
}) => {
    const rows = [['Mã lịch hẹn', result.appointmentCode], ['Khoa', result.specialtyName], ['Bác sĩ', result.doctorName],
        ['Cơ sở', result.facilityName], ['Ngày giờ', result.slotDate ? `${formatVietnameseDate(result.slotDate.split('T')[0])}${result.startTime ? ` · ${result.startTime.slice(0, 5)}` : ''}${result.endTime ? ` – ${result.endTime.slice(0, 5)}` : ''}` : undefined], ['Lý do', result.reason]];
    return <section className={styles.bookingSummaryCard} aria-label="Kết quả đặt lịch">
        <h4 className={styles.bookingSummaryTitle}><CheckCircle2 size={20} />Đặt lịch khám thành công</h4>
        <dl className={styles.summaryTable}>{rows.filter(([, value]) => value).map(([label, value]) => <div className={styles.summaryRow} key={label}><dt className={styles.summaryLabel}>{label}</dt><dd className={styles.summaryValue}>{value}</dd></div>)}</dl>
        <button type="button" className={`${styles.actionBtn} ${styles.btnPrimary}`} disabled={busy} onClick={() => onAction({
            id: `view-booking-${result.appointmentId}`, type: 'ViewMyAppointments', label: 'Xem lịch sắp khám', style: 'primary',
            requiresAuthentication: true, requiresConfirmation: false,
            payload: { targetUrl: `/patient/appointments?tab=upcoming&appointmentId=${result.appointmentId}` }
        })}>Xem lịch sắp khám</button>
    </section>;
};
