import { useEffect, useRef, useState } from 'react';
import { Button, Image } from 'antd';
import { FileText } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticAttachmentDto } from '../../types/diagnostic';
import { useDialog } from '../../contexts/DialogContext';
import { diagnosticNotificationTime } from '../../utils/diagnosticLabels';
import styles from './DiagnosticAttachmentList.module.css';

interface Props {
    attachments?: DiagnosticAttachmentDto[];
    canDelete?: boolean;
    onDelete?: (attachmentId: number) => Promise<void>;
    compact?: boolean;
}

function Attachment({ attachment, canDelete, onDelete }: { attachment: DiagnosticAttachmentDto } & Pick<Props, 'canDelete' | 'onDelete'>) {
    const { showConfirm, showToast } = useDialog();
    const [url, setUrl] = useState<string>();
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const [deleting, setDeleting] = useState(false);
    const [attempt, setAttempt] = useState(0);
    const urls = useRef(new Set<string>());
    const lifecycle = useRef({ active: false });
    const deleteLock = useRef(false);
    const pdfLock = useRef(false);
    const isPdf = attachment.contentType === 'application/pdf';

    useEffect(() => {
        const current = { active: true };
        lifecycle.current = current;
        const createdUrls = urls.current;
        if (!isPdf) {
            void diagnosticApi.getAttachmentBlob(attachment.id).then(blob => {
                if (!current.active) return;
                const objectUrl = URL.createObjectURL(blob);
                createdUrls.add(objectUrl);
                setUrl(objectUrl);
            }).catch(() => { if (current.active) setError('Không thể tải ảnh.'); });
        }
        return () => {
            current.active = false;
            createdUrls.forEach(value => URL.revokeObjectURL(value));
            createdUrls.clear();
        };
    }, [attachment.id, isPdf, attempt]);

    const openPdf = async () => {
        if (pdfLock.current) return;
        // Open during the user gesture so browsers do not block the asynchronous preview.
        const tab = window.open('about:blank', '_blank');
        if (!tab) { setError('Trình duyệt đã chặn tab xem PDF. Vui lòng cho phép rồi thử lại.'); return; }
        tab.opener = null;
        if (url) { tab.location.replace(url); return; }
        const current = lifecycle.current;
        pdfLock.current = true; setBusy(true); setError('');
        try {
            const blob = await diagnosticApi.getAttachmentBlob(attachment.id);
            if (!current.active) { tab.close(); return; }
            const objectUrl = URL.createObjectURL(blob);
            urls.current.add(objectUrl);
            setUrl(objectUrl);
            tab.location.replace(objectUrl);
        } catch {
            tab.close();
            if (current.active) setError('Không thể tải PDF.');
        } finally {
            pdfLock.current = false;
            if (current.active) setBusy(false);
        }
    };

    const remove = () => showConfirm(`Xóa tệp “${attachment.originalFileName}”?`, () => {
        if (!onDelete || deleteLock.current || !lifecycle.current.active) return;
        const current = lifecycle.current;
        deleteLock.current = true; setDeleting(true);
        void onDelete(attachment.id).catch((failure: unknown) => {
            if (current.active) showToast((failure as { message?: string })?.message || 'Không thể xóa tệp.', 'error');
        }).finally(() => { deleteLock.current = false; if (current.active) setDeleting(false); });
    }, 'Xóa tệp đính kèm');

    const size = attachment.sizeBytes >= 1024 * 1024
        ? `${(attachment.sizeBytes / (1024 * 1024)).toFixed(1)} MB`
        : `${Math.max(1, Math.ceil(attachment.sizeBytes / 1024))} KB`;
    return <li className={styles.attachment}>
        <div className={styles.preview}>
            {isPdf ? <Button type="text" aria-label={`Mở PDF ${attachment.originalFileName}`} disabled={busy} onClick={() => void openPdf()}><FileText aria-hidden="true" />{busy ? 'Đang tải PDF...' : 'PDF'}</Button>
                : error ? null : url ? <Image className={styles.image} src={url} alt={attachment.originalFileName} onError={() => setError('Không thể hiển thị ảnh.')} /> : <span role="status">Đang tải ảnh...</span>}
        </div>
        <div className={styles.details}>
            {isPdf ? <Button type="link" className={styles.fileName} disabled={busy} onClick={() => void openPdf()}>{attachment.originalFileName}</Button> : <span className={styles.fileName}>{attachment.originalFileName}</span>}
            <span className={styles.metadata}>{size} • {diagnosticNotificationTime(attachment.uploadedAtUtc)} • {attachment.uploadedByUserName}</span>
            {error && <div role="alert" className={styles.error}>{error} <Button size="small" disabled={busy} onClick={() => {
                if (isPdf) void openPdf();
                else { setError(''); setUrl(undefined); setAttempt(value => value + 1); }
            }}>Thử lại</Button></div>}
        </div>
        {canDelete && onDelete && <Button danger size="small" disabled={deleting} onClick={remove} aria-label={deleting ? 'Đang xóa...' : `Xóa ${attachment.originalFileName}`}>{deleting ? 'Đang xóa...' : 'Xóa'}</Button>}
    </li>;
}

export function DiagnosticAttachmentList({ attachments, canDelete = false, onDelete, compact = false }: Props) {
    if (!attachments?.length) return <p className={styles.metadata}>Chưa có tệp đính kèm.</p>;
    // Recreate children on a changed list, revoking every URL from the previous list.
    const signature = JSON.stringify(attachments);
    return <ul key={signature} className={`${styles.attachments} ${compact ? styles.compact : ''}`} aria-label="Tệp đính kèm">
        {attachments.map(attachment => <Attachment key={attachment.id} attachment={attachment} canDelete={canDelete} onDelete={onDelete} />)}
    </ul>;
}
