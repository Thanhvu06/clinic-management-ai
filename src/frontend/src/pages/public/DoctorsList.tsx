import React, { useState, useEffect, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { User, Search, ChevronRight, Calendar } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import styles from './PublicPages.module.css';

interface Doctor {
    id: number;
    fullName: string;
    academicTitle: string;
    experienceYears: number;
    specialtyId?: number;
    specialtyName?: string;
    description?: string;
}

interface Specialty {
    id: number;
    specialtyName: string;
}

export const DoctorsList: React.FC = () => {
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedSpecialty, setSelectedSpecialty] = useState<string>('all');
    const [sortBy, setSortBy] = useState<'name' | 'exp'>('exp');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchData = async () => {
            try {
                setLoading(true);
                const [docRes, specRes] = await Promise.all([
                    axiosClient.get<any, any>('/doctors'),
                    axiosClient.get<any, any>('/specialties')
                ]);

                if (docRes.success) {
                    setDoctors(docRes.data || []);
                } else {
                    setError('Không thể tải danh sách bác sĩ.');
                }

                if (specRes.success) {
                    setSpecialties(specRes.data || []);
                }
            } catch (err: any) {
                setError(err.response?.data?.message || 'Có lỗi xảy ra khi tải dữ liệu.');
            } finally {
                setLoading(false);
            }
        };

        fetchData();
    }, []);

    const filteredDoctors = useMemo(() => {
        return doctors
            .filter(d => {
                const matchesSearch = 
                    d.fullName.toLowerCase().includes(searchTerm.toLowerCase()) ||
                    (d.academicTitle && d.academicTitle.toLowerCase().includes(searchTerm.toLowerCase())) ||
                    (d.specialtyName && d.specialtyName.toLowerCase().includes(searchTerm.toLowerCase()));
                
                const matchesSpecialty = 
                    selectedSpecialty === 'all' || 
                    d.specialtyId?.toString() === selectedSpecialty ||
                    d.specialtyName === selectedSpecialty;

                return matchesSearch && matchesSpecialty;
            })
            .sort((a, b) => {
                if (sortBy === 'exp') {
                    return b.experienceYears - a.experienceYears;
                }
                return a.fullName.localeCompare(b.fullName, 'vi');
            });
    }, [doctors, searchTerm, selectedSpecialty, sortBy]);

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Breadcrumb">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Đội ngũ Bác sĩ</span>
                </nav>
                <h1 className={styles.pageTitle}>Đội ngũ Bác sĩ Chuyên khoa ClinicCare</h1>
                <p className={styles.pageSubtitle}>
                    Các chuyên gia y tế giàu y đức, được đào tạo bài bản trong nước và quốc tế, tận tâm vì sức khỏe toàn diện của người bệnh.
                </p>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.filterGroup}>
                    <div className={styles.searchBox}>
                        <Search className={styles.searchBoxIcon} size={18} />
                        <input
                            type="text"
                            placeholder="Tìm bác sĩ theo tên hoặc học hàm..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            aria-label="Tìm bác sĩ"
                        />
                    </div>
                    
                    <select
                        className={styles.selectFilter}
                        value={selectedSpecialty}
                        onChange={(e) => setSelectedSpecialty(e.target.value)}
                        aria-label="Lọc theo chuyên khoa"
                    >
                        <option value="all">Tất cả chuyên khoa</option>
                        {specialties.map(s => (
                            <option key={s.id} value={s.id.toString()}>{s.specialtyName}</option>
                        ))}
                    </select>

                    <select
                        className={styles.selectFilter}
                        value={sortBy}
                        onChange={(e) => setSortBy(e.target.value as 'name' | 'exp')}
                        aria-label="Sắp xếp bác sĩ"
                    >
                        <option value="exp">Kinh nghiệm nhiều nhất</option>
                        <option value="name">Tên A-Z</option>
                    </select>
                </div>
                
                <div className={styles.resultCount}>
                    Hiển thị {filteredDoctors.length} bác sĩ
                </div>
            </div>

            {error && (
                <div style={{ padding: '16px 20px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', marginBottom: '24px' }}>
                    {error}
                </div>
            )}

            {loading ? (
                <div className={styles.grid4}>
                    {[1, 2, 3, 4, 5, 6, 7, 8].map(i => (
                        <div key={i} className={styles.skeletonCard} />
                    ))}
                </div>
            ) : filteredDoctors.length > 0 ? (
                <div className={styles.grid4}>
                    {filteredDoctors.map(doc => (
                        <div key={doc.id} className={styles.doctorCard}>
                            <div className={styles.doctorAvatar}>
                                <User size={44} />
                            </div>
                            <h2 className={styles.doctorName}>
                                {doc.academicTitle ? `${doc.academicTitle}. ` : ''}{doc.fullName}
                            </h2>
                            <span className={styles.doctorSpecialty}>
                                {doc.specialtyName || 'Bác sĩ Đa khoa'}
                            </span>
                            <span className={styles.doctorExp}>
                                {doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Bác sĩ chuyên khoa'}
                            </span>
                            
                            <p style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)', lineClamp: 2, overflow: 'hidden', textOverflow: 'ellipsis', margin: '0 0 16px 0', minHeight: '38px' }}>
                                {doc.description || 'Chuyên gia thăm khám và điều trị chuyên sâu, tận tâm đồng hành cùng người bệnh.'}
                            </p>

                            <div className={styles.doctorActions}>
                                <Link 
                                    to={`/doctors/${doc.id}`} 
                                    className={styles.cardActionLink}
                                    style={{ justifyContent: 'center' }}
                                >
                                    Xem lý lịch & hồ sơ <ChevronRight size={15} />
                                </Link>
                                <Link 
                                    to={`/patient/book?doctorId=${doc.id}${doc.specialtyId ? `&specialtyId=${doc.specialtyId}` : ''}`}
                                    className="btn btn-primary btn-sm"
                                    style={{ textDecoration: 'none', justifyContent: 'center' }}
                                >
                                    <Calendar size={14} /> Đặt lịch khám
                                </Link>
                            </div>
                        </div>
                    ))}
                </div>
            ) : (
                <div className={styles.emptyState}>
                    <User size={48} style={{ opacity: 0.3, marginBottom: '12px' }} />
                    <div className={styles.emptyStateTitle}>Không tìm thấy bác sĩ phù hợp</div>
                    <p>Vui lòng thử tìm kiếm với từ khóa hoặc chuyên khoa khác.</p>
                </div>
            )}
        </div>
    );
};
