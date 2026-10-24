import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Pill } from 'lucide-react';
import { medicineApi, medicineImageUrl } from '../../../api/medicineApi';
import type { PublicMedicineDto } from '../../../types';
import { InlineError } from '../../../components/common/InlineError';
import { getApiErrorMessage } from '../../../utils/apiError';
import card from '../landing/MedicineSection.module.css';
import styles from '../MedicineDetail.module.css';

export function RelatedMedicines({ medicine }: { medicine: PublicMedicineDto }) {
    const [items, setItems] = useState<PublicMedicineDto[]>([]);
    const [categoryId, setCategoryId] = useState<number>();
    const [loading, setLoading] = useState(Boolean(medicine.categoryName));
    const [error, setError] = useState('');
    const [attempt, setAttempt] = useState(0);
    useEffect(() => {
        let active = true;
        if (!medicine.categoryName) return;
        void (async () => {
            const categories = await medicineApi.getPublicCategories();
            if (!categories.success) throw new Error();
            const category = (Array.isArray(categories.data) ? categories.data : []).find(item => item.name === medicine.categoryName);
            if (!category || !active) return;
            setCategoryId(category.id);
            const response = await medicineApi.getPublicMedicines({ categoryId: category.id, pageSize: 6 });
            if (!response.success) throw new Error();
            if (active) setItems(response.data?.items?.filter(item => item.id !== medicine.id).slice(0, 5) ?? []);
        })().catch(err => { if (active) setError(getApiErrorMessage(err, 'Không thể tải thuốc cùng nhóm.')); })
            .finally(() => { if (active) setLoading(false); });
        return () => { active = false; };
    }, [medicine.id, medicine.categoryName, attempt]);
    if (!loading && !error && !items.length) return null;
    return <section className={styles.related} aria-labelledby="related-medicines-title">
        <div className={styles.sectionHeader}><h2 id="related-medicines-title">Thuốc cùng nhóm</h2>
            {categoryId && <Link to="/medicines">Xem bảng giá thuốc</Link>}</div>
        {loading ? <p role="status">Đang tải thuốc cùng nhóm...</p> : error ? <InlineError message={error} onRetry={() => {
            setError(''); setLoading(true); setAttempt(value => value + 1);
        }} /> : <div className={card.grid}>{items.map(item => <Link key={item.id} to={`/medicines/${item.id}`} className={card.card}>
            <div className={card.imageArea}>{item.imageUrl
                ? <img src={medicineImageUrl(item.imageUrl)} alt={`Thuốc ${item.name}`} loading="lazy" width={240} height={240} />
                : <Pill size={36} strokeWidth={1} aria-hidden="true" />}
                {item.isPrescriptionRequired && <div className={card.labels}><span className={card.label}>Cần đơn bác sĩ</span></div>}
            </div>
            <div className={card.body}><h3 className={card.name}>{item.name}</h3>
                {(item.strength || item.dosageForm) && <p>{[item.strength, item.dosageForm].filter(Boolean).join(' · ')}</p>}
                <div className={card.price}>{item.unitPrice == null ? <span>Liên hệ nhà thuốc</span>
                    : <><strong>{new Intl.NumberFormat('vi-VN').format(item.unitPrice)} đ</strong><span> / {item.unit}</span></>}</div>
            </div>
        </Link>)}</div>}
    </section>;
}
