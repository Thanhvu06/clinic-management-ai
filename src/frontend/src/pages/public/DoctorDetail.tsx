import React, { useState, useEffect } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { User, Calendar, Stethoscope, Award, Phone, CheckCircle2, Clock, CalendarDays } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { DoctorAvailabilityDto, DoctorDayAvailabilityDto } from '../../types';
import styles from './PublicPages.module.css';
import { formatDoctorName } from '../../utils/doctorNameHelper';

interface DoctorDetail {
    id: number;
    fullName: string;
    academicTitle?: string;
    experienceYears: number;
    description?: string;
}

interface Specialty {
    id: number;
    specialtyName: string;
    specialtyCode: string;
}

export const DoctorDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();

    const [doctor, setDoctor] = useState<DoctorDetail | null>(null);
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [availability, setAvailability] = useState<DoctorAvailabilityDto | null>(null);
    const [selectedDate, setSelectedDate] = useState<string>('');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchDoctorData = async () => {
            if (!id) return;
            try {
                setLoading(true);
                const [docRes, specRes, availRes] = await Promise.all([
                    axiosClient.get<any, any>(`/doctors/${id}`),
                    axiosClient.get<any, any>(`/doctors/${id}/specialties`).catch(() => ({ success: false, data: [] })),
                    axiosClient.get<any, any>(`/doctors/${id}/availability`).catch(() => ({ success: false, data: null }))
                ]);

                if (docRes.success && docRes.data) {
                    setDoctor(docRes.data);
                } else {
                    setError('Không tìm thấy thông tin bác sĩ.');
                }

                if (specRes.success && specRes.data) {
                    setSpecialties(specRes.data);
                }

                if (availRes.success && availRes.data) {
                    setAvailability(availRes.data);
                    if (availRes.data.days && availRes.data.days.length > 0) {
                        setSelectedDate(availRes.data.days[0].date);
                    }
                }
            } catch (err: any) {
                setError(err.response?.data?.message || 'Không thể tải thông tin bác sĩ.');
            } finally {
                setLoading(false);
            }
        };

        fetchDoctorData();
    }, [id]);

    const getDoctorInitials = (name?: string) => {
        if (!name) return 'BS';
        const parts = name.trim().split(/\s+/);
        if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
        return (parts[parts.length - 2][0] + parts[parts.length - 1][0]).toUpperCase();
    };

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
    const selectedDayData: DoctorDayAvailabilityDto | undefined = availability?.days.find(d => d.date === selectedDate);

    const handleSlotClick = (slotId: number) => {
        const specParam = primarySpecialty ? `&specialtyId=${primarySpecialty.id}` : '';
        navigate(`/patient/book?doctorId=${doctor.id}${specParam}&date=${selectedDate}&slotId=${slotId}`);
    };

    return (
        <div className={styles.pageContainer}>
            <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/doctors">Bác sĩ</Link>
                <span>/</span>
                <span aria-current="page">{formatDoctorName(doctor.academicTitle, doctor.fullName)}</span>
            </nav>

            <div className={styles.detailLayout}>
                {/* Main Column */}
                <div className={styles.mainColumn}>
                    <div className={styles.detailHeader} style={{ display: 'flex', gap: '24px', alignItems: 'center' }}>
                        <div 
                            className={styles.doctorAvatar} 
                            style={{ 
                                width: '90px', 
                                height: '90px', 
                                margin: 0, 
                                flexShrink: 0, 
                                background: '#f1f5f9', 
                                color: 'var(--c-primary)', 
                                fontWeight: 800, 
                                fontSize: '1.75rem',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                borderRadius: '50%'
                            }}
                        >
                            {getDoctorInitials(doctor.fullName)}
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
                                    <Award size={15} color="var(--c-secondary)" /> 
                                    {doctor.experienceYears > 0 ? `${doctor.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}
                                </span>
                            </div>
                        </div>
                    </div>

                    {/* 14-Day Availability Schedule */}
                    <div style={{ background: '#ffffff', border: '1px solid var(--c-border)', borderRadius: '14px', padding: '24px', margin: '28px 0', boxShadow: 'var(--shadow-sm)' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '16px' }}>
                            <CalendarDays size={22} color="var(--c-primary)" />
                            <div>
                                <h2 style={{ fontSize: '1.15rem', fontWeight: 700, color: 'var(--c-navy)', margin: 0 }}>
                                    Lịch khám & Khung giờ còn trống (14 ngày tới)
                                </h2>
                                <p style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)', margin: '2px 0 0 0' }}>
                                    Chọn ngày để xem các ca trực sáng/chiều và khung giờ 30 phút khả dụng
                                </p>
                            </div>
                        </div>

                        {availability && availability.days && availability.days.length > 0 ? (
                            <>
                                {/* 14-day date strip */}
                                <div style={{ display: 'flex', gap: '8px', overflowX: 'auto', paddingBottom: '12px', marginBottom: '16px' }}>
                                    {availability.days.map(d => {
                                        const isSelected = d.date === selectedDate;
                                        const hasSlots = d.availableSlots && d.availableSlots.length > 0;
                                        const dateParts = d.date.split('-');
                                        const displayDate = `${dateParts[2]}/${dateParts[1]}`;

                                        return (
                                            <button
                                                key={d.date}
                                                type="button"
                                                onClick={() => setSelectedDate(d.date)}
                                                style={{
                                                    minWidth: '76px',
                                                    padding: '10px 8px',
                                                    borderRadius: '10px',
                                                    border: isSelected ? '2px solid var(--c-primary)' : '1px solid var(--c-border)',
                                                    background: isSelected ? '#e0f2fe' : hasSlots ? '#ffffff' : '#f8fafc',
                                                    cursor: 'pointer',
                                                    textAlign: 'center',
                                                    transition: 'all 0.15s'
                                                }}
                                            >
                                                <div style={{ fontSize: '0.75rem', fontWeight: 600, color: isSelected ? 'var(--c-primary)' : '#64748b' }}>
                                                    {d.dayOfWeek}
                                                </div>
                                                <div style={{ fontSize: '1rem', fontWeight: 700, color: 'var(--c-navy)', margin: '2px 0' }}>
                                                    {displayDate}
                                                </div>
                                                <div style={{ fontSize: '0.7rem', color: hasSlots ? '#059669' : '#94a3b8', fontWeight: 600 }}>
                                                    {hasSlots ? `${d.availableSlots.length} slot` : 'Hết chỗ'}
                                                </div>
                                            </button>
                                        );
                                    })}
                                </div>

                                {/* Active day slots display */}
                                {selectedDayData ? (
                                    <div style={{ background: '#f8fafc', borderRadius: '10px', padding: '16px' }}>
                                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                                            <span style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--c-navy)' }}>
                                                Khung giờ ngày {selectedDayData.date} ({selectedDayData.dayOfWeek}):
                                            </span>
                                            {selectedDayData.scheduleBlocks && selectedDayData.scheduleBlocks.length > 0 && (
                                                <span style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)' }}>
                                                    Ca trực: {selectedDayData.scheduleBlocks.map(b => `${b.startTime.substring(0, 5)} - ${b.endTime.substring(0, 5)}`).join(' | ')}
                                                </span>
                                            )}
                                        </div>

                                        {selectedDayData.availableSlots && selectedDayData.availableSlots.length > 0 ? (
                                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                                                {selectedDayData.availableSlots.map(slot => (
                                                    <button
                                                        key={slot.slotId}
                                                        type="button"
                                                        onClick={() => handleSlotClick(slot.slotId)}
                                                        style={{
                                                            padding: '8px 14px',
                                                            borderRadius: '8px',
                                                            border: '1px solid #bae6fd',
                                                            background: '#ffffff',
                                                            color: 'var(--c-primary)',
                                                            fontWeight: 600,
                                                            fontSize: '0.875rem',
                                                            cursor: 'pointer',
                                                            display: 'inline-flex',
                                                            alignItems: 'center',
                                                            gap: '6px',
                                                            transition: 'all 0.15s'
                                                        }}
                                                        onMouseEnter={(e) => {
                                                            e.currentTarget.style.background = 'var(--c-primary)';
                                                            e.currentTarget.style.color = '#ffffff';
                                                        }}
                                                        onMouseLeave={(e) => {
                                                            e.currentTarget.style.background = '#ffffff';
                                                            e.currentTarget.style.color = 'var(--c-primary)';
                                                        }}
                                                    >
                                                        <Clock size={14} />
                                                        {slot.startTime.substring(0, 5)} - {slot.endTime.substring(0, 5)}
                                                    </button>
                                                ))}
                                            </div>
                                        ) : (
                                            <div style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', padding: '10px 0', textAlign: 'center' }}>
                                                Bác sĩ không có khung giờ trống trong ngày này. Vui lòng chọn ngày khác.
                                            </div>
                                        )}
                                    </div>
                                ) : null}
                            </>
                        ) : (
                            <div style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', textAlign: 'center', padding: '16px' }}>
                                Chưa có thông tin lịch trực cho 14 ngày tới.
                            </div>
                        )}
                    </div>

                    <h2 className={styles.sectionTitle}>Giới thiệu & Quá trình công tác</h2>
                    <p style={{ lineHeight: 1.7, color: 'var(--c-text)', fontSize: '1rem', whiteSpace: 'pre-line' }}>
                        {doctor.description || 'Bác sĩ chuyên khoa tại phòng khám ClinicCare, chuyên sâu trong chẩn đoán và điều trị bệnh lý chuyên khoa với thái độ y đức chuẩn mực và tận tâm.'}
                    </p>

                    <h2 className={styles.sectionTitle}>Lĩnh vực chuyên môn</h2>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '12px', marginBottom: '32px' }}>
                        {[
                            'Thăm khám lâm sàng và chẩn đoán bệnh lý chuyên khoa',
                            'Tư vấn phác đồ điều trị cá thể hóa theo tiêu chuẩn y tế hiện đại',
                            'Theo dõi và phòng ngừa tái phát cho người bệnh',
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
                            ClinicCare tổ chức lịch khám theo các slot tiêu chuẩn 30 phút. Bác sĩ dành thời gian thăm khám kỹ lưỡng, giải thích kết quả xét nghiệm và tư vấn phác đồ điều trị phù hợp nhất cho bạn.
                        </p>
                    </div>
                </div>

                {/* Sticky Sidebar */}
                <aside className={styles.sidebarCard}>
                    <h3 style={{ fontSize: '1.2rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '16px' }}>
                        Đặt lịch khám bác sĩ
                    </h3>
                    <p style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
                        Chọn ngày và khung giờ khám trực tuyến cùng {formatDoctorName(doctor.academicTitle, doctor.fullName)}.
                    </p>

                    <Link 
                        to={`/patient/book?doctorId=${doctor.id}${primarySpecialty ? `&specialtyId=${primarySpecialty.id}` : ''}`}
                        className="btn btn-primary"
                        style={{ width: '100%', justifyContent: 'center', padding: '12px 20px', fontSize: '1rem', marginBottom: '16px', textDecoration: 'none' }}
                    >
                        <Calendar size={18} /> Đặt lịch khám ngay
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
                            <Phone size={18} color="var(--c-primary)" /> Tổng đài phòng khám
                        </div>
                        <div style={{ fontSize: '1.2rem', fontWeight: 800, color: 'var(--c-primary)', marginBottom: '4px' }}>
                            028 3930 1234
                        </div>
                        <div style={{ fontSize: '0.8rem', color: 'var(--c-text-muted)' }}>
                            Tư vấn và hỗ trợ đặt hẹn 07:30 - 17:30 hàng ngày.
                        </div>
                    </div>
                </aside>
            </div>
        </div>
    );
};
