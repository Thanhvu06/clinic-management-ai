import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Stethoscope, User, Calendar, Bot, Phone, ChevronRight, Sparkles, CheckCircle2 } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import styles from './PublicPages.module.css';
import { formatDoctorName } from '../../utils/doctorNameHelper';

interface Specialty {
    id: number;
    specialtyCode: string;
    specialtyName: string;
    description: string;
    aiEnabled: boolean;
}

interface Doctor {
    id: number;
    fullName: string;
    academicTitle?: string;
    experienceYears: number;
    specialtyId?: number;
    specialtyName?: string;
    description?: string;
}

export const SpecialtyDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const [specialty, setSpecialty] = useState<Specialty | null>(null);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchSpecialtyData = async () => {
            if (!id) return;
            try {
                setLoading(true);
                const [specRes, docRes] = await Promise.all([
                    axiosClient.get<any, any>(`/specialties/${id}`),
                    axiosClient.get<any, any>(`/specialties/${id}/doctors?page=1&pageSize=20`).catch(() => ({ success: false, data: { items: [] } }))
                ]);

                if (specRes.success && specRes.data) {
                    setSpecialty(specRes.data);
                } else {
                    setError('Không tìm thấy thông tin chuyên khoa.');
                }

                if (docRes.success && docRes.data) {
                    setDoctors(docRes.data.items || docRes.data || []);
                }
            } catch (err: any) {
                setError(err.response?.data?.message || 'Không thể tải thông tin chuyên khoa.');
            } finally {
                setLoading(false);
            }
        };

        fetchSpecialtyData();
    }, [id]);

    if (loading) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.skeletonCard} style={{ minHeight: '400px' }} />
            </div>
        );
    }

    if (error || !specialty) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.emptyState}>
                    <Stethoscope size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>{error || 'Chuyên khoa không tồn tại'}</div>
                    <p style={{ marginBottom: '20px' }}>Chuyên khoa bạn đang tìm kiếm có thể đã bị thay đổi hoặc không có sẵn.</p>
                    <Link to="/specialties" className={styles.cardActionLink}>
                        &larr; Quay lại danh sách chuyên khoa
                    </Link>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.pageContainer}>
            <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/specialties">Chuyên khoa</Link>
                <span>/</span>
                <span aria-current="page">{specialty.specialtyName}</span>
            </nav>

            <div className={styles.detailLayout}>
                {/* Main Column */}
                <div className={styles.mainColumn}>
                    <div className={styles.detailHeader}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '12px' }}>
                            <div className={styles.cardIcon} style={{ margin: 0 }}>
                                <Stethoscope size={28} />
                            </div>
                            <div>
                                <span style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)', fontWeight: 600 }}>
                                    MÃ KHOA: {specialty.specialtyCode}
                                </span>
                                <h1 className={styles.pageTitle} style={{ fontSize: '1.85rem', margin: 0 }}>
                                    {specialty.specialtyName}
                                </h1>
                            </div>
                        </div>
                        {specialty.aiEnabled && (
                            <div style={{ 
                                display: 'inline-flex', alignItems: 'center', gap: '6px',
                                marginTop: '12px', padding: '6px 14px', borderRadius: '16px',
                                background: '#ecfdf5', border: '1px solid #a7f3d0', color: '#065f46',
                                fontSize: '0.85rem', fontWeight: 600
                            }}>
                                <Sparkles size={16} /> Chuyên khoa được tích hợp hỗ trợ sàng lọc triệu chứng bằng AI
                            </div>
                        )}
                    </div>

                    <h2 className={styles.sectionTitle}>Giới thiệu chuyên khoa</h2>
                    <p style={{ lineHeight: 1.7, color: 'var(--c-text)', fontSize: '1rem' }}>
                        {specialty.description || 'Khoa phụ trách thăm khám, chẩn đoán và điều trị toàn diện các bệnh lý chuyên khoa với phác đồ y khoa cập nhật nhất và trang thiết bị hiện đại.'}
                    </p>

                    <h2 className={styles.sectionTitle}>Chỉ định thăm khám thường gặp</h2>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '12px', marginBottom: '32px' }}>
                        {[
                            'Khám và tầm soát định kỳ các triệu chứng bất thường',
                            'Theo dõi và điều trị các bệnh lý mãn tính chuyên sâu',
                            'Tư vấn chế độ dinh dưỡng và lối sống phòng ngừa biến chứng',
                            'Hội chẩn liên chuyên khoa đối với các ca bệnh phức tạp'
                        ].map((item, idx) => (
                            <div key={idx} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '0.9rem', color: 'var(--c-text)' }}>
                                <CheckCircle2 size={18} color="var(--c-secondary)" style={{ flexShrink: 0, marginTop: '2px' }} />
                                <span>{item}</span>
                            </div>
                        ))}
                    </div>

                    <h2 className={styles.sectionTitle}>
                        Đội ngũ Bác sĩ phụ trách ({doctors.length})
                    </h2>
                    {doctors.length > 0 ? (
                        <div className={styles.grid3} style={{ marginTop: '16px' }}>
                            {doctors.map(doc => (
                                <div key={doc.id} className={styles.doctorCard}>
                                    <div className={styles.doctorAvatar}>
                                        <User size={36} />
                                    </div>
                                    <h3 className={styles.doctorName}>
                                        {formatDoctorName(doc.academicTitle, doc.fullName)}
                                    </h3>
                                    <span className={styles.doctorSpecialty}>
                                        {specialty.specialtyName}
                                    </span>
                                    <span className={styles.doctorExp}>
                                        {doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}
                                    </span>
                                    <div className={styles.doctorActions}>
                                        <Link to={`/doctors/${doc.id}`} className={styles.cardActionLink} style={{ justifyContent: 'center' }}>
                                            Hồ sơ bác sĩ <ChevronRight size={15} />
                                        </Link>
                                        <Link 
                                            to={`/patient/book?specialtyId=${specialty.id}&doctorId=${doc.id}`}
                                            className="btn btn-primary btn-sm"
                                            style={{ textDecoration: 'none', justifyContent: 'center' }}
                                        >
                                            <Calendar size={14} /> Đặt khám với bác sĩ
                                        </Link>
                                    </div>
                                </div>
                            ))}
                        </div>
                    ) : (
                        <div style={{ padding: '24px', background: '#f8fafc', borderRadius: '8px', color: 'var(--c-text-muted)', textAlign: 'center' }}>
                            Hiện chưa có danh sách bác sĩ cụ thể cho chuyên khoa này. Bạn vẫn có thể đăng ký đặt lịch tổng quát.
                        </div>
                    )}
                </div>

                {/* Sticky Sidebar */}
                <aside className={styles.sidebarCard}>
                    <h3 style={{ fontSize: '1.2rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '16px' }}>
                        Đăng ký Thăm khám
                    </h3>
                    <p style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
                        Chọn bác sĩ chuyên khoa hoặc đặt hẹn để được tiếp đón theo khung giờ mong muốn, không chờ đợi.
                    </p>

                    <Link 
                        to={`/patient/book?specialtyId=${specialty.id}`}
                        className="btn btn-primary"
                        style={{ width: '100%', justifyContent: 'center', padding: '12px 20px', fontSize: '1rem', marginBottom: '12px', textDecoration: 'none' }}
                    >
                        <Calendar size={18} /> Đặt lịch khám ngay
                    </Link>

                    {specialty.aiEnabled && (
                        <Link 
                            to="/patient/ai-consultation"
                            className="btn btn-secondary"
                            style={{ width: '100%', justifyContent: 'center', padding: '10px 18px', fontSize: '0.925rem', marginBottom: '20px', textDecoration: 'none' }}
                        >
                            <Bot size={18} /> Định tuyến triệu chứng AI
                        </Link>
                    )}

                    <div style={{ borderTop: '1px solid var(--c-border-light)', paddingTop: '20px', marginTop: '16px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: 'var(--c-navy)', fontWeight: 600, fontSize: '0.95rem', marginBottom: '6px' }}>
                            <Phone size={18} color="var(--c-primary)" /> Hỗ trợ khách hàng
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
