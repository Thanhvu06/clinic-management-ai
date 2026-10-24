import { Link } from 'react-router-dom';
import { Tag } from 'antd';
import { InlineError } from '../../../components/common/InlineError';
import { formatDoctorName } from '../../../utils/doctorNameHelper';
import { bookingDate, slotLabel } from './specialtyTypes';
import type { useSpecialtyDetail } from './useSpecialtyDetail';
import styles from '../SpecialtyDetail.module.css';

export function SpecialtyDoctors({ data }: { data: ReturnType<typeof useSpecialtyDetail> }) {
    const { specialty, doctors, recommendations, recommendationsLoading, recommendationsError, fetchRecommendations } = data;
    if (!specialty) return null;
    return <>
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
                                    <Link to={bookingUrl} className={styles.bookingLink}>Đặt lịch</Link>
                                    <Link to={`/doctors/${doctor.doctorId}`} className={styles.profileLink}>Hồ sơ</Link>
                                </div>
                            </article>;
                        })}
                    </div>}
                </section>}

                <h2 className={styles.sectionTitle}>Bác sĩ của khoa ({doctors.length})</h2>
                {doctors.length > 0 ? <div className={styles.doctorGrid}>
                    {doctors.map(doc => <article key={doc.id} className={styles.doctorCard}>
                        <h3 className={styles.doctorName}>{formatDoctorName(doc.academicTitle, doc.fullName)}</h3>
                        <span className={styles.doctorSpecialty}>{specialty.specialtyName}</span>
                        <span className={styles.doctorExperience}>{doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}</span>
                        <div className={styles.doctorActions}>
                            <Link to={`/patient/book?specialtyId=${specialty.id}&doctorId=${doc.id}`} className={styles.bookingLink}>Đặt khám với bác sĩ</Link>
                            <Link to={`/doctors/${doc.id}`} className={styles.profileLink}>Hồ sơ bác sĩ</Link>
                        </div>
                    </article>)}
                </div> : <div className={styles.doctorsEmpty}>Khoa chưa có bác sĩ nhận lịch trực tuyến. Bạn vẫn có thể đặt lịch theo chuyên khoa.</div>}
    </>;
}
