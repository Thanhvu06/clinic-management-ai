import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Search } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import styles from './PublicPages.module.css';

interface Specialty {
    id: number;
    specialtyCode: string;
    specialtyName: string;
    description: string;
    aiEnabled: boolean;
}

export const SpecialtiesList: React.FC = () => {
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchSpecialties = async () => {
            try {
                setLoading(true);
                const res = await axiosClient.get<any, any>('/specialties');
                if (res.success) {
                    setSpecialties(res.data || []);
                } else {
                    setError('Không thể tải danh sách chuyên khoa.');
                }
            } catch (err: any) {
                setError(getApiErrorMessage(err, 'Có lỗi xảy ra khi tải dữ liệu.'));
            } finally {
                setLoading(false);
            }
        };

        fetchSpecialties();
    }, []);

    const filteredSpecialties = specialties.filter(s => 
        s.specialtyName.toLowerCase().includes(searchTerm.toLowerCase()) ||
        (s.description && s.description.toLowerCase().includes(searchTerm.toLowerCase()))
    );

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Chuyên khoa</span>
                </nav>
                <h1 className={styles.pageTitle}>Chuyên khoa</h1>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.filterGroup}>
                    <div className={styles.searchBox}>
                        <Search className={styles.searchBoxIcon} size={18} />
                        <input
                            type="text"
                            placeholder="Tìm chuyên khoa theo tên hoặc mô tả"
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            aria-label="Tìm chuyên khoa"
                        />
                    </div>
                </div>
                <div className={styles.resultCount}>
                    Hiển thị {filteredSpecialties.length} chuyên khoa
                </div>
            </div>

            {error && (
                <div className={styles.surface6}>
                    {error}
                </div>
            )}

            {loading ? (
                <div className={styles.grid3}>
                    {[1, 2, 3, 4, 5, 6].map(i => (
                        <div key={i} className={styles.skeletonCard} />
                    ))}
                </div>
            ) : filteredSpecialties.length > 0 ? (
                <div className={styles.grid3}>
                    {filteredSpecialties.map(spec => (
                        <div key={spec.id} className={styles.card}>
                            <h2 className={styles.cardTitle}>{spec.specialtyName}</h2>
                            {spec.description && <p className={styles.cardDesc}>{spec.description}</p>}
                            {spec.aiEnabled && <span className={styles.flexLayout25}>Trợ lý AI có thể gợi ý khoa này</span>}
                            <div className={styles.cardFooter}>
                                <Link to={`/specialties/${spec.id}`} className={styles.cardActionLink}>Xem chi tiết</Link>
                                <Link to={`/patient/book?specialtyId=${spec.id}`} className={styles.cardActionLink}>Đặt lịch</Link>
                            </div>
                        </div>
                    ))}
                </div>
            ) : (
                <div className={styles.emptyState}>
                    <div className={styles.emptyStateTitle}>Không tìm thấy chuyên khoa phù hợp</div>
                    <p>Thử tìm với từ khóa khác.</p>
                </div>
            )}
        </div>
    );
};
