import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, Link } from 'react-router-dom';
import { Tag } from 'antd';
import { Stethoscope, User, Calendar, Bot, ChevronRight, Sparkles } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { InlineError } from '../../components/common/InlineError';
import { formatDoctorName } from '../../utils/doctorNameHelper';
import { toLocalDateString } from '../../utils/formatters';
import styles from './SpecialtyDetail.module.css';

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
}
interface RecommendedDoctor {
    doctorId: number;
    fullName: string;
    experienceYears: number;
    experienceScore: number;
    availabilityScore: number;
    totalScore: number;
    earliestAvailableSlot: string | null;
}
interface ApiResponse<T> { success: boolean; data: T; message?: string; }

const slotFormatter = new Intl.DateTimeFormat('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh', weekday: 'long', day: '2-digit', month: '2-digit',
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
});
const bookingDateFormatter = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit',
});
function bookingDate(slot: string): string {
    const parts = bookingDateFormatter.formatToParts(new Date(slot));
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value;
    return `${part('year')}-${part('month')}-${part('day')}`;
}
function slotLabel(slot: string): string {
    const parts = slotFormatter.formatToParts(new Date(slot));
    const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value;
    return `${part('weekday')}, ${part('day')}/${part('month')}, ${part('hour')}:${part('minute')}`;
}
function errorMessage(error: unknown, fallback: string): string {
    return error && typeof error === 'object' && 'message' in error && typeof error.message === 'string'
        ? error.message : fallback;
}

