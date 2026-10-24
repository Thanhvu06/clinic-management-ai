import { Link } from 'react-router-dom';
import { formatVndCurrency, parseIncludedServices } from '../../../utils/formatters';
import { packageImage } from './landingImages';
import type { LandingPackage } from './landingTypes';
import shared from './LandingSections.module.css';
import styles from './PackageSection.module.css';

export function PackageSection({ packages }: { packages: LandingPackage[] }) {
    if (!packages.length) return null;
    const featured = packages.find(pkg => pkg.name.toLocaleLowerCase('vi-VN').includes('tổng quát')) ?? packages[0];
    const ordered = [featured, ...packages.filter(pkg => pkg.id !== featured.id)].slice(0, 6);
    return <section id="packages" className={shared.section} aria-labelledby="landing-packages-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}><h2 id="landing-packages-title">Gói khám</h2><Link to="/health-packages">Tất cả gói khám</Link></div>
            <div className={styles.grid}>{ordered.map((pkg, index) => {
                const image = packageImage(pkg.name, pkg.imageUrl || pkg.ImageUrl);
                const count = parseIncludedServices(pkg.includedServicesJson || pkg.includedServices).length;
                return <article key={pkg.id} className={`${styles.card} ${index === 0 ? styles.featured : ''}`}>
                    {image && <img src={image} alt={`Minh họa ${pkg.name}`} loading="lazy" width={800} height={600} className={styles.image} />}
                    <div className={styles.body}>
                        <h3>{pkg.name}</h3>
                        {pkg.targetAudience && <p className={shared.muted}>Dành cho: {pkg.targetAudience}</p>}
                        <p className={shared.muted}>Gồm {count} dịch vụ</p>
                        <div className={styles.price}>{formatVndCurrency(pkg.price)}</div>
                        <div className={shared.cardActions}>
                            <Link to={`/patient/health-packages/${pkg.id}/register`}>Đăng ký</Link>
                            <Link to={`/health-packages/${pkg.id}`}>Chi tiết</Link>
                        </div>
                    </div>
                </article>;
            })}</div>
        </div>
    </section>;
}
