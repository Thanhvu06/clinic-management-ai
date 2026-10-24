import { Link } from 'react-router-dom';
import { Pill } from 'lucide-react';
import { medicineImageUrl } from '../../../api/medicineApi';
import type { PublicMedicineDto } from '../../../types';
import shared from './LandingSections.module.css';
import styles from './MedicineSection.module.css';

const priority = (medicine: PublicMedicineDto) => Number(Boolean(medicine.imageUrl)) * 2 + Number(medicine.unitPrice != null);

export function MedicineSection({ medicines }: { medicines: PublicMedicineDto[] }) {
    if (!medicines.length) return null;
    const featured = [...medicines].sort((a, b) => priority(b) - priority(a)).slice(0, 10);
    return <section className={shared.section} aria-labelledby="landing-medicines-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}>
                <h2 id="landing-medicines-title">Thuốc tại nhà thuốc phòng khám</h2><Link to="/medicines">Xem bảng giá thuốc</Link>
            </div>
            <div className={styles.grid}>{featured.map(medicine => <Link key={medicine.id} to={`/medicines/${medicine.id}`} className={styles.card}>
                <div className={styles.imageArea}>{medicine.imageUrl
                    ? <img src={medicineImageUrl(medicine.imageUrl)} alt={`Thuốc ${medicine.name}`} loading="lazy" width={240} height={240} />
                    : <Pill size={36} strokeWidth={1} aria-hidden="true" />}
                    {(medicine.isPrescriptionRequired || medicine.availability === 'out_of_stock') && <div className={styles.labels}>
                        {medicine.isPrescriptionRequired && <span className={styles.label}>Cần đơn bác sĩ</span>}
                        {medicine.availability === 'out_of_stock' && <span className={styles.unavailable}>Hết hàng</span>}
                    </div>}
                </div>
                <div className={styles.body}>
                    <h3 className={styles.name}>{medicine.name}</h3>
                    {(medicine.strength || medicine.dosageForm) && <p>{[medicine.strength, medicine.dosageForm].filter(Boolean).join(' · ')}</p>}
                    <div className={styles.price}>{medicine.unitPrice != null
                        ? <><strong>{new Intl.NumberFormat('vi-VN').format(medicine.unitPrice)} đ</strong><span> / {medicine.unit}</span></>
                        : <span>Liên hệ nhà thuốc</span>}</div>
                </div>
            </Link>)}</div>
        </div>
    </section>;
}
