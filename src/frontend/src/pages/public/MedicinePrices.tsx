import { useEffect, useState } from 'react';
import { Col, Input, Pagination, Row, Select } from 'antd';
import { medicineApi } from '../../api/medicineApi';
import type { MedicinePage, PublicMedicineCategoryDto, PublicMedicineDto } from '../../types';
import { MedicineCard } from '../../components/medicines/MedicineCard';
import { EmptyState, InlineError, LoadingState } from '../../components/common';
import { spacing } from '../../theme/tokens';
import styles from './MedicinePrices.module.css';

export function MedicinePrices() {
    const [search, setSearch] = useState('');
    const [debouncedSearch, setDebouncedSearch] = useState('');
    const [categoryId, setCategoryId] = useState<number>();
    const [type, setType] = useState<'all' | 'rx' | 'otc'>('all');
    const [page, setPage] = useState(1);
    const [retry, setRetry] = useState(0);
    const [categories, setCategories] = useState<PublicMedicineCategoryDto[]>([]);
    const [categoriesError, setCategoriesError] = useState('');
    const [result, setResult] = useState<{ key: string; data?: MedicinePage<PublicMedicineDto>; error?: string }>({ key: '' });
    const requestKey = JSON.stringify({ search: debouncedSearch, categoryId, type, page, retry });
    const loading = result.key !== requestKey;

    useEffect(() => {
        const timer = setTimeout(() => { setDebouncedSearch(search.trim()); setPage(1); }, 300);
        return () => clearTimeout(timer);
    }, [search]);

    useEffect(() => {
        let active = true;
        medicineApi.getPublicCategories().then(response => {
            if (!active) return;
            if (response.success && response.data) {
                setCategories(response.data);
                setCategoriesError('');
            } else setCategoriesError('Chưa thể tải danh mục thuốc. Bạn vui lòng thử lại.');
        }).catch(() => { if (active) setCategoriesError('Chưa thể tải danh mục thuốc. Bạn vui lòng thử lại.'); });
        return () => { active = false; };
    }, [retry]);

    useEffect(() => {
        let active = true;
        medicineApi.getPublicMedicines({
            search: debouncedSearch || undefined, categoryId,
            type: type === 'all' ? undefined : type, page, pageSize: 12,
        }).then(response => {
            if (!active) return;
            setResult(response.success && response.data ? { key: requestKey, data: response.data }
                : { key: requestKey, error: 'Chưa thể tải bảng giá thuốc. Bạn vui lòng thử lại.' });
        }).catch(() => {
            if (active) setResult({ key: requestKey, error: 'Chưa thể tải bảng giá thuốc. Bạn vui lòng thử lại.' });
        });
        return () => { active = false; };
    }, [debouncedSearch, categoryId, type, page, retry, requestKey]);

    const retryLoad = () => setRetry(value => value + 1);

    return (
        <section className={styles.page} aria-labelledby="medicine-prices-title">
            <h1 id="medicine-prices-title">Tra cứu giá thuốc</h1>
            <p className={styles.intro}>Tìm thuốc và tham khảo giá trước khi đến quầy dược.</p>
            <div className={styles.filters}>
                <Input aria-label="Tìm theo tên hoặc hoạt chất" placeholder="Tìm theo tên hoặc hoạt chất"
                    value={search} onChange={event => setSearch(event.target.value)} allowClear className={styles.search} />
                <Select aria-label="Danh mục thuốc" placeholder="Tất cả danh mục" allowClear
                    className={styles.select} value={categoryId}
                    options={categories.map(category => ({ value: category.id, label: category.name }))}
                    onChange={value => { setCategoryId(value); setPage(1); }} />
                <Select aria-label="Loại thuốc" className={styles.select} value={type}
                    options={[{ value: 'all', label: 'Tất cả' }, { value: 'rx', label: 'Thuốc kê đơn' }, { value: 'otc', label: 'Không kê đơn' }]}
                    onChange={value => { setType(value); setPage(1); }} />
            </div>
            {categoriesError && <InlineError message={categoriesError} onRetry={retryLoad} />}
            {loading ? <LoadingState message="Đang tải bảng giá thuốc..." /> : result.error ? (
                <InlineError message={result.error} onRetry={retryLoad} />
            ) : !result.data?.items.length ? (
                <EmptyState title="Chưa tìm thấy thuốc phù hợp" description="Bạn thử đổi từ khóa hoặc bộ lọc nhé." />
            ) : (
                <>
                    <Row gutter={[spacing.md, spacing.md]}>
                        {result.data.items.map(medicine => (
                            <Col key={medicine.id} xs={24} sm={12} lg={6}><MedicineCard medicine={medicine} publicView /></Col>
                        ))}
                    </Row>
                    <div className={styles.pagination}>
                        <Pagination current={page} pageSize={12} total={result.data.totalItems}
                            showSizeChanger={false} onChange={setPage} />
                    </div>
                </>
            )}
        </section>
    );
}
