import { Card, Descriptions } from 'antd';
import styles from '../MedicineDetail.module.css';

export interface PrescribedDose {
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
    quantity: number;
}
export function PrescribedDoseCard({ prescribed, unit }: { prescribed: PrescribedDose; unit: string }) {
    return <Card className={styles.prescribed} title="Liều bác sĩ kê cho bạn">
        <p><strong>Làm theo liều trong đơn, ưu tiên hơn hướng dẫn chung.</strong></p>
        <Descriptions column={1} items={[
            { key: 'dosage', label: 'Liều dùng', children: prescribed.dosage || 'Chưa cập nhật' },
            { key: 'frequency', label: 'Tần suất', children: prescribed.frequency || 'Chưa cập nhật' },
            { key: 'duration', label: 'Số ngày', children: prescribed.durationDays == null ? 'Chưa cập nhật' : `${prescribed.durationDays} ngày` },
            { key: 'quantity', label: 'Số lượng', children: `${prescribed.quantity} ${unit}` },
            { key: 'instructions', label: 'Lời dặn', children: prescribed.instructions || 'Chưa cập nhật' },
        ]} />
    </Card>;
}
