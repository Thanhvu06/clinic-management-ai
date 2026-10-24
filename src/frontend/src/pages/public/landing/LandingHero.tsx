import { CalendarDays, ClipboardList, Clock3, MapPin, MessageCircle, PackageCheck, Phone, Pill } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ChatConsultationLink } from '../../../components/chat/ChatConsultationLink';
import type { ClinicLocationDto } from '../../../types';
import { EarliestSlots } from './EarliestSlots';
import { LandingSearch } from './LandingSearch';
import type { LandingDoctor, LandingPackage, LandingSpecialty } from './landingTypes';
import shared from './LandingSections.module.css';
import styles from './LandingHero.module.css';

export function LandingHero({ location, specialties, doctors, healthPackages, loading, onLookup }: {
    location?: ClinicLocationDto; specialties: LandingSpecialty[]; doctors: LandingDoctor[];
    healthPackages: LandingPackage[]; loading: boolean; onLookup: () => void;
}) {
    return <>
        {location && (location.phone || location.openingHours || location.address) && <div className={styles.infoStrip}>
            <div className={`${shared.container} ${styles.infoContent}`}>
                {location.phone && <a href={`tel:${location.phone.replace(/\s+/g, '')}`}><Phone size={14} aria-hidden="true" />{location.phone}</a>}
                {location.openingHours && <span><Clock3 size={14} aria-hidden="true" />{location.openingHours}</span>}
                {location.address && <span className={styles.address}><MapPin size={14} aria-hidden="true" />{location.address}</span>}
            </div>
        </div>}
        <section className={styles.hero} aria-labelledby="landing-title">
            <img className={styles.heroImage} src="/images/landing/hero-hanh-lang.webp" alt="Hành lang sáng thoáng tại cơ sở y tế" width={1600} height={900} fetchPriority="high" />
            <div className={styles.overlay} />
            <div className={`${shared.container} ${styles.heroGrid}`}>
                <div className={styles.heroCopy}>
                    <h1 id="landing-title">Đặt lịch khám tại <span>ClinicCare AI</span></h1>
                    <p>Khám theo lịch hẹn, xem đơn thuốc và kết quả xét nghiệm trực tuyến.</p>
                    <LandingSearch specialties={specialties} doctors={doctors} healthPackages={healthPackages} />
                </div>
                <EarliestSlots doctors={doctors} loading={loading} />
            </div>
            <div className={`${shared.container} ${styles.shortcuts}`}>
                <nav className={styles.quickLinks} aria-label="Lối tắt">
                    <Link to="/patient/book"><CalendarDays size={25} aria-hidden="true" /><span>Đặt lịch khám</span></Link>
                    <button type="button" onClick={onLookup}><ClipboardList size={25} aria-hidden="true" /><span>Tra cứu lịch hẹn</span></button>
                    <Link to="/health-packages"><PackageCheck size={25} aria-hidden="true" /><span>Gói khám</span></Link>
                    <Link to="/medicines"><Pill size={25} aria-hidden="true" /><span>Bảng giá thuốc</span></Link>
                    <ChatConsultationLink to="/patient/ai-consultation"><MessageCircle size={25} aria-hidden="true" /><span>Hỏi trợ lý AI</span></ChatConsultationLink>
                </nav>
            </div>
        </section>
    </>;
}
