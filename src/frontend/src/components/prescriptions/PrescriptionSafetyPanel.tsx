import { Alert, Tag } from 'antd';
import { InlineError, LoadingState } from '../common';
import type { PrescriptionSafetyResultDto } from '../../types/prescriptionSafety';
import styles from './PrescriptionSafetyPanel.module.css';

interface Props {
    result?: PrescriptionSafetyResultDto | null;
    loading?: boolean;
    error?: string;
    onRetry?: () => void;
    overrideReason?: string | null;
}

const severityLabels: Record<number, string> = { 1: 'Nhẹ', 2: 'Vừa', 3: 'Nặng', 4: 'Đe dọa tính mạng' };

export function PrescriptionSafetyPanel({ result, loading = false, error, onRetry, overrideReason }: Props) {
    return <section className={styles.panel} aria-label="An toàn kê đơn">
        <h3 className={styles.title}>An toàn kê đơn</h3>
        {loading ? <LoadingState message="Đang kiểm tra an toàn đơn thuốc..." />
            : error ? <InlineError message={error} onRetry={onRetry} />
            : <>
                {!!result?.allergies.length && <div className={styles.allergies}><strong>Dị ứng đã ghi nhận:</strong>{result.allergies.map(allergy => <Tag key={allergy.id} color={allergy.severity >= 3 ? 'error' : allergy.severity === 2 ? 'warning' : 'default'} title={allergy.reactionDescription || undefined}>{allergy.allergenName} • {severityLabels[allergy.severity] || allergy.severityName}</Tag>)}</div>}
                {result?.warnings.length ? <div className={styles.warnings}>{result.warnings.map((warning, index) => <Alert key={`${warning.type}:${warning.medicineIds.join(',')}:${index}`} type={warning.level === 'Danger' ? 'error' : warning.level === 'Warning' ? 'warning' : 'info'} showIcon title={warning.title} description={warning.detail} />)}</div>
                    : <p className={styles.empty}>Chưa phát hiện cảnh báo</p>}
                {overrideReason && <p className={styles.reason}><strong>Lý do bác sĩ:</strong> {overrideReason}</p>}
            </>}
        <p className={styles.note}>Dữ liệu tương tác mang tính tham khảo, cần dược sĩ/bác sĩ xác nhận.</p>
    </section>;
}
