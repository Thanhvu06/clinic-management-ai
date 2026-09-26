import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Stethoscope, Search, ChevronRight, Calendar, Sparkles } from 'lucide-react';
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
                setError(err.response?.data?.message || 'Có lỗi xảy ra khi tải dữ liệu.');
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
                <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Chuyên khoa</span>
                </nav>
                <h1 className={styles.pageTitle}>Danh mục Chuyên khoa Khám bệnh</h1>
                <p className={styles.pageSubtitle}>
                    ClinicCare cung cấp dịch vụ khám chữa bệnh đa dạng với các chuyên khoa mũi nhọn, trang thiết bị tiên tiến cùng đội ngũ bác sĩ chuyên sâu.
                </p>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.filterGroup}>
                    <div className={styles.searchBox}>
                        <Search className={styles.searchBoxIcon} size={18} />
                        <input
                            type="text"
                            placeholder="Tìm kiếm chuyên khoa theo tên hoặc triệu chứng..."
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
                <div style={{ padding: '16px 20px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', marginBottom: '24px' }}>
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
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                                <div className={styles.cardIcon}>
                                    <Stethoscope size={24} />
                                </div>
                                {spec.aiEnabled && (
                                    <span style={{ 
                                        display: 'inline-flex', alignItems: 'center', gap: '4px',
                                        fontSize: '0.75rem', fontWeight: 600, color: 'var(--c-primary)',
                                        background: 'rgba(14, 116, 144, 0.1)', padding: '4px 8px', borderRadius: '12px' 
                                    }}>
                                        <Sparkles size={12} /> Hỗ trợ AI
                                    </span>
                                )}
                            </div>
                            <h2 className={styles.cardTitle}>{spec.specialtyName}</h2>
                            <p className={styles.cardDesc}>
                                {spec.description || 'Chăm sóc sức khỏe toàn diện và điều trị chuyên sâu theo chuẩn y khoa.'}
                            </p>
                            <div className={styles.cardFooter}>
                                <Link to={`/specialties/${spec.id}`} className={styles.cardActionLink}>
                                    Xem chi tiết <ChevronRight size={16} />
                                </Link>
                                <Link 
                                    to={`/patient/book?specialtyId=${spec.id}`} 
                                    className={styles.cardActionLink}
                                    style={{ color: 'var(--c-secondary)' }}
                                >
                                    <Calendar size={15} /> Đặt lịch
                                </Link>
                            </div>
                        </div>
                    ))}
                </div>
            ) : (
                <div className={styles.emptyState}>
                    <Stethoscope size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>Không tìm thấy chuyên khoa phù hợp</div>
                    <p>Vui lòng thử tìm kiếm với từ khóa khác.</p>
                </div>
            )}
        </div>
    );
};
