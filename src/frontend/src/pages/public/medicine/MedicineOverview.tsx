import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Tag } from 'antd';
import { Pill } from 'lucide-react';
import type { PublicMedicineDto } from '../../../types';
import { medicineImageUrl } from '../../../api/medicineApi';
import { PublicLocations } from '../specialty/PublicLocations';
import styles from '../MedicineDetail.module.css';

export function MedicineOverview({ medicine }: { medicine: PublicMedicineDto }) {
    const [failedImage, setFailedImage] = useState<string | null>(null);
    const image = medicine.imageUrl ? medicineImageUrl(medicine.imageUrl) : null;
    const facts = [
        ['Mã thuốc', medicine.code], ['Hoạt chất', medicine.activeIngredient], ['Hàm lượng', medicine.strength],
        ['Dạng bào chế', medicine.dosageForm], ['Đơn vị', medicine.unit], ['Nhóm', medicine.categoryName],
        ...(medicine.storageInstructions?.trim() ? [['Bảo quản', medicine.storageInstructions]] : []),
    ];
    return <div className={styles.summary}>
        <div className={styles.imageArea}>
            {image && failedImage !== image ? <img src={image} alt={medicine.name} className={styles.image} onError={() => setFailedImage(image)} />
                : <div role="img" aria-label="Chưa có ảnh thuốc" className={styles.placeholder}><Pill size={80} strokeWidth={1} /></div>}
        </div>
        <div className={styles.overview}>
            <h1>{medicine.name}</h1>
            <div className={styles.tags}>
                <Tag color={medicine.availability === 'out_of_stock' ? 'error' : medicine.availability === 'low' ? 'warning' : 'success'}>
                    {medicine.availability === 'out_of_stock' ? 'Hết hàng' : medicine.availability === 'low' ? 'Sắp hết' : 'Còn hàng'}
                </Tag><Tag>{medicine.isPrescriptionRequired ? 'Thuốc kê đơn' : 'Không kê đơn'}</Tag>
            </div>
            <p className={styles.price}>{medicine.unitPrice == null ? 'Chưa có giá'
                : <><strong>{new Intl.NumberFormat('vi-VN').format(medicine.unitPrice)} đ</strong><span> / {medicine.unit}</span></>}</p>
            <div className={styles.notice}>
                {medicine.isPrescriptionRequired ? <>
                    <p>Thuốc này chỉ được bán theo đơn của bác sĩ. Vui lòng khám để được kê đơn.</p>
                    <Link to="/patient/book" className={styles.bookingLink}>Đặt lịch khám</Link>
                </> : <><p><strong>Có bán tại nhà thuốc phòng khám</strong></p><PublicLocations /></>}
            </div>
            <dl className={styles.facts}>{facts.map(([label, value]) => <div key={label}>
                <dt>{label}</dt><dd>{value?.trim() || 'Chưa cập nhật'}</dd>
            </div>)}</dl>
        </div>
    </div>;
}
