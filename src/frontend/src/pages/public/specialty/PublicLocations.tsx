import { useEffect, useState } from 'react';
import { getPublicLocations } from '../../../api/publicLocationsApi';
import type { ClinicLocationDto } from '../../../types';
import { getApiErrorMessage } from '../../../utils/apiError';
import { InlineError } from '../../../components/common/InlineError';
import styles from '../SpecialtyDetail.module.css';

export function PublicLocations() {
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [attempt, setAttempt] = useState(0);
    useEffect(() => {
        let active = true;
        void getPublicLocations().then(response => {
            if (!response.success) throw new Error();
            if (active) setLocations(Array.isArray(response.data) ? response.data.filter(item => item.isActive) : []);
        }).catch(err => {
            if (active) setError(getApiErrorMessage(err, 'Không thể tải thông tin cơ sở khám.'));
        }).finally(() => { if (active) setLoading(false); });
        return () => { active = false; };
    }, [attempt]);
    if (loading) return <p role="status">Đang tải thông tin cơ sở khám...</p>;
    if (error) return <InlineError message={error} onRetry={() => { setError(''); setLoading(true); setAttempt(value => value + 1); }} />;
    if (!locations.length) return <p>Chưa cập nhật thông tin cơ sở khám.</p>;
    return <div className={styles.locations}>{locations.map(location => <address key={location.id}>
        <strong>{location.name}</strong>
        <span>{location.address}</span>
        <span>Giờ làm việc: {location.openingHours}</span>
        <a href={`tel:${location.phone.replace(/\s/g, '')}`}>{location.phone}</a>
    </address>)}</div>;
}
