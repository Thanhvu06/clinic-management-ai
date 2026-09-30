import { useEffect, useId, useRef, useState } from "react";
import type { AiBookingWizardRequest, AiBookingWizardResponse } from "../types/ai";
import styles from "./MedicalChatWidget.module.css";

interface Props {
    state: AiBookingWizardResponse | null;
    busy: boolean;
    onStep: (step: AiBookingWizardRequest["step"], token?: string, reason?: string) => Promise<void>;
}

export const BookingWizard = ({ state, busy, onStep }: Props) => {
    const [reason, setReason] = useState("");
    const reasonId = useId();
    const heading = useRef<HTMLHeadingElement>(null);
    useEffect(() => {
        heading.current?.focus();
    }, [state?.step]);
    if (!state) return null;
    return <section className={styles.bookingSummaryCard} aria-label="Đặt lịch khám từng bước" aria-busy={busy}>
        <h4 ref={heading} tabIndex={-1}>{state.title}</h4>
        <p role={state.errorCode ? "alert" : "status"}>{state.message}</p>
        <div className={styles.quickPrompts}>
            {state.options.map(option => <button key={option.token} type="button" className={styles.quickPromptChip}
                aria-label={option.hint ? `${option.label}, ${option.hint}` : option.label} disabled={busy}
                onClick={() => void onStep("pick", option.token)}>
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
        <div className={styles.quickPrompts}>
            {state.canGoBack && state.backToken && <button type="button" aria-label="Quay lại bước trước" disabled={busy}
                onClick={() => { setReason(""); void onStep("back", state.backToken); }}>Quay lại</button>}
            <button type="button" aria-label="Bắt đầu lại đặt lịch" disabled={busy} onClick={() => { setReason(""); void onStep("start"); }}>Bắt đầu lại</button>
        </div>
        {state.step === "stopped" && state.errorCode === "EMERGENCY" && <a href="tel:115" aria-label="Gọi cấp cứu 115">Gọi cấp cứu 115</a>}
    </section>;
};
