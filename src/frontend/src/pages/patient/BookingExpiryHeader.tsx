import { Alert } from 'antd';
import { OnlineBookingNotice } from './OnlineBookingNotice';
import styles from './BookAppointment.module.css';

export function BookingExpiryHeader({ revisitRequestId }: { revisitRequestId?: number | null }) {
    return <>
        <OnlineBookingNotice />
        {revisitRequestId && <Alert className={styles.notice} role="status" type="info" showIcon title="Bạn đang chọn khung giờ cho một đề xuất tái khám. Lịch mới chỉ được tạo sau khi xác nhận ở bước cuối." />}
        <div className={styles.pageHeader}><h1 className={styles.pageTitle}>Đặt lịch khám</h1></div>
    </>;
}
