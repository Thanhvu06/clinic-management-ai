import React, { useState, useEffect } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { User, Calendar, Stethoscope, Award, CheckCircle2, Clock, CalendarDays } from 'lucide-react';
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
                <div className={[styles.skeletonCard, styles.spacing1].join(' ')} />
            </div>
        );
    }

    if (error || !doctor) {
        return (
            <div className={styles.pageContainer}>
                <div className={styles.emptyState}>
                    <User size={48} className={styles.icon1} />
                    <div className={styles.emptyStateTitle}>{error || 'Bác sĩ không tồn tại'}</div>
                    <p className={styles.paragraph1}>Thông tin bác sĩ có thể đã được cập nhật hoặc không còn công tác.</p>
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
            <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/doctors">Bác sĩ</Link>
                <span>/</span>
                <span aria-current="page">{formatDoctorName(doctor.academicTitle, doctor.fullName)}</span>
            </nav>

            <div className={styles.detailLayout}>
                {/* Main Column */}
                <div className={styles.mainColumn}>
                    <div className={[styles.detailHeader, styles.flexLayout1].join(' ')}>
                        <div 
                            className={[styles.doctorAvatar, styles.flexLayout2].join(' ')}
                        >
                            {getDoctorInitials(doctor.fullName)}
                        </div>
                        <div>
                            <span className={styles.text1}>
                                {doctor.academicTitle || 'Bác sĩ Chuyên khoa'}
                            </span>
                            <h1 className={[styles.pageTitle, styles.heading1].join(' ')}>
                                {doctor.fullName}
                            </h1>
                            <div className={styles.flexLayout3}>
                                {specialties.map(s => (
                                    <Link 
                                        key={s.id} 
                                        to={`/specialties/${s.id}`}
                                        className={[styles.doctorSpecialty, styles.icon2].join(' ')}
                                    >
                                        <Stethoscope size={13} className={styles.icon3} />
                                        {s.specialtyName}
                                    </Link>
                                ))}
                                <span className={styles.flexLayout4}>
                                    <Award size={15} color="var(--cc-color-secondary)" />
                                    {doctor.experienceYears > 0 ? `${doctor.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}
                                </span>
                            </div>
                        </div>
                    </div>

                    {/* 14-Day Availability Schedule */}
                    <div className={styles.surface1}>
                        <div className={styles.flexLayout5}>
                            <CalendarDays size={22} color="var(--cc-color-primary)" />
                            <div>
                                <h2 className={styles.heading2}>
                                    Lịch khám & Khung giờ còn trống (14 ngày tới)
                                </h2>
                                <p className={styles.paragraph2}>
                                    Chọn ngày để xem các ca trực sáng/chiều và khung giờ 30 phút khả dụng
                                </p>
                            </div>
                        </div>

                        {availability && availability.days && availability.days.length > 0 ? (
                            <>
                                {/* 14-day date strip */}
                                <div className={styles.flexLayout6}>
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
                                                className={[styles.button1, (isSelected ? styles.state1 : styles.state2), (isSelected ? styles.state3 : (hasSlots ? styles.state4 : styles.state5))].join(' ')}
                                            >
                                                <div className={[styles.text2, (isSelected ? styles.state6 : styles.state7)].join(' ')}>
                                                    {d.dayOfWeek}
                                                </div>
                                                <div className={styles.text3}>
                                                    {displayDate}
                                                </div>
                                                <div className={[styles.text4, (hasSlots ? styles.state8 : styles.state9)].join(' ')}>
                                                    {hasSlots ? `${d.availableSlots.length} slot` : 'Hết chỗ'}
                                                </div>
                                            </button>
                                        );
                                    })}
                                </div>

                                {/* Active day slots display */}
                                {selectedDayData ? (
                                    <div className={styles.surface2}>
                                        <div className={styles.flexLayout7}>
                                            <span className={styles.text5}>
                                                Khung giờ ngày {selectedDayData.date} ({selectedDayData.dayOfWeek}):
                                            </span>
                                            {selectedDayData.scheduleBlocks && selectedDayData.scheduleBlocks.length > 0 && (
                                                <span className={styles.text6}>
                                                    Ca trực: {selectedDayData.scheduleBlocks.map(b => `${b.startTime.substring(0, 5)} - ${b.endTime.substring(0, 5)}`).join(' | ')}
                                                </span>
                                            )}
                                        </div>

                                        {selectedDayData.availableSlots && selectedDayData.availableSlots.length > 0 ? (
                                            <div className={styles.flexLayout8}>
                                                {selectedDayData.availableSlots.map(slot => (
                                                    <button
                                                        key={slot.slotId}
                                                        type="button"
                                                        onClick={() => handleSlotClick(slot.slotId)}
                                                        className={styles.flexLayout9}
                                                        onMouseEnter={(e) => {
                                                            e.currentTarget.style.background = 'var(--cc-color-primary)';
                                                            e.currentTarget.style.color = 'var(--cc-color-bg-container)';
                                                        }}
                                                        onMouseLeave={(e) => {
                                                            e.currentTarget.style.background = 'var(--cc-color-bg-container)';
                                                            e.currentTarget.style.color = 'var(--cc-color-primary)';
                                                        }}
                                                    >
                                                        <Clock size={14} />
                                                        {slot.startTime.substring(0, 5)} - {slot.endTime.substring(0, 5)}
                                                    </button>
                                                ))}
                                            </div>
                                        ) : (
                                            <div className={styles.text7}>
                                                Bác sĩ không có khung giờ trống trong ngày này. Vui lòng chọn ngày khác.
                                            </div>
                                        )}
                                    </div>
                                ) : null}
                            </>
                        ) : (
                            <div className={styles.text8}>
                                Chưa có thông tin lịch trực cho 14 ngày tới.
                            </div>
                        )}
                    </div>

                    <h2 className={styles.sectionTitle}>Giới thiệu & Quá trình công tác</h2>
                    <p className={styles.paragraph3}>
                        {doctor.description || 'Bác sĩ chuyên khoa tại phòng khám ClinicCare, chuyên sâu trong chẩn đoán và điều trị bệnh lý chuyên khoa với thái độ y đức chuẩn mực và tận tâm.'}
                    </p>

                    <h2 className={styles.sectionTitle}>Lĩnh vực chuyên môn</h2>
                    <div className={styles.gridLayout1}>
                        {[
                            'Thăm khám lâm sàng và chẩn đoán bệnh lý chuyên khoa',
                            'Tư vấn phác đồ điều trị cá thể hóa theo tiêu chuẩn y tế hiện đại',
                            'Theo dõi và phòng ngừa tái phát cho người bệnh',
                            'Hướng dẫn quản lý sức khỏe chủ động và cải thiện lối sống'
                        ].map((item, idx) => (
                            <div key={idx} className={styles.flexLayout10}>
                                <CheckCircle2 size={18} color="var(--cc-color-secondary)" className={styles.icon4} />
                                <span>{item}</span>
                            </div>
                        ))}
                    </div>

                    <div className={styles.surface3}>
                        <h3 className={styles.flexLayout11}>
                            <Clock size={18} color="var(--cc-color-primary)" /> Quy chuẩn ca khám 30 phút
                        </h3>
                        <p className={styles.paragraph4}>
                            ClinicCare tổ chức lịch khám theo các slot tiêu chuẩn 30 phút. Bác sĩ dành thời gian thăm khám kỹ lưỡng, giải thích kết quả xét nghiệm và tư vấn phác đồ điều trị phù hợp nhất cho bạn.
                        </p>
                    </div>
                </div>

                {/* Sticky Sidebar */}
                <aside className={styles.sidebarCard}>
                    <h3 className={styles.heading3}>
                        Đặt lịch khám bác sĩ
                    </h3>
                    <p className={styles.paragraph5}>
                        Chọn ngày và khung giờ khám trực tuyến cùng {formatDoctorName(doctor.academicTitle, doctor.fullName)}.
                    </p>

                    <Link 
                        to={`/patient/book?doctorId=${doctor.id}${primarySpecialty ? `&specialtyId=${primarySpecialty.id}` : ''}`}
                        className={["btn btn-primary", styles.icon5].join(' ')}
                    >
                        <Calendar size={18} /> Đặt lịch khám ngay
                    </Link>

                    {primarySpecialty && (
                        <Link 
                            to={`/specialties/${primarySpecialty.id}`}
                            className={["btn btn-secondary", styles.icon6].join(' ')}
                        >
                            <Stethoscope size={16} /> Xem khoa {primarySpecialty.specialtyName}
                        </Link>
                    )}
                </aside>
            </div>
        </div>
    );
};
