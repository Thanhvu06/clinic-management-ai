import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import type { HealthPackageDto } from '../../types';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';
import { InlineError } from '../../components/common/InlineError';
import { LoadingState } from '../../components/common/LoadingState';
import { LinkButton } from '../../components/common/LinkButton';
import shared from './PublicPages.module.css';
import styles from './HealthPackageDetail.module.css';

type HealthPackage = HealthPackageDto;

export const HealthPackageDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const [pkg, setPkg] = useState<HealthPackage | null>(null);
    const [loading, setLoading] = useState(true);
    const [notFound, setNotFound] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const [reloadKey, setReloadKey] = useState(0);

    useEffect(() => {
        const fetchPackage = async () => {
            if (!id) return;
            try {
                const res = await axiosClient.get<any, any>(`/health-packages/${id}`);
                setError(null);
                setNotFound(!(res.success && res.data));
                if (res.success && res.data) setPkg(res.data);
            } catch (err: any) {
                if (err?.status === 404) setNotFound(true);
                else setError(getApiErrorMessage(err, 'Không thể tải thông tin gói khám.'));
            } finally {
                setLoading(false);
            }
        };
        void fetchPackage();
    }, [id, reloadKey]);

    const retry = () => {
        setLoading(true);
        setError(null);
        setReloadKey(key => key + 1);
    };

    if (loading) {
        return <div className={shared.pageContainer}><LoadingState message="Đang tải gói khám..." /></div>;
    }

    if (error) {
        return <div className={shared.pageContainer}><InlineError message={error} onRetry={retry} /></div>;
    }

    if (notFound || !pkg) {
        return (
            <div className={shared.pageContainer}>
                <div className={shared.emptyState}>
                    <div className={shared.emptyStateTitle}>Không tìm thấy gói khám</div>
                    <p className={shared.paragraph1}>Gói khám có thể đã ngừng áp dụng.</p>
                    <Link to="/health-packages" className={shared.cardActionLink}>Quay lại danh sách gói khám</Link>
                </div>
            </div>
        );
    }

    const services = (pkg.includedServices && pkg.includedServices.length > 0)
        ? pkg.includedServices
        : parseIncludedServices(pkg.includedServicesJson);

    return (
        <div className={shared.pageContainer}>
            <nav className={shared.breadcrumb} aria-label="Đường dẫn trang">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/health-packages">Gói khám</Link>
                <span>/</span>
                <span aria-current="page">{pkg.name}</span>
            </nav>

            <div className={styles.layout}>
                <div className={styles.main}>
                    <header>
                        <h1 className={styles.title}>{pkg.name}</h1>
                        <div className={styles.meta}>
                            <span>Mã gói {pkg.code}</span>
                            {pkg.targetAudience && <span>Dành cho: {pkg.targetAudience}</span>}
                        </div>
                        {pkg.description && <p className={styles.description}>{pkg.description}</p>}
                    </header>

                    {services.length > 0 && (
                        <section className={styles.panel} aria-labelledby="package-services-title">
                            <h2 id="package-services-title" className={styles.sectionTitle}>Dịch vụ trong gói ({services.length})</h2>
                            <ol className={styles.services}>
                                {services.map((service: string, idx: number) => <li key={idx}>{service}</li>)}
                            </ol>
                        </section>
                    )}

                    <section className={styles.panel} aria-labelledby="package-prepare-title">
                        <h2 id="package-prepare-title" className={styles.sectionTitle}>Chuẩn bị trước khi khám</h2>
                        <ul className={styles.notes}>
                            <li><strong>Nhịn ăn:</strong> nhịn ăn ít nhất 6–8 tiếng trước khi lấy máu xét nghiệm, chỉ uống nước lọc.</li>
                            <li><strong>Thuốc đang dùng:</strong> mang theo đơn thuốc hoặc báo bác sĩ, lễ tân các thuốc bạn đang dùng.</li>
                            <li><strong>Trang phục:</strong> mặc quần áo rộng để thuận tiện khám, đo điện tim và chụp X-quang.</li>
                            <li><strong>Giấy tờ:</strong> mang căn cước công dân hoặc giấy tờ tùy thân có ảnh khi làm thủ tục.</li>
                        </ul>
                    </section>
                </div>

                <aside className={styles.aside} aria-labelledby="package-price-title">
                    <span id="package-price-title" className={styles.priceLabel}>Giá gói</span>
                    <div className={styles.price}>{formatVndCurrency(pkg.price)}</div>
                    <p className={styles.muted}>Sau khi đăng ký, lễ tân sẽ gọi xác nhận ngày khám.</p>
                    <LinkButton type="primary" size="large" block to={`/patient/health-packages/${pkg.id}/register`}>
                        Đăng ký gói khám
                    </LinkButton>
                    <Link to="/health-packages" className={styles.secondaryLink}>Xem các gói khám khác</Link>
                </aside>
            </div>
        </div>
    );
};
