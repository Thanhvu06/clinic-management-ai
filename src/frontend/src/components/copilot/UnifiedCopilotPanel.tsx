import React, { useEffect, useMemo, useRef } from 'react';
import { Bot, CheckCircle2, ExternalLink, RotateCcw, Send, ShieldCheck, Square, X } from 'lucide-react';
import { useUnifiedCopilot, type UnifiedCopilotMessage } from './useUnifiedCopilot';
import { providerStateLabel, toolDisplayName } from './copilotConfig';
import styles from './UnifiedCopilotPanel.module.css';

const LABELS: Record<string, string> = {
    doctorName: 'Bác sĩ', doctor: 'Bác sĩ', specialtyName: 'Chuyên khoa', specialty: 'Chuyên khoa',
    facilityName: 'Cơ sở', facility: 'Cơ sở', room: 'Phòng', appointmentCode: 'Mã lịch hẹn',
    status: 'Trạng thái', slotDate: 'Ngày', date: 'Ngày', startTime: 'Bắt đầu', endTime: 'Kết thúc',
    openingHours: 'Giờ làm việc', address: 'Địa chỉ', phone: 'Điện thoại', code: 'Mã',
    sourceType: 'Nguồn dữ liệu', updatedAtUtc: 'Cập nhật'
};

const safeRoute = (route?: string | null): route is string => Boolean(route && route.startsWith('/') && !route.startsWith('//') && !route.includes('://') && !route.includes('..') && !route.includes('\\'));

const formatValue = (key: string, value: unknown): string => {
    if (value === null || value === undefined || value === '') return '';
    if (typeof value === 'boolean') return value ? 'Có' : 'Không';
    if (typeof value === 'number') return key.toLowerCase().includes('price') ? `${value.toLocaleString('vi-VN')} ₫` : String(value);
    if (typeof value === 'string' && key.toLowerCase().includes('utc')) {
        const parsed = new Date(value);
        if (!Number.isNaN(parsed.valueOf())) return parsed.toLocaleString('vi-VN');
    }
    return String(value);
};

const displayRows = (data: unknown): Array<Record<string, unknown>> => {
    if (Array.isArray(data)) return data.filter((item): item is Record<string, unknown> => Boolean(item && typeof item === 'object')).slice(0, 6);
    return data && typeof data === 'object' ? [data as Record<string, unknown>] : [];
};

const visibleFields = (row: Record<string, unknown>) => Object.entries(row)
    .filter(([key, value]) => !['sourceId', 'id', 'patientId', 'userId', 'facilityId', 'token'].includes(key) && value !== null && value !== undefined && value !== '')
    .slice(0, 8);

const pendingStatus = (row: Record<string, unknown>): string | null => {
    const value = row.status;
    if (typeof value !== 'string') return null;
    return ['pending_confirmation', 'expired', 'stale', 'completed', 'failed', 'failed_terminal'].includes(value.toLowerCase()) ? value : null;
};

const MessageBubble: React.FC<{ item: UnifiedCopilotMessage; onRetry: (text: string) => void }> = ({ item, onRetry }) => {
    const response = item.response;
    const cards = response?.cards ?? [];
    return (
        <div className={styles.messageRow} data-role={item.role}>
            <div className={`${styles.bubble} ${item.role === 'user' ? styles.user : styles.assistant} ${item.error ? styles.error : ''}`}>
                <div>{item.content}</div>
                {response?.clarification && <div className={styles.clarification}>{response.clarification}</div>}
                {response?.safetyNotice && <div className={styles.clarification}>{response.safetyNotice}</div>}
                {cards.map((card, index) => {
                    const rows = displayRows(card.data);
                    const sources = Array.from(new Map(
                        [...(card.sources ?? []), ...(index === 0 ? (response?.sources ?? []) : [])]
                            .map(source => [`${source.name}:${source.kind}`, source] as const)
                    ).values());
                    return (
                        <article className={styles.card} key={`${card.type}-${index}`}>
                            <div className={styles.cardTitle}><span>{card.title}</span>{pendingStatus(rows[0] ?? {}) && <span>{pendingStatus(rows[0] ?? {})}</span>}</div>
                            {card.description && <p className={styles.cardDescription}>{card.description}</p>}
                            {rows.map((row, rowIndex) => (
                                <ul className={styles.dataList} key={`${card.type}-${rowIndex}`}>
                                    {pendingStatus(row) && <li aria-label="Trạng thái thao tác">Trạng thái thao tác: {pendingStatus(row)}</li>}
                                    {visibleFields(row).map(([key, value]) => <li key={key}><strong>{LABELS[key] ?? key}:</strong> {formatValue(key, value)}</li>)}
                                </ul>
                            ))}
                            {sources.length > 0 && <div className={styles.sources} aria-label="Nguồn dữ liệu">
                                {sources.slice(0, 5).map((source, sourceIndex) => <span className={styles.source} key={`${source.name}-${sourceIndex}`}>{source.name}{source.kind ? ` · ${source.kind}` : ''}</span>)}
                            </div>}
                        </article>
                    );
                })}
                {item.error && item.retryText && <button type="button" className={styles.retryButton} onClick={() => onRetry(item.retryText!)}><RotateCcw size={13} /> Thử lại</button>}
            </div>
        </div>
    );
};

