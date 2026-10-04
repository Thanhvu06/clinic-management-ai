import React, { useEffect, useRef, useState } from 'react';
import { Bot, CheckCircle2, ExternalLink, RotateCcw, Send, ShieldCheck, Square, X } from 'lucide-react';
import { useUnifiedCopilot, type UnifiedCopilotMessage } from './useUnifiedCopilot';
import { ProviderStatus } from './ProviderStatus';
import './chatTokens.css';
import { toolDisplayName } from './copilotConfig';
import { copilotCardEmptyMessage, renderCopilotCardData } from './copilotDataRenderers';
import { SuggestionChips } from './SuggestionChips';
import { sanitizeSuggestions } from './useSuggestionMenu';
import styles from './UnifiedCopilotPanel.module.css';

const safeRoute = (route?: string | null): route is string => Boolean(route && route.startsWith('/') && !route.startsWith('//') && !route.includes('://') && !route.includes('..') && !route.includes('\\'));

const normalizeText = (value?: string | null) => value?.trim().replace(/\s+/g, ' ').toLocaleLowerCase('vi-VN');

const MessageBubble: React.FC<{
    item: UnifiedCopilotMessage;
    onRetry: (item: UnifiedCopilotMessage) => void;
    latest: boolean;
}> = ({ item, onRetry, latest }) => {
    const response = item.response;
    const cards = response?.cards ?? [];
    return (
        <div className={styles.messageRow} data-role={item.role} id={latest ? "copilot-latest-reply" : undefined}>
            <div data-chat-bubble className={`${styles.bubble} ${item.role === 'user' ? styles.user : styles.assistant} ${item.error ? /quá nhiều|giới hạn|phản hồi quá lâu/i.test(item.content) ? styles.clarification : styles.error : ''}`}>
                <div>{item.content}</div>
                {import.meta.env.DEV && response?.errorCode && <div className={styles.clarification}>Mã xử lý: {response.errorCode}</div>}
                {response?.clarification && normalizeText(response.clarification) !== normalizeText(item.content) && <div className={styles.clarification}>{response.clarification}</div>}
                {response?.safetyNotice && normalizeText(response.safetyNotice) !== normalizeText(item.content) && normalizeText(response.safetyNotice) !== normalizeText(response.clarification) && <div className={styles.clarification}>{response.safetyNotice}</div>}
                {cards.map((card, index) => {
                    // An empty card shows no frame, title or sources: only one
                    // secondary line that adds to the reply. A public catalog
                    // description names what was searched, so it wins over the
                    // generic empty message; staff cards keep the empty message.
                    const emptyMessage = copilotCardEmptyMessage(card);
                    if (emptyMessage !== null) {
                        const lines = card.type === 'clinic_knowledge' ? [card.description?.trim(), emptyMessage] : [emptyMessage];
                        const secondary = lines.find(line => line && normalizeText(line) !== normalizeText(item.content));
                        return secondary ? <p className={styles.cardDescription} key={`${card.type}-${index}`}>{secondary}</p> : null;
                    }
                    const sources = Array.from(new Map(
                        [...(card.sources ?? []), ...(index === 0 ? (response?.sources ?? []) : [])]
                            .map(source => [`${source.name}:${source.kind}`, source] as const)
                    ).values());
                    return (
                        <article className={styles.card} key={`${card.type}-${index}`}>
                            <div className={styles.cardTitle}><span>{card.title}</span></div>
                            {card.description && normalizeText(card.description) !== normalizeText(item.content) && <p className={styles.cardDescription}>{card.description}</p>}
                            {card.retrievedAtUtc && <p className={styles.cardDescription}>Dữ liệu đọc lúc: {new Date(card.retrievedAtUtc).toLocaleString('vi-VN')}</p>}
                            {renderCopilotCardData(card)}
                            {sources.length > 0 && <div className={styles.sources} aria-label="Nguồn dữ liệu">
                                {sources.slice(0, 5).map((source, sourceIndex) => <span className={styles.source} title={`${source.name} · ${source.kind ?? ''} · ${source.status ?? ''}`} key={`${source.name}-${sourceIndex}`}>{import.meta.env.DEV ? `${source.name}${source.kind ? ` · ${source.kind}` : ''}${source.status ? ` · ${source.status}` : ''}` : source.kind === 'database' ? 'Dữ liệu phòng khám' : 'Danh mục phòng khám'}</span>)}
                            </div>}
                        </article>
                    );
                })}
                {item.error && item.retryText && <button type="button" className={styles.retryButton} onClick={() => onRetry(item)}><RotateCcw size={13} /> Thử lại</button>}
            </div>
        </div>
    );
};

