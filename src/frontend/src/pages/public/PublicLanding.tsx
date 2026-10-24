import { useState } from 'react';
import { Skeleton } from 'antd';
import { AppointmentLookupModal } from '../../components/AppointmentLookupModal';
import { LandingHero } from './landing/LandingHero';
import { SpecialtySection } from './landing/SpecialtySection';
import { PackageSection } from './landing/PackageSection';
import { MedicineSection } from './landing/MedicineSection';
import { DoctorSection } from './landing/DoctorSection';
import { VisitSteps } from './landing/VisitSteps';
import { LocationSection } from './landing/LocationSection';
import { LandingFaq } from './landing/LandingFaq';
import { useLandingData } from './landing/useLandingData';
import styles from './PublicLanding.module.css';

export function PublicLanding() {
    const data = useLandingData();
    const [isLookupModalOpen, setIsLookupModalOpen] = useState(false);
    return <main className={styles.mainContainer}>
        <AppointmentLookupModal isOpen={isLookupModalOpen} onClose={() => setIsLookupModalOpen(false)} />
        <LandingHero {...data} location={data.locations[0]} onLookup={() => setIsLookupModalOpen(true)} />
        {data.loading ? <div className={styles.pending} role="status" aria-label="Đang tải thông tin phòng khám"><Skeleton active paragraph={{ rows: 4 }} /></div> : <>
            <SpecialtySection specialties={data.specialties} />
            <PackageSection packages={data.healthPackages} />
            <MedicineSection medicines={data.medicines} />
            <DoctorSection doctors={data.doctors} />
        </>}
        <VisitSteps />
        <LocationSection locations={data.locations} />
        <LandingFaq />
    </main>;
}
