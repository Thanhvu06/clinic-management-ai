import { formatDateOnly } from '../../utils/formatters';
import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { Button } from 'antd';
import axiosClient from '../../api/axiosClient';
import type { DoctorAvailabilityDto, DoctorDayAvailabilityDto } from '../../types';
import { InlineError } from '../../components/common/InlineError';
import { LoadingState } from '../../components/common/LoadingState';
import { LinkButton } from '../../components/common/LinkButton';
import shared from './PublicPages.module.css';
import styles from './DoctorDetail.module.css';
import { formatDoctorName } from '../../utils/doctorNameHelper';
import { formatFreeSlotCount, formatScheduleDayShort, formatScheduleWeekday, formatTimeRange } from './doctorScheduleLabels';

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

const getDoctorInitials = (name?: string) => {
    if (!name) return 'BS';
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
    return (parts[parts.length - 2][0] + parts[parts.length - 1][0]).toUpperCase();
};

export const DoctorDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();

    const [doctor, setDoctor] = useState<DoctorDetail | null>(null);
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [availability, setAvailability] = useState<DoctorAvailabilityDto | null>(null);
    const [selectedDate, setSelectedDate] = useState<string>('');
    const [loading, setLoading] = useState(true);
    const [notFound, setNotFound] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [reloadKey, setReloadKey] = useState(0);

    useEffect(() => {
        const fetchDoctorData = async () => {
            if (!id) return;
            try {
                const [docRes, specRes, availRes] = await Promise.all([
                    axiosClient.get<any, any>(`/doctors/${id}`),
                    axiosClient.get<any, any>(`/doctors/${id}/specialties`).catch(() => ({ success: false, data: [] })),
                    axiosClient.get<any, any>(`/doctors/${id}/availability`).catch(() => ({ success: false, data: null }))
                ]);

                setError(null);
                setNotFound(!(docRes.success && docRes.data));
                if (docRes.success && docRes.data) setDoctor(docRes.data);

                if (specRes.success && specRes.data) setSpecialties(specRes.data);

                if (availRes.success && availRes.data) {
                    setAvailability(availRes.data);
                    const days: DoctorDayAvailabilityDto[] = availRes.data.days || [];
                    const firstOpen = days.find(d => d.availableSlots?.length) ?? days[0];
                    if (firstOpen) setSelectedDate(firstOpen.date);
                }
            } catch (err: any) {
                if (err?.status === 404) setNotFound(true);
                else setError(getApiErrorMessage(err, 'Không thể tải thông tin bác sĩ.'));
            } finally {
                setLoading(false);
            }
        };
        void fetchDoctorData();
    }, [id, reloadKey]);

    const retry = () => {
        setLoading(true);
        setError(null);
        setReloadKey(key => key + 1);
    };

    if (loading) {
        return <div className={shared.pageContainer}><LoadingState message="Đang tải thông tin bác sĩ..." /></div>;
    }

    if (error) {
        return <div className={shared.pageContainer}><InlineError message={error} onRetry={retry} /></div>;
    }

    if (notFound || !doctor) {
        return (
            <div className={shared.pageContainer}>
                <div className={shared.emptyState}>
                    <div className={shared.emptyStateTitle}>Không tìm thấy bác sĩ</div>
                    <p className={shared.paragraph1}>Bác sĩ có thể đã ngừng nhận lịch khám.</p>
                    <Link to="/doctors" className={shared.cardActionLink}>Quay lại danh sách bác sĩ</Link>
                </div>
            </div>
        );
    }

    const doctorName = formatDoctorName(doctor.academicTitle, doctor.fullName);
    const primarySpecialty = specialties[0];
    const specialtyParam = primarySpecialty ? `&specialtyId=${primarySpecialty.id}` : '';
    const days = availability?.days ?? [];
    const selectedDay = days.find(d => d.date === selectedDate);
    const nextOpenDay = days.find(d => d.availableSlots?.length);
    const description = doctor.description?.trim();

    const handleSlotClick = (slotId: number) => {
        navigate(`/patient/book?doctorId=${doctor.id}${specialtyParam}&date=${selectedDate}&slotId=${slotId}`);
    };

    return (
        <div className={shared.pageContainer}>
            <nav className={shared.breadcrumb} aria-label="Đường dẫn trang">
                <Link to="/">Trang chủ</Link>
                <span>/</span>
                <Link to="/doctors">Bác sĩ</Link>
                <span>/</span>
                <span aria-current="page">{doctorName}</span>
            </nav>

            <div className={styles.layout}>
                <div className={styles.main}>
                    <header className={styles.profile}>
                        <div className={styles.avatar} aria-hidden="true">{getDoctorInitials(doctor.fullName)}</div>
                        <div className={styles.identity}>
                            <h1 className={styles.name}>{doctorName}</h1>
                            <div className={styles.meta}>
                                {specialties.map(s => (
                                    <Link key={s.id} to={`/specialties/${s.id}`}>{s.specialtyName}</Link>
                                ))}
                                <span>{doctor.experienceYears > 0 ? `${doctor.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}</span>
                            </div>
                        </div>
                    </header>

                    <section className={styles.panel} aria-labelledby="doctor-schedule-title">
                        <h2 id="doctor-schedule-title" className={styles.sectionTitle}>Lịch còn trống 14 ngày tới</h2>
                        {days.length > 0 ? (
                            <>
                                <div className={styles.dateStrip} role="group" aria-label="Chọn ngày khám">
                                    {days.map(d => {
                                        const count = d.availableSlots?.length ?? 0;
                                        return (
                                            <button
                                                key={d.date}
                                                type="button"
                                                aria-pressed={d.date === selectedDate}
                                                onClick={() => setSelectedDate(d.date)}
                                                className={styles.dayButton}
                                            >
                                                <span className={styles.dayLabel}>{formatScheduleDayShort(d.date)}</span>
                                                <span className={count > 0 ? styles.dayCount : styles.dayFull}>{formatFreeSlotCount(count)}</span>
                                            </button>
                                        );
                                    })}
                                </div>

                                {selectedDay && (
                                    <div className={styles.dayDetail}>
                                        <div className={styles.dayHeading}>
                                            <strong>{formatScheduleWeekday(selectedDay.date)}, {formatDateOnly(selectedDay.date)}</strong>
                                            {selectedDay.scheduleBlocks?.length > 0 && (
                                                <span className={styles.shift}>
                                                    Ca trực {selectedDay.scheduleBlocks.map(b => formatTimeRange(b.startTime, b.endTime)).join(' · ')}
                                                </span>
                                            )}
                                        </div>
                                        {selectedDay.availableSlots?.length > 0 ? (
                                            <div className={styles.slots}>
                                                {selectedDay.availableSlots.map(slot => (
                                                    <Button key={slot.slotId} onClick={() => handleSlotClick(slot.slotId)}>
                                                        {formatTimeRange(slot.startTime, slot.endTime)}
                                                    </Button>
                                                ))}
                                            </div>
                                        ) : (
                                            <p className={styles.muted}>Ngày này đã hết giờ trống. Bạn chọn ngày khác nhé.</p>
                                        )}
                                    </div>
                                )}
                            </>
                        ) : (
                            <p className={styles.muted}>Bác sĩ chưa có lịch khám trong 14 ngày tới.</p>
                        )}
                    </section>

                    {description && (
                        <section className={styles.panel} aria-labelledby="doctor-about-title">
                            <h2 id="doctor-about-title" className={styles.sectionTitle}>Giới thiệu</h2>
                            <p className={styles.about}>{description}</p>
                        </section>
                    )}
                </div>

                <aside className={styles.aside} aria-labelledby="doctor-booking-title">
                    <h2 id="doctor-booking-title" className={styles.sectionTitle}>Đặt lịch khám</h2>
                    <p className={styles.muted}>
                        {nextOpenDay
                            ? `Giờ trống gần nhất: ${formatScheduleDayShort(nextOpenDay.date)}, ${nextOpenDay.availableSlots[0].startTime.substring(0, 5)}`
                            : 'Chưa có giờ trống trong 14 ngày tới.'}
                    </p>
                    <LinkButton type="primary" size="large" block to={`/patient/book?doctorId=${doctor.id}${specialtyParam}`}>
                        Đặt lịch khám
                    </LinkButton>
                    {primarySpecialty && (
                        <Link to={`/specialties/${primarySpecialty.id}`} className={styles.secondaryLink}>
                            Xem chuyên khoa {primarySpecialty.specialtyName}
                        </Link>
                    )}
                </aside>
            </div>
        </div>
    );
};
