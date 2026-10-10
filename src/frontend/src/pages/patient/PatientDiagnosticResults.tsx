import React, { useCallback, useEffect, useState } from 'react';
import { Card, Collapse, Tabs, Tag } from 'antd';
import { CheckCircle } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto } from '../../types';
import { PageHeader } from '../../components/common/PageHeader';
import { DataTable } from '../../components/common/DataTable';
import { EmptyState } from '../../components/common/EmptyState';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';
import styles from './PatientDiagnosticResults.module.css';

export const PatientDiagnosticResults: React.FC = () => {
    const [orders, setOrders] = useState<DiagnosticOrderDto[]>([]);
    const [vitals, setVitals] = useState<any[]>([]);
    const [ordersLoading, setOrdersLoading] = useState(true);
    const [vitalsLoading, setVitalsLoading] = useState(true);
    const [ordersError, setOrdersError] = useState('');
    const [vitalsError, setVitalsError] = useState('');
    const fetchOrders = useCallback(async () => {
        setOrdersLoading(true); setOrdersError('');
        try {
            const res = await diagnosticApi.getPatientOrders(1, 50);
            if (!res.success) throw new Error(res.message || 'Không thể tải kết quả cận lâm sàng của bạn.');
            setOrders(res.data?.items || []);
        } catch (err: any) { setOrdersError(err?.message || 'Không thể tải kết quả cận lâm sàng của bạn.'); }
        finally { setOrdersLoading(false); }
    }, []);
    const fetchVitals = useCallback(async () => {
        setVitalsLoading(true); setVitalsError('');
        try {
            const res = await diagnosticApi.getPatientVitals(30);
            if (!res.success) throw new Error(res.message || 'Không thể tải chỉ số sinh hiệu của bạn.');
            setVitals(res.data || []);
        } catch (err: any) { setVitalsError(err?.message || 'Không thể tải chỉ số sinh hiệu của bạn.'); }
        finally { setVitalsLoading(false); }
    }, []);
    useEffect(() => { void fetchOrders(); void fetchVitals(); }, [fetchOrders, fetchVitals]);
    const orderContent = ordersLoading ? <LoadingState message="Đang tải dữ liệu y tế..." /> : ordersError ? <InlineError message={ordersError} onRetry={fetchOrders} /> : orders.length === 0 ? <EmptyState title="Chưa có kết quả cận lâm sàng nào" description="Các kết quả xét nghiệm, siêu âm sau khi hoàn tất sẽ được cập nhật tại đây." /> :
        <Collapse defaultActiveKey={[String(orders[0].id)]} items={orders.map(order => {
            const reviewed = !!order.reviewedAtUtc;
            const complete = order.items.length > 0 && order.items.every(item => !!item.result);
            return { key: String(order.id), label: <div className={styles.orderHeading}><strong>{order.orderCode}</strong><span>Khám: #{order.appointmentCode} • Bác sĩ: {order.orderingDoctorName} ({order.specialtyName || '---'})</span></div>, extra: <div className={styles.orderStatus}><Tag color={reviewed ? 'success' : complete ? 'processing' : 'warning'}>{reviewed ? 'Đã có kết luận bác sĩ' : complete ? 'Đã có kết quả' : 'Đang chờ kết quả'}</Tag><span>{new Date(order.orderedAtUtc).toLocaleDateString('vi-VN')}</span></div>, children: <div className={styles.results}>
                <div className={styles.orderDetails}>
                    <div className={styles.resultLine}><strong>Chỉ định:</strong> {order.clinicalIndication}</div>
                    {order.note && <div className={styles.resultLine}><strong>Ghi chú:</strong> {order.note}</div>}
                    {reviewed && <div className={styles.review}><CheckCircle size={16} /> Bác sĩ {order.reviewedByDoctorName} đã xác nhận xem kết quả lúc {new Date(order.reviewedAtUtc!).toLocaleString('vi-VN')}</div>}
                </div>
                {order.items.map((item, index) => <Card size="small" key={item.id} title={`${index + 1}. ${item.serviceName} (${item.serviceCode})`} extra={<Tag>{item.category === 'Laboratory' ? 'Xét nghiệm' : item.category === 'Ultrasound' ? 'Siêu âm' : item.category === 'Imaging' ? 'Chẩn đoán hình ảnh' : item.category}</Tag>}>
                    {item.result ? <div className={styles.results}>
                        <div className={`${styles.resultLine} ${styles.resultText}`}><strong>Kết quả:</strong> {item.result.resultText}</div>
                        <div className={styles.resultSummary}>
                            {item.result.conclusion && <div className={styles.resultLine}><strong>Kết luận:</strong> {item.result.conclusion}</div>}
                            {item.result.referenceRange && <div className={styles.resultLine}><strong>Chỉ số bình thường:</strong> {item.result.referenceRange}</div>}
                            {item.result.unit && <div className={styles.resultLine}><strong>Đơn vị:</strong> {item.result.unit}</div>}
                        </div>
                        <div className={styles.resultAuthor}>Thực hiện bởi: {item.result.resultedByUserName} • {new Date(item.result.resultedAtUtc).toLocaleString('vi-VN')}</div>
                    </div> : <div className={styles.pending}>Đang chờ kỹ thuật viên trả kết quả...</div>}
                </Card>)}
            </div> };
        })} />;
    const vitalsContent = vitalsLoading ? <LoadingState message="Đang tải dữ liệu y tế..." /> : vitalsError ? <InlineError message={vitalsError} onRetry={fetchVitals} /> : vitals.length === 0 ? <EmptyState title="Chưa có lịch sử sinh hiệu nào" /> :
        <DataTable data={vitals} keyExtractor={v => `${v.recordedAtUtc}-${v.appointmentCode}`} columns={[
            { header: 'Ngày khám', accessor: v => <div>{new Date(v.recordedAtUtc).toLocaleDateString('vi-VN')}<div className={styles.resultAuthor}>#{v.appointmentCode}</div></div> },
            { header: 'Cân nặng', align: 'center', accessor: v => v.weight ? `${v.weight} kg` : '---' },
            { header: 'Chiều cao', align: 'center', accessor: v => v.height ? `${v.height} cm` : '---' },
            { header: 'BMI', align: 'center', accessor: v => v.bmi ?? '---' },
            { header: 'Huyết áp', align: 'center', accessor: v => v.bloodPressureSystolic && v.bloodPressureDiastolic ? `${v.bloodPressureSystolic}/${v.bloodPressureDiastolic}` : '---' },
            { header: 'Nhịp tim', align: 'center', accessor: v => v.heartRate ? `${v.heartRate} bpm` : '---' },
            { header: 'Thân nhiệt', align: 'center', accessor: v => v.temperature ? `${v.temperature}°C` : '---' },
            { header: 'SpO2', align: 'center', accessor: v => v.spO2 ? `${v.spO2}%` : '---' }
        ]} />;
    return <div className={styles.page}><PageHeader title="Kết Quả Cận Lâm Sàng & Sinh Hiệu" subtitle="Theo dõi lịch sử xét nghiệm, siêu âm và chỉ số sức khỏe của bạn tại ClinicCare" /><Tabs items={[{ key: 'orders', label: `Phiếu cận lâm sàng (${orders.length})`, children: orderContent }, { key: 'vitals', label: `Chỉ số sinh hiệu (${vitals.length})`, children: vitalsContent }]} /></div>;
};
