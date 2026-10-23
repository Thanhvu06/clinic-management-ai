import { useEffect, useState } from 'react';
import { getPublicLocations } from '../api/publicLocationsApi';
import type { ClinicLocationDto } from '../types';
import { InlineError } from '../components/common/InlineError';
import { getApiErrorMessage } from '../utils/apiError';
import styles from './PublicLayout.module.css';

export const PublicClinicFooter = () => {
    const [locations, setLocations] = useState<ClinicLocationDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [attempt, setAttempt] = useState(0);

    useEffect(() => {
        let active = true;
        const fetchLocations = async () => {
            setLoading(true);
            setError(null);
            try {
                const response = await getPublicLocations();
                if (!response.success) throw new Error('Không thể tải thông tin cơ sở khám.');
                if (active) setLocations(response.data ?? []);
            } catch (err) {
                if (active) setError(getApiErrorMessage(err, 'Không thể tải thông tin cơ sở khám.'));
            } finally {
                if (active) setLoading(false);
            }
        };
        void fetchLocations();
        return () => { active = false; };
    }, [attempt]);

    if (loading) return <p role="status">Đang tải thông tin cơ sở khám...</p>;
    if (error) return <InlineError message={error} onRetry={() => setAttempt(value => value + 1)} />;
    if (locations.length === 0) return null;

    return <section aria-label="Thông tin cơ sở khám" className={styles.footerClinics}>
        {locations.map(location => <div key={location.id}>
            <p>{location.name}</p>
            <p>{location.address}</p>
            <p>{location.openingHours}</p>
            <p>{location.phone}</p>
        </div>)}
    </section>;
};
