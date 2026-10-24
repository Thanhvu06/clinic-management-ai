import { Link } from 'react-router-dom';
import { formatDoctorName } from '../../../utils/doctorNameHelper';
import type { LandingDoctor } from './landingTypes';
import shared from './LandingSections.module.css';
import styles from './DoctorSection.module.css';

const doctorInitials = (fullName: string = '') => {
    const name = fullName.replace(/^(?:(?:PGS|GS|ThS|TS|BS|CKII|CKI)\.?\s*)+/i, '').trim();
    return name.split(/\s+/).filter(Boolean).slice(-2).map(word => word[0]).join('').toLocaleUpperCase('vi-VN');
};

export function DoctorSection({ doctors }: { doctors: LandingDoctor[] }) {
    if (!doctors.length) return null;
    return <section id="doctors" className={shared.section} aria-labelledby="landing-doctors-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}><h2 id="landing-doctors-title">Bác sĩ</h2><Link to="/doctors">Tất cả bác sĩ</Link></div>
            <div className={styles.grid}>{doctors.slice(0, 8).map(doc => <article key={doc.id} className={shared.card}>
                <div className={styles.heading}>
                    <span className={styles.avatar} aria-hidden="true">{doctorInitials(doc.fullName)}</span>
                    <div><h3>{formatDoctorName(doc.academicTitle, doc.fullName)}</h3>
                        {doc.specialtyName && <p className={shared.muted}>{doc.specialtyName}</p>}</div>
                </div>
                <p className={shared.muted}>{doc.experienceYears && doc.experienceYears > 0 ? `${doc.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}</p>
                <div className={shared.cardActions}><Link to={`/patient/book?doctorId=${doc.id}`}>Đặt khám</Link><Link to={`/doctors/${doc.id}`}>Hồ sơ</Link></div>
            </article>)}</div>
        </div>
    </section>;
}
