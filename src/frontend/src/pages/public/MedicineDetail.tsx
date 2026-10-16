import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { Alert, Button, Card, Collapse, Descriptions, Tag } from 'antd';
import { Pill } from 'lucide-react';
import { medicineApi, medicineImageUrl } from '../../api/medicineApi';
import type { PublicMedicineDto } from '../../types';
import { EmptyState, InlineError, LoadingState } from '../../components/common';
import { formatVndCurrency } from '../../utils/formatters';
import styles from './MedicineDetail.module.css';

interface PrescribedDose {
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
    quantity: number;
}
type DetailState = { id?: string } & (
    { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'missing' } | { kind: 'ready'; medicine: PublicMedicineDto }
);
const reference = 'Thông tin mang tính tham khảo. Đọc kỹ hướng dẫn sử dụng và hỏi ý kiến bác sĩ/dược sĩ trước khi dùng.';

export function MedicineDetail() {
    const { id } = useParams();
    const navigate = useNavigate();
    const location = useLocation();
    const [state, setState] = useState<DetailState>({ id, kind: 'loading' });
    const [attempt, setAttempt] = useState(0);
    const [failedImage, setFailedImage] = useState<string | null>(null);
    const validId = !!id && /^\d+$/.test(id) && Number.isSafeInteger(Number(id)) && Number(id) > 0;
    useEffect(() => {
        let current = true;
        if (!validId) return;
        medicineApi.getPublicMedicine(Number(id)).then(response => {
            if (!response.success) throw new Error(response.message || 'Không thể tải thông tin thuốc.');
            if (current) setState(response.data ? { id, kind: 'ready', medicine: response.data } : { id, kind: 'missing' });
        }).catch((error: { status?: number; message?: string }) => {
            if (current) setState(error.status === 404 ? { id, kind: 'missing' } : { id, kind: 'error', message: error.message || 'Không thể tải thông tin thuốc.' });
        });
        return () => { current = false; };
    }, [id, attempt, validId]);

    const visible = !validId ? { kind: 'missing' as const } : state.id === id ? state : { kind: 'loading' as const };
    const prescribed = (location.state as { prescribed?: PrescribedDose } | null)?.prescribed;
    const medicine = visible.kind === 'ready' ? visible.medicine : null;
    const image = medicine?.imageUrl ? medicineImageUrl(medicine.imageUrl) : null;
    const content = (value?: string | null) => <p className={styles.text}>{value?.trim() || 'Chưa cập nhật'}</p>;
    const goBack = () => {
        // React Router tracks entries created inside the app; a direct visit has index zero.
        if ((window.history.state?.idx ?? 0) > 0 || location.key !== 'default') navigate(-1);
        else navigate('/medicines');
    };

    return <main className={styles.page}>
        <Button onClick={goBack}>Quay lại</Button>
        {visible.kind === 'loading' ? <LoadingState message="Đang tải thông tin thuốc..." />
            : visible.kind === 'error' ? <InlineError message={visible.message} onRetry={() => { setState({ id, kind: 'loading' }); setAttempt(value => value + 1); }} />
                : visible.kind === 'missing' ? <EmptyState title="Không tìm thấy thuốc" description="Thuốc không tồn tại hoặc đã ngừng kinh doanh." />
                    : medicine && <>
                        <Card>
                            <div className={styles.summary}>
                                <div className={styles.imageArea}>
                                    {image && failedImage !== image ? <img src={image} alt={medicine.name} className={styles.image} onError={() => setFailedImage(image)} />
                                        : <div role="img" aria-label="Chưa có ảnh thuốc" className={styles.placeholder}><Pill size={64} /></div>}
                                </div>
                                <div className={styles.overview}>
                                    <h1>{medicine.name}</h1>
                                    <div className={styles.tags}>
                                        {medicine.isPrescriptionRequired && <Tag>Kê đơn</Tag>}
                                        <Tag color={medicine.availability === 'out_of_stock' ? 'error' : medicine.availability === 'low' ? 'warning' : 'success'}>
                                            {medicine.availability === 'out_of_stock' ? 'Hết hàng' : medicine.availability === 'low' ? 'Sắp hết' : 'Còn hàng'}
                                        </Tag>
                                    </div>
                                    <p className={styles.price}>{medicine.unitPrice == null ? 'Chưa có giá' : formatVndCurrency(medicine.unitPrice)}</p>
                                    <Descriptions column={1} items={[
                                        { key: 'code', label: 'Mã thuốc', children: medicine.code },
                                        { key: 'ingredient', label: 'Hoạt chất', children: medicine.activeIngredient || 'Chưa cập nhật' },
                                        { key: 'strength', label: 'Hàm lượng', children: medicine.strength || 'Chưa cập nhật' },
                                        { key: 'form', label: 'Dạng bào chế', children: medicine.dosageForm || 'Chưa cập nhật' },
                                        { key: 'unit', label: 'Đơn vị', children: medicine.unit },
                                        { key: 'category', label: 'Nhóm', children: medicine.categoryName || 'Chưa cập nhật' },
                                    ]} />
                                </div>
                            </div>
                        </Card>
                        {medicine.isPrescriptionRequired && <Alert type="warning" showIcon title="Thuốc kê đơn — chỉ dùng khi có đơn của bác sĩ." />}
                        {prescribed && <Card className={styles.prescribed} title="Liều bác sĩ kê cho bạn">
                            <p><strong>Làm theo liều trong đơn, ưu tiên hơn hướng dẫn chung.</strong></p>
                            <Descriptions column={1} items={[
                                { key: 'dosage', label: 'Liều dùng', children: prescribed.dosage || 'Chưa cập nhật' },
                                { key: 'frequency', label: 'Tần suất', children: prescribed.frequency || 'Chưa cập nhật' },
                                { key: 'duration', label: 'Số ngày', children: prescribed.durationDays == null ? 'Chưa cập nhật' : `${prescribed.durationDays} ngày` },
                                { key: 'quantity', label: 'Số lượng', children: `${prescribed.quantity} ${medicine.unit}` },
                                { key: 'instructions', label: 'Lời dặn', children: prescribed.instructions || 'Chưa cập nhật' },
                            ]} />
                        </Card>}
                        <Collapse defaultActiveKey={['indications', 'dosage']} items={[
                            { key: 'indications', label: 'Chỉ định', children: content(medicine.indications) },
                            { key: 'dosage', label: 'Liều dùng và cách dùng', children: content(medicine.dosageAndAdministration) },
                            { key: 'contraindications', label: 'Chống chỉ định', children: content(medicine.contraindications) },
                            { key: 'sideEffects', label: 'Tác dụng không mong muốn', children: content(medicine.sideEffects) },
                            { key: 'precautions', label: 'Thận trọng', children: content(medicine.precautions) },
                            { key: 'interactions', label: 'Tương tác thuốc', children: medicine.interactions?.length ? <div className={styles.interactions}>
                                {medicine.interactions.map((interaction, index) => <div key={`${interaction.otherIngredient}-${index}`} className={styles.interaction}>
                                    <Tag color={interaction.severity === 'Major' || interaction.severity === 'Contraindicated' ? 'error' : interaction.severity === 'Moderate' ? 'warning' : 'default'}>{interaction.severity}</Tag>
                                    <strong>{interaction.otherIngredient}</strong>
                                    {content(interaction.description)}{content(interaction.recommendation)}
                                </div>)}
                            </div> : <p>Chưa ghi nhận tương tác trong dữ liệu hệ thống</p> },
                            { key: 'overdose', label: 'Quá liều và xử trí', children: content(medicine.overdoseManagement) },
                            { key: 'storage', label: 'Bảo quản', children: content(medicine.storageInstructions) },
                            { key: 'description', label: 'Mô tả', children: content(medicine.description) },
                        ]} />
                    </>}
        <Alert type="info" showIcon title={reference} />
    </main>;
}
