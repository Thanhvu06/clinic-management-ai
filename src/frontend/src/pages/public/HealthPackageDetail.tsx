import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Package, CheckCircle2, CalendarCheck, Users, ShieldCheck, AlertCircle } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { HealthPackageDto } from '../../types';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';
import styles from './PublicPages.module.css';

type HealthPackage = HealthPackageDto;

export const HealthPackageDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const [pkg, setPkg] = useState<HealthPackage | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchPackage = async () => {
            if (!id) return;
            try {
                setLoading(true);
                const res = await axiosClient.get<any, any>(`/health-packages/${id}`);
                if (res.success && res.data) {
                    setPkg(res.data);
                } else {
                    setError('Không tìm thấy thông tin gói khám.');
                }
            } catch (err: any) {
                setError(getApiErrorMessage(err, 'Không thể tải thông tin gói khám.'));
            } finally {
                setLoading(false);
            }
        };

        fetchPackage();
    }, [id]);

    if (loading) {
        return (
            <div className={styles.pageContainer}>
                <div className={[styles.skeletonCard, styles.spacing1].join(' ')} />
            </div>
        );
    }

    if (error || !pkg) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.emptyState}>
                    <Package size={48} className={styles.icon1} />
                    <div className={styles.emptyStateTitle}>{error || 'Gói khám không tồn tại'}</div>
                    <p className={styles.paragraph1}>Gói khám bạn đang tìm kiếm có thể đã ngừng cung cấp hoặc không khả dụng.</p>
                    <Link to="/health-packages" className={styles.cardActionLink}>
                        &larr; Quay lại danh sách gói khám
                    </Link>
                </div>
            </div>
        );
    }

    const services = (pkg.includedServices && pkg.includedServices.length > 0)
        ? pkg.includedServices
        : parseIncludedServices(pkg.includedServicesJson);

    return (
        <div className={styles.pageContainer}>
            <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/health-packages">Gói khám</Link>
                <span>/</span>
                <span aria-current="page">{pkg.name}</span>
            </nav>

            <div className={styles.detailLayout}>
                {/* Main Column */}
                <div className={styles.mainColumn}>
                    <div className={styles.detailHeader}>
                        <div className={styles.flexLayout16}>
                            <span className={styles.text11}>
                                {pkg.code}
                            </span>
                            <div className={[styles.packageAudience, styles.spacing3].join(' ')}>
                                <Users size={15} color="var(--cc-color-primary)" /> {pkg.targetAudience}
                            </div>
                        </div>
                        <h1 className={[styles.pageTitle, styles.heading5].join(' ')}>{pkg.name}</h1>
                        <p className={styles.paragraph6}>
                            {pkg.description}
                        </p>
                    </div>

                    <h2 className={styles.sectionTitle}>
                        <CheckCircle2 size={22} color="var(--cc-color-secondary)" /> Danh mục xét nghiệm & dịch vụ bao gồm ({services.length})
                    </h2>
                    <p className={styles.paragraph7}>
                        Toàn bộ các danh mục dưới đây đã được tính trọn gói, không phát sinh chi phí phụ.
                    </p>

                    <div className={styles.gridLayout2}>
                        {services.map((service: string, idx: number) => (
                            <div 
                                key={idx} 
                                className={styles.flexLayout17}
                            >
                                <span className={styles.text12}>{idx + 1}.</span>
                                <span>{service}</span>
                            </div>
                        ))}
                    </div>

                    <h2 className={styles.sectionTitle}>
                        <AlertCircle size={22} color="var(--cc-color-primary)" /> Lưu ý chuẩn bị trước khi đến khám
                    </h2>
                    <ul className={styles.text13}>
                        <li><strong>Nhịn ăn:</strong> Nhịn ăn ít nhất 6-8 tiếng trước khi lấy mẫu máu xét nghiệm (chỉ được uống nước lọc).</li>
                        <li><strong>Thuốc đang dùng:</strong> Vui lòng mang theo đơn thuốc hoặc thông báo trước với bác sĩ/lễ tân về các loại thuốc bạn đang sử dụng định kỳ để được hướng dẫn phù hợp.</li>
                        <li><strong>Trang phục:</strong> Mặc quần áo rộng rãi, thuận tiện để thăm khám lâm sàng, đo điện tim và chụp X-quang.</li>
                        <li><strong>Giấy tờ:</strong> Mang theo Căn cước công dân hoặc giấy tờ tùy thân có ảnh khi làm thủ tục tại quầy lễ tân.</li>
                    </ul>

                    <div className={styles.surface5}>
                        <div className={styles.flexLayout18}>
                            <ShieldCheck size={20} /> Cam kết chất lượng dịch vụ ClinicCare
                        </div>
                        <p className={styles.paragraph8}>
                            Quy trình thăm khám và xét nghiệm được thực hiện đồng bộ theo đúng tiêu chuẩn chuyên môn y khoa. Sau khi hoàn tất các chỉ định, bác sĩ chuyên khoa sẽ trực tiếp đọc kết quả, giải thích chi tiết và tư vấn định hướng chăm sóc sức khỏe cụ thể cho bạn.
                        </p>
                    </div>
                </div>

                {/* Sticky Sidebar */}
                <aside className={styles.sidebarCard}>
                    <span className={styles.packagePriceLabel}>Chi phí trọn gói</span>
                    <div className={[styles.packagePrice, styles.text14].join(' ')}>
                        {formatVndCurrency(pkg.price)}
                    </div>

                    <p className={styles.paragraph9}>
                        Đăng ký trực tuyến để được điều phối thứ tự ưu tiên tại phòng khám và chuẩn bị hồ sơ bệnh án trước.
                    </p>

                    <Link 
                        to={`/patient/health-packages/${pkg.id}/register`}
                        className={["btn btn-primary", styles.icon9].join(' ')}
                    >
                        <CalendarCheck size={18} /> Đăng ký gói khám này
                    </Link>

                    <Link 
                        to="/health-packages"
                        className={["btn btn-secondary", styles.icon10].join(' ')}
                    >
                        Xem các gói khám khác
                    </Link>
                </aside>
            </div>
        </div>
    );
};
