import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { Button } from 'antd';
import { medicineApi } from '../../api/medicineApi';
import type { PublicMedicineDto } from '../../types';
import { EmptyState, InlineError, LoadingState } from '../../components/common';
import { getApiErrorMessage } from '../../utils/apiError';
import { MedicineOverview } from './medicine/MedicineOverview';
import { MedicineArticle } from './medicine/MedicineArticle';
import { RelatedMedicines } from './medicine/RelatedMedicines';
import { PrescribedDoseCard } from './medicine/PrescribedDoseCard';
import type { PrescribedDose } from './medicine/PrescribedDoseCard';
import styles from './MedicineDetail.module.css';

type DetailState = { id?: string } & (
    { kind: 'loading' } | { kind: 'error'; message: string } | { kind: 'missing' } | { kind: 'ready'; medicine: PublicMedicineDto }
);
export function MedicineDetail() {
    const { id } = useParams();
    const navigate = useNavigate();
    const location = useLocation();
    const [state, setState] = useState<DetailState>({ id, kind: 'loading' });
    const [attempt, setAttempt] = useState(0);
    const validId = !!id && /^\d+$/.test(id) && Number.isSafeInteger(Number(id)) && Number(id) > 0;
    useEffect(() => {
        let current = true;
        if (!validId) return;
        medicineApi.getPublicMedicine(Number(id)).then(response => {
            if (!response.success) throw new Error(response.message || 'Không thể tải thông tin thuốc.');
            if (current) setState(response.data ? { id, kind: 'ready', medicine: response.data } : { id, kind: 'missing' });
        }).catch((error: { status?: number }) => {
            if (current) setState(error.status === 404 ? { id, kind: 'missing' } : { id, kind: 'error', message: getApiErrorMessage(error, 'Không thể tải thông tin thuốc.') });
        });
        return () => { current = false; };
    }, [id, attempt, validId]);
    const visible = !validId ? { kind: 'missing' as const } : state.id === id ? state : { kind: 'loading' as const };
    const prescribed = (location.state as { prescribed?: PrescribedDose } | null)?.prescribed;
    const medicine = visible.kind === 'ready' ? visible.medicine : null;
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
                        <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                            <Link to="/">Trang chủ</Link><span>/</span><Link to="/medicines">Bảng giá thuốc</Link><span>/</span>
                            {medicine.categoryName && <><span>{medicine.categoryName}</span><span>/</span></>}
                            <span aria-current="page">{medicine.name}</span>
                        </nav>
                        <MedicineOverview key={medicine.id} medicine={medicine} />
                        {prescribed && <PrescribedDoseCard prescribed={prescribed} unit={medicine.unit} />}
                        <MedicineArticle key={`article-${medicine.id}`} medicine={medicine} />
                        <RelatedMedicines key={`related-${medicine.id}`} medicine={medicine} />
                    </>}
    </main>;
}
