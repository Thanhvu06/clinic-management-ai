import { ChatConsultationLink } from '../../components/chat/ChatConsultationLink';
import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Button, Collapse, Input } from 'antd';
import { Search } from 'lucide-react';
import styles from './PublicLanding.module.css';
import axiosClient from '../../api/axiosClient';
import { AppointmentLookupModal } from '../../components/AppointmentLookupModal';
import { InlineError } from '../../components/common/InlineError';
import { LoadingState } from '../../components/common/LoadingState';
import type { ClinicLocationDto } from '../../types';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';
import { formatDoctorName } from '../../utils/doctorNameHelper';
import { getApiErrorMessage } from '../../utils/apiError';

const doctorInitials = (fullName: string = '') => {
    const name = fullName.replace(/^(?:(?:PGS|GS|ThS|TS|BS|CKII|CKI)\.?\s*)+/i, '').trim();
    return name.split(/\s+/).filter(Boolean).slice(-2).map(word => word[0]).join('').toLocaleUpperCase('vi-VN');
};

const listOf = (res: any) => (res?.success && Array.isArray(res.data) ? res.data : []);
const matches = (value: unknown, query: string) => typeof value === 'string' && value.toLowerCase().includes(query);

export const PublicLanding: React.FC = () => {
    const navigate = useNavigate();
    const [specialties, setSpecialties] = useState<any[]>([]);
    const [doctors, setDoctors] = useState<any[]>([]);
    const [healthPackages, setHealthPackages] = useState<any[]>([]);
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [searchQuery, setSearchQuery] = useState('');
    const [debouncedQuery, setDebouncedQuery] = useState('');
    const [isLookupModalOpen, setIsLookupModalOpen] = useState(false);
    const [openFaq, setOpenFaq] = useState<string | undefined>('0');

    const [reloadKey, setReloadKey] = useState(0);

    useEffect(() => {
        const fetchData = async () => {
            try {
                const [specRes, docRes, pkgRes, locRes] = await Promise.all([
                    axiosClient.get<any, any>('/specialties'),
                    axiosClient.get<any, any>('/doctors'),
                    axiosClient.get<any, any>('/health-packages').catch(() => null),
                    axiosClient.get<any, any>('/locations').catch(() => null)
                ]);
                setError(null);
                setSpecialties(listOf(specRes));
                setDoctors(listOf(docRes));
                setHealthPackages(listOf(pkgRes));
                setLocations(listOf(locRes));
            } catch (err) {
                setError(getApiErrorMessage(err, 'Không tải được thông tin phòng khám.'));
            } finally {
                setLoading(false);
            }
        };
        void fetchData();
    }, [reloadKey]);

    useEffect(() => {
        const timer = setTimeout(() => setDebouncedQuery(searchQuery.trim().toLowerCase()), 300);
        return () => clearTimeout(timer);
    }, [searchQuery]);

    const suggestedSpecialties = debouncedQuery ? specialties.filter(s => matches(s.specialtyName || s.name, debouncedQuery)).slice(0, 5) : [];
    const suggestedDoctors = debouncedQuery ? doctors.filter(d => matches(d.fullName, debouncedQuery)).slice(0, 5) : [];
    const suggestedPackages = debouncedQuery ? healthPackages.filter(p => matches(p.name, debouncedQuery)).slice(0, 5) : [];
    const showSuggestions = debouncedQuery.length > 0 && debouncedQuery === searchQuery.trim().toLowerCase();
    const hasSuggestions = suggestedSpecialties.length + suggestedDoctors.length + suggestedPackages.length > 0;

    const handleSearch = (e: React.FormEvent) => {
        e.preventDefault();
        if (searchQuery.trim()) {
            navigate(`/search?q=${encodeURIComponent(searchQuery.trim())}`);
        }
    };

    return (
        <main className={styles.mainContainer}>
            <AppointmentLookupModal isOpen={isLookupModalOpen} onClose={() => setIsLookupModalOpen(false)} />

            <section className={styles.hero} aria-labelledby="landing-title">
                <div className={styles.container}>
                    <h1 id="landing-title" className={styles.heroTitle}>Đặt lịch khám tại ClinicCare</h1>
                    <div className={styles.searchArea}>
                        <form onSubmit={handleSearch} className={styles.searchForm} role="search">
                            <Input
                                size="large"
                                prefix={<Search size={18} aria-hidden="true" />}
                                placeholder="Tìm bác sĩ, chuyên khoa, gói khám"
                                value={searchQuery}
                                onChange={e => setSearchQuery(e.target.value)}
                                onKeyDown={e => { if (e.key === 'Escape') setSearchQuery(''); }}
                                aria-label="Tìm bác sĩ, chuyên khoa, gói khám"
                                className={styles.searchInput}
                            />
                            <Button type="primary" size="large" htmlType="submit">Tìm kiếm</Button>
                        </form>

                        {showSuggestions && (
                            <div className={styles.suggestions} role="region" aria-label="Gợi ý tìm kiếm">
                                {!hasSuggestions ? (
                                    <p className={styles.suggestionEmpty}>Không có gợi ý nhanh. Bấm Tìm kiếm để xem tất cả kết quả.</p>
                                ) : (
                                    <>
                                        {suggestedSpecialties.length > 0 && (
                                            <div className={styles.suggestionGroup}>
                                                <h2>Chuyên khoa</h2>
                                                {suggestedSpecialties.map(s => <Link key={s.id} to={`/specialties/${s.id}`}>{s.specialtyName || s.name}</Link>)}
                                            </div>
                                        )}
                                        {suggestedDoctors.length > 0 && (
                                            <div className={styles.suggestionGroup}>
                                                <h2>Bác sĩ</h2>
                                                {suggestedDoctors.map(d => (
                                                    <Link key={d.id} to={`/doctors/${d.id}`}>
                                                        {formatDoctorName(d.academicTitle, d.fullName)}
                                                        {d.specialtyName && <span className={styles.muted}> · {d.specialtyName}</span>}
                                                    </Link>
                                                ))}
                                            </div>
                                        )}
                                        {suggestedPackages.length > 0 && (
                                            <div className={styles.suggestionGroup}>
                                                <h2>Gói khám</h2>
                                                {suggestedPackages.map(p => <Link key={p.id} to={`/health-packages/${p.id}`}>{p.name}</Link>)}
                                            </div>
                                        )}
                                    </>
                                )}
                                <Link to={`/search?q=${encodeURIComponent(searchQuery.trim())}`} className={styles.suggestionAll}>Xem tất cả kết quả</Link>
                            </div>
                        )}
                    </div>

                    <nav className={styles.quickLinks} aria-label="Lối tắt">
                        <Link to="/patient/book">Đặt lịch khám</Link>
                        <button type="button" onClick={() => setIsLookupModalOpen(true)}>Tra cứu lịch hẹn</button>
                        <Link to="/medicines">Bảng giá thuốc</Link>
                        <ChatConsultationLink to="/patient/ai-consultation">Hỏi trợ lý AI</ChatConsultationLink>
                    </nav>
                </div>
            </section>

            {loading ? (
                <div className={styles.container}><LoadingState message="Đang tải thông tin phòng khám..." /></div>
            ) : error ? (
                <div className={styles.container}><InlineError message={error} onRetry={() => { setLoading(true); setError(null); setReloadKey(key => key + 1); }} /></div>
            ) : (
                <>
                    {specialties.length > 0 && (
                        <section id="specialties" className={styles.section} aria-labelledby="landing-specialties-title">
                            <div className={styles.container}>
                                <div className={styles.sectionHeader}>
                                    <h2 id="landing-specialties-title">Chuyên khoa</h2>
                                    <Link to="/specialties">Tất cả chuyên khoa</Link>
                                </div>
                                <div className={styles.specialtyGrid}>
                                    {specialties.slice(0, 12).map(spec => (
                                        <Link key={spec.id} to={`/specialties/${spec.id}`} className={styles.specialtyItem}>
                                            <span className={styles.specialtyName}>{spec.specialtyName || spec.name}</span>
                                            {spec.description && <span className={styles.specialtyDesc}>{spec.description}</span>}
                                        </Link>
                                    ))}
                                </div>
                                <p className={styles.aiLine}>
                                    Chưa biết nên khám khoa nào? <ChatConsultationLink to="/patient/ai-consultation">Hỏi trợ lý AI</ChatConsultationLink> để được gợi ý chuyên khoa theo triệu chứng.
                                </p>
                            </div>
                        </section>
                    )}

                    {doctors.length > 0 && (
                        <section id="doctors" className={styles.section} aria-labelledby="landing-doctors-title">
                            <div className={styles.container}>
                                <div className={styles.sectionHeader}>
                                    <h2 id="landing-doctors-title">Bác sĩ</h2>
                                    <Link to="/doctors">Tất cả bác sĩ</Link>
                                </div>
                                <div className={styles.cardGrid}>
                                    {doctors.slice(0, 8).map(doc => (
                                        <article key={doc.id} className={styles.card}>
                                            <div className={styles.doctorHeading}>
                                                <span className={styles.doctorAvatar} aria-hidden="true">{doctorInitials(doc.fullName)}</span>
                                                <div>
                                                    <h3>{formatDoctorName(doc.academicTitle, doc.fullName)}</h3>
                                                    {doc.specialtyName && <p className={styles.muted}>{doc.specialtyName}</p>}
                                                </div>
                                            </div>
                                            <p className={styles.muted}>{doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}</p>
                                            <div className={styles.cardActions}>
                                                <Link to={`/patient/book?doctorId=${doc.id}`}>Đặt khám</Link>
                                                <Link to={`/doctors/${doc.id}`}>Hồ sơ</Link>
                                            </div>
                                        </article>
                                    ))}
                                </div>
                            </div>
                        </section>
                    )}

                    {healthPackages.length > 0 && (
                        <section id="packages" className={styles.section} aria-labelledby="landing-packages-title">
                            <div className={styles.container}>
                                <div className={styles.sectionHeader}>
                                    <h2 id="landing-packages-title">Gói khám</h2>
                                    <Link to="/health-packages">Tất cả gói khám</Link>
                                </div>
                                <div className={styles.cardGrid}>
                                    {healthPackages.slice(0, 6).map(pkg => {
                                        const serviceCount = parseIncludedServices(pkg.includedServicesJson || pkg.includedServices).length;
                                        return (
                                            <article key={pkg.id || pkg.code} className={styles.card}>
                                                <h3>{pkg.name}</h3>
                                                {pkg.targetAudience && <p className={styles.muted}>Dành cho: {pkg.targetAudience}</p>}
                                                {serviceCount > 0 && <p className={styles.muted}>Gồm {serviceCount} dịch vụ</p>}
                                                <div className={styles.price}>{formatVndCurrency(pkg.price)}</div>
                                                <div className={styles.cardActions}>
                                                    <Link to={`/patient/health-packages/${pkg.id}/register`}>Đăng ký</Link>
                                                    <Link to={`/health-packages/${pkg.id}`}>Chi tiết</Link>
                                                </div>
                                            </article>
                                        );
                                    })}
                                </div>
                            </div>
                        </section>
                    )}

                    {locations.length > 0 && (
                        <section id="locations" className={styles.section} aria-labelledby="landing-locations-title">
                            <div className={styles.container}>
                                <div className={styles.sectionHeader}>
                                    <h2 id="landing-locations-title">Cơ sở khám</h2>
                                    <Link to="/locations">Tất cả cơ sở</Link>
                                </div>
                                <div className={styles.locationGrid}>
                                    {locations.slice(0, 4).map(loc => (
                                        <article key={loc.id} className={styles.card}>
                                            <h3>{loc.name}</h3>
                                            <p>{loc.address}</p>
                                            {loc.openingHours && <p className={styles.muted}>Giờ làm việc: {loc.openingHours}</p>}
                                            {loc.phone && <p className={styles.muted}>Điện thoại: <a href={`tel:${loc.phone.replace(/\s+/g, '')}`}>{loc.phone}</a></p>}
                                        </article>
                                    ))}
                                </div>
                            </div>
                        </section>
                    )}
                </>
            )}

            <section className={styles.section} aria-labelledby="landing-faq-title">
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <h2 id="landing-faq-title">Câu hỏi thường gặp</h2>
                    </div>
                    <div className={styles.faqContainer}>
                        <Collapse accordion collapsible="icon" activeKey={openFaq ? [openFaq] : []}
                            onChange={keys => setOpenFaq(Array.isArray(keys) ? keys[0] : keys)}
                            items={faqItems.map((item, idx) => ({
                                key: String(idx),
                                label: <Button type="text" className={styles.faqQuestion}
                                    aria-expanded={openFaq === String(idx)}
                                    onClick={() => setOpenFaq(openFaq === String(idx) ? undefined : String(idx))}>
                                    {item.question}
                                </Button>,
                                children: <p className={styles.faqAnswer}>{item.answer}</p>
                            }))} />
                    </div>
                </div>
            </section>
        </main>
    );
};

