import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Search, Calendar, Bot, Stethoscope, ChevronRight, CheckCircle2, ShieldCheck, Clock, FileText, User } from 'lucide-react';
import styles from './PublicLanding.module.css';
import axiosClient from '../../api/axiosClient';

export const PublicLanding: React.FC = () => {
    const [specialties, setSpecialties] = useState<any[]>([]);
    const [doctors, setDoctors] = useState<any[]>([]);
    const [searchQuery, setSearchQuery] = useState('');
    const [loading, setLoading] = useState(true);
    const [isSearching, setIsSearching] = useState(false);
    const [showDropdown, setShowDropdown] = useState(false);
    
    // Filtered results
    const [filteredSpecialties, setFilteredSpecialties] = useState<any[]>([]);
    const [filteredDoctors, setFilteredDoctors] = useState<any[]>([]);

    useEffect(() => {
        const fetchData = async () => {
            try {
                const [specRes, docRes] = await Promise.all([
                    axiosClient.get<any, any>('/specialties'),
                    axiosClient.get<any, any>('/doctors')
                ]);
                if (specRes.success) setSpecialties(specRes.data || []);
                if (docRes.success) setDoctors(docRes.data || []);
            } catch (error) {
                console.error("Failed to fetch landing data", error);
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

    return (
        <main className={styles.mainContainer}>
            {/* 1. Hero Section */}
            <section className={styles.heroSection}>
                <div className={styles.container}>
                    <div className={styles.heroContent}>
                        <div className={styles.heroText}>
                            <span className={styles.eyebrow}>Phòng khám đa khoa ứng dụng công nghệ</span>
                            <h1>Chăm sóc sức khỏe toàn diện với trợ lý AI</h1>
                            <p>Trải nghiệm dịch vụ y tế hiện đại, cá nhân hóa. Đặt lịch nhanh chóng, theo dõi hồ sơ bệnh án mọi lúc mọi nơi.</p>
                            <div className={styles.heroActions}>
                                <Link to="/patient/book" className={styles.btnPrimary}>
                                    <Calendar size={20} /> Đặt lịch khám
                                </Link>
                                <Link to="/patient/ai-consultation" className={styles.btnSecondary}>
                                    <Bot size={20} /> Tư vấn chọn chuyên khoa
                                </Link>
                            </div>
                            <div className={styles.trustPoints}>
                                <span className={styles.trustPoint}><CheckCircle2 size={16} /> Đặt lịch trực tuyến</span>
                                <span className={styles.trustPoint}><CheckCircle2 size={16} /> Theo dõi lịch hẹn</span>
                                <span className={styles.trustPoint}><CheckCircle2 size={16} /> Bảo mật thông tin</span>
                            </div>
                        </div>
                        <div className={styles.heroImageWrapper}>
                            <div className={styles.imageDecoration}></div>
                            <img 
                                src="https://images.unsplash.com/photo-1579684385127-1ef15d508118?auto=format&fit=crop&q=80&w=800" 
                                alt="Bác sĩ tư vấn thân thiện" 
                                className={styles.heroImage}
                            />
                            <div className={styles.floatingBadge}>
                                <div className={styles.badgeIcon}><Stethoscope size={20} /></div>
                                <div className={styles.badgeText}>
                                    <strong>Đội ngũ chuyên nghiệp</strong>
                                    <span>Tận tâm chăm sóc</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            {/* 2. Search & Quick Actions */}
            <section className={styles.searchSection}>
                <div className={styles.container}>
                    <div className={styles.searchCard}>
                        <form onSubmit={handleSearch} className={styles.searchForm}>
                            <div className={styles.searchInputWrapper}>
                                <Search className={styles.searchIcon} size={20} />
                                <input 
                                    type="text" 
                                    placeholder="Tìm kiếm bác sĩ, chuyên khoa..." 
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

            {/* 3. Featured Specialties */}
            <section id="specialties" className={styles.sectionLight}>
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <h2>Chuyên khoa nổi bật</h2>
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
                                    <p>{spec.description || 'Chăm sóc chuyên sâu với trang thiết bị hiện đại.'}</p>
                                </Link>
                            ))}
                        </div>
                    ) : (
                        <div className={styles.emptyState}>Chưa có dữ liệu chuyên khoa</div>
                    )}
                </div>
            </section>

            {/* 4. AI Suggestion Teaser */}
            <section className={styles.aiTeaserSection}>
                <div className={styles.container}>
                    <div className={styles.aiTeaserCard}>
                        <div className={styles.aiTeaserContent}>
                            <h2>Không chắc chắn cần khám chuyên khoa nào?</h2>
                            <p>Trợ lý AI của chúng tôi có thể giúp bạn phân tích triệu chứng và gợi ý chuyên khoa phù hợp nhất để đặt lịch.</p>
                            <div className={styles.symptomChips}>
                                <span className={styles.chip}>Đau đầu chóng mặt</span>
                                <span className={styles.chip}>Ho kéo dài</span>
                                <span className={styles.chip}>Đau nhức xương khớp</span>
                            </div>
                            <div className={styles.aiWarning}>
                                <AlertTriangleIcon size={16} /> 
                                <span>Lưu ý: AI chỉ hỗ trợ gợi ý chọn chuyên khoa, không thay thế chẩn đoán của bác sĩ.</span>
                            </div>
                            <Link to="/patient/ai-consultation" className={styles.btnAi}>
                                <Bot size={20} /> Bắt đầu trò chuyện với AI
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

            {/* 5. Featured Doctors */}
            <section id="doctors" className={styles.sectionLight}>
                <div className={styles.container}>
                    <div className={styles.sectionHeader}>
                        <h2>Đội ngũ Bác sĩ</h2>
                        <Link to="/patient/book" className={styles.viewAll}>
                            Xem tất cả <ChevronRight size={20} />
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
                                        <p className={styles.doctorSpec}>{doc.specialtyName || 'Đa khoa'}</p>
                                        <p className={styles.doctorExp}>{doc.experienceYears || 5} năm kinh nghiệm</p>
                                        <Link to={`/patient/book?doctorId=${doc.id}`} className={styles.btnOutline}>
                                            Đặt lịch
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

            {/* 6. Booking Process */}
            <section className={styles.processSection}>
                <div className={styles.container}>
                    <div className={styles.sectionHeaderCenter}>
                        <h2>Quy trình khám bệnh dễ dàng</h2>
                        <p>Trải nghiệm dịch vụ y tế liền mạch với 4 bước đơn giản</p>
                    </div>
                    <div className={styles.processSteps}>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>1</div>
                            <div className={styles.stepIcon}><Stethoscope size={24} /></div>
                            <h3>Chọn dịch vụ</h3>
                            <p>Mô tả triệu chứng cho AI hoặc tự chọn chuyên khoa cần khám.</p>
                        </div>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>2</div>
                            <div className={styles.stepIcon}><Calendar size={24} /></div>
                            <h3>Đặt lịch hẹn</h3>
                            <p>Chọn bác sĩ và khung giờ phù hợp với lịch trình của bạn.</p>
                        </div>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>3</div>
                            <div className={styles.stepIcon}><Clock size={24} /></div>
                            <h3>Đến khám</h3>
                            <p>Đến phòng khám theo giờ đã hẹn, không cần chờ đợi lấy số.</p>
                        </div>
                        <div className={styles.stepCard}>
                            <div className={styles.stepNumber}>4</div>
                            <div className={styles.stepIcon}><FileText size={24} /></div>
                            <h3>Nhận kết quả</h3>
                            <p>Theo dõi hồ sơ bệnh án và đơn thuốc trực tiếp trên hệ thống.</p>
                        </div>
                    </div>
                </div>
            </section>

            {/* 7. Why Choose Us */}
            <section className={styles.whySection}>
                <div className={styles.container}>
                    <div className={styles.whyGrid}>
                        <div className={styles.whyImageWrapper}>
                             <img 
                                src="https://images.unsplash.com/photo-1519494026892-80bbd2d6fd0d?auto=format&fit=crop&q=80&w=800" 
                                alt="Cơ sở vật chất phòng khám" 
                                className={styles.whyImage}
                            />
                        </div>
                        <div className={styles.whyContent}>
                            <h2>Tại sao chọn ClinicCare AI?</h2>
                            <ul className={styles.whyList}>
                                <li>
                                    <div className={styles.whyIcon}><Calendar size={20} /></div>
                                    <div>
                                        <h4>Đặt lịch thuận tiện</h4>
                                        <p>Hệ thống đặt lịch trực tuyến 24/7 giúp bạn chủ động thời gian.</p>
                                    </div>
                                </li>
                                <li>
                                    <div className={styles.whyIcon}><FileText size={20} /></div>
                                    <div>
                                        <h4>Quy trình rõ ràng</h4>
                                        <p>Mọi bước từ đặt lịch đến nhận kết quả đều được minh bạch và theo dõi dễ dàng.</p>
                                    </div>
                                </li>
                                <li>
                                    <div className={styles.whyIcon}><ShieldCheck size={20} /></div>
                                    <div>
                                        <h4>Bảo mật và phân quyền</h4>
                                        <p>Dữ liệu cá nhân và hồ sơ y tế được bảo vệ chặt chẽ trên hệ thống.</p>
                                    </div>
                                </li>
                                <li>
                                    <div className={styles.whyIcon}><Bot size={20} /></div>
                                    <div>
                                        <h4>AI hỗ trợ chọn chuyên khoa</h4>
                                        <p>Giúp bạn dễ dàng tìm đúng bác sĩ chuyên môn dựa trên triệu chứng ban đầu.</p>
                                    </div>
                                </li>
                            </ul>
                        </div>
                    </div>
                </div>
            </section>
        </main>
    );
};

const AlertTriangleIcon = ({ size }: { size: number }) => (
    <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/>
        <line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>
    </svg>
);
