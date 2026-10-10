import React, { useCallback, useEffect, useState } from 'react';
import { flushSync } from 'react-dom';
import { Alert, Button, Card, Descriptions, Input, Tag } from 'antd';
import { Lightbulb, Printer, Search } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Breadcrumb } from '../../components/Breadcrumb';
import { PageHeader } from '../../components/common/PageHeader';
import { DataTable } from '../../components/common/DataTable';
import { EmptyState } from '../../components/common/EmptyState';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';
import { formatDateOnly, formatVndCurrency } from '../../utils/formatters';
import styles from './PatientPrescriptions.module.css';

interface PrescriptionItem {
    medicineId: number; name: string; unit: string; quantity: number;
    unitPrice?: number | null; lineTotal?: number | null;
    dosage: string; frequency: string; durationDays?: number; instructions?: string;
}
interface Prescription {
    id: number; code: string; appointmentId: number; appointmentCode: string; appointmentDate: string;
    doctorName: string; specialtyName: string; diagnosis: string; status: string; notes?: string;
    createdAt: string; dispensedAt?: string; totalAmount?: number | null; priceIsReference?: boolean; items: PrescriptionItem[];
}
export const PatientPrescriptions: React.FC = () => {
    const [prescriptions, setPrescriptions] = useState<Prescription[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [searchTerm, setSearchTerm] = useState('');
    const [printingId, setPrintingId] = useState<number | null>(null);
    const fetchPrescriptions = useCallback(async () => {
        setLoading(true); setError('');
        try {
            const res = await axiosClient.get<any, ApiResponse<Prescription[]>>('/patients/me/prescriptions');
            if (!res.success) throw new Error(res.message || 'Không thể tải danh sách đơn thuốc.');
            setPrescriptions(res.data || []);
        } catch (err: any) { setError(err?.message || 'Không thể tải danh sách đơn thuốc.'); }
        finally { setLoading(false); }
    }, []);
    useEffect(() => { void fetchPrescriptions(); }, [fetchPrescriptions]);
    const handlePrint = (id: number) => {
        flushSync(() => setPrintingId(id));
        document.body.classList.add('cc-prescription-printing');
        try { window.print(); }
        finally {
            document.body.classList.remove('cc-prescription-printing');
            flushSync(() => setPrintingId(null));
        }
    };
    const term = searchTerm.trim().toLowerCase();
    const filtered = prescriptions.filter(p => [p.code, p.doctorName, p.specialtyName, p.diagnosis, ...p.items.map(i => i.name)].some(value => value.toLowerCase().includes(term)));
    const price = (value?: number | null) => value == null ? '—' : formatVndCurrency(value);
    return <div className={styles.page}>
        <Breadcrumb items={[{ label: 'Trang chủ', path: '/patient' }, { label: 'Đơn thuốc của tôi' }]} />
        <PageHeader title="Đơn thuốc điện tử" subtitle="Xem lịch sử đơn thuốc được bác sĩ chỉ định và theo dõi tình trạng cấp phát" />
        <Input allowClear prefix={<Search size={18} />} placeholder="Tìm theo mã đơn, bác sĩ, tên thuốc..." value={searchTerm} onChange={e => setSearchTerm(e.target.value)} />
        {loading ? <LoadingState message="Đang tải danh sách đơn thuốc từ hệ thống..." /> : error ? <InlineError message={error} onRetry={fetchPrescriptions} /> : filtered.length === 0 ? <EmptyState title="Chưa có đơn thuốc nào" description={term ? 'Không tìm thấy đơn thuốc khớp với từ khóa tìm kiếm.' : 'Các đơn thuốc do bác sĩ kê sau khi hoàn tất ca khám sẽ hiển thị tại đây.'} /> : filtered.map(p =>
            <Card key={p.id} data-prescription-id={p.id} className={printingId === p.id ? styles.printingPrescription : undefined} title={<div className={styles.heading}><span>Mã đơn: {p.code}</span><Tag color={p.status === 'Dispensed' ? 'success' : 'warning'}>{p.status === 'Dispensed' ? 'Đã cấp thuốc' : 'Chờ quầy dược cấp'}</Tag></div>} extra={<Button className={styles.printButton} title="In đơn thuốc" icon={<Printer size={18} />} onClick={() => handlePrint(p.id)}>In đơn thuốc</Button>}>
                <div className={styles.content}>
                    <div className={styles.appointment}><span>Ngày kê: {formatDateOnly(p.appointmentDate)}</span>{p.appointmentCode && <span>Lịch hẹn: <strong>{p.appointmentCode}</strong></span>}</div>
                    <Descriptions column={{ xs: 1, md: 2 }} items={[{ key: 'doctor', label: 'Bác sĩ chỉ định', children: `${p.doctorName} (${p.specialtyName})` }, { key: 'diagnosis', label: 'Chẩn đoán / Tóm tắt ca khám', children: p.diagnosis }]} />
                    <h4>Danh sách thuốc chỉ định ({p.items.length})</h4>
                    <DataTable data={p.items} keyExtractor={item => `${item.medicineId}-${p.items.indexOf(item)}`} columns={[
                        { header: 'STT', width: 40, accessor: item => p.items.indexOf(item) + 1 },
                        { header: 'Tên thuốc', accessor: item => <div><strong>{item.name}</strong>{item.instructions && <div className={styles.instructions}><Lightbulb size={14} />{item.instructions}</div>}</div> },
                        { header: 'Liều dùng', accessor: 'dosage' }, { header: 'Tần suất', accessor: 'frequency' },
                        { header: 'Số ngày', accessor: item => item.durationDays ? `${item.durationDays} ngày` : '-' },
                        { header: 'Số lượng', align: 'right', accessor: item => `${item.quantity} ${item.unit}` },
                        { header: 'Đơn giá', align: 'right', accessor: item => price(item.unitPrice) },
                        { header: 'Thành tiền', align: 'right', accessor: item => price(item.lineTotal) }
                    ]} />
                    <p className={styles.total}><strong>Tổng tiền thuốc: {price(p.totalAmount)}</strong></p>
                    {p.priceIsReference && <p className={styles.reference}>Giá tham khảo theo bảng giá hiện tại, có thể khác khi thanh toán.</p>}
                    {p.notes && <Alert type="warning" showIcon title="Lời dặn của bác sĩ điều trị" description={p.notes} />}
                </div>
            </Card>)}
    </div>;
};
