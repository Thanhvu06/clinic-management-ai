import { useState, type ReactNode } from 'react';
import type { AiAction } from '../types/ai';
import styles from './MedicalChatWidget.module.css';

/** Presentation only: all action callbacks and stale checks belong to the caller. */
export const BookingActionChoices = ({ actions, renderAction, latest = true }: {
    actions: AiAction[]; renderAction: (action: AiAction) => ReactNode; latest?: boolean;
}) => {
    const [expanded, setExpanded] = useState(false);
    const slots = actions.filter(action => action.type === 'SelectSlot');
    const doctors = actions.filter(action => action.type === 'SelectDoctor');
    const other = actions.filter(action => action.type !== 'SelectSlot' && action.type !== 'SelectDoctor');
    return <div className={styles.actionChoices}>
        {latest && doctors.length > 0 && <div className={styles.choiceGrid} aria-label="Chọn bác sĩ">{doctors.map(renderAction)}</div>}
        {latest && slots.length > 0 && <>
            <div className={styles.slotGrid} aria-label="Chọn giờ khám">{(expanded ? slots : slots.slice(0, 6)).map(renderAction)}</div>
            {slots.length > 6 && <button type="button" className={styles.textLink} onClick={() => setExpanded(value => !value)}>{expanded ? 'Thu gọn' : 'Xem thêm'}</button>}
        </>}
        {!latest && (slots.length > 0 || doctors.length > 0) && <p className={styles.completedStep}>Lựa chọn ở bước trước ✓</p>}
        {other.map(renderAction)}
    </div>;
};
