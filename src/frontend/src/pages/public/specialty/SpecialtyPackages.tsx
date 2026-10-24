import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import axiosClient from '../../../api/axiosClient';
import { InlineError } from '../../../components/common/InlineError';
import { getApiErrorMessage } from '../../../utils/apiError';
import { formatVndCurrency } from '../../../utils/formatters';
import { relatedPackages } from './relatedPackages';
import { packageImage } from '../landing/landingImages';
import type { LandingPackage } from '../landing/landingTypes';
import styles from '../SpecialtyDetail.module.css';

export function SpecialtyPackages({ name }: { name: string }) {
    const [packages, setPackages] = useState<LandingPackage[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [attempt, setAttempt] = useState(0);
    useEffect(() => {
        let active = true;
        void axiosClient.get<unknown, { success: boolean; data: LandingPackage[] }>('/health-packages').then(response => {
            if (!response.success) throw new Error();
            if (active) setPackages(Array.isArray(response.data) ? relatedPackages(response.data, name) : []);
        }).catch(err => { if (active) setError(getApiErrorMessage(err, 'Không thể tải gói khám liên quan.')); })
            .finally(() => { if (active) setLoading(false); });
        return () => { active = false; };
    }, [name, attempt]);
    if (!loading && !error && !packages.length) return null;
    return <section className={styles.contentSection} aria-labelledby="specialty-packages-title">
        <h2 id="specialty-packages-title" className={styles.sectionTitle}>Gói khám liên quan</h2>
        {loading ? <p role="status">Đang tải gói khám liên quan...</p> : error ? <InlineError message={error} onRetry={() => {
            setLoading(true); setError(''); setAttempt(value => value + 1);
        }} /> : <div className={styles.packageGrid}>{packages.map(pkg => {
            const image = packageImage(pkg.name, pkg.imageUrl || pkg.ImageUrl);
            return <article key={pkg.id} className={styles.packageCard}>
                {image && <img src={image} alt={`Minh họa ${pkg.name}`} loading="lazy" width={800} height={600} />}
                <div><h3>{pkg.name}</h3><p>{pkg.targetAudience}</p><strong>{formatVndCurrency(pkg.price)}</strong>
                    <Link to={`/health-packages/${pkg.id}`} className={styles.profileLink}>Chi tiết gói khám</Link></div>
            </article>;
        })}</div>}
    </section>;
}
