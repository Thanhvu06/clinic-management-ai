import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Search } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';
import styles from './PublicPages.module.css';
import { LinkButton } from '../../components/common/LinkButton';

interface HealthPackage {
    id: number;
    code: string;
    name: string;
    targetAudience: string;
    description: string;
    price: number;
    includedServices?: string[];
    includedServicesJson?: string;
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
                setError(getApiErrorMessage(err, 'Có lỗi xảy ra khi tải gói khám.'));
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
                <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Gói khám</span>
                </nav>
                <h1 className={styles.pageTitle}>Gói khám sức khỏe</h1>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.filterGroup}>
                    <div className={styles.searchBox}>
                        <Search className={styles.searchBoxIcon} size={18} />
                        <input
                            type="text"
                            placeholder="Tìm theo tên gói hoặc đối tượng"
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
            ) : filteredPackages.length > 0 ? (
                <div className={styles.grid3}>
                    {filteredPackages.map(pkg => {
                        const services = (pkg.includedServices && pkg.includedServices.length > 0)
                            ? pkg.includedServices
                            : parseIncludedServices(pkg.includedServicesJson);
                        return (
                            <div key={pkg.id} className={styles.packageCard}>
                                <span className={styles.packageBadge}>{pkg.code}</span>
                                <h2 className={styles.packageName}>{pkg.name}</h2>
                                {pkg.targetAudience && <div className={styles.packageAudience}>Dành cho: {pkg.targetAudience}</div>}
                                <p className={styles.packageDesc}>
                                    {pkg.description}
                                </p>

                                {services.length > 0 && (
                                    <ul className={styles.serviceList}>
                                        {services.slice(0, 4).map((service, idx) => (
                                            <li key={idx} className={styles.serviceItem}>{service}</li>
                                        ))}
                                        {services.length > 4 && (
                                            <li className={styles.text15}>
                                                và {services.length - 4} dịch vụ khác
                                            </li>
                                        )}
                                    </ul>
                                )}

                                <div className={styles.packagePriceRow}>
                                    <div>
                                        <span className={styles.packagePriceLabel}>Giá gói</span>
                                        <div className={styles.packagePrice}>{formatVndCurrency(pkg.price)}</div>
                                    </div>
                                    <div className={styles.flexLayout23}>
                                        <LinkButton to={`/health-packages/${pkg.id}`}>Chi tiết</LinkButton>
                                        <LinkButton type="primary" to={`/patient/health-packages/${pkg.id}/register`}>Đăng ký</LinkButton>
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            ) : (
                <div className={styles.emptyState}>
                    <div className={styles.emptyStateTitle}>Không tìm thấy gói khám phù hợp</div>
                    <p>Thử tìm với từ khóa khác.</p>
                </div>
            )}
        </div>
    );
};
