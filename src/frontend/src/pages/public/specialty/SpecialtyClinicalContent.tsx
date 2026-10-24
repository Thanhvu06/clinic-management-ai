import { Check } from 'lucide-react';
import type { SpecialtyContent } from './specialtyContent';
import styles from '../SpecialtyDetail.module.css';

export function SpecialtyClinicalContent({ content }: { content: SpecialtyContent }) {
    return <>
        <section className={styles.contentSection}>
            <h2 className={styles.sectionTitle}>Khoa khám những bệnh gì</h2>
            <ul className={styles.conditionGrid}>{content.conditions.map(item => <li key={item}><Check size={16} aria-hidden="true" />{item}</li>)}</ul>
        </section>
        <section className={styles.contentSection}>
            <h2 className={styles.sectionTitle}>Khi nào nên đi khám</h2>
            <ul className={styles.readingList}>{content.whenToVisit.map(item => <li key={item}>{item}</li>)}</ul>
        </section>
        <section className={styles.contentSection}>
            <h2 className={styles.sectionTitle}>Dịch vụ thường thực hiện</h2>
            <p className={styles.recommendationsExplanation}>Bác sĩ lựa chọn thăm khám và cận lâm sàng tùy tình trạng; không phải lượt khám nào cũng cần thực hiện tất cả.</p>
            <ul className={styles.serviceGrid}>{content.services.map(item => <li key={item}>{item}</li>)}</ul>
        </section>
        <section className={styles.contentSection}>
            <h2 className={styles.sectionTitle}>Chuẩn bị trước khi khám</h2>
            <ol className={styles.readingList}>{content.prepare.map(item => <li key={item}>{item}</li>)}</ol>
        </section>
    </>;
}
