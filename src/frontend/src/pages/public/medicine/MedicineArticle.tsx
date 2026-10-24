import { useEffect, useState } from 'react';
import { Tag } from 'antd';
import type { PublicMedicineDto } from '../../../types';
import { medicineSections } from './medicineSections';
import styles from '../MedicineDetail.module.css';

export function MedicineArticle({ medicine }: { medicine: PublicMedicineDto }) {
    const sections = medicineSections(medicine);
    const [activeId, setActiveId] = useState(sections[0]?.id);
    const sectionIds = sections.map(item => item.id).join(',');
    useEffect(() => {
        const update = () => {
            const ids = sectionIds.split(',').filter(Boolean);
            let current = ids[0];
            for (const id of ids) {
                const element = document.getElementById(`medicine-${id}`);
                if (element && element.getBoundingClientRect().top <= (window.innerWidth <= 768 ? 200 : 180)) current = id;
            }
            setActiveId(current);
        };
        window.addEventListener('scroll', update, { passive: true });
        update();
        return () => window.removeEventListener('scroll', update);
    }, [sectionIds]);
    return <div className={styles.articleLayout}>
        {sections.length > 0 && <nav className={styles.toc} aria-label="Mục lục thông tin thuốc">
            <p>Nội dung thuốc</p><div>{sections.map(item => <a key={item.id} href={`#medicine-${item.id}`}
                aria-current={activeId === item.id ? 'location' : undefined}
                onClick={() => setActiveId(item.id)}>{item.title}</a>)}</div>
        </nav>}
        <article className={sections.length ? styles.article : styles.fullArticle}>
            {sections.length ? sections.map(item => <section id={`medicine-${item.id}`} key={item.id} className={styles.articleSection}>
                <h2>{item.title}</h2>
                {item.id !== 'interactions' ? <p className={styles.text}>{item.text}</p> : <div className={styles.interactions}>
                    {medicine.interactions?.map((interaction, index) => <div key={`${interaction.otherIngredient}-${index}`} className={styles.interaction}>
                        <Tag color={interaction.severity === 'Major' || interaction.severity === 'Contraindicated' ? 'error' : interaction.severity === 'Moderate' ? 'warning' : 'default'}>{interaction.severity}</Tag>
                        <strong>{interaction.otherIngredient}</strong>
                        {interaction.description?.trim() && <p className={styles.text}>{interaction.description}</p>}
                        {interaction.recommendation?.trim() && <p className={styles.text}>{interaction.recommendation}</p>}
                    </div>)}
                </div>}
            </section>) : <p>Chưa cập nhật nội dung hướng dẫn cho thuốc này. Vui lòng hỏi bác sĩ/dược sĩ trước khi sử dụng.</p>}
            <p className={styles.reference}>Thông tin thuốc chỉ mang tính tham khảo. Dùng thuốc theo chỉ định của bác sĩ/dược sĩ.</p>
        </article>
    </div>;
}
