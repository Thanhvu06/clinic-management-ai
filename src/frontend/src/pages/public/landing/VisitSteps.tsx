import { CalendarDays, ClipboardCheck, ReceiptText, Stethoscope } from 'lucide-react';
import shared from './LandingSections.module.css';
import styles from './VisitSteps.module.css';

const steps = [
    { icon: CalendarDays, title: 'Đặt lịch trực tuyến', detail: 'Chọn chuyên khoa, bác sĩ và giờ khám phù hợp.' },
    { icon: ClipboardCheck, title: 'Làm thủ tục tại quầy lễ tân', detail: 'Đọc mã lịch hẹn (APT-...) khi đến phòng khám.' },
    { icon: Stethoscope, title: 'Khám cùng bác sĩ', detail: 'Bác sĩ khám, chỉ định cận lâm sàng nếu cần.' },
    { icon: ReceiptText, title: 'Nhận đơn thuốc và thanh toán', detail: 'Xem đơn thuốc, kết quả đã công bố trên tài khoản.' },
];

export function VisitSteps() {
    return <section className={shared.section} aria-labelledby="landing-steps-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}><h2 id="landing-steps-title">Quy trình khám tại phòng khám</h2></div>
            <ol className={styles.steps}>{steps.map(({ icon: Icon, title, detail }, index) => <li key={title}>
                <div className={styles.marker}><Icon size={24} strokeWidth={1.5} aria-hidden="true" /><span>Bước {index + 1}</span></div>
                <h3>{title}</h3><p>{detail}</p>
            </li>)}</ol>
        </div>
    </section>;
}
