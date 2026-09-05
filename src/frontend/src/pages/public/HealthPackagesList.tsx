import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Package, Check, Search, Users, CalendarCheck } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';
import styles from './PublicPages.module.css';

interface HealthPackage {
    id: number;
    code: string;
    name: string;
    targetAudience: string;
    description: string;
    price: number;
    includedServicesJson: string;
    isActive: boolean;
}

export const HealthPackagesList: React.FC = () => {
    const [packages, setPackages] = useState<HealthPackage[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchPackages = async () => {
            try {
                setLoading(true);
                const res = await axiosClient.get<any, any>('/health-packages');
                if (res.success) {
                    setPackages(res.data || []);
                } else {
                    setError('Không thể tải danh sách gói khám.');
                }
            } catch (err: any) {
                setError(err.response?.data?.message || 'Có lỗi xảy ra khi tải gói khám.');
            } finally {
                setLoading(false);
            }
        };

        fetchPackages();
    }, []);

    const filteredPackages = packages.filter(p =>
        p.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
        p.targetAudience.toLowerCase().includes(searchTerm.toLowerCase()) ||
        p.code.toLowerCase().includes(searchTerm.toLowerCase()) ||
        (p.description && p.description.toLowerCase().includes(searchTerm.toLowerCase()))
    );

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Gói khám sức khỏe</span>
                </nav>
                <h1 className={styles.pageTitle}>Gói Khám Sức Khỏe & Tầm Soát Toàn Diện</h1>
                <p className={styles.pageSubtitle}>
                    Chương trình khám sức khỏe định kỳ được thiết kế chuyên biệt theo từng độ tuổi và nhu cầu, kết hợp hệ thống cận lâm sàng đồng bộ giúp phát hiện sớm các nguy cơ tiềm ẩn.
                </p>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.filterGroup}>
                    <div className={styles.searchBox}>
                        <Search className={styles.searchBoxIcon} size={18} />
                        <input
                            type="text"
                            placeholder="Tìm kiếm theo tên gói khám hoặc đối tượng..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            aria-label="Tìm gói khám"
                        />
                    </div>
                </div>
                <div className={styles.resultCount}>
                    Hiển thị {filteredPackages.length} gói khám
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
            ) : filteredPackages.length > 0 ? (
                <div className={styles.grid3}>
                    {filteredPackages.map(pkg => {
                        const services = parseIncludedServices(pkg.includedServicesJson);
                        return (
                            <div key={pkg.id} className={styles.packageCard}>
                                <span className={styles.packageBadge}>{pkg.code}</span>
                                <h2 className={styles.packageName}>{pkg.name}</h2>
                                <div className={styles.packageAudience}>
                                    <Users size={15} color="var(--c-primary)" /> {pkg.targetAudience}
                                </div>
                                <p className={styles.packageDesc}>
                                    {pkg.description}
                                </p>

                                {services.length > 0 && (
                                    <ul className={styles.serviceList}>
                                        {services.slice(0, 4).map((service, idx) => (
                                            <li key={idx} className={styles.serviceItem}>
                                                <Check size={16} />
                                                <span>{service}</span>
                                            </li>
                                        ))}
                                        {services.length > 4 && (
                                            <li style={{ color: 'var(--c-primary)', fontWeight: 600, fontSize: '0.8rem', paddingLeft: '24px' }}>
                                                + {services.length - 4} danh mục & xét nghiệm khác
                                            </li>
                                        )}
                                    </ul>
                                )}

                                <div className={styles.packagePriceRow}>
                                    <div>
                                        <span className={styles.packagePriceLabel}>Chi phí trọn gói</span>
                                        <div className={styles.packagePrice}>{formatVndCurrency(pkg.price)}</div>
                                    </div>
                                    <div style={{ display: 'flex', gap: '8px' }}>
                                        <Link 
                                            to={`/health-packages/${pkg.id}`}
                                            className="btn btn-secondary btn-sm"
                                            style={{ textDecoration: 'none' }}
                                        >
                                            Chi tiết
                                        </Link>
                                        <Link 
                                            to={`/patient/health-packages/${pkg.id}/register`}
                                            className="btn btn-primary btn-sm"
                                            style={{ textDecoration: 'none' }}
                                        >
                                            <CalendarCheck size={14} /> Đăng ký
                                        </Link>
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            ) : (
                <div className={styles.emptyState}>
                    <Package size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>Không tìm thấy gói khám phù hợp</div>
                    <p>Vui lòng thử từ khóa tìm kiếm khác hoặc liên hệ hotline để được tư vấn.</p>
                </div>
            )}
        </div>
    );
};