export const UnifiedCopilotPanel: React.FC = () => {
    const copilot = useUnifiedCopilot();
    const launcherRef = useRef<HTMLButtonElement>(null);
    const inputRef = useRef<HTMLTextAreaElement>(null);
    const panelRef = useRef<HTMLElement>(null);
    const tools = useMemo(() => copilot.catalogTools?.length ? copilot.catalogTools : copilot.config.defaultTools, [copilot.catalogTools, copilot.config.defaultTools]);

    useEffect(() => {
        if (!copilot.open) {
            launcherRef.current?.focus();
            return;
        }
        const timer = window.setTimeout(() => inputRef.current?.focus(), 0);
        return () => window.clearTimeout(timer);
    }, [copilot.open]);

    const onPanelKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            copilot.setOpen(false);
            return;
        }
        if (event.key !== 'Tab' || !panelRef.current) return;
        const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>('button:not(:disabled), textarea, a[href]'));
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };

    if (!copilot.user) return null;
    if (!copilot.open) return <div className={styles.shell}><button ref={launcherRef} type="button" className={styles.launcher} onClick={() => copilot.setOpen(true)} aria-label={`Mở ${copilot.config.label}`}><Bot size={18} /> {copilot.config.shortLabel}</button></div>;

    const providerStatus = copilot.messages.slice().reverse().find(message => message.response)?.response?.providerStatus ?? 'NotCalled';
    const latestResponse = copilot.messages.slice().reverse().find(message => message.response)?.response;
    const suggestedPrompts = latestResponse?.suggestedPrompts?.length ? latestResponse.suggestedPrompts : copilot.config.prompts;
    const pendingCapability = copilot.pendingAction
        ? copilot.actionCapabilities.find(capability => capability.tool.name === copilot.pendingAction?.toolName)
        : undefined;

    return (
        <div className={styles.shell}>
            <section ref={panelRef} className={styles.panel} role="dialog" aria-modal="true" aria-label={copilot.config.label} onKeyDown={onPanelKeyDown}>
                <header className={styles.header}>
                    <div className={styles.title}><Bot size={20} /><div className={styles.titleText}><strong>{copilot.config.label}</strong><span>{copilot.config.description}</span></div></div>
                    <div className={styles.headerActions}>
                        {copilot.loading && <button type="button" className={`${styles.iconButton} ${styles.cancelButton}`} onClick={copilot.abort} aria-label="Dừng yêu cầu Copilot"><Square size={16} /></button>}
                        <button type="button" className={styles.iconButton} onClick={copilot.reset} aria-label="Đặt lại phiên Copilot"><RotateCcw size={16} /></button>
                        <button type="button" className={styles.iconButton} onClick={() => copilot.setOpen(false)} aria-label="Đóng Copilot"><X size={18} /></button>
                    </div>
                </header>
                <div className={styles.statusBar} data-state={providerStatus}><span className={styles.statusDot} /> <span>{providerStateLabel(providerStatus)}</span><span className={styles.mode}>{latestResponse?.assistantMode ?? 'Ready'}{latestResponse?.plannerMode ? ` · ${latestResponse.plannerMode}` : ''}</span></div>
                <div className={styles.messages} aria-live="polite" aria-relevant="additions">
                    <p className={styles.cardDescription}><ShieldCheck size={13} /> Dữ liệu và quyền truy cập do backend kiểm tra; Copilot này không tự thực hiện thao tác ghi.</p>
                    {copilot.messages.map(message => <MessageBubble key={message.id} item={message} onRetry={text => void copilot.send(text)} />)}
                    {copilot.loading && <div className={`${styles.messageRow} ${styles.assistant}`}><div className={styles.bubble}>Đang kiểm tra dữ liệu…</div></div>}
                </div>
                {copilot.actionCapabilities.length > 0 && <section className={styles.actionTray} aria-label="Thao tác có xác nhận">
                    <div className={styles.actionHeading}><strong>Thao tác có xác nhận</strong><span>Backend kiểm tra lại quyền, resource và điều kiện domain.</span></div>
                    {copilot.catalogError && <p className={styles.actionError}>{copilot.catalogError}</p>}
                    {copilot.actionCapabilities.map(capability => <div className={styles.actionRow} key={capability.tool.name}>
                        <div className={styles.actionCopy}><strong>{toolDisplayName(capability.tool)}</strong><span>{capability.tool.description}</span>{!capability.enabled && <small>{capability.reason}</small>}</div>
                        <button type="button" className={styles.actionButton} disabled={!capability.enabled || Boolean(copilot.actionLoading)} onClick={() => void copilot.prepareAction(capability)}>{copilot.actionLoading === capability.tool.name ? 'Đang chuẩn bị…' : 'Xem trước'}</button>
                    </div>)}
                    {copilot.pendingAction && <div className={styles.actionPreview} aria-label="Xem trước thao tác">
                        <div className={styles.cardTitle}><span>Xem trước từ backend</span><span>{copilot.pendingActionExpired ? 'expired' : copilot.pendingAction.status}</span></div>
                        <div className={styles.previewLine}><strong>Resource đã chọn:</strong> {copilot.pendingAction.preview.resource.identity}</div>
                        {copilot.pendingAction.preview.resource.facility && <div className={styles.previewLine}><strong>Cơ sở:</strong> {copilot.pendingAction.preview.resource.facility}</div>}
                        {copilot.pendingAction.preview.resource.department && <div className={styles.previewLine}><strong>Khoa:</strong> {copilot.pendingAction.preview.resource.department}</div>}
                        {copilot.pendingAction.preview.resource.subject && <div className={styles.previewLine}><strong>Đối tượng:</strong> {copilot.pendingAction.preview.resource.subject}</div>}
                        {copilot.pendingAction.preview.resource.encounter && <div className={styles.previewLine}><strong>Ca khám:</strong> {copilot.pendingAction.preview.resource.encounter}</div>}
                        {copilot.pendingAction.preview.resource.currentStatus && <div className={styles.previewLine}><strong>Trạng thái hiện tại:</strong> {copilot.pendingAction.preview.resource.currentStatus}</div>}
                        <div className={styles.previewLine}><strong>Thay đổi sau xác nhận:</strong></div>
                        {copilot.pendingAction.preview.changes.map(change => <div className={styles.previewLine} key={change.kind}><strong>{change.summary}</strong>{change.items.map(item => <div key={`${change.kind}-${item.label}-${item.value}`}>&nbsp;{item.label}: {item.value}{item.quantity !== null && item.quantity !== undefined ? ` · SL ${item.quantity}${item.unit ? ` ${item.unit}` : ''}` : ''}</div>)}</div>)}
                        <div className={styles.previewLine}><strong>Hậu quả:</strong> {copilot.pendingAction.preview.consequence}</div>
                        <div className={styles.previewLine}><strong>Kiểm tra backend:</strong> {copilot.pendingAction.preview.confirmationSummary}</div>
                        <div className={styles.previewLine}><strong>Dữ liệu kiểm tra lúc:</strong> {new Date(copilot.pendingAction.preview.validatedAtUtc).toLocaleString('vi-VN')}</div>
                        <div className={styles.previewLine}><strong>Hết hạn:</strong> {new Date(copilot.pendingAction.preview.expiresAtUtc).toLocaleString('vi-VN')}</div>
                        {copilot.pendingActionExpired && <p className={styles.actionError} role="alert">Preview đã hết hạn; backend sẽ từ chối token cũ. Hãy chuẩn bị lại để nhận dữ liệu kiểm tra mới.</p>}
                        <button type="button" className={styles.confirmButton} disabled={Boolean(copilot.actionLoading) || copilot.pendingActionExpired} onClick={() => void copilot.confirmAction()}><CheckCircle2 size={14} /> Xác nhận thao tác</button>
                        {copilot.pendingActionExpired && pendingCapability?.enabled && <button type="button" className={styles.actionButton} disabled={Boolean(copilot.actionLoading)} onClick={() => void copilot.prepareAction(pendingCapability)}>Chuẩn bị lại xem trước</button>}
                    </div>}
                    {copilot.actionFeedback && <p className={copilot.actionFeedback.status === 'completed' ? styles.actionSuccess : styles.actionError} role="status">{copilot.actionFeedback.message}</p>}
                </section>}
                <div className={styles.toolTray} aria-label="Công cụ được phép">{tools.map(tool => <span className={styles.toolBadge} key={typeof tool === 'string' ? tool : tool.name}>{toolDisplayName(tool)}</span>)}</div>
                <div className={styles.prompts}>{suggestedPrompts.slice(0, 4).map(prompt => <button type="button" className={styles.prompt} key={prompt} onClick={() => void copilot.send(prompt)} disabled={copilot.loading}>{prompt}</button>)}</div>
                <form className={styles.form} onSubmit={event => { event.preventDefault(); void copilot.send(); }}>
                    <textarea ref={inputRef} className={styles.input} value={copilot.input} onChange={event => copilot.setInput(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void copilot.send(); } }} maxLength={500} rows={2} placeholder="Hỏi về công việc hoặc thông tin phòng khám…" aria-label="Nội dung Copilot" />
                    <button type="submit" className={styles.sendButton} disabled={copilot.loading || !copilot.input.trim()} aria-label="Gửi yêu cầu Copilot"><Send size={17} /></button>
                </form>
                {safeRoute(latestResponse?.navigationRoute) && <a className={styles.route} href={latestResponse!.navigationRoute!}><ExternalLink size={13} /> {copilot.config.navigationLabel}</a>}
            </section>
        </div>
    );
};

export default UnifiedCopilotPanel;