export const UnifiedCopilotPanel: React.FC = () => {
    const copilot = useUnifiedCopilot();
    const launcherRef = useRef<HTMLButtonElement>(null);
    const inputRef = useRef<HTMLTextAreaElement>(null);
    const panelRef = useRef<HTMLElement>(null);
    const [actionTrayOpen, setActionTrayOpen] = useState(false);

    useEffect(() => {
        if (!copilot.open) {
            launcherRef.current?.focus();
            return;
        }
        const timer = window.setTimeout(() => inputRef.current?.focus(), 0);
        return () => window.clearTimeout(timer);
    }, [copilot.open]);

    useEffect(() => {
        panelRef.current?.querySelector('#copilot-latest-reply')?.scrollIntoView({ block: 'start', behavior: 'instant' });
    }, [copilot.messages, copilot.open]);

    const onPanelKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            copilot.setOpen(false);
            return;
        }
        if (event.key !== 'Tab' || !panelRef.current) return;
        const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>('button:not(:disabled), textarea, a[href], summary')).filter(element => !element.closest('details:not([open])') || element.tagName === 'SUMMARY');
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };

    if (!copilot.user) return null;
    if (!copilot.open) return <div className={styles.shell}><button ref={launcherRef} type="button" className={styles.launcher} onClick={() => copilot.setOpen(true)} aria-label={`Mở ${copilot.config.label}`}><Bot size={18} /> {copilot.config.shortLabel}</button></div>;

    const latestAssistant = copilot.messages.slice().reverse().find(message => message.role === 'assistant');
    const empty = !copilot.messages.some(message => message.role === 'user');
    const latestResponse = copilot.messages.slice().reverse().find(message => message.response)?.response;
    // providerStatus is a legacy/raw detail (for example "Timeout"); the
    // stable UI state is providerState (Degraded/Unavailable/etc.).
    const providerStatus = latestResponse?.providerState ?? latestResponse?.providerStatus ?? 'NotCalled';
    const needsMenu = !latestAssistant?.response || !latestAssistant.response.suggestions?.length || latestAssistant.response.assistantMode === 'Clarifying' || latestAssistant.response.executionMode === 'ManualHandoff';
    const suggestions = sanitizeSuggestions(latestAssistant?.response?.suggestions?.length ? latestAssistant.response.suggestions : needsMenu ? copilot.menuSuggestions : []);
    const hasCase = Boolean(copilot.resourceContext?.appointmentId || copilot.resourceContext?.visitId);
    const suggestedPrompts = copilot.role === 'Doctor' && !hasCase ? ['Xem hàng đợi của tôi'] : latestResponse?.suggestedPrompts?.length ? latestResponse.suggestedPrompts : copilot.config.prompts;
    const pendingCapability = copilot.pendingAction
        ? copilot.actionCapabilities.find(capability => capability.tool.name === copilot.pendingAction?.toolName)
        : undefined;

    return (
        <div className={styles.shell}>
            <section ref={panelRef} className={styles.panel} data-chat-panel role="dialog" aria-modal="true" aria-label={copilot.config.label} onKeyDown={onPanelKeyDown}>
                <header className={styles.header}>
                    <div className={styles.title}><Bot size={20} /><div className={styles.titleText}><strong>{copilot.config.label}</strong><span>{copilot.config.description}</span></div></div>
                    <div className={styles.headerActions}>
                        {copilot.loading && <button type="button" className={`${styles.iconButton} ${styles.cancelButton}`} onClick={copilot.abort} aria-label="Dừng yêu cầu Copilot"><Square size={16} /></button>}
                        <button type="button" className={styles.iconButton} onClick={() => { setActionTrayOpen(false); copilot.reset(); }} aria-label="Đặt lại phiên Copilot"><RotateCcw size={16} /></button>
                        <button type="button" className={styles.iconButton} onClick={() => copilot.setOpen(false)} aria-label="Đóng Copilot"><X size={18} /></button>
                    </div>
                </header>
                <ProviderStatus state={providerStatus} error={latestAssistant?.error ? latestAssistant.content : undefined} detail={`${latestResponse?.assistantMode ?? 'Ready'} · ${latestResponse?.plannerMode ?? 'Deterministic'}`} />
                <div className={styles.messages} data-chat-messages aria-live="polite" aria-relevant="additions">
                    <p className={styles.cardDescription}><ShieldCheck size={13} /> Thông tin được kiểm tra theo quyền của bạn. Thao tác thay đổi cần xem trước và xác nhận.</p>
                    {copilot.messages.map(message => <MessageBubble
                        key={message.id}
                        item={message}
                        latest={message.id === latestAssistant?.id}
                        onRetry={failed => void (failed.retrySuggestionCode
                            ? copilot.sendSuggestion({ code: failed.retrySuggestionCode, label: failed.retryText ?? '' })
                            : copilot.send(failed.retryText ?? ''))}
                    />)}
                    {copilot.loading && <div className={`${styles.messageRow} ${styles.assistant}`}><div className={styles.bubble}>Đang kiểm tra dữ liệu…</div></div>}
                {copilot.actionCapabilities.length > 0 && <details className={styles.actionTray} aria-label="Thao tác có xác nhận" open={Boolean(actionTrayOpen || copilot.pendingAction || copilot.actionFeedback)} onToggle={event => setActionTrayOpen(event.currentTarget.open)}>
                    <summary className={styles.actionHeading}>Thao tác có xác nhận ({copilot.actionCapabilities.length})<span>Xem trước và xác nhận trước khi thực hiện.</span></summary>
                    {copilot.resourceSelection && <p className={styles.cardDescription}>Thông tin đang mở: {copilot.resourceSelection.label ?? copilot.resourceSelection.source}</p>}
                    {copilot.catalogError && <p className={styles.actionError}>{copilot.catalogError}</p>}
                    {copilot.actionCapabilities.map(capability => <div className={styles.actionRow} key={capability.tool.name}>
                        <div className={styles.actionCopy}><strong>{toolDisplayName(capability.tool)}</strong><span>{capability.tool.description}</span>{!capability.enabled && <small>{capability.reason}</small>}</div>
                        <button type="button" className={styles.actionButton} disabled={!capability.enabled || Boolean(copilot.actionLoading)} onClick={() => void copilot.prepareAction(capability)}>{copilot.actionLoading === capability.tool.name ? 'Đang chuẩn bị…' : 'Xem trước'}</button>
                    </div>)}
                    {copilot.pendingAction && <div className={styles.actionPreview} aria-label="Xem trước thao tác">
                        <div className={styles.cardTitle}><span>Thông tin xem trước</span><span>{copilot.pendingActionExpired ? 'Đã hết hạn' : 'Chờ xác nhận'}</span></div>
                        <div className={styles.previewLine}><strong>Thông tin đã chọn:</strong> {copilot.pendingAction.preview.resource.identity}</div>
                        {copilot.pendingAction.preview.resource.facility && <div className={styles.previewLine}><strong>Cơ sở:</strong> {copilot.pendingAction.preview.resource.facility}</div>}
                        {copilot.pendingAction.preview.resource.department && <div className={styles.previewLine}><strong>Khoa:</strong> {copilot.pendingAction.preview.resource.department}</div>}
                        {copilot.pendingAction.preview.resource.subject && <div className={styles.previewLine}><strong>Đối tượng:</strong> {copilot.pendingAction.preview.resource.subject}</div>}
                        {copilot.pendingAction.preview.resource.encounter && <div className={styles.previewLine}><strong>Ca khám:</strong> {copilot.pendingAction.preview.resource.encounter}</div>}
                        {copilot.pendingAction.preview.resource.currentStatus && <div className={styles.previewLine}><strong>Trạng thái hiện tại:</strong> {copilot.pendingAction.preview.resource.currentStatus}</div>}
                        <div className={styles.previewLine}><strong>Thay đổi sau xác nhận:</strong></div>
                        {copilot.pendingAction.preview.changes.map(change => <div className={styles.previewLine} key={change.kind}><strong>{change.summary}</strong>{change.items.map(item => <div key={`${change.kind}-${item.label}-${item.value}`}>&nbsp;{item.label}: {item.value}{item.quantity !== null && item.quantity !== undefined ? ` · SL ${item.quantity}${item.unit ? ` ${item.unit}` : ''}` : ''}</div>)}</div>)}
                        <div className={styles.previewLine}><strong>Hậu quả:</strong> {copilot.pendingAction.preview.consequence}</div>
                        <div className={styles.previewLine}><strong>Kiểm tra hệ thống:</strong> {copilot.pendingAction.preview.confirmationSummary}</div>
                        <div className={styles.previewLine}><strong>Dữ liệu kiểm tra lúc:</strong> {new Date(copilot.pendingAction.preview.validatedAtUtc).toLocaleString('vi-VN')}</div>
                        <div className={styles.previewLine}><strong>Nguồn kiểm tra:</strong> {copilot.pendingAction.preview.sources.map(source => `${source.name} · ${source.kind}${source.status ? ` · ${source.status}` : ''}`).join('; ')}</div>
                        <div className={styles.previewLine}><strong>Hết hạn:</strong> {new Date(copilot.pendingAction.preview.expiresAtUtc).toLocaleString('vi-VN')}</div>
                        {copilot.pendingActionExpired && <p className={styles.actionError} role="alert">Thông tin xem trước đã hết hạn. Hãy chuẩn bị lại để nhận dữ liệu kiểm tra mới.</p>}
                        <button type="button" className={styles.confirmButton} disabled={Boolean(copilot.actionLoading) || copilot.pendingActionExpired} onClick={() => void copilot.confirmAction()}><CheckCircle2 size={14} /> Xác nhận thao tác</button>
                        <button type="button" className={styles.actionButton} disabled={Boolean(copilot.actionLoading)} onClick={() => void copilot.cancelAction()}>Hủy thao tác</button>
                        {copilot.pendingActionExpired && pendingCapability?.enabled && <button type="button" className={styles.actionButton} disabled={Boolean(copilot.actionLoading)} onClick={() => void copilot.prepareAction(pendingCapability)}>Chuẩn bị lại xem trước</button>}
                    </div>}
                    {copilot.actionFeedback && <p className={copilot.actionFeedback.status === 'completed' ? styles.actionSuccess : copilot.actionFeedback.status === 'cancelled' ? styles.actionNeutral : styles.actionError} role="status">{copilot.actionFeedback.message}</p>}
                </details>}
                </div>
                <div className={styles.composer} data-chat-composer>
                {suggestions.length > 0 && <div className={styles.suggestionFooter} aria-describedby="copilot-latest-reply">
                    {(latestAssistant?.response?.assistantMode === 'Clarifying' || latestAssistant?.response?.executionMode === 'ManualHandoff') && <p className={styles.cardDescription}>Bạn có thể chọn một trong các gợi ý bên dưới.</p>}
                    <SuggestionChips suggestions={suggestions} variant={empty ? 'grid' : 'compact'} disabled={copilot.loading} onSelect={suggestion => void copilot.sendSuggestion(suggestion)} ariaLabel={empty || !latestAssistant?.response ? "Gợi ý theo vai trò" : "Gợi ý tiếp theo"} />
                </div>}
                {suggestions.length === 0 && <div className={styles.prompts} role="group" aria-label="Gợi ý dự phòng">{suggestedPrompts.slice(0, 4).map(prompt => <button type="button" className={styles.prompt} key={prompt} onClick={() => void copilot.send(prompt)} disabled={copilot.loading}>{prompt}</button>)}</div>}
                <form className={styles.form} onSubmit={event => { event.preventDefault(); void copilot.send(); }}>
                    <textarea ref={inputRef} className={styles.input} value={copilot.input} onChange={event => copilot.setInput(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void copilot.send(); } }} maxLength={500} rows={2} placeholder="Hỏi về công việc hoặc thông tin phòng khám…" aria-label="Nội dung Copilot" />
                    <button type="submit" className={styles.sendButton} disabled={copilot.loading || !copilot.input.trim()} aria-label="Gửi yêu cầu Copilot"><Send size={17} /></button>
                </form>
                {safeRoute(latestResponse?.navigationRoute) && <a className={styles.route} href={latestResponse!.navigationRoute!}><ExternalLink size={13} /> {copilot.config.navigationLabel}</a>}
                </div>
            </section>
        </div>
    );
};

export default UnifiedCopilotPanel;
