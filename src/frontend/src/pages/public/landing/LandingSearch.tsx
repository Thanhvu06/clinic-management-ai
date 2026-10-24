import { useEffect, useState, type FormEvent } from 'react';
import { Button, Input } from 'antd';
import { Search } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { formatDoctorName } from '../../../utils/doctorNameHelper';
import type { LandingDoctor, LandingPackage, LandingSpecialty } from './landingTypes';
import styles from './LandingSearch.module.css';

const matches = (value: string | undefined, query: string) => value?.toLocaleLowerCase('vi-VN').includes(query);

export function LandingSearch({ specialties, doctors, healthPackages }: {
    specialties: LandingSpecialty[]; doctors: LandingDoctor[]; healthPackages: LandingPackage[];
}) {
    const navigate = useNavigate();
    const [searchQuery, setSearchQuery] = useState('');
    const [debouncedQuery, setDebouncedQuery] = useState('');
    useEffect(() => {
        const timer = setTimeout(() => setDebouncedQuery(searchQuery.trim().toLocaleLowerCase('vi-VN')), 300);
        return () => clearTimeout(timer);
    }, [searchQuery]);

    const suggestedSpecialties = debouncedQuery ? specialties.filter(s => matches(s.specialtyName || s.name, debouncedQuery)).slice(0, 5) : [];
    const suggestedDoctors = debouncedQuery ? doctors.filter(d => matches(d.fullName, debouncedQuery)).slice(0, 5) : [];
    const suggestedPackages = debouncedQuery ? healthPackages.filter(p => matches(p.name, debouncedQuery)).slice(0, 5) : [];
    const showSuggestions = debouncedQuery.length > 0 && debouncedQuery === searchQuery.trim().toLocaleLowerCase('vi-VN');
    const hasSuggestions = suggestedSpecialties.length + suggestedDoctors.length + suggestedPackages.length > 0;
    const handleSearch = (event: FormEvent) => {
        event.preventDefault();
        if (searchQuery.trim()) navigate(`/search?q=${encodeURIComponent(searchQuery.trim())}`);
    };

    return <div className={styles.searchArea}>
        <form onSubmit={handleSearch} className={styles.searchForm} role="search">
            <Input size="large" prefix={<Search size={18} aria-hidden="true" />} placeholder="Tìm bác sĩ, chuyên khoa, gói khám"
                value={searchQuery} onChange={event => setSearchQuery(event.target.value)}
                onKeyDown={event => { if (event.key === 'Escape') setSearchQuery(''); }}
                aria-label="Tìm bác sĩ, chuyên khoa, gói khám" className={styles.searchInput} />
            <Button type="primary" size="large" htmlType="submit">Tìm kiếm</Button>
        </form>
        {showSuggestions && <div className={styles.suggestions} role="region" aria-label="Gợi ý tìm kiếm">
            {!hasSuggestions ? <p className={styles.suggestionEmpty}>Không có gợi ý nhanh. Bấm Tìm kiếm để xem tất cả kết quả.</p> : <>
                {suggestedSpecialties.length > 0 && <div className={styles.suggestionGroup}>
                    <h2>Chuyên khoa</h2>{suggestedSpecialties.map(s => <Link key={s.id} to={`/specialties/${s.id}`}>{s.specialtyName || s.name}</Link>)}
                </div>}
                {suggestedDoctors.length > 0 && <div className={styles.suggestionGroup}>
                    <h2>Bác sĩ</h2>{suggestedDoctors.map(d => <Link key={d.id} to={`/doctors/${d.id}`}>
                        {formatDoctorName(d.academicTitle, d.fullName)}{d.specialtyName && <span> · {d.specialtyName}</span>}
                    </Link>)}
                </div>}
                {suggestedPackages.length > 0 && <div className={styles.suggestionGroup}>
                    <h2>Gói khám</h2>{suggestedPackages.map(p => <Link key={p.id} to={`/health-packages/${p.id}`}>{p.name}</Link>)}
                </div>}
            </>}
            <Link to={`/search?q=${encodeURIComponent(searchQuery.trim())}`} className={styles.suggestionAll}>Xem tất cả kết quả</Link>
        </div>}
    </div>;
}
