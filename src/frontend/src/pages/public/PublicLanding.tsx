import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { 
    Search, Calendar, Bot, Stethoscope, ChevronRight, CheckCircle2, 
    ShieldCheck, Clock, FileText, User, ChevronDown, ChevronUp, MapPin, 
    Phone, Award, Sparkles, Check, CalendarCheck, Package 
} from 'lucide-react';
import styles from './PublicLanding.module.css';
import axiosClient from '../../api/axiosClient';
import { AppointmentLookupModal } from '../../components/AppointmentLookupModal';
import type { ClinicLocationDto } from '../../types';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';

export const PublicLanding: React.FC = () => {
    const navigate = useNavigate();
    const [specialties, setSpecialties] = useState<any[]>([]);
    const [doctors, setDoctors] = useState<any[]>([]);
    const [healthPackages, setHealthPackages] = useState<any[]>([]);
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [searchQuery, setSearchQuery] = useState('');
    const [loading, setLoading] = useState(true);
    const [packagesError, setPackagesError] = useState(false);
    const [isSearching, setIsSearching] = useState(false);
    const [showDropdown, setShowDropdown] = useState(false);
    const [isLookupModalOpen, setIsLookupModalOpen] = useState(false);
    const [openFaq, setOpenFaq] = useState<number | null>(0);
    
    // Filtered results for quick search dropdown
    const [filteredSpecialties, setFilteredSpecialties] = useState<any[]>([]);
    const [filteredDoctors, setFilteredDoctors] = useState<any[]>([]);

    useEffect(() => {
        const fetchData = async () => {
            try {
                const [specRes, docRes, pkgRes, locRes] = await Promise.all([
                    axiosClient.get<any, any>('/specialties'),
                    axiosClient.get<any, any>('/doctors'),
                    axiosClient.get<any, any>('/health-packages').catch(() => ({ success: false, data: [] })),
                    axiosClient.get<any, any>('/locations').catch(() => ({ success: false, data: [] }))
                ]);
                if (specRes.success) setSpecialties(specRes.data || []);
                if (docRes.success) setDoctors(docRes.data || []);
                if (pkgRes.success && pkgRes.data) {
                    setHealthPackages(pkgRes.data);
                } else {
                    setPackagesError(true);
                }
                if (locRes.success && locRes.data) {
                    setLocations(locRes.data);
                }
            } catch (error) {
                console.error("Failed to fetch landing data", error);
                setPackagesError(true);
            } finally {
                setLoading(false);
            }
        };
        fetchData();
    }, []);

    useEffect(() => {
        const delayDebounceFn = setTimeout(() => {
            if (searchQuery.trim()) {
                setIsSearching(true);
                const query = searchQuery.toLowerCase();
                setFilteredSpecialties(specialties.filter(s => (s.specialtyName || s.name)?.toLowerCase().includes(query)));
                setFilteredDoctors(doctors.filter(d => (d.fullName)?.toLowerCase().includes(query)));
                setShowDropdown(true);
                setIsSearching(false);
            } else {
                setShowDropdown(false);
            }
        }, 300);
        return () => clearTimeout(delayDebounceFn);
    }, [searchQuery, specialties, doctors]);

    const handleSearch = (e: React.FormEvent) => {
        e.preventDefault();
        if (searchQuery.trim()) {
            navigate(`/search?q=${encodeURIComponent(searchQuery.trim())}`);
        }
    };

    const toggleFaq = (index: number) => {
        setOpenFaq(openFaq === index ? null : index);
    };

    return (
        <main className={styles.mainContainer}>
            <AppointmentLookupModal isOpen={isLookupModalOpen} onClose={() => setIsLookupModalOpen(false)} />

            {/* 1. Hero Section */}
            <section className={styles.heroSection}>
                <div className={styles.container}>
                    <div className={styles.heroContent}>
                        <div className={styles.heroText}>
                            <span className={styles.eyebrow}>Hệ thống Y tế Kỹ thuật số ClinicCare</span>
                            <h1>Chăm sóc sức khỏe thông minh với Trợ lý AI</h1>
                            <p>
                                Đặt khám chuyên khoa dễ dàng, định tuyến triệu chứng chuẩn xác bằng Trí tuệ Nhân tạo và quản lý hồ sơ bệnh án trực tuyến 24/7.
                            </p>
                            
                            <div className={styles.heroActions}>
                                <Link to="/patient/book" className={styles.btnPrimary} title="Đặt lịch khám">
                                    <Calendar size={18} /> Đặt lịch khám
                                </Link>
                                <Link to="/specialties" className={styles.btnSecondary} title="Xem danh mục chuyên khoa">
                                    <Stethoscope size={18} /> Tìm chuyên khoa
                                </Link>
                                <button 
                                    type="button" 
                                    onClick={() => setIsLookupModalOpen(true)} 
                                    className={styles.btnSecondary}
                                    style={{ background: '#f0f9ff', borderColor: '#0284c7', color: '#0284c7' }}
                                    title="Tra cứu lịch hẹn"
                                >
                                    <Search size={18} /> Tra cứu lịch hẹn
                                </button>
                            </div>

                            <div className={styles.trustPoints}>
                                <span className={styles.trustPoint}><CheckCircle2 size={16} /> Đặt lịch không chờ đợi</span>
                                <span className={styles.trustPoint}><CheckCircle2 size={16} /> Bác sĩ chuyên khoa đầu ngành</span>
                                <span className={styles.trustPoint}><CheckCircle2 size={16} /> AI phân luồng bảo mật</span>
                            </div>
                        </div>

                        <div className={styles.heroImageWrapper}>
                            <div className={styles.imageDecoration}></div>
                            <img 
                                src="https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&q=80&w=800" 
                                alt="Bác sĩ chuyên khoa ClinicCare" 
                                className={styles.heroImage}
                            />
                            <div className={styles.floatingBadge}>
                                <div className={styles.badgeIcon}><Sparkles size={20} /></div>
                                <div className={styles.badgeText}>
                                    <strong>Trợ lý AI ClinicCare</strong>
                                    <span>Tư vấn định tuyến 24/7</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            {/* 2. Benefit Pillars Bar (Replaces Fake Stats) */}
            <section className={styles.statsSection}>
                <div className={styles.container}>
                    <div className={styles.statsGrid}>
                        <div className={styles.statItem} style={{ padding: '0 16px' }}>
                            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '8px' }}>
                                <CalendarCheck size={28} color="var(--c-primary)" />
                            </div>
                            <div style={{ fontWeight: 700, fontSize: '1.05rem', color: 'var(--c-navy)' }}>Đặt lịch trực tuyến 24/7</div>
                            <div className={styles.statLabel}>Chủ động chọn giờ khám, tiếp đón ưu tiên không chờ đợi</div>
                        </div>
                        <div className={styles.statItem} style={{ padding: '0 16px' }}>
                            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '8px' }}>
                                <Sparkles size={28} color="var(--c-primary)" />
                            </div>
                            <div style={{ fontWeight: 700, fontSize: '1.05rem', color: 'var(--c-navy)' }}>Định tuyến AI an toàn</div>
                            <div className={styles.statLabel}>Gợi ý chuyên khoa phù hợp theo triệu chứng bất thường</div>
                        </div>
                        <div className={styles.statItem} style={{ padding: '0 16px' }}>
                            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '8px' }}>
                                <Stethoscope size={28} color="var(--c-primary)" />
                            </div>
                            <div style={{ fontWeight: 700, fontSize: '1.05rem', color: 'var(--c-navy)' }}>Bác sĩ giàu kinh nghiệm</div>
                            <div className={styles.statLabel}>Chuyên gia y tế tận tâm, đào tạo chính quy trong & ngoài nước</div>
                        </div>
                        <div className={styles.statItem} style={{ padding: '0 16px' }}>
                            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '8px' }}>
                                <ShieldCheck size={28} color="var(--c-primary)" />
                            </div>
                            <div style={{ fontWeight: 700, fontSize: '1.05rem', color: 'var(--c-navy)' }}>Minh bạch chi phí & hồ sơ</div>
                            <div className={styles.statLabel}>Quản lý lịch hẹn, đơn thuốc và hồ sơ bệnh án trực tuyến</div>
                        </div>
                    </div>
                </div>
            </section>

            {/* 3. Search Section */}
            <section className={styles.searchSection}>
                <div className={styles.container}>
                    <div className={styles.searchCard}>
                        <form onSubmit={handleSearch} className={styles.searchForm}>
                            <div className={styles.searchInputWrapper}>
                                <Search className={styles.searchIcon} size={20} />
                                <input 
                                    type="text" 
                                    placeholder="Tìm kiếm bác sĩ, chuyên khoa khám bệnh, gói khám..." 
                                    value={searchQuery}
                                    onChange={(e) => setSearchQuery(e.target.value)}
                                    className={styles.searchInput}
                                    aria-label="Tìm kiếm dịch vụ y tế"
                                />
                            </div>
                            <button type="submit" className={styles.btnSearch}>Tìm kiếm</button>
                        </form>
                        
                        {/* Search Dropdown Panel */}
                        {showDropdown && (
                            <div className={styles.searchDropdown}>
                                <div className={styles.dropdownHeader}>
                                    Gợi ý tìm kiếm cho "{searchQuery}"
                                    <button className={styles.closeDropdown} onClick={() => setShowDropdown(false)}>&times;</button>
                                </div>
                                
                                {isSearching ? (
                                    <div className={styles.dropdownLoading}>Đang tìm kiếm...</div>
                                ) : (
                                    <div className={styles.dropdownContent}>
                                        {filteredSpecialties.length === 0 && filteredDoctors.length === 0 ? (
                                            <div className={styles.dropdownEmpty}>
                                                Không tìm thấy kết quả nhanh. Nhấn "Tìm kiếm" để xem toàn bộ kết quả.
                                            </div>
                                        ) : (
                                            <>
                                                {filteredSpecialties.length > 0 && (
                                                    <div className={styles.dropdownGroup}>
                                                        <h4>Chuyên khoa ({filteredSpecialties.length})</h4>
                                                        <ul>
                                                            {filteredSpecialties.map(s => (
                                                                <li key={s.id}>
                                                                    <Link to={`/specialties/${s.id}`} onClick={() => setShowDropdown(false)}>
                                                                        <Stethoscope size={16} /> {s.specialtyName || s.name}
                                                                    </Link>
                                                                </li>
                                                            ))}
                                                        </ul>
                                                    </div>
                                                )}
                                                
                                                {filteredDoctors.length > 0 && (
                                                    <div className={styles.dropdownGroup}>
                                                        <h4>Bác sĩ ({filteredDoctors.length})</h4>
                                                        <ul>
                                                            {filteredDoctors.map(d => (
                                                                <li key={d.id}>
                                                                    <Link to={`/doctors/${d.id}`} onClick={() => setShowDropdown(false)}>
                                                                        <User size={16} /> {d.academicTitle ? d.academicTitle + ". " : ""}{d.fullName} <span className={styles.mutedText}>- {d.specialtyName || 'Đa khoa'}</span>
                                                                    </Link>
                                                                </li>
                                                            ))}
                                                        </ul>
                                                    </div>
                                                )}

                                                <div style={{ padding: '10px 16px', borderTop: '1px solid #f1f5f9', textAlign: 'center' }}>
                                                    <Link 
                                                        to={`/search?q=${encodeURIComponent(searchQuery)}`}
                                                        style={{ fontSize: '0.875rem', color: 'var(--c-primary)', fontWeight: 600, textDecoration: 'none' }}
                                                        onClick={() => setShowDropdown(false)}
                                                    >
                                                        Xem toàn bộ kết quả tìm kiếm &rarr;
                                                    </Link>
                                                </div>
                                            </>
                                        )}
                                    </div>
                                )}
                            </div>
                        )}
                    </div>
                </div>
            </section>

            {/* 4. Featured Specialties */}
            <section id="specialties" className={styles.sectionLight}>
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <div>
                            <h2>Danh mục Chuyên khoa</h2>
                            <p style={{ margin: '4px 0 0', color: '#64748b' }}>Đa dạng chuyên khoa y tế phục vụ khám chữa bệnh toàn diện</p>
                        </div>
                        <Link to="/specialties" className={styles.viewAll}>
                            Xem tất cả <ChevronRight size={20} />
                        </Link>
                    </div>
                    
                    {loading ? (
                        <div className={styles.grid}>
                            {[1, 2, 3, 4, 5, 6].map(i => (
                                <div key={i} className={styles.skeletonCard}></div>
                            ))}
                        </div>
                    ) : specialties.length > 0 ? (
                        <div className={styles.grid}>
                            {specialties.slice(0, 8).map(spec => (
                                <div key={spec.id} className={styles.specialtyCard}>
                                    <div className={styles.specialtyIconWrapper}>
                                        <Stethoscope size={28} />
                                    </div>
                                    <h3>{spec.specialtyName || spec.name}</h3>
                                    <p>{spec.description || 'Chăm sóc sức khỏe chuyên sâu với đội ngũ chuyên gia tận tâm.'}</p>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '16px', paddingTop: '12px', borderTop: '1px solid #f1f5f9' }}>
                                        <Link to={`/specialties/${spec.id}`} style={{ fontSize: '0.875rem', color: 'var(--c-primary)', fontWeight: 600, textDecoration: 'none', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                            Xem chi tiết <ChevronRight size={14} />
                                        </Link>
                                        <Link to={`/patient/book?specialtyId=${spec.id}`} style={{ fontSize: '0.875rem', color: 'var(--c-secondary)', fontWeight: 600, textDecoration: 'none', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                            <Calendar size={14} /> Đặt lịch
                                        </Link>
                                    </div>
                                </div>
                            ))}
                        </div>
                    ) : (
                        <div className={styles.emptyState}>Chưa có dữ liệu chuyên khoa</div>
                    )}
                </div>
            </section>

            {/* 5. Health Care Packages (ClinicCare Branded) */}
            <section id="packages" style={{ padding: '70px 0', backgroundColor: '#ffffff' }}>
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <div>
                            <h2>Gói Chăm Sóc Sức Khỏe ClinicCare</h2>
                            <p style={{ margin: '4px 0 0', color: '#64748b' }}>Thiết kế khoa học, tiết kiệm chi phí và tầm soát toàn diện từng đối tượng</p>
                        </div>
                        <Link to="/health-packages" className={styles.viewAll}>
                            Xem tất cả gói khám <ChevronRight size={20} />
                        </Link>
                    </div>

                    {loading ? (
                        <div className={styles.packageGrid}>
                            {[1, 2, 3].map(i => (
                                <div key={i} className={styles.skeletonCard} style={{ minHeight: '260px' }}></div>
                            ))}
                        </div>
                    ) : healthPackages.length > 0 ? (
                        <div className={styles.packageGrid}>
                            {healthPackages.slice(0, 6).map(pkg => {
                                const services = parseIncludedServices(pkg.includedServicesJson || pkg.includedServices);
                                return (
                                    <div key={pkg.id || pkg.code} className={styles.packageCard}>
                                        <div>
                                            <span className={styles.packageBadge}>{pkg.code}</span>
                                            <h3 className={styles.packageName}>{pkg.name}</h3>
                                            <div className={styles.packageAudience}>👥 Đối tượng: {pkg.targetAudience}</div>
                                            <p className={styles.packageDesc}>{pkg.description}</p>
                                            
                                            {services.length > 0 && (
                                                <ul className={styles.packageIncluded}>
                                                    {services.slice(0, 4).map((srv: string, i: number) => (
                                                        <li key={i}>
                                                            <Check size={16} color="#0284c7" style={{ flexShrink: 0, marginTop: '2px' }} />
                                                            <span>{srv}</span>
                                                        </li>
                                                    ))}
                                                    {services.length > 4 && (
                                                        <li style={{ color: '#0284c7', fontWeight: 600, fontSize: '0.8rem' }}>
                                                            + {services.length - 4} xét nghiệm và dịch vụ khác
                                                        </li>
                                                    )}
                                                </ul>
                                            )}
                                        </div>

                                        <div className={styles.packageFooter}>
                                            <div>
                                                <span style={{ fontSize: '0.75rem', color: '#64748b', display: 'block' }}>Chi phí trọn gói</span>
                                                <div className={styles.packagePrice}>{formatVndCurrency(pkg.price)}</div>
                                            </div>
                                            <div style={{ display: 'flex', gap: '8px' }}>
                                                <Link to={`/health-packages/${pkg.id}`} className={styles.btnSecondary} style={{ padding: '8px 14px', fontSize: '0.85rem' }}>
                                                    Chi tiết
                                                </Link>
                                                <Link to={`/patient/health-packages/${pkg.id}/register`} className={styles.btnPrimary} style={{ padding: '8px 14px', fontSize: '0.85rem' }}>
                                                    Đăng ký
                                                </Link>
                                            </div>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    ) : (
                        <div className={styles.emptyState}>
                            <Package size={40} style={{ opacity: 0.3, marginBottom: '8px' }} />
                            <div>{packagesError ? 'Không thể tải danh sách gói khám. Vui lòng thử lại sau hoặc liên hệ hotline 1900 1234 để được tư vấn.' : 'Danh sách gói khám đang được cập nhật. Quý khách vui lòng liên hệ hotline 1900 1234 để được tư vấn.'}</div>
                        </div>
                    )}
                </div>
            </section>

            {/* 6. AI Consultation Teaser */}
            <section className={styles.aiTeaserSection}>
                <div className={styles.container}>
                    <div className={styles.aiTeaserCard}>
                        <div className={styles.aiTeaserContent}>
                            <h2>Bạn chưa rõ nên khám chuyên khoa nào?</h2>
                            <p>Trợ lý AI của ClinicCare giúp bạn phân tích sơ bộ các biểu hiện bất thường để gợi ý chuyên khoa phù hợp và tiết kiệm thời gian nhất.</p>
                            <div className={styles.symptomChips}>
                                <span className={styles.chip}>Đau tức ngực, hồi hộp</span>
                                <span className={styles.chip}>Đau đầu, chóng mặt kéo dài</span>
                                <span className={styles.chip}>Ho sốt, đau rát họng</span>
                                <span className={styles.chip}>Đau nhức khớp gối, thắt lưng</span>
                            </div>
                            <div className={styles.aiWarning}>
                                <ShieldCheck size={18} /> 
                                <span>Lưu ý an toàn: Trợ lý AI chỉ mang tính định tuyến tham khảo, không chẩn đoán hay kê đơn thuốc.</span>
                            </div>
                            <Link to="/patient/ai-consultation" className={styles.btnAi}>
                                <Bot size={20} /> Trò chuyện với Trợ lý AI
                            </Link>
                        </div>
                        <div className={styles.aiTeaserVisual}>
                            <div className={styles.aiCircle}>
                                <Bot size={64} className={styles.aiIconLg} />
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            {/* 7. Doctor Team */}
            <section id="doctors" className={styles.sectionLight}>
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <div>
                            <h2>Đội ngũ Bác sĩ Chuyên khoa</h2>
                            <p style={{ margin: '4px 0 0', color: '#64748b' }}>Bác sĩ giỏi chuyên môn, giàu y đức và giàu kinh nghiệm điều trị</p>
                        </div>
                        <Link to="/doctors" className={styles.viewAll}>
                            Xem danh sách <ChevronRight size={20} />
                        </Link>
                    </div>

                    {loading ? (
                        <div className={styles.grid4}>
                            {[1, 2, 3, 4].map(i => (
                                <div key={i} className={styles.skeletonDoctorCard}></div>
                            ))}
                        </div>
                    ) : doctors.length > 0 ? (
                        <div className={styles.grid4}>
                            {doctors.slice(0, 4).map(doc => (
                                <div key={doc.id} className={styles.doctorCard}>
                                    <div className={styles.doctorAvatar}>
                                        <User size={40} className={styles.placeholderAvatar} />
                                    </div>
                                    <div className={styles.doctorInfo}>
                                        <h3>{doc.academicTitle ? `${doc.academicTitle}. ` : ''}{doc.fullName}</h3>
                                        <p className={styles.doctorSpec}>{doc.specialtyName || 'Bác sĩ Đa khoa'}</p>
                                        <p className={styles.doctorExp}>{doc.experienceYears || 5} năm kinh nghiệm</p>
                                        <div style={{ display: 'flex', gap: '8px', marginTop: '12px' }}>
                                            <Link to={`/doctors/${doc.id}`} className={styles.btnOutline} style={{ flex: 1, padding: '8px 10px', fontSize: '0.85rem' }}>
                                                Hồ sơ
                                            </Link>
                                            <Link to={`/patient/book?doctorId=${doc.id}`} className={styles.btnPrimary} style={{ flex: 1, padding: '8px 10px', fontSize: '0.85rem' }}>
                                                Đặt khám
                                            </Link>
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    ) : (
                        <div className={styles.emptyState}>Chưa có dữ liệu bác sĩ</div>
                    )}
                </div>
            </section>

            {/* 8. Clinic Locations (Điểm khám) */}
            <section id="locations" style={{ padding: '70px 0', backgroundColor: '#ffffff' }}>
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <div>
                            <h2>Hệ thống Điểm khám ClinicCare</h2>
                            <p style={{ margin: '4px 0 0', color: '#64748b' }}>Mạng lưới phòng khám rộng khắp, cơ sở vật chất khang trang, hiện đại</p>
                        </div>
                        <Link to="/locations" className={styles.viewAll}>
                            Xem tất cả cơ sở <ChevronRight size={20} />
                        </Link>
                    </div>

                    <div className={styles.locationsGrid}>
                        {locations.slice(0, 3).map((loc) => (
                            <div key={loc.id} className={styles.locationCard}>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                                    <Award size={18} color="#0284c7" />
                                    <h4 className={styles.locationName}>{loc.name}</h4>
                                </div>
                                <div className={styles.locationDetail}>
                                    <MapPin size={16} color="#64748b" style={{ flexShrink: 0, marginTop: '2px' }} />
                                    <span>{loc.address}</span>
                                </div>
                                <div className={styles.locationDetail}>
                                    <Clock size={16} color="#64748b" style={{ flexShrink: 0 }} />
                                    <span>{loc.openingHours}</span>
                                </div>
                                <div className={styles.locationDetail}>
                                    <Phone size={16} color="#64748b" style={{ flexShrink: 0 }} />
                                    <span>{loc.phone}</span>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            </section>

            {/* 9. 4-Step Booking Process */}
            <section className={styles.processSection}>
                <div className={styles.container}>
                    <div className={styles.sectionHeaderCenter}>
                        <h2>Quy trình đặt lịch 4 bước đơn giản</h2>
                        <p>Tiết kiệm thời gian, tối ưu trải nghiệm khám chữa bệnh</p>
                    </div>
                    <div className={styles.processSteps}>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>1</div>
                            <div className={styles.stepIcon}><Stethoscope size={24} /></div>
                            <h3>Chọn chuyên khoa</h3>
                            <p>Tự chọn chuyên khoa mong muốn hoặc nhờ AI phân tích triệu chứng.</p>
                        </div>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>2</div>
                            <div className={styles.stepIcon}><Calendar size={24} /></div>
                            <h3>Chọn bác sĩ & Giờ khám</h3>
                            <p>Chọn khung giờ 30 phút thuận tiện với lịch trình của bạn.</p>
                        </div>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>3</div>
                            <div className={styles.stepIcon}><Clock size={24} /></div>
                            <h3>Nhận mã hẹn tức thì</h3>
                            <p>Hệ thống tự động xác nhận và lưu trữ trong tài khoản của bạn.</p>
                        </div>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>4</div>
                            <div className={styles.stepIcon}><FileText size={24} /></div>
                            <h3>Khám & Nhận đơn thuốc</h3>
                            <p>Đến khám đúng giờ và tra cứu kết quả, đơn thuốc trực tuyến.</p>
                        </div>
                    </div>
                </div>
            </section>

            {/* 10. FAQ Section */}
            <section style={{ padding: '70px 0', backgroundColor: '#f8fafc' }}>
                <div className={styles.container}>
                    <div className={styles.sectionHeaderCenter}>
                        <h2>Câu hỏi thường gặp</h2>
                        <p>Giải đáp thắc mắc phổ biến về quy trình khám bệnh tại ClinicCare</p>
                    </div>

                    <div className={styles.faqContainer}>
                        {faqItems.map((item, idx) => (
                            <div key={idx} className={styles.faqItem}>
                                <button 
                                    type="button" 
                                    onClick={() => toggleFaq(idx)} 
                                    className={styles.faqQuestion}
                                >
                                    <span>{item.question}</span>
                                    {openFaq === idx ? <ChevronUp size={20} color="#0284c7" /> : <ChevronDown size={20} color="#64748b" />}
                                </button>
                                {openFaq === idx && (
                                    <div className={styles.faqAnswer}>
                                        {item.answer}
                                    </div>
                                )}
                            </div>
                        ))}
                    </div>
                </div>
            </section>
        </main>
    );
};

// FAQ items
const faqItems = [
    {
        question: "Tôi cần chuẩn bị gì khi đến khám theo lịch hẹn đã đặt?",
        answer: "Quý khách chỉ cần đến trước giờ hẹn 10-15 phút, xuất trình mã lịch hẹn (APT-...) hoặc số điện thoại tại quầy lễ tân để được tiếp đón ngay mà không cần bốc số chờ đợi."
    },
    {
        question: "Trợ lý AI của ClinicCare hoạt động như thế nào và có an toàn không?",
        answer: "Trợ lý AI phân tích các mô tả triệu chứng bạn cung cấp để định hướng chuyên khoa phù hợp nhất dựa trên danh mục thực tế của phòng khám. Hệ thống tuyệt đối không chẩn đoán thay bác sĩ, không kê đơn và không thu thập thông tin định danh cá nhân nhạy cảm."
    },
    {
        question: "Tôi có thể đổi hoặc hủy lịch hẹn đã đặt được không?",
        answer: "Quý khách hoàn toàn có thể gửi yêu cầu dời lịch sang khung giờ khác hoặc hủy lịch trực tiếp trong mục 'Lịch hẹn của tôi'. Bộ phận lễ tân sẽ xét duyệt và phản hồi nhanh chóng."
    },
    {
        question: "Khung thời gian cho mỗi ca khám là bao lâu?",
        answer: "Mỗi slot khám bệnh tiêu chuẩn tại ClinicCare kéo dài 30 phút, đảm bảo bác sĩ có đủ thời gian thăm khám kỹ lưỡng, lắng nghe và tư vấn chi tiết cho từng bệnh nhân."
    },
    {
        question: "Làm thế nào để tôi xem lại đơn thuốc và kết quả ca khám?",
        answer: "Sau khi bác sĩ hoàn tất ca khám, kết quả tóm tắt và đơn thuốc điện tử sẽ được cập nhật ngay trên tài khoản của bạn tại mục 'Đơn thuốc của tôi'. Bạn có thể xem lại hoặc in ra bất kỳ lúc nào."
    },
    {
        question: "Tôi cần chuẩn bị gì khi đến khám gói sức khỏe?",
        answer: "Quý khách nên nhịn ăn ít nhất 6-8 tiếng trước khi lấy mẫu máu xét nghiệm, uống đủ nước lọc và mang theo các kết quả khám, đơn thuốc đang dùng nếu có."
    }
];
