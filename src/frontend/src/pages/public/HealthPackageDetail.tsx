import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Package, CheckCircle2, CalendarCheck, Phone, Users, ShieldCheck, AlertCircle } from 'lucide-react';
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
                setError(err.response?.data?.message || 'Không thể tải thông tin gói khám.');
            } finally {
                setLoading(false);
            }
        };

        fetchPackage();
    }, [id]);

    if (loading) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.skeletonCard} style={{ minHeight: '400px' }} />
            </div>
        );
    }

    if (error || !pkg) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.emptyState}>
                    <Package size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>{error || 'Gói khám không tồn tại'}</div>
                    <p style={{ marginBottom: '20px' }}>Gói khám bạn đang tìm kiếm có thể đã ngừng cung cấp hoặc không khả dụng.</p>
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
            <nav className={styles.breadcrumb} aria-label="Breadcrumb">
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
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                            <span style={{ 
                                background: 'var(--c-navy)', color: 'white', 
                                padding: '4px 10px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 700 
                            }}>
                                {pkg.code}
                            </span>
                            <div className={styles.packageAudience} style={{ margin: 0 }}>
                                <Users size={15} color="var(--c-primary)" /> {pkg.targetAudience}
                            </div>
                        </div>
                        <h1 className={styles.pageTitle} style={{ fontSize: '1.9rem' }}>{pkg.name}</h1>
                        <p style={{ fontSize: '1.05rem', color: 'var(--c-text)', lineHeight: 1.6, marginTop: '12px' }}>
                            {pkg.description}
                        </p>
                    </div>

                    <h2 className={styles.sectionTitle}>
                        <CheckCircle2 size={22} color="var(--c-secondary)" /> Danh mục xét nghiệm & dịch vụ bao gồm ({services.length})
                    </h2>
                    <p style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', marginBottom: '16px' }}>
                        Toàn bộ các danh mục dưới đây đã được tính trọn gói, không phát sinh chi phí phụ.
                    </p>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '12px', marginBottom: '32px' }}>
                        {services.map((service: string, idx: number) => (
                            <div 
                                key={idx} 
                                style={{ 
                                    display: 'flex', alignItems: 'flex-start', gap: '10px', 
                                    padding: '12px 16px', background: '#f8fafc', borderRadius: '8px',
                                    border: '1px solid var(--c-border-light)', fontSize: '0.925rem', color: 'var(--c-navy)'
                                }}
                            >
                                <span style={{ fontWeight: 700, color: 'var(--c-primary)', minWidth: '20px' }}>{idx + 1}.</span>
                                <span>{service}</span>
                            </div>
                        ))}
                    </div>

                    <h2 className={styles.sectionTitle}>
                        <AlertCircle size={22} color="var(--c-primary)" /> Lưu ý chuẩn bị trước khi đến khám
                    </h2>
                    <ul style={{ paddingLeft: '20px', color: 'var(--c-text)', lineHeight: 1.8, fontSize: '0.925rem', marginBottom: '32px' }}>
                        <li><strong>Nhịn ăn:</strong> Nhịn ăn ít nhất 6-8 tiếng trước khi lấy mẫu máu xét nghiệm (chỉ được uống nước lọc).</li>
                        <li><strong>Thuốc đang dùng:</strong> Vui lòng mang theo đơn thuốc hoặc thông báo trước với bác sĩ/lễ tân về các loại thuốc bạn đang sử dụng định kỳ để được hướng dẫn phù hợp.</li>
                        <li><strong>Trang phục:</strong> Mặc quần áo rộng rãi, thuận tiện để thăm khám lâm sàng, đo điện tim và chụp X-quang.</li>
                        <li><strong>Giấy tờ:</strong> Mang theo Căn cước công dân hoặc giấy tờ tùy thân có ảnh khi làm thủ tục tại quầy lễ tân.</li>
                    </ul>

                    <div style={{ background: '#ecfdf5', border: '1px solid #a7f3d0', borderRadius: '12px', padding: '20px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: '#065f46', fontWeight: 700, marginBottom: '6px' }}>
                            <ShieldCheck size={20} /> Cam kết chất lượng dịch vụ ClinicCare
                        </div>
                        <p style={{ margin: 0, fontSize: '0.875rem', color: '#047857', lineHeight: 1.5 }}>
                            Quy trình thăm khám và xét nghiệm được thực hiện đồng bộ theo đúng tiêu chuẩn chuyên môn y khoa. Sau khi hoàn tất các chỉ định, bác sĩ chuyên khoa sẽ trực tiếp đọc kết quả, giải thích chi tiết và tư vấn định hướng chăm sóc sức khỏe cụ thể cho bạn.
                        </p>
                    </div>
                </div>

                {/* Sticky Sidebar */}
                <aside className={styles.sidebarCard}>
                    <span className={styles.packagePriceLabel}>Chi phí trọn gói</span>
                    <div className={styles.packagePrice} style={{ fontSize: '2rem', marginBottom: '16px' }}>
                        {formatVndCurrency(pkg.price)}
                    </div>

                    <p style={{ fontSize: '0.875rem', color: 'var(--c-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
                        Đăng ký trực tuyến để được điều phối thứ tự ưu tiên tại phòng khám và chuẩn bị hồ sơ bệnh án trước.
                    </p>

                    <Link 
                        to={`/patient/health-packages/${pkg.id}/register`}
                        className="btn btn-primary"
                        style={{ width: '100%', justifyContent: 'center', padding: '12px 20px', fontSize: '1rem', marginBottom: '12px', textDecoration: 'none' }}
                    >
                        <CalendarCheck size={18} /> Đăng ký gói khám này
                    </Link>

                    <Link 
                        to="/health-packages"
                        className="btn btn-secondary"
                        style={{ width: '100%', justifyContent: 'center', padding: '10px 18px', fontSize: '0.9rem', marginBottom: '20px', textDecoration: 'none' }}
                    >
                        Xem các gói khám khác
                    </Link>

                    <div style={{ borderTop: '1px solid var(--c-border-light)', paddingTop: '20px', marginTop: '16px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: 'var(--c-navy)', fontWeight: 600, fontSize: '0.95rem', marginBottom: '6px' }}>
                            <Phone size={18} color="var(--c-primary)" /> Hotline tư vấn gói khám
                        </div>
                        <div style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)', lineHeight: 1.5 }}>
                            Thông tin liên hệ lễ tân chưa được cấu hình trong hệ thống.
                        </div>
                    </div>
                </aside>
            </div>
        </div>
    );
};
