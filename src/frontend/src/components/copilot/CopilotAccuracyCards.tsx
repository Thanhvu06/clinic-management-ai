import type { AiCopilotCard } from '../../api/aiCopilotApi';
import { formatDateOnly } from '../../utils/formatters';
import styles from './UnifiedCopilotPanel.module.css';

const types = new Set([
    'reception_pending_change_requests', 'reception_unpaid_invoices', 'technician_service_guidance',
    'pharmacy_medicine_stock', 'pharmacy_medicine_batches', 'admin_revenue_report',
    'admin_active_doctors', 'admin_pending_leave_requests',
]);
type Data = Record<string, unknown>;
const record = (value: unknown): Data => value && typeof value === 'object' && !Array.isArray(value) ? value as Data : {};
const rows = (value: unknown): Data[] => {
    const items = Array.isArray(value) ? value : record(value).items;
    return Array.isArray(items) ? items.filter(item => item && typeof item === 'object').map(record) : [];
};
const text = (value: unknown): string => typeof value === 'string' || typeof value === 'number' ? String(value) : '';
const money = (value: unknown): string => typeof value === 'number' && Number.isFinite(value) ? `${value.toLocaleString('vi-VN')} ₫` : '';
const date = (value: unknown): string => typeof value === 'string' ? formatDateOnly(value) : '';
const localDateTime = (value: unknown): string => typeof value === 'string' ? `${formatDateOnly(value)} ${value.slice(11, 16)}`.trim() : '';

export const isAccuracyCard = (type: string): boolean => types.has(type);
export const accuracyCardEmptyMessage = (card: AiCopilotCard): string | null | undefined => {
    if (!isAccuracyCard(card.type)) return undefined;
    if (card.type === 'admin_revenue_report') return typeof record(card.data).totalRevenue === 'number' ? null : 'Chưa có dữ liệu báo cáo doanh thu.';
    return rows(card.data).length ? null : 'Không có bản ghi phù hợp.';
};

const Line = ({ label, value }: { label: string; value: unknown }) => text(value)
    ? <div className={styles.typedLine}><strong>{label}:</strong> {text(value)}</div> : null;

export function CopilotAccuracyCard({ card }: { card: AiCopilotCard }) {
    const envelope = record(card.data);
    const empty = accuracyCardEmptyMessage(card);
    if (empty) return <p className={styles.typedNotice} role="status">{empty}</p>;
    if (card.type === 'admin_revenue_report') return <div className={styles.typedList}><article className={styles.typedItem}>
        <strong>Báo cáo doanh thu</strong>
        <Line label="Từ ngày" value={date(envelope.fromDate)} /><Line label="Đến ngày" value={date(envelope.toDate)} />
        <Line label="Doanh thu" value={money(envelope.totalRevenue)} /><Line label="Giao dịch thành công" value={envelope.totalSucceededTransactions} />
    </article></div>;
    return <div className={styles.typedList}>
        {typeof envelope.totalItems === 'number' && <Line label="Tổng số" value={envelope.totalItems} />}
        {rows(card.data).map((item, index) => <article className={styles.typedItem} key={index}>
            {card.type === 'reception_pending_change_requests' && <>
                <strong>{text(item.appointmentCode)}</strong><Line label="Bệnh nhân" value={item.patientName} />
                <Line label="Yêu cầu" value={item.requestType === 'Reschedule' ? 'Đổi lịch' : item.requestType === 'Cancellation' ? 'Hủy lịch' : item.requestType} />
                <Line label="Trạng thái" value={item.status === 'Pending' ? 'Chờ xử lý' : item.status} />
            </>}
            {card.type === 'reception_unpaid_invoices' && <>
                <strong>{text(item.invoiceCode)}</strong><Line label="Bệnh nhân" value={item.patientName} />
                <Line label="Số tiền" value={money(item.totalAmount)} /><Line label="Trạng thái" value="Chưa thanh toán" />
            </>}
            {card.type === 'technician_service_guidance' && <>
                <strong>{text(item.name)} ({text(item.code)})</strong>
                <Line label="Chuẩn bị" value={item.preparationInstructions ?? 'Chưa cấu hình'} /><Line label="Loại bệnh phẩm" value={item.specimenType ?? 'Chưa cấu hình'} />
            </>}
            {card.type === 'pharmacy_medicine_stock' && <>
                <strong>{text(item.name)} ({text(item.code)})</strong><Line label="Tồn khả dụng" value={`${text(item.availableStock)} ${text(item.unit)}`} />
            </>}
            {card.type === 'pharmacy_medicine_batches' && <>
                <strong>{text(item.batchNumber)}</strong><Line label="Thuốc" value={item.medicineName} />
                <Line label="Còn lại" value={`${text(item.quantityRemaining)} ${text(item.unit)}`} /><Line label="Hạn dùng" value={date(item.expiryDate)} />
            </>}
            {card.type === 'admin_active_doctors' && <strong>{text(item.fullName)}</strong>}
            {card.type === 'admin_pending_leave_requests' && <>
                <strong>{text(item.doctorName)}</strong><Line label="Từ" value={localDateTime(item.startDateTime)} />
                <Line label="Đến" value={localDateTime(item.endDateTime)} /><Line label="Trạng thái" value={item.status === 'Pending' ? 'Chờ duyệt' : item.status} />
            </>}
        </article>)}
    </div>;
}
