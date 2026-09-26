import React, { useState } from 'react';
import { Bot, Send, X, ExternalLink, ShieldCheck } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { sendRoleCopilotMessage, type AiCopilotResponse } from '../api/aiCopilotApi';
import styles from './RoleCopilotPanel.module.css';

const labels: Record<string, string> = {
    Receptionist: 'Lễ tân', Doctor: 'Bác sĩ', DiagnosticTechnician: 'Kỹ thuật viên', Pharmacist: 'Dược sĩ', Admin: 'Quản trị viên'
};

const providerLabel = (status: string) => ({
    NotCalled: 'Dữ liệu nội bộ đã kiểm chứng', Online: 'AI provider đã kiểm chứng', Degraded: 'Chế độ dự phòng',
    Unavailable: 'Provider không khả dụng', SafetyBlocked: 'Đã chặn vì an toàn'
}[status] ?? 'Đã kiểm tra');

export const RoleCopilotPanel: React.FC = () => {
    const { user } = useAuth();
    const [open, setOpen] = useState(false);
    const [input, setInput] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [response, setResponse] = useState<AiCopilotResponse | null>(null);

    if (!user || user.role === 'Patient') return null;
    const roleLabel = labels[user.role] ?? user.role;

    const send = async (value = input) => {
        const message = value.trim();
        if (!message || loading) return;
        setLoading(true); setError(null);
        try { setResponse(await sendRoleCopilotMessage(message)); setInput(''); }
        catch (reason) { setError(reason instanceof Error ? reason.message : 'Không thể xử lý yêu cầu.'); }
        finally { setLoading(false); }
    };

    return (
        <div className={styles.container}>
            {!open && <button className={styles.launcher} type="button" onClick={() => setOpen(true)} aria-label={`Mở Copilot ${roleLabel}`}><Bot size={18} /> Copilot</button>}
            {open && <section className={styles.panel} aria-label={`Copilot ${roleLabel}`}>
                <header className={styles.header}><div><strong>Copilot {roleLabel}</strong><span><ShieldCheck size={13} /> Theo quyền backend</span></div><button type="button" onClick={() => setOpen(false)} aria-label="Đóng Copilot"><X size={17} /></button></header>
                <div className={styles.body} aria-live="polite">
                    <p className={styles.notice}>Chỉ đọc dữ liệu nghiệp vụ trong phạm vi cơ sở/ca được phân công. AI không tự thực hiện thao tác ghi.</p>
                    {response && <><div className={styles.provider}>{providerLabel(response.providerStatus)}</div><p>{response.message}</p>{response.safetyNotice && <p className={styles.warning}>{response.safetyNotice}</p>}{response.navigationRoute && <a href={response.navigationRoute} className={styles.route}>Mở màn hình nghiệp vụ <ExternalLink size={13} /></a>}{response.cards.map((card, index) => <div className={styles.card} key={`${card.type}-${index}`}><strong>{card.title}</strong><span>{card.description}</span></div>)}</>}
                    {error && <p className={styles.error}>{error}</p>}
                    <div className={styles.prompts}>{(response?.suggestedPrompts ?? ['Xem dữ liệu workspace của tôi']).map(prompt => <button type="button" key={prompt} onClick={() => void send(prompt)} disabled={loading}>{prompt}</button>)}</div>
                </div>
                <form className={styles.form} onSubmit={event => { event.preventDefault(); void send(); }}><input value={input} onChange={event => setInput(event.target.value)} maxLength={500} placeholder="Hỏi về công việc hôm nay..." aria-label="Nội dung Copilot" /><button type="submit" disabled={loading || !input.trim()} aria-label="Gửi yêu cầu Copilot"><Send size={16} /></button></form>
            </section>}
        </div>
    );
};
