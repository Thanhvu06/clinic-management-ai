import { useState } from 'react';
import { Card, Tag } from 'antd';
import { Pill } from 'lucide-react';
import { medicineImageUrl } from '../../api/medicineApi';
import { formatVndCurrency } from '../../utils/formatters';
import type { PublicMedicineDto } from '../../types';
import styles from './MedicineCard.module.css';

interface MedicineCardProps {
    medicine: Pick<PublicMedicineDto, 'code' | 'name' | 'unit' | 'strength' | 'activeIngredient' | 'unitPrice' | 'imageUrl' | 'isPrescriptionRequired'> & {
        stockQuantity?: number;
        reorderLevel?: number;
        availability?: PublicMedicineDto['availability'];
    };
    publicView?: boolean;
}

export function MedicineCard({ medicine, publicView = false }: MedicineCardProps) {
    const [failedImage, setFailedImage] = useState<string | null>(null);
    const image = medicine.imageUrl ? medicineImageUrl(medicine.imageUrl) : null;
    const stock = medicine.stockQuantity ?? 0;
    const availability = publicView ? medicine.availability : stock === 0 ? 'out_of_stock'
        : medicine.reorderLevel != null && stock <= medicine.reorderLevel ? 'low' : 'in_stock';
    const stockLabel = availability === 'out_of_stock' ? 'Hết hàng' : availability === 'low' ? 'Sắp hết' : 'Còn hàng';
    const stockColor = availability === 'out_of_stock' ? 'error' : availability === 'low' ? 'warning' : 'success';

    return (
        <Card className={styles.card}>
            <div className={styles.imageArea}>
                {image && failedImage !== image ? (
                    <img src={image} alt={medicine.name} className={styles.image} onError={() => setFailedImage(image)} />
                ) : (
                    <div role="img" aria-label="Chưa có ảnh thuốc" className={styles.placeholder}><Pill size={48} /></div>
                )}
            </div>
            <div className={styles.details}>
                <span className={styles.code}>{medicine.code}</span>
                <h3 className={styles.name}>{medicine.name}</h3>
                {medicine.activeIngredient && <p className={styles.secondary}>{medicine.activeIngredient}</p>}
                <p className={styles.secondary}>{medicine.strength || 'Chưa có hàm lượng'} · {medicine.unit}</p>
                <p className={styles.price}>{medicine.unitPrice == null ? 'Chưa có giá' : formatVndCurrency(medicine.unitPrice)}</p>
                <div className={styles.tags}>
                    {!publicView && <span>Tồn kho: {stock} {medicine.unit}</span>}
                    <Tag color={stockColor}>{stockLabel}</Tag>
                    {medicine.isPrescriptionRequired && <Tag>Kê đơn</Tag>}
                </div>
                {publicView && medicine.isPrescriptionRequired && (
                    <p className={styles.note}>Thuốc kê đơn — chỉ bán theo đơn của bác sĩ.</p>
                )}
            </div>
        </Card>
    );
}
