import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { User, Calendar, Stethoscope, Award, Phone, CheckCircle2, Clock } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import styles from './PublicPages.module.css';

interface DoctorDetail {
    id: number;
    fullName: string;
    academicTitle: string;
    experienceYears: number;
    description: string;
}

interface Specialty {
    id: number;
    specialtyName: string;
    specialtyCode: string;
}

export const DoctorDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const [doctor, setDoctor] = useState<DoctorDetail | null>(null);
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchDoctorData = async () => {
            if (!id) return;
            try {
                setLoading(true);
                const [docRes, specRes] = await Promise.all([
                    axiosClient.get<any, any>(`/doctors/${id}`),
                    axiosClient.get<any, any>(`/doctors/${id}/specialties`).catch(() => ({ success: false, data: [] }))
                ]);

                if (docRes.success && docRes.data) {
                    setDoctor(docRes.data);
                } else {
                    setError('Không tìm thấy thông tin bác sĩ.');
                }

                if (specRes.success && specRes.data) {
                    setSpecialties(specRes.data);
                }
            } catch (err: any) {
                setError(err.response?.data?.message || 'Không thể tải thông tin bác sĩ.');
            } finally {
                setLoading(false);
            }
        };

        fetchDoctorData();
    }, [id]);

    if (loading) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.skeletonCard} style={{ minHeight: '400px' }} />
            </div>
        );
    }

    if (error || !doctor) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.emptyState}>
                    <User size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>{error || 'Bác sĩ không tồn tại'}</div>
                    <p style={{ marginBottom: '20px' }}>Thông tin bác sĩ có thể đã được cập nhật hoặc không còn công tác.</p>
                    <Link to="/doctors" className={styles.cardActionLink}>
                        &larr; Quay lại danh sách bác sĩ
                    </Link>
                </div>
            </div>
        );
    }

    const primarySpecialty = specialties[0];

    return (
        <div className={styles.pageContainer}>
            <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/doctors">Bác sĩ</Link>
                <span>/</span>
                <span aria-current="page">{doctor.academicTitle ? `${doctor.academicTitle}. ` : ''}{doctor.fullName}</span>
            </nav>

            <div className={styles.detailLayout}>
                {/* Main Column */}
                <div className={styles.mainColumn}>
                    <div className={styles.detailHeader} style={{ display: 'flex', gap: '24px', alignItems: 'center' }}>
                        <div className={styles.doctorAvatar} style={{ width: '100px', height: '100px', margin: 0, flexShrink: 0 }}>
                            <User size={52} />
                        </div>
                        <div>
                            <span style={{ fontSize: '0.85rem', color: 'var(--c-primary)', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                {doctor.academicTitle || 'Bác sĩ Chuyên khoa'}
                            </span>
                            <h1 className={styles.pageTitle} style={{ fontSize: '1.9rem', margin: '4px 0 8px 0' }}>
                                {doctor.fullName}
                            </h1>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', alignItems: 'center' }}>
                                {specialties.map(s => (
                                    <Link 
                                        key={s.id} 
                                        to={`/specialties/${s.id}`}
                                        className={styles.doctorSpecialty}
                                        style={{ textDecoration: 'none', margin: 0 }}
                                    >
                                        <Stethoscope size={13} style={{ display: 'inline', marginRight: '4px' }} />
                                        {s.specialtyName}
                                    </Link>
                                ))}
                                <span style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                    <Award size={15} color="var(--c-secondary)" /> {doctor.experienceYears > 0 ? `${doctor.experienceYears} năm kinh nghiệm` : 'Bác sĩ chính quy'}
                                </span>
                            </div>
                        </div>
                    </div>

                    <h2 className={styles.sectionTitle}>Giới thiệu & Quá trình công tác</h2>
                    <p style={{ lineHeight: 1.7, color: 'var(--c-text)', fontSize: '1rem', whiteSpace: 'pre-line' }}>
                        {doctor.description || 'Bác sĩ có nhiều năm kinh nghiệm công tác tại các bệnh viện lớn tuyến trung ương, chuyên sâu trong chẩn đoán và điều trị bệnh lý chuyên khoa với thái độ y đức chuẩn mực và tận tâm.'}
                    </p>

                    <h2 className={styles.sectionTitle}>Lĩnh vực chuyên môn mũi nhọn</h2>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '12px', marginBottom: '32px' }}>
                        {[
                            'Thăm khám lâm sàng và chẩn đoán bệnh lý chuyên khoa',
                            'Tư vấn phác đồ điều trị cá thể hóa theo tiêu chuẩn y tế hiện đại',
                            'Theo dõi và phòng ngừa tái phát cho bệnh nhân',
                            'Hướng dẫn quản lý sức khỏe chủ động và cải thiện lối sống'
                        ].map((item, idx) => (
                            <div key={idx} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '0.9rem', color: 'var(--c-text)' }}>
                                <CheckCircle2 size={18} color="var(--c-secondary)" style={{ flexShrink: 0, marginTop: '2px' }} />
                                <span>{item}</span>
                            </div>
                        ))}
                    </div>

                    <div style={{ background: '#f8fafc', border: '1px solid var(--c-border-light)', borderRadius: '12px', padding: '20px', marginTop: '24px' }}>
                        <h3 style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--c-navy)', margin: '0 0 8px 0', display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <Clock size={18} color="var(--c-primary)" /> Quy chuẩn ca khám 30 phút
                        </h3>
                        <p style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', margin: 0, lineHeight: 1.5 }}>
                            ClinicCare tổ chức lịch khám theo các slot tiêu chuẩn 30 phút. Bác sĩ dành toàn bộ thời gian thăm khám kỹ lưỡng, giải thích kết quả xét nghiệm và tư vấn phác đồ điều trị phù hợp nhất cho bạn.
                        </p>
                    </div>
                </div>

                {/* Sticky Sidebar */}
                <aside className={styles.sidebarCard}>
                    <h3 style={{ fontSize: '1.2rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '16px' }}>
                        Đặt lịch khám bác sĩ
                    </h3>
                    <p style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
                        Chọn ngày và khung giờ khám còn trống của {doctor.academicTitle ? `${doctor.academicTitle}. ` : ''}{doctor.fullName}.
                    </p>

                    <Link 
                        to={`/patient/book?doctorId=${doctor.id}${primarySpecialty ? `&specialtyId=${primarySpecialty.id}` : ''}`}
                        className="btn btn-primary"
                        style={{ width: '100%', justifyContent: 'center', padding: '12px 20px', fontSize: '1rem', marginBottom: '16px', textDecoration: 'none' }}
                    >
                        <Calendar size={18} /> Chọn ngày & giờ khám
                    </Link>

                    {primarySpecialty && (
                        <Link 
                            to={`/specialties/${primarySpecialty.id}`}
                            className="btn btn-secondary"
                            style={{ width: '100%', justifyContent: 'center', padding: '10px 18px', fontSize: '0.925rem', marginBottom: '20px', textDecoration: 'none' }}
                        >
                            <Stethoscope size={16} /> Xem khoa {primarySpecialty.specialtyName}
                        </Link>
                    )}

                    <div style={{ borderTop: '1px solid var(--c-border-light)', paddingTop: '20px', marginTop: '16px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: 'var(--c-navy)', fontWeight: 600, fontSize: '0.95rem', marginBottom: '6px' }}>
                            <Phone size={18} color="var(--c-primary)" /> Hỗ trợ đặt hẹn
                        </div>
                        <div style={{ fontSize: '1.2rem', fontWeight: 800, color: 'var(--c-primary)', marginBottom: '4px' }}>
                            1900 1234
                        </div>
                        <div style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)' }}>
                            Tư vấn và giải đáp thắc mắc 07:00 - 19:00 hàng ngày.
                        </div>
                    </div>
                </aside>
            </div>
        </div>
    );
};