export const SpecialtyDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const [specialty, setSpecialty] = useState<Specialty | null>(null);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [recommendations, setRecommendations] = useState<RecommendedDoctor[]>([]);
    const [recommendationsLoading, setRecommendationsLoading] = useState(true);
    const [recommendationsError, setRecommendationsError] = useState<string | null>(null);
    const pageRequest = useRef<AbortController | null>(null);
    const recommendationsRequest = useRef<AbortController | null>(null);

    const fetchSpecialtyData = useCallback(async () => {
        pageRequest.current?.abort();
        const controller = new AbortController();
        pageRequest.current = controller;
        setLoading(true);
        setError(null);
        setSpecialty(null);
        setDoctors([]);
        try {
            if (!id) return;
            const [specRes, docRes] = await Promise.all([
                axiosClient.get<unknown, ApiResponse<Specialty>>(`/specialties/${id}`, { signal: controller.signal }),
                axiosClient.get<unknown, ApiResponse<{ items: Doctor[] } | Doctor[]>>(
                    `/specialties/${id}/doctors?page=1&pageSize=20`, { signal: controller.signal },
                ).catch(() => ({ success: false, data: [] as Doctor[] })),
            ]);
            if (controller.signal.aborted) return;
            if (specRes.success && specRes.data) setSpecialty(specRes.data);
            if (docRes.success && docRes.data) setDoctors(Array.isArray(docRes.data) ? docRes.data : docRes.data.items || []);
        } catch (err: unknown) {
            if (controller.signal.aborted) return;
            if (!(err && typeof err === 'object' && 'status' in err && err.status === 404)) {
                setError(errorMessage(err, 'Không thể tải thông tin chuyên khoa.'));
            }
        } finally {
            if (!controller.signal.aborted) setLoading(false);
        }
    }, [id]);

    const fetchRecommendations = useCallback(async () => {
        recommendationsRequest.current?.abort();
        const controller = new AbortController();
        recommendationsRequest.current = controller;
        setRecommendationsLoading(true);
        setRecommendationsError(null);
        setRecommendations([]);
        try {
            if (!id) return;
            const response = await axiosClient.get<unknown, ApiResponse<RecommendedDoctor[]>>(
                `/specialties/${id}/recommended-doctors`,
                { params: { fromDate: toLocalDateString(), days: 14 }, signal: controller.signal },
            );
            if (controller.signal.aborted) return;
            if (!response.success) throw new Error(response.message || 'Không thể tải bác sĩ được gợi ý.');
            setRecommendations(response.data.slice(0, 3));
        } catch (err: unknown) {
            if (!controller.signal.aborted) setRecommendationsError(errorMessage(err, 'Không thể tải bác sĩ được gợi ý.'));
        } finally {
            if (!controller.signal.aborted) setRecommendationsLoading(false);
        }
    }, [id]);

    useEffect(() => {
        let active = true;
        // Start both requests after the route effect; discard a superseded route's work.
        void Promise.resolve().then(() => {
            if (!active) return;
            void fetchSpecialtyData();
            void fetchRecommendations();
        });
        return () => {
            active = false;
            pageRequest.current?.abort();
            recommendationsRequest.current?.abort();
        };
    }, [fetchSpecialtyData, fetchRecommendations]);

    if (loading) return <div className={styles.pageContainer} role="status" aria-label="Đang tải chuyên khoa"><div className={styles.pageSkeleton} /></div>;
    if (error) return <div className={styles.pageContainer}><InlineError message={error} onRetry={() => void fetchSpecialtyData()} /></div>;
    if (!specialty) return <div className={styles.pageContainer}>
        <div className={styles.emptyState}>
            <Stethoscope size={48} className={styles.emptyIcon} />
            <div className={styles.emptyStateTitle}>Không tìm thấy thông tin chuyên khoa.</div>
            <p className={styles.emptyDescription}>Chuyên khoa bạn đang tìm kiếm có thể đã bị thay đổi hoặc không có sẵn.</p>
            <Link to="/specialties" className={styles.profileLink}>&larr; Quay lại danh sách chuyên khoa</Link>
        </div>
    </div>;

    return <div className={styles.pageContainer}>
        <nav className={styles.breadcrumb} aria-label="Breadcrumb">
            <Link to="/">Trang chủ</Link><span>/</span><Link to="/specialties">Chuyên khoa</Link><span>/</span>
            <span aria-current="page">{specialty.specialtyName}</span>
        </nav>
        <div className={styles.detailLayout}>
            <div className={styles.mainColumn}>
                <div className={styles.detailHeader}>
                    <div className={styles.headerIdentity}>
                        <div className={styles.specialtyIcon}><Stethoscope size={28} /></div>
                        <div><span className={styles.specialtyCode}>MÃ KHOA: {specialty.specialtyCode}</span><h1 className={styles.pageTitle}>{specialty.specialtyName}</h1></div>
                    </div>
                    {specialty.aiEnabled && <div className={styles.aiBadge}><Sparkles size={16} /> Chuyên khoa được tích hợp hỗ trợ sàng lọc triệu chứng bằng AI</div>}
                </div>
                <h2 className={styles.sectionTitle}>Giới thiệu chuyên khoa</h2>
                <p className={styles.specialtyDescription}>{specialty.description || 'Khoa phụ trách thăm khám, chẩn đoán và điều trị toàn diện các bệnh lý chuyên khoa với phác đồ y khoa cập nhật nhất và trang thiết bị hiện đại.'}</p>

                {(recommendationsLoading || recommendationsError || recommendations.length > 0) && <section className={styles.recommendationsSection} aria-labelledby="recommended-doctors-title">
                    <h2 id="recommended-doctors-title" className={styles.sectionTitle}>Bác sĩ được gợi ý</h2>
                    <p className={styles.recommendationsExplanation}>Gợi ý dựa trên kinh nghiệm và lịch trống gần nhất của bác sĩ.</p>
                    {recommendationsLoading ? <div className={styles.doctorGrid} role="status" aria-label="Đang tải bác sĩ được gợi ý">
                        {[0, 1, 2].map(item => <div key={item} className={styles.recommendationSkeleton} />)}
                    </div> : recommendationsError ? <InlineError message={recommendationsError} onRetry={() => void fetchRecommendations()} /> : <div className={styles.doctorGrid}>
                        {recommendations.map((doctor, index) => {
                            const knownDoctor = doctors.find(item => item.id === doctor.doctorId);
                            const score = Math.round(doctor.totalScore * 100);
                            const bookingUrl = `/patient/book?specialtyId=${specialty.id}&doctorId=${doctor.doctorId}`
                                + (doctor.earliestAvailableSlot ? `&date=${bookingDate(doctor.earliestAvailableSlot)}` : '');
                            return <article key={doctor.doctorId} className={styles.recommendationCard}>
                                {index === 0 && <Tag className={styles.bestMatchBadge}>Phù hợp nhất</Tag>}
                                <h3 className={styles.doctorName}>{knownDoctor ? formatDoctorName(knownDoctor.academicTitle, doctor.fullName) : doctor.fullName}</h3>
                                <p className={styles.doctorExperience}>{doctor.experienceYears} năm kinh nghiệm</p>
                                <p className={styles.availableSlot}>{doctor.earliestAvailableSlot ? `Lịch trống sớm nhất: ${slotLabel(doctor.earliestAvailableSlot)}` : 'Chưa có lịch trống trong 14 ngày tới'}</p>
                                <div className={styles.scoreSummary}><span>Điểm phù hợp</span><strong>{score}%</strong></div>
                                <div className={styles.scoreBar} role="progressbar" aria-label={`Điểm phù hợp của ${doctor.fullName}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={score}>
                                    {Array.from({ length: 100 }, (_, step) => <span key={step} aria-hidden="true" className={step < score ? styles.scoreFilled : styles.scoreEmpty} />)}
                                </div>
                                <div className={styles.recommendationActions}>
                                    <Link to={bookingUrl} className={styles.bookingLink}><Calendar size={14} /> Đặt lịch</Link>
                                    <Link to={`/doctors/${doctor.doctorId}`} className={styles.profileLink}>Hồ sơ <ChevronRight size={15} /></Link>
                                </div>
                            </article>;
                        })}
                    </div>}
                </section>}

                <h2 className={styles.sectionTitle}>Đội ngũ Bác sĩ phụ trách ({doctors.length})</h2>
                {doctors.length > 0 ? <div className={styles.doctorGrid}>
                    {doctors.map(doc => <article key={doc.id} className={styles.doctorCard}>
                        <div className={styles.doctorAvatar}><User size={36} /></div>
                        <h3 className={styles.doctorName}>{formatDoctorName(doc.academicTitle, doc.fullName)}</h3>
                        <span className={styles.doctorSpecialty}>{specialty.specialtyName}</span>
                        <span className={styles.doctorExperience}>{doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}</span>
                        <div className={styles.doctorActions}>
                            <Link to={`/doctors/${doc.id}`} className={styles.profileLink}>Hồ sơ bác sĩ <ChevronRight size={15} /></Link>
                            <Link to={`/patient/book?specialtyId=${specialty.id}&doctorId=${doc.id}`} className={styles.bookingLink}><Calendar size={14} /> Đặt khám với bác sĩ</Link>
                        </div>
                    </article>)}
                </div> : <div className={styles.doctorsEmpty}>Hiện chưa có danh sách bác sĩ cụ thể cho chuyên khoa này. Bạn vẫn có thể đăng ký đặt lịch tổng quát.</div>}
            </div>
            <aside className={styles.sidebarCard}>
                <h3 className={styles.sidebarTitle}>Đăng ký Thăm khám</h3>
                <p className={styles.sidebarDescription}>Chọn bác sĩ chuyên khoa hoặc đặt hẹn để được tiếp đón theo khung giờ mong muốn, không chờ đợi.</p>
                <Link to={`/patient/book?specialtyId=${specialty.id}`} className={styles.bookingLink}><Calendar size={18} /> Đặt lịch khám ngay</Link>
                {specialty.aiEnabled && <Link to="/patient/ai-consultation" className={styles.aiLink}><Bot size={18} /> Định tuyến triệu chứng AI</Link>}
            </aside>
        </div>
    </div>;
};
