import type { DiagnosticServiceDto } from '../../types';
import styles from './DoctorExaminationWorkspace.module.css';

export function DiagnosticServiceOption({ service, selected, alreadyOrdered, categoryLabel, onToggle }: {
    service: DiagnosticServiceDto;
    selected: boolean;
    alreadyOrdered: boolean;
    categoryLabel: string;
    onToggle: () => void;
}) {
    return <label className={[styles.serviceOption, selected ? styles.selectedOptionBorder : styles.defaultOptionBorder, selected ? styles.selectedSurface : styles.optionSurface].join(' ')}>
        <div className={styles.iconTextRow}>
            <input type="checkbox" checked={selected && !alreadyOrdered} disabled={alreadyOrdered} onChange={onToggle} className={styles.serviceCheckbox} />
            <div>
                <div className={styles.serviceName}>{service.name}</div>
                <div className={styles.secondaryCaption}><code>{service.code}</code> • {categoryLabel}</div>
                {alreadyOrdered && <span className={styles.secondaryCaption}>Đã chỉ định</span>}
            </div>
        </div>
        {service.preparationInstructions && <div className={styles.preparationInstructions}>{service.preparationInstructions}</div>}
    </label>;
}
