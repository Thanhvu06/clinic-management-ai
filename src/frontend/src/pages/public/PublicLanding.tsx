import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { 
    Search, Calendar, Bot, Stethoscope, ChevronRight, CheckCircle2, 
    ShieldCheck, Clock, FileText, User, ChevronDown, ChevronUp, MapPin, 
    Phone, Award, Sparkles, Check 
} from 'lucide-react';
import styles from './PublicLanding.module.css';
import axiosClient from '../../api/axiosClient';
import { AppointmentLookupModal } from '../../components/AppointmentLookupModal';

export const PublicLanding: React.FC = () => {
    const [specialties, setSpecialties] = useState<any[]>([]);
    const [doctors, setDoctors] = useState<any[]>([]);
    const [healthPackages, setHealthPackages] = useState<any[]>([]);
    const [searchQuery, setSearchQuery] = useState('');
    const [loading, setLoading] = useState(true);
    const [isSearching, setIsSearching] = useState(false);
    const [showDropdown, setShowDropdown] = useState(false);
    const [isLookupModalOpen, setIsLookupModalOpen] = useState(false);
    const [openFaq, setOpenFaq] = useState<number | null>(0);
    
    // Filtered results
    const [filteredSpecialties, setFilteredSpecialties] = useState<any[]>([]);
    const [filteredDoctors, setFilteredDoctors] = useState<any[]>([]);

    useEffect(() => {
        const fetchData = async () => {
            try {
                const [specRes, docRes, pkgRes] = await Promise.all([
                    axiosClient.get<any, any>('/specialties'),
                    axiosClient.get<any, any>('/doctors'),
                    axiosClient.get<any, any>('/health-packages').catch(() => ({ success: false, data: [] }))
                ]);
                if (specRes.success) setSpecialties(specRes.data || []);
                if (docRes.success) setDoctors(docRes.data || []);
                if (pkgRes.success && pkgRes.data && pkgRes.data.length > 0) {
                    setHealthPackages(pkgRes.data);
                } else {
                    // Seed fallback
                    setHealthPackages(defaultPackages);
                }
            } catch (error) {
                console.error("Failed to fetch landing data", error);
                setHealthPackages(defaultPackages);
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
            setShowDropdown(true);
        }
    };

    const toggleFaq = (index: number) => {
        setOpenFaq(openFaq === index ? null : index);
    };

    const formatPrice = (price: number) => {
        return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(price);
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
                            
                            {/* 3 CTAs per Section 2 requirement */}
                            <div className={styles.heroActions}>
                                <Link to="/patient/book" className={styles.btnPrimary} title="Đặt lịch khám">
                                    <Calendar size={18} /> Đặt lịch khám
                                </Link>
                                <a href="#specialties" className={styles.btnSecondary} title="Tìm chuyên khoa">
                                    <Stethoscope size={18} /> Tìm chuyên khoa
                                </a>
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

            {/* 2. Stats Counter Bar */}
            <section className={styles.statsSection}>
                <div className={styles.container}>
                    <div className={styles.statsGrid}>
                        <div className={styles.statItem}>
                            <div className={styles.statNumber}>40+</div>
                            <div className={styles.statLabel}>Phòng khám & Điểm phục vụ</div>
                        </div>
                        <div className={styles.statItem}>
                            <div className={styles.statNumber}>150+</div>
                            <div className={styles.statLabel}>Bác sĩ chuyên khoa giàu kinh nghiệm</div>
                        </div>
                        <div className={styles.statItem}>
                            <div className={styles.statNumber}>300.000+</div>
                            <div className={styles.statLabel}>Lượt khám được phục vụ chu đáo</div>
                        </div>
                        <div className={styles.statItem}>
                            <div className={styles.statNumber}>99.4%</div>
                            <div className={styles.statLabel}>Bệnh nhân đánh giá hài lòng</div>
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
                                    placeholder="Tìm kiếm bác sĩ, chuyên khoa khám bệnh..." 
                                    value={searchQuery}
                                    onChange={(e) => setSearchQuery(e.target.value)}
                                    className={styles.searchInput}
                                />
                            </div>
                            <button type="submit" className={styles.btnSearch}>Tìm kiếm</button>
                        </form>
                        
                        {/* Search Dropdown Panel */}
                        {showDropdown && (
                            <div className={styles.searchDropdown}>
                                <div className={styles.dropdownHeader}>
                                    Kết quả tìm kiếm cho "{searchQuery}"
                                    <button className={styles.closeDropdown} onClick={() => setShowDropdown(false)}>&times;</button>
                                </div>
                                
                                {isSearching ? (
                                    <div className={styles.dropdownLoading}>Đang tìm kiếm...</div>
                                ) : (
                                    <div className={styles.dropdownContent}>
                                        {filteredSpecialties.length === 0 && filteredDoctors.length === 0 ? (
                                            <div className={styles.dropdownEmpty}>Không tìm thấy kết quả nào phù hợp.</div>
                                        ) : (
                                            <>
                                                {filteredSpecialties.length > 0 && (
                                                    <div className={styles.dropdownGroup}>
                                                        <h4>Chuyên khoa ({filteredSpecialties.length})</h4>
                                                        <ul>
                                                            {filteredSpecialties.map(s => (
                                                                <li key={s.id}>
                                                                    <Link to={`/patient/book?specialtyId=${s.id}`} onClick={() => setShowDropdown(false)}>
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
                                                                    <Link to={`/patient/book?doctorId=${d.id}`} onClick={() => setShowDropdown(false)}>
                                                                        <User size={16} /> {d.academicTitle ? d.academicTitle + ". " : ""}{d.fullName} <span className={styles.mutedText}>- {d.specialtyName || 'Đa khoa'}</span>
                                                                    </Link>
                                                                </li>
                                                            ))}
                                                        </ul>
                                                    </div>
                                                )}
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
                        <Link to="/patient/book" className={styles.viewAll}>
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
                                <Link to={`/patient/book?specialtyId=${spec.id}`} key={spec.id} className={styles.specialtyCard}>
                                    <div className={styles.specialtyIconWrapper}>
                                        <Stethoscope size={28} />
                                    </div>
                                    <h3>{spec.specialtyName || spec.name}</h3>
                                    <p>{spec.description || 'Chăm sóc sức khỏe chuyên sâu với đội ngũ chuyên gia tận tâm.'}</p>
                                </Link>
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
                        <Link to="/patient/book" className={styles.viewAll}>
                            Đặt khám ngay <ChevronRight size={20} />
                        </Link>
                    </div>

                    <div className={styles.packageGrid}>
                        {healthPackages.map(pkg => (
                            <div key={pkg.id || pkg.code} className={styles.packageCard}>
                                <div>
                                    <span className={styles.packageBadge}>{pkg.code}</span>
                                    <h3 className={styles.packageName}>{pkg.name}</h3>
                                    <div className={styles.packageAudience}>👥 Đối tượng: {pkg.targetAudience}</div>
                                    <p className={styles.packageDesc}>{pkg.description}</p>
                                    
                                    {pkg.includedServices && pkg.includedServices.length > 0 && (
                                        <ul className={styles.packageIncluded}>
                                            {pkg.includedServices.slice(0, 4).map((srv: string, i: number) => (
                                                <li key={i}>
                                                    <Check size={16} color="#0284c7" style={{ flexShrink: 0, marginTop: '2px' }} />
                                                    <span>{srv}</span>
                                                </li>
                                            ))}
                                            {pkg.includedServices.length > 4 && (
                                                <li style={{ color: '#0284c7', fontWeight: 600, fontSize: '0.8rem' }}>
                                                    + {pkg.includedServices.length - 4} xét nghiệm và dịch vụ khác
                                                </li>
                                            )}
                                        </ul>
                                    )}
                                </div>

                                <div className={styles.packageFooter}>
                                    <div>
                                        <span style={{ fontSize: '0.75rem', color: '#64748b', display: 'block' }}>Chi phí trọn gói</span>
                                        <div className={styles.packagePrice}>{formatPrice(pkg.price)}</div>
                                    </div>
                                    <Link to="/patient/book" className={styles.btnPrimary} style={{ padding: '8px 16px', fontSize: '0.875rem' }}>
                                        Đặt gói khám
                                    </Link>
                                </div>
                            </div>
                        ))}
                    </div>
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
                        <Link to="/patient/book" className={styles.viewAll}>
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
                                        <Link to={`/patient/book?doctorId=${doc.id}`} className={styles.btnOutline}>
                                            Đặt lịch hẹn
                                        </Link>
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
                    </div>

                    <div className={styles.locationsGrid}>
                        {clinicLocations.map((loc, idx) => (
                            <div key={idx} className={styles.locationCard}>
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
                                    <span>{loc.hours}</span>
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
                            <p>Hệ thống tự động xác nhận và thông báo lịch hẹn qua tin nhắn.</p>
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

// Seed packages fallback data
const defaultPackages = [
    {
        code: "PKG01",
        name: "Gói Khám Sức Khỏe Tổng Quát Tiêu Chuẩn",
        targetAudience: "Mọi độ tuổi từ 18 trở lên",
        description: "Kiểm tra toàn diện các chỉ số huyết học, chức năng gan, thận, đường huyết, mỡ máu, X-quang phổi và siêu âm bụng tổng quát.",
        price: 1250000,
        includedServices: ["Khám nội tổng quát", "Công thức máu 18 chỉ số", "Đo đường huyết Glucose", "Men gan AST/ALT", "Chức năng thận Ure/Creatinine", "X-quang tim phổi thẳng", "Siêu âm bụng tổng quát"]
    },
    {
        code: "PKG02",
        name: "Gói Tầm Soát Tim Mạch Toàn Diện",
        targetAudience: "Người trưởng thành, trung niên và có tiền sử tim mạch",
        description: "Tầm soát chuyên sâu bệnh lý mạch vành, huyết áp, rối loạn nhịp tim và xơ vữa động mạch.",
        price: 2800000,
        includedServices: ["Khám chuyên khoa Tim mạch", "Điện tâm đồ ECG 12 chuyển đạo", "Siêu âm tim Doppler màu", "Bộ mỡ máu toàn phần (Cholesterol, Triglyceride, HDL, LDL)", "Đo chỉ số xơ vữa ABI", "Tư vấn chế độ dinh dưỡng"]
    },
    {
        code: "PKG03",
        name: "Gói Chăm Sóc Sức Khỏe Nhi Khoa Toàn Diện",
        targetAudience: "Trẻ em từ 0 - 15 tuổi",
        description: "Đánh giá phát triển thể chất, dinh dưỡng, tầm soát thiếu máu, vi chất và kiểm tra tai mũi họng tổng quát.",
        price: 950000,
        includedServices: ["Khám chuyên khoa Nhi", "Đánh giá chỉ số phát triển chiều cao - cân nặng", "Tổng phân tích tế bào máu", "Kiểm tra vi chất kẽm, canxi, sắt", "Nội soi tai mũi họng", "Tư vấn tiêm chủng"]
    },
    {
        code: "PKG04",
        name: "Gói Tầm Soát Sức Khỏe Phụ Nữ Chuyên Sâu",
        targetAudience: "Nữ giới từ 18 tuổi trở lên",
        description: "Tầm soát bệnh lý phụ khoa, ung thư cổ tử cung, tầm soát tuyến vú và các rối loạn nội tiết.",
        price: 1950000,
        includedServices: ["Khám Sản phụ khoa chuyên sâu", "Soi tươi dịch âm đạo", "Siêu âm đầu dò tử cung buồng trứng", "Siêu âm tuyến vú 2 bên", "Xét nghiệm Pap smear tầm soát sớm", "Định lượng hormon nội tiết"]
    },
    {
        code: "PKG05",
        name: "Gói Khám Cơ Xương Khớp & Loãng Xương",
        targetAudience: "Người cao tuổi, nhân viên văn phòng, người vận động thể thao",
        description: "Tầm soát thoái hóa khớp, thoát vị đĩa đệm, viêm khớp dạng thấp và đo mật độ xương toàn thân.",
        price: 1650000,
        includedServices: ["Khám chuyên khoa Cơ xương khớp", "Đo mật độ xương DEXA", "X-quang khớp gối / cột sống thắt lưng", "Xét nghiệm Axit Uric (gút)", "Định lượng Canxi và Vitamin D3"]
    },
    {
        code: "PKG06",
        name: "Gói Tầm Soát Gan Mật & Rối Loạn Chuyển Hóa",
        targetAudience: "Người có nguy cơ gan nhiễm mỡ, viêm gan, đái tháo đường",
        description: "Đánh giá chức năng gan mật, tầm soát virus viêm gan B/C, men gan và các chỉ số rối loạn chuyển hóa.",
        price: 1750000,
        includedServices: ["Khám chuyên khoa Nội", "Xét nghiệm HBsAg, Anti-HCV", "Men gan toàn diện AST, ALT, GGT", "Siêu âm Doppler gan mật tụy lách", "Chỉ số đường huyết HbA1c"]
    }
];

// Clinic Locations seed data
const clinicLocations = [
    {
        name: "Cơ sở Trung tâm Quận 1",
        address: "45 Lê Duẩn, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
        hours: "07:00 - 19:00 (Hàng ngày)",
        phone: "028 3822 1111"
    },
    {
        name: "Cơ sở Đa khoa Quận 5",
        address: "215 Hồng Bàng, Phường 11, Quận 5, TP. Hồ Chí Minh",
        hours: "07:00 - 19:00 (Hàng ngày)",
        phone: "028 3855 2222"
    },
    {
        name: "Cơ sở Nam Sài Gòn Quận 7",
        address: "123 Nguyễn Văn Linh, Phường Tân Phong, Quận 7, TP. Hồ Chí Minh",
        hours: "07:00 - 19:00 (Hàng ngày)",
        phone: "028 3776 3333"
    },
    {
        name: "Cơ sở TP. Thủ Đức",
        address: "56 Võ Văn Ngân, Phường Linh Chiểu, TP. Thủ Đức, TP. Hồ Chí Minh",
        hours: "07:00 - 19:00 (Hàng ngày)",
        phone: "028 3722 4444"
    },
    {
        name: "Cơ sở Hà Nội - Đống Đa",
        address: "178 Thái Hà, Phường Trung Liệt, Quận Đống Đa, Hà Nội",
        hours: "07:00 - 19:00 (Hàng ngày)",
        phone: "024 3857 5555"
    },
    {
        name: "Cơ sở Đà Nẵng",
        address: "92 Quang Trung, Phường Thạch Thang, Quận Hải Châu, TP. Đà Nẵng",
        hours: "07:00 - 19:00 (Hàng ngày)",
        phone: "0236 388 6666"
    }
];

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
        question: "Tôi có thể đặt lịch cho người thân không?",
        answer: "Có, quý khách có thể đặt lịch hẹn và ghi rõ thông tin người khám trong phần mô tả triệu chứng hoặc liên hệ trực tiếp hotline 1900 1234 để được hỗ trợ đăng ký nhanh."
    }
];
