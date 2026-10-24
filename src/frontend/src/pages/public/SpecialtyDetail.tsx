import { createElement } from 'react';
import { Link } from 'react-router-dom';
import { ChatConsultationLink } from '../../components/chat/ChatConsultationLink';
import { InlineError } from '../../components/common/InlineError';
import { specialtyIcon } from './landing/specialtyIcons';
import { specialtyContent } from './specialty/specialtyContent';
import { useSpecialtyDetail } from './specialty/useSpecialtyDetail';
import { SpecialtyDoctors } from './specialty/SpecialtyDoctors';
import { SpecialtyClinicalContent } from './specialty/SpecialtyClinicalContent';
import { SpecialtyPackages } from './specialty/SpecialtyPackages';
import { PublicLocations } from './specialty/PublicLocations';
import styles from './SpecialtyDetail.module.css';

export const SpecialtyDetail = () => {
    const data = useSpecialtyDetail();
    const { specialty, loading, error, fetchSpecialtyData } = data;
    if (loading) return <div className={styles.pageContainer} role="status" aria-label="Đang tải chuyên khoa"><div className={styles.pageSkeleton} /></div>;
    if (error) return <div className={styles.pageContainer}><InlineError message={error} onRetry={() => void fetchSpecialtyData()} /></div>;
    if (!specialty) return <div className={styles.pageContainer}><div className={styles.emptyState}>
        <div className={styles.emptyStateTitle}>Không tìm thấy chuyên khoa</div>
        <p className={styles.emptyDescription}>Chuyên khoa có thể đã đổi tên hoặc ngừng nhận lịch.</p>
        <Link to="/specialties" className={styles.profileLink}>Quay lại danh sách chuyên khoa</Link>
    </div></div>;
    const content = specialtyContent[specialty.specialtyCode];
    const fee = specialty.consultationFee == null ? 'Chưa cập nhật' : `${new Intl.NumberFormat('vi-VN').format(specialty.consultationFee)} đ / lượt`;
    const actions = <>
        <Link to={`/patient/book?specialtyId=${specialty.id}`} className={styles.bookingLink}>Đặt lịch khám</Link>
        {specialty.aiEnabled && <ChatConsultationLink to="/patient/ai-consultation" className={styles.aiLink}>Hỏi trợ lý AI</ChatConsultationLink>}
    </>;
    return <div className={styles.pageContainer}>
        <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
            <Link to="/">Trang chủ</Link><span>/</span><Link to="/specialties">Chuyên khoa</Link><span>/</span>
            <span aria-current="page">{specialty.specialtyName}</span>
        </nav>
        <header className={styles.detailHeader}>
            <div className={styles.headerIdentity}>{createElement(specialtyIcon(specialty.specialtyName), { size: 42, strokeWidth: 1.4, 'aria-hidden': true })}<div>
                <p className={styles.eyebrow}>Chuyên khoa</p><h1 className={styles.pageTitle}>{specialty.specialtyName}</h1>
            </div></div>
            <p className={styles.specialtyDescription}>{content?.intro || specialty.description}</p>
            <p className={styles.fee}>Phí khám: <strong>{fee}</strong></p>
            <div className={styles.headerActions}>{actions}</div>
        </header>
        <div className={styles.detailLayout}>
            <aside className={styles.sidebar}>
                <section className={`${styles.sidebarCard} ${styles.bookingCard}`} aria-label="Đặt lịch theo chuyên khoa">
                    <h2 className={styles.sidebarTitle}>Đặt lịch khám</h2>
                    <p className={styles.sidebarDescription}>Chọn bác sĩ và giờ khám ở bước tiếp theo.</p>
                    <Link to={`/patient/book?specialtyId=${specialty.id}`} className={styles.bookingLink}>Đặt lịch khám</Link>
                </section>
                <section className={styles.sidebarCard} aria-labelledby="visit-information-title">
                    <h2 id="visit-information-title" className={styles.sidebarTitle}>Thông tin khám</h2>
                    <dl className={styles.visitFacts}><div><dt>Phí khám</dt><dd>{fee}</dd></div><div><dt>Thời lượng 1 lượt</dt><dd>30 phút</dd></div></dl>
                    <PublicLocations />
                </section>
            </aside>
            <div className={styles.mainColumn}>
                {content && <SpecialtyClinicalContent content={content} />}
                <SpecialtyPackages key={specialty.id} name={specialty.specialtyName} />
            </div>
        </div>
        <div className={styles.doctorSection}><SpecialtyDoctors data={data} /></div>
        <p className={styles.disclaimer}>Thông tin chỉ mang tính tham khảo, không thay thế chẩn đoán của bác sĩ.</p>
    </div>;
};
