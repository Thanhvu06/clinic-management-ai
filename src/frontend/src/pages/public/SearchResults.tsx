import React, { useState, useEffect, useMemo } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { Search, Stethoscope, User, Package, ChevronRight, Calendar, CalendarCheck } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { formatVndCurrency } from '../../utils/formatters';
import styles from './PublicPages.module.css';

export const SearchResults: React.FC = () => {
    const [searchParams, setSearchParams] = useSearchParams();
    const query = searchParams.get('q') || '';
    const [inputValue, setInputValue] = useState(query);
    const [activeTab, setActiveTab] = useState<'all' | 'specialties' | 'doctors' | 'packages'>('all');

    const [specialties, setSpecialties] = useState<any[]>([]);
    const [doctors, setDoctors] = useState<any[]>([]);
    const [packages, setPackages] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);

    useEffect(() => {
        setInputValue(query);
    }, [query]);

    useEffect(() => {
        const fetchAllData = async () => {
            try {
                setLoading(true);
                const [specRes, docRes, pkgRes] = await Promise.all([
                    axiosClient.get<any, any>('/specialties'),
                    axiosClient.get<any, any>('/doctors'),
                    axiosClient.get<any, any>('/health-packages').catch(() => ({ success: false, data: [] }))
                ]);

                if (specRes.success) setSpecialties(specRes.data || []);
                if (docRes.success) setDoctors(docRes.data || []);
                if (pkgRes.success) setPackages(pkgRes.data || []);
            } catch (err) {
                console.error("Failed to load search index data", err);
            } finally {
                setLoading(false);
            }
        };

        fetchAllData();
    }, []);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setSearchParams({ q: inputValue.trim() });
    };

    const normalizedQuery = query.toLowerCase().trim();

    const matchedSpecialties = useMemo(() => {
        if (!normalizedQuery) return specialties;
        return specialties.filter(s =>
            (s.specialtyName || s.name)?.toLowerCase().includes(normalizedQuery) ||
            s.description?.toLowerCase().includes(normalizedQuery)
        );
    }, [specialties, normalizedQuery]);

    const matchedDoctors = useMemo(() => {
        if (!normalizedQuery) return doctors;
        return doctors.filter(d =>
            d.fullName?.toLowerCase().includes(normalizedQuery) ||
            d.academicTitle?.toLowerCase().includes(normalizedQuery) ||
            d.specialtyName?.toLowerCase().includes(normalizedQuery)
        );
    }, [doctors, normalizedQuery]);

    const matchedPackages = useMemo(() => {
        if (!normalizedQuery) return packages;
        return packages.filter(p =>
            p.name?.toLowerCase().includes(normalizedQuery) ||
            p.code?.toLowerCase().includes(normalizedQuery) ||
            p.targetAudience?.toLowerCase().includes(normalizedQuery) ||
            p.description?.toLowerCase().includes(normalizedQuery)
        );
    }, [packages, normalizedQuery]);

    const totalMatches = matchedSpecialties.length + matchedDoctors.length + matchedPackages.length;

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Tìm kiếm</span>
                </nav>
                <h1 className={styles.pageTitle}>
                    {query ? `Kết quả tìm kiếm cho "${query}"` : 'Tìm kiếm dịch vụ y tế'}
                </h1>
                <p className={styles.pageSubtitle}>
                    Tra cứu nhanh chóng thông tin chuyên khoa, bác sĩ điều trị và các gói khám sức khỏe tổng quát.
                </p>
            </div>

            {/* Search Bar */}
            <div className={styles.toolbar}>
                <form onSubmit={handleSearchSubmit} className={styles.searchBox} style={{ width: '100%' }}>
                    <Search className={styles.searchBoxIcon} size={20} />
                    <input
                        type="text"
                        placeholder="Tìm theo tên bác sĩ, chuyên khoa, triệu chứng hoặc gói khám..."
                        value={inputValue}
                        onChange={(e) => setInputValue(e.target.value)}
                        aria-label="Từ khóa tìm kiếm"
                    />
                </form>
            </div>

            {/* Category Tabs */}
            <div style={{ display: 'flex', gap: '10px', flexWrap: 'wrap', marginBottom: '28px', borderBottom: '1px solid var(--c-border)', paddingBottom: '12px' }}>
                {[
                    { id: 'all', label: `Tất cả (${totalMatches})` },
                    { id: 'specialties', label: `Chuyên khoa (${matchedSpecialties.length})` },
                    { id: 'doctors', label: `Bác sĩ (${matchedDoctors.length})` },
                    { id: 'packages', label: `Gói khám (${matchedPackages.length})` }
                ].map(tab => (
                    <button
                        key={tab.id}
                        type="button"
                        onClick={() => setActiveTab(tab.id as any)}
                        style={{
                            padding: '8px 16px',
                            borderRadius: '8px',
                            border: 'none',
                            background: activeTab === tab.id ? 'var(--c-primary)' : 'transparent',
                            color: activeTab === tab.id ? '#ffffff' : 'var(--c-text)',
                            fontWeight: 600,
                            fontSize: '0.9rem',
                            cursor: 'pointer',
                            transition: 'all 0.2s'
                        }}
                    >
                        {tab.label}
                    </button>
                ))}
            </div>

            {loading ? (
                <div className={styles.grid3}>
                    {[1, 2, 3, 4, 5, 6].map(i => (
                        <div key={i} className={styles.skeletonCard} />
                    ))}
                </div>
            ) : totalMatches === 0 ? (
                <div className={styles.emptyState}>
                    <Search size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>Không tìm thấy kết quả phù hợp</div>
                    <p>Hãy thử tìm với các từ khóa ngắn hơn, tên chuyên khoa (Nội khoa, Tim mạch...) hoặc liên hệ trợ lý AI.</p>
                    <Link 
                        to="/patient/ai-consultation"
                        className="btn btn-secondary"
                        style={{ display: 'inline-flex', marginTop: '16px', textDecoration: 'none' }}
                    >
                        Nhờ Trợ lý AI định tuyến triệu chứng
                    </Link>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '40px' }}>
                    {/* Specialties Section */}
                    {(activeTab === 'all' || activeTab === 'specialties') && matchedSpecialties.length > 0 && (
                        <div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                                <h2 style={{ fontSize: '1.3rem', fontWeight: 700, color: 'var(--c-navy)', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Stethoscope size={20} color="var(--c-primary)" /> Chuyên khoa ({matchedSpecialties.length})
                                </h2>
                                <Link to="/specialties" className={styles.cardActionLink}>
                                    Tất cả chuyên khoa <ChevronRight size={16} />
                                </Link>
                            </div>
                            <div className={styles.grid3}>
                                {matchedSpecialties.map(spec => (
                                    <div key={spec.id} className={styles.card}>
                                        <h3 className={styles.cardTitle}>{spec.specialtyName || spec.name}</h3>
                                        <p className={styles.cardDesc}>{spec.description || 'Chuyên khoa mũi nhọn tại ClinicCare.'}</p>
                                        <div className={styles.cardFooter}>
                                            <Link to={`/specialties/${spec.id}`} className={styles.cardActionLink}>
                                                Xem chi tiết <ChevronRight size={16} />
                                            </Link>
                                            <Link to={`/patient/book?specialtyId=${spec.id}`} className={styles.cardActionLink} style={{ color: 'var(--c-secondary)' }}>
                                                <Calendar size={14} /> Đặt khám
                                            </Link>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {/* Doctors Section */}
                    {(activeTab === 'all' || activeTab === 'doctors') && matchedDoctors.length > 0 && (
                        <div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                                <h2 style={{ fontSize: '1.3rem', fontWeight: 700, color: 'var(--c-navy)', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <User size={20} color="var(--c-primary)" /> Bác sĩ chuyên khoa ({matchedDoctors.length})
                                </h2>
                                <Link to="/doctors" className={styles.cardActionLink}>
                                    Tất cả bác sĩ <ChevronRight size={16} />
                                </Link>
                            </div>
                            <div className={styles.grid4}>
                                {matchedDoctors.map(doc => (
                                    <div key={doc.id} className={styles.doctorCard}>
                                        <div className={styles.doctorAvatar}>
                                            <User size={36} />
                                        </div>
                                        <h3 className={styles.doctorName}>
                                            {doc.academicTitle ? `${doc.academicTitle}. ` : ''}{doc.fullName}
                                        </h3>
                                        <span className={styles.doctorSpecialty}>
                                            {doc.specialtyName || 'Bác sĩ Đa khoa'}
                                        </span>
                                        <div className={styles.doctorActions}>
                                            <Link to={`/doctors/${doc.id}`} className={styles.cardActionLink} style={{ justifyContent: 'center' }}>
                                                Xem hồ sơ <ChevronRight size={15} />
                                            </Link>
                                            <Link 
                                                to={`/patient/book?doctorId=${doc.id}${doc.specialtyId ? `&specialtyId=${doc.specialtyId}` : ''}`}
                                                className="btn btn-primary btn-sm"
                                                style={{ textDecoration: 'none', justifyContent: 'center' }}
                                            >
                                                <Calendar size={14} /> Đặt lịch
                                            </Link>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {/* Packages Section */}
                    {(activeTab === 'all' || activeTab === 'packages') && matchedPackages.length > 0 && (
                        <div>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px' }}>
                                <h2 style={{ fontSize: '1.3rem', fontWeight: 700, color: 'var(--c-navy)', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <Package size={20} color="var(--c-primary)" /> Gói khám sức khỏe ({matchedPackages.length})
                                </h2>
                                <Link to="/health-packages" className={styles.cardActionLink}>
                                    Tất cả gói khám <ChevronRight size={16} />
                                </Link>
                            </div>
                            <div className={styles.grid3}>
                                {matchedPackages.map(pkg => (
                                    <div key={pkg.id} className={styles.packageCard}>
                                        <span className={styles.packageBadge}>{pkg.code}</span>
                                        <h3 className={styles.packageName}>{pkg.name}</h3>
                                        <p className={styles.packageDesc}>{pkg.description}</p>
                                        <div className={styles.packagePriceRow}>
                                            <div className={styles.packagePrice}>{formatVndCurrency(pkg.price)}</div>
                                            <div style={{ display: 'flex', gap: '8px' }}>
                                                <Link to={`/health-packages/${pkg.id}`} className="btn btn-secondary btn-sm" style={{ textDecoration: 'none' }}>
                                                    Chi tiết
                                                </Link>
                                                <Link to={`/patient/health-packages/${pkg.id}/register`} className="btn btn-primary btn-sm" style={{ textDecoration: 'none' }}>
                                                    <CalendarCheck size={14} /> Đăng ký
                                                </Link>
                                            </div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
};
