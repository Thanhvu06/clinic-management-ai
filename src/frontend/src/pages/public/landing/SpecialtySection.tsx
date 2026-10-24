import { Link } from 'react-router-dom';
import { ChatConsultationLink } from '../../../components/chat/ChatConsultationLink';
import type { LandingSpecialty } from './landingTypes';
import { specialtyIcon } from './specialtyIcons';
import shared from './LandingSections.module.css';
import styles from './SpecialtySection.module.css';

export function SpecialtySection({ specialties }: { specialties: LandingSpecialty[] }) {
    if (!specialties.length) return null;
    return <section id="specialties" className={shared.section} aria-labelledby="landing-specialties-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}><h2 id="landing-specialties-title">Chuyên khoa</h2><Link to="/specialties">Tất cả chuyên khoa</Link></div>
            <div className={styles.grid}>{specialties.slice(0, 12).map(spec => {
                const name = spec.specialtyName || spec.name || '';
                const Icon = specialtyIcon(name);
                return <Link key={spec.id} to={`/specialties/${spec.id}`} className={styles.item}>
                    <Icon size={26} strokeWidth={1.5} aria-hidden="true" />
                    <span className={styles.name}>{name}</span>
                    {spec.description && spec.description.length < 60 && <span className={styles.description}>{spec.description}</span>}
                </Link>;
            })}</div>
            <p className={styles.aiLine}>Chưa biết nên khám khoa nào? <ChatConsultationLink to="/patient/ai-consultation">Hỏi trợ lý AI</ChatConsultationLink> để được gợi ý chuyên khoa theo triệu chứng.</p>
        </div>
    </section>;
}
