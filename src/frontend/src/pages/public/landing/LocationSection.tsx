import { Link } from 'react-router-dom';
import type { ClinicLocationDto } from '../../../types';
import shared from './LandingSections.module.css';
import styles from './LocationSection.module.css';

export function LocationSection({ locations }: { locations: ClinicLocationDto[] }) {
    if (!locations.length) return null;
    return <section id="locations" className={shared.section} aria-labelledby="landing-locations-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}>
                <h2 id="landing-locations-title">{locations.length === 1 ? 'Liên hệ' : 'Cơ sở khám'}</h2>
                {locations.length > 1 && <Link to="/locations">Tất cả cơ sở</Link>}
            </div>
            <div className={styles.grid}>{locations.slice(0, 4).map(loc => <article key={loc.id} className={styles.location}>
                <div><h3>{loc.name}</h3><p>{loc.address}</p></div>
                <div>{loc.openingHours && <p className={shared.muted}>Giờ làm việc: {loc.openingHours}</p>}
                    {loc.phone && <p className={shared.muted}>Điện thoại: <a href={`tel:${loc.phone.replace(/\s+/g, '')}`}>{loc.phone}</a></p>}</div>
                {loc.address && <a href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(loc.address)}`} target="_blank" rel="noopener noreferrer" className={styles.directions}>Chỉ đường</a>}
            </article>)}</div>
        </div>
    </section>;
}
