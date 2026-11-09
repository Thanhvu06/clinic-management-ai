import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Search } from 'lucide-react';
import { Input, Select } from 'antd';
import axiosClient from '../../api/axiosClient';
import styles from './PublicPages.module.css';
import { LinkButton } from '../../components/common/LinkButton';
import { formatDoctorName } from '../../utils/doctorNameHelper';
import { InlineError } from '../../components/common/InlineError';

interface Doctor {
    id: number;
    fullName: string;
    academicTitle?: string;
    experienceYears: number;
    specialtyId?: number;
    specialtyName?: string;
    description?: string;
    specialties?: Specialty[];
}

interface Specialty {
    id: number;
    specialtyName: string;
}

const doctorSpecialties = (doctor: Doctor): Specialty[] => doctor.specialties ??
    (doctor.specialtyId && doctor.specialtyName ? [{ id: doctor.specialtyId, specialtyName: doctor.specialtyName }] : []);

export const DoctorsList: React.FC = () => {
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedSpecialty, setSelectedSpecialty] = useState<string>('all');
    const [sortBy, setSortBy] = useState<'name' | 'exp'>('exp');
    const [error, setError] = useState<string | null>(null);
    const [reloadKey, setReloadKey] = useState(0);

    useEffect(() => {
        const fetchData = async () => {
            try {
                setLoading(true);
                setError(null);
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
                } else {
                    setError('Không thể tải danh sách chuyên khoa.');
                }
            } catch (err: any) {
                setError(getApiErrorMessage(err, 'Có lỗi xảy ra khi tải dữ liệu.'));
            } finally {
                setLoading(false);
            }
        };

        fetchData();
    }, [reloadKey]);

    const getDoctorInitials = (name?: string) => {
        if (!name) return 'BS';
        const parts = name.trim().split(/\s+/);
        if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
        return (parts[parts.length - 2][0] + parts[parts.length - 1][0]).toUpperCase();
    };

    const filteredDoctors = useMemo(() => {
        return doctors
            .filter(d => {
                const matchesSearch = 
                    d.fullName.toLowerCase().includes(searchTerm.toLowerCase()) ||
                    (d.academicTitle && d.academicTitle.toLowerCase().includes(searchTerm.toLowerCase())) ||
                    doctorSpecialties(d).some(s => s.specialtyName.toLowerCase().includes(searchTerm.toLowerCase()));
                
                const matchesSpecialty = 
                    selectedSpecialty === 'all' || 
                    d.specialtyId?.toString() === selectedSpecialty ||
                    d.specialtyName === selectedSpecialty ||
                    doctorSpecialties(d).some(s => s.id.toString() === selectedSpecialty);

                return matchesSearch && matchesSpecialty;
            })
            .sort((a, b) => {
                if (sortBy === 'exp') {
                    return (b.experienceYears || 0) - (a.experienceYears || 0);
                }
                return a.fullName.localeCompare(b.fullName, 'vi');
            });
    }, [doctors, searchTerm, selectedSpecialty, sortBy]);

    return (
        <div className={styles.pageContainer}>
            <div className={styles.pageHeader}>
                <nav className={styles.breadcrumb} aria-label="Đường dẫn trang">
                    <Link to="/">Trang chủ</Link>
                    <span>/</span>
                    <span aria-current="page">Bác sĩ</span>
                </nav>
                <h1 className={styles.pageTitle}>Đội ngũ bác sĩ</h1>
            </div>

            <div className={styles.toolbar}>
                <div className={styles.filterGroup}>
                    <div className={styles.searchBox}>
                        <Search className={styles.searchBoxIcon} size={18} />
                        <Input
                            type="text"
                            placeholder="Tìm bác sĩ theo tên hoặc học hàm..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            aria-label="Tìm bác sĩ"
                        />
                    </div>
                    
                    <Select
                        className={styles.selectFilter}
                        value={selectedSpecialty}
                        onChange={setSelectedSpecialty}
                        aria-label="Lọc theo chuyên khoa"
                        options={[{ value: 'all', label: 'Tất cả chuyên khoa' },
                            ...specialties.map(s => ({ value: s.id.toString(), label: s.specialtyName }))]}
                    />

                    <Select
                        className={styles.selectFilter}
                        value={sortBy}
                        onChange={setSortBy}
                        aria-label="Sắp xếp bác sĩ"
                        options={[{ value: 'exp', label: 'Kinh nghiệm nhiều nhất' }, { value: 'name', label: 'Tên A-Z' }]}
                    />
                </div>
                
                <div className={styles.resultCount}>
                    Hiển thị {filteredDoctors.length} bác sĩ
                </div>
            </div>

            {error ? (
                <InlineError message={error} onRetry={() => setReloadKey(key => key + 1)} />
            ) : loading ? (
                <div className={styles.grid4}>
                    {[1, 2, 3, 4, 5, 6, 7, 8].map(i => (
                        <div key={i} className={styles.skeletonCard} />
                    ))}
                </div>
            ) : filteredDoctors.length > 0 ? (
                <div className={styles.grid4}>
                    {filteredDoctors.map(doc => (
                        <div key={doc.id} className={styles.doctorCard}>
                            <div className={styles.doctorHeading}>
                                <div className={styles.doctorAvatar} aria-hidden="true">
                                    {getDoctorInitials(doc.fullName)}
                                </div>
                                <div>
                                    <h2 className={styles.doctorName}>
                                        {formatDoctorName(doc.academicTitle, doc.fullName)}
                                    </h2>
                                    <span className={styles.doctorSpecialty}>{doctorSpecialties(doc).map(s => s.specialtyName).join(' · ') || doc.specialtyName}</span>
                                </div>
                            </div>
                            <span className={styles.doctorExp}>
                                {doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}
                            </span>
                            {doc.description && <p className={styles.paragraph10}>{doc.description}</p>}

                            <div className={styles.doctorActions}>
                                <LinkButton
                                    type="primary"
                                    block
                                    to={`/patient/book?doctorId=${doc.id}${selectedSpecialty !== 'all' ? `&specialtyId=${selectedSpecialty}` : doc.specialtyId ? `&specialtyId=${doc.specialtyId}` : ''}`}
                                >
                                    Đặt lịch khám
                                </LinkButton>
                                <Link to={`/doctors/${doc.id}`} className={styles.cardActionLink}>
                                    Xem hồ sơ
                                </Link>
                            </div>
                        </div>
                    ))}
                </div>
            ) : (
                <div className={styles.emptyState}>
                    <div className={styles.emptyStateTitle}>Không tìm thấy bác sĩ phù hợp</div>
                    <p>Thử tìm với tên khác hoặc chọn chuyên khoa khác.</p>
                </div>
            )}
        </div>
    );
};