const faqItems = [
    {
        question: 'Tôi cần chuẩn bị gì khi đến khám theo lịch hẹn?',
        answer: 'Bạn đến trước giờ hẹn 10–15 phút và đọc mã lịch hẹn (APT-...) hoặc số điện thoại tại quầy lễ tân để làm thủ tục.'
    },
    {
        question: 'Trợ lý AI dùng để làm gì?',
        answer: 'Trợ lý AI đọc mô tả triệu chứng của bạn và gợi ý chuyên khoa có trong danh mục của phòng khám. Trợ lý không chẩn đoán và không kê đơn; bác sĩ là người khám và kết luận.'
    },
    {
        question: 'Tôi có thể đổi hoặc hủy lịch hẹn không?',
        answer: 'Có. Vào mục Lịch hẹn của tôi để gửi yêu cầu đổi giờ hoặc hủy lịch. Lễ tân sẽ duyệt yêu cầu và báo lại cho bạn.'
    },
    {
        question: 'Mỗi lượt khám kéo dài bao lâu?',
        answer: 'Mỗi khung giờ đặt lịch dài 30 phút.'
    },
    {
        question: 'Tôi xem đơn thuốc và kết quả khám ở đâu?',
        answer: 'Sau khi khám xong, đơn thuốc nằm ở mục Đơn thuốc của tôi, kết quả xét nghiệm nằm ở mục Kết quả cận lâm sàng. Bạn có thể in đơn thuốc từ trang này.'
    },
    {
        question: 'Khám gói sức khỏe cần chuẩn bị gì?',
        answer: 'Nhịn ăn 6–8 tiếng trước khi lấy máu xét nghiệm, chỉ uống nước lọc. Mang theo kết quả khám và đơn thuốc đang dùng nếu có.'
    }
];
