import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import type { AiBookingWizardRequest, AiBookingWizardResponse } from "../types/ai";
import styles from "./MedicalChatWidget.module.css";

const steps = ['specialty', 'doctor', 'day', 'slot', 'reason', 'review'] as const;
const labels = ['Chuyên khoa', 'Bác sĩ', 'Ngày', 'Giờ', 'Lý do', 'Xem lại'];

interface Props {
    state: AiBookingWizardResponse | null;
    busy: boolean;
    onStep: (step: AiBookingWizardRequest["step"], token?: string, reason?: string) => Promise<void>;
    reviewContent?: ReactNode;
}

export const BookingWizard = ({ state, busy, onStep, reviewContent }: Props) => {
    const [reason, setReason] = useState("");
    const [selections, setSelections] = useState<string[]>([]);
    const reasonId = useId();
    const heading = useRef<HTMLHeadingElement>(null);
    const card = useRef<HTMLElement>(null);
    useEffect(() => {
        card.current?.scrollIntoView({ block: 'start', behavior: 'instant' });
        heading.current?.focus({ preventScroll: true });
    }, [state?.step]);
    if (!state) return null;
    const index = steps.indexOf(state.step as typeof steps[number]);
    const completed = [state.summary?.specialtyName ?? selections[0], state.summary?.doctorName ?? selections[1],
        state.summary?.slotDate ?? selections[2], state.summary?.startTime ? `${state.summary.startTime} – ${state.summary.endTime}` : selections[3],
        reason ? 'Đã nhập lý do' : selections[4]];
    return <section ref={card} className={styles.wizard} data-wizard-step={state.step} aria-label="Đặt lịch khám từng bước" aria-busy={busy}>
        {index >= 0 && <div className={styles.wizardProgress} aria-label={`Bước ${index + 1}/6: ${labels.join(' → ')}`}>
            <strong>Bước {index + 1}/6:</strong><span className={styles.progressLabels}>{labels.join(' → ')}</span>
            <div className={styles.progressDots} aria-hidden="true">{steps.map((step, position) => <i key={step} data-completed={position <= index} />)}</div>
        </div>}
        {completed.slice(0, Math.max(0, index)).map((value, position) => value && <p className={styles.completedStep} data-completed-step key={labels[position]}>{labels[position]}: {value} ✓</p>)}
        {state.canGoBack && state.backToken && <button type="button" className={styles.textLink} aria-label="Quay lại bước trước" disabled={busy}
            onClick={() => { setReason(""); void onStep("back", state.backToken); }}>← Quay lại</button>}
        <h4 ref={heading} tabIndex={-1}>{state.title}</h4>
        <p role={state.errorCode ? "alert" : "status"}>{state.message}</p>
        {state.step === 'review' && reviewContent}
        <div className={`${styles.wizardOptions} ${state.step === 'slot' ? styles.slotGrid : ['specialty', 'doctor'].includes(state.step) ? styles.choiceGrid : styles.quickPrompts}`}>
            {state.options.map(option => <button key={option.token} type="button" className={styles.quickPromptChip}
                aria-label={option.hint ? `${option.label}, ${option.hint}` : option.label} disabled={busy}
                onClick={() => { setSelections(previous => { const next = [...previous]; next[index] = option.label; return next; }); void onStep("pick", option.token); }}>
                {option.label}{option.hint && <small> · {option.hint}</small>}
            </button>)}
        </div>
        {state.step === "reason" && <form onSubmit={event => {
            event.preventDefault();
            if (!busy) void onStep("reason", state.reasonToken, reason);
        }}>
            <label htmlFor={reasonId}>Lý do khám (10–500 ký tự)</label>
            <textarea id={reasonId} aria-label="Lý do khám từ 10 đến 500 ký tự" className={styles.textarea}
                minLength={10} maxLength={500} required disabled={busy} value={reason} onChange={event => setReason(event.target.value)} />
            <button type="submit" disabled={busy || reason.trim().length < 10} className={styles.actionBtn}>Xem lại thông tin khám</button>
        </form>}
        <button type="button" className={styles.textLink} aria-label="Bắt đầu lại đặt lịch" disabled={busy} onClick={() => { setReason(""); setSelections([]); void onStep("start"); }}>Bắt đầu lại</button>
        {state.step === "stopped" && state.errorCode === "EMERGENCY" && <a href="tel:115" aria-label="Gọi cấp cứu 115">Gọi cấp cứu 115</a>}
    </section>;
};
