import { formatDateTime } from '../../utils/formatters';
import { getGenderLabel } from '../../utils/genderLabels';
import React, { useState, useEffect, useCallback, useRef } from 'react';
import { Link, useParams, useNavigate } from 'react-router-dom';
import { 
    ArrowLeft, CheckCircle, PlayCircle, Save, Check, FlaskConical, Waves, Scan, Stethoscope
} from 'lucide-react';
import { Alert, Button, Card, Input, Modal, Select, Tag, Upload } from 'antd';
import { DiagnosticAttachmentList } from '../../components/diagnostics/DiagnosticAttachmentList';
import { DataTable, EmptyState, LoadingState, PageHeader, StatusBadge, InlineError } from '../../components/common';
import { diagnosticCategoryLabels, diagnosticStatusLabels, diagnosticStatusTone, diagnosticResultFlagLabels, diagnosticFlagTone, previewDiagnosticFlag, isCriticalDiagnosticFlag, diagnosticNotificationTime, formatDelta } from '../../utils/diagnosticLabels';
import styles from './TechnicianOrderDetail.module.css';
import { specimenStatusLabels, specimenRejectionLabels, specimenRejectionValues } from '../../utils/diagnosticLabels';
import type { SpecimenRejectionReason } from '../../types/diagnostic';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto, RecordDiagnosticResultRequest } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { useCopilotResource } from '../../components/copilot/copilotResourceContext';
import { ClinicalPageErrorBoundary } from '../../components/diagnostics/ClinicalPageErrorBoundary';
const categoryIcons = { Laboratory: FlaskConical, Ultrasound: Waves, Imaging: Scan, Other: Stethoscope };
const quickConclusions = ['Bình thường', 'Bất thường — đề nghị bác sĩ xem', 'Cần làm lại'];

export const TechnicianOrderDetail: React.FC = () => {
    const { id } = useParams();
    return <ClinicalPageErrorBoundary key={id}><TechnicianOrderContent /></ClinicalPageErrorBoundary>;
};

const TechnicianOrderContent: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const orderId = Number(id);
    const navigate = useNavigate();
    const { showAlert, showToast, showConfirm } = useDialog();
    const { setSelection } = useCopilotResource();

    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [actionLoading, setActionLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [expandedResults, setExpandedResults] = useState<Record<number, boolean>>({});
    const isMounted = useRef(false);
    const requestVersion = useRef(0);

    // Form inputs state for items
    const [itemResults, setItemResults] = useState<Record<number, {
        resultText: string;
        conclusion: string;
        referenceRange: string;
        unit: string;
    }>>({});
    const [copilotItemId, setCopilotItemId] = useState<number | null>(null);
    const [savingItemId, setSavingItemId] = useState<number | null>(null);
    const [parameterValues, setParameterValues] = useState<Record<number, Record<number, string>>>({});
    const [parameterErrors, setParameterErrors] = useState<Record<number, string>>({});
    const [specimenNotes, setSpecimenNotes] = useState<Record<number, string>>({});
    const [specimenBusy, setSpecimenBusy] = useState<number | null>(null);
    const [specimenError, setSpecimenError] = useState('');
    const [rejectItemId, setRejectItemId] = useState<number | null>(null);
    const [rejectionReason, setRejectionReason] = useState<SpecimenRejectionReason>('Hemolyzed');
    const [rejectionNote, setRejectionNote] = useState('');
    const [rejectionError, setRejectionError] = useState('');
    const [verificationNote, setVerificationNote] = useState('');
    const [returnOpen, setReturnOpen] = useState(false);
    const [returnReason, setReturnReason] = useState('');
    const [verificationBusy, setVerificationBusy] = useState(false);
    const [verificationError, setVerificationError] = useState('');
    const [attachmentBusy, setAttachmentBusy] = useState<{ itemId: number; action: 'upload' | 'delete' } | null>(null);
    const attachmentLock = useRef(false);

    const uploadAttachment = async (itemId: number, file: File) => {
        if (attachmentLock.current) {
            showToast('Một tệp khác đang được xử lý.', 'error');
            throw new Error('Một tệp khác đang được xử lý.');
        }
        const version = requestVersion.current;
        attachmentLock.current = true; setAttachmentBusy({ itemId, action: 'upload' });
        try {
            const response = await diagnosticApi.uploadAttachment(orderId, itemId, file);
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải tệp lên.');
            if (isMounted.current && version === requestVersion.current) setOrder(response.data);
        } catch (failure: unknown) {
            if (isMounted.current && version === requestVersion.current) showToast((failure as { message?: string })?.message || 'Không thể tải tệp lên.', 'error');
            throw failure;
        } finally {
            attachmentLock.current = false;
            if (isMounted.current) setAttachmentBusy(null);
        }
    };

    const deleteAttachment = async (itemId: number, attachmentId: number) => {
        if (attachmentLock.current) throw new Error('Một tệp khác đang được xử lý.');
        const version = requestVersion.current;
        attachmentLock.current = true; setAttachmentBusy({ itemId, action: 'delete' });
        try {
            const response = await diagnosticApi.deleteAttachment(orderId, itemId, attachmentId);
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể xóa tệp.');
            if (isMounted.current && version === requestVersion.current) setOrder(response.data);
        } finally {
            attachmentLock.current = false;
            if (isMounted.current) setAttachmentBusy(null);
        }
    };

    useEffect(() => {
        const selectedOrderId = order?.id;
        const selectedOrderVersion = order?.rowVersion ?? null;
        if (!selectedOrderId) {
            setSelection(null);
            return;
        }
        setSelection({
            context: { diagnosticOrderId: selectedOrderId, ...(copilotItemId ? { itemId: copilotItemId } : {}) },
            source: 'technician-order-detail',
            label: 'Phiếu cận lâm sàng đang mở',
            resourceVersion: selectedOrderVersion,
            actionArguments: copilotItemId ? {
                resultText: itemResults[copilotItemId]?.resultText?.trim() || undefined,
                conclusion: itemResults[copilotItemId]?.conclusion?.trim() || undefined,
                referenceRange: itemResults[copilotItemId]?.referenceRange?.trim() || undefined,
                unit: itemResults[copilotItemId]?.unit?.trim() || undefined
            } : undefined
        });
    }, [order?.id, order?.rowVersion, copilotItemId, itemResults, setSelection]);

    const loadOrder = useCallback(async () => {
        const version = ++requestVersion.current;
        if (!isMounted.current || version !== requestVersion.current) return;
        if (!orderId) { setOrder(null); setLoading(false); return; }
        setLoading(true);
        setError(null);
        try {
            const res = await diagnosticApi.getTechnicianOrderById(orderId);
            if (!isMounted.current || version !== requestVersion.current) return;
            if (!res.success) throw new Error(res.message || 'Không thể tải chi tiết phiếu chỉ định.');
            if (res.success && res.data) {
                setOrder(res.data);
                setCopilotItemId(previous => {
                    const incomplete = res.data!.items.filter(item => item.status !== 'Completed' && item.status !== 'Cancelled');
                    return previous && incomplete.some(item => item.id === previous)
                        ? previous
                        : incomplete.length === 1 ? incomplete[0].id : null;
                });
                
                // Prepopulate results
                const initial: typeof itemResults = {};
                const initialValues: typeof parameterValues = {};
                res.data.items.forEach(item => {
                    const firstValue = item.result?.values?.[0];
                    const savedText = item.result?.resultText || '';
                    const summaryStart = firstValue ? savedText.indexOf(`${firstValue.parameterName} (${firstValue.parameterCode}):`) : -1;
                    initial[item.id] = {
                        resultText: summaryStart >= 0 ? savedText.slice(0, summaryStart).trim() : savedText,
                        conclusion: item.result?.conclusion || '',
                        referenceRange: item.result?.referenceRange || '',
                        unit: item.result?.unit || ''
                    };
                    initialValues[item.id] = Object.fromEntries((item.result?.values || []).map(value => [value.parameterId, String(value.value)]));
                });
                setItemResults(initial);
                setParameterValues(initialValues);
                setParameterErrors({});
            } else setOrder(null);
        } catch (err: unknown) {
            if (isMounted.current && version === requestVersion.current) {
                const detailError = err as { message?: string; errorCode?: string; status?: number } | null;
                if (detailError?.errorCode === 'RESOURCE_NOT_FOUND' || detailError?.status === 404) {
                    setOrder(null);
                    setError(null);
                } else setError(detailError?.message || 'Không thể tải chi tiết phiếu chỉ định.');
            }
        } finally {
            if (isMounted.current && version === requestVersion.current) setLoading(false);
        }
    }, [orderId]);

    useEffect(() => {
        isMounted.current = true;
        void Promise.resolve().then(() => loadOrder());
        return () => { isMounted.current = false; };
    }, [loadOrder]);

    useEffect(() => {
        const refresh = () => { void loadOrder(); };
        window.addEventListener('cliniccare:copilot-action-completed', refresh);
        return () => window.removeEventListener('cliniccare:copilot-action-completed', refresh);
    }, [loadOrder]);

    const handleStartOrder = async () => {
        if (!order) return;
        setActionLoading(true);
        try {
            const res = await diagnosticApi.startTechnicianOrder(order.id, { rowVersion: order.rowVersion });
            if (res.success && res.data) {
                setOrder(res.data);
                showToast('Đã tiếp nhận thực hiện phiếu chỉ định!', 'success');
            }
        } catch (err: any) {
            showAlert(err.message || 'Không thể tiếp nhận thực hiện phiếu chỉ định.', 'Lỗi', 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleSaveItemResult = async (itemId: number) => {
        if (!order || order.verificationStatus === 'Pending') return;
        const currentData = itemResults[itemId];
        const item = order.items.find(i => i.id === itemId);
        const parameters = item?.parameters || [];
        const missing = parameters.filter(parameter => !(parameterValues[itemId]?.[parameter.id] ?? '').trim());
        const invalid = parameters.filter(parameter => {
            const value = Number(parameterValues[itemId]?.[parameter.id]);
            return !Number.isFinite(value) || value < 0 || value >= 1_000_000;
        });
        if (missing.length > 0 || invalid.length > 0) {
            setParameterErrors(previous => ({ ...previous, [itemId]: missing.length > 0
                ? `Vui lòng nhập đủ chỉ số: ${missing.map(parameter => parameter.name).join(', ')}.`
                : 'Giá trị chỉ số phải từ 0 đến dưới 1.000.000.' }));
            return;
        }
        if (!currentData || (parameters.length === 0 && !currentData.resultText.trim())) {
            showAlert('Vui lòng nhập kết quả chi tiết cho dịch vụ này.', 'Thiếu dữ liệu', 'warning');
            return;
        }

        setParameterErrors(previous => ({ ...previous, [itemId]: '' }));
        setSavingItemId(itemId);
        try {
            const req: RecordDiagnosticResultRequest = {
                resultText: currentData.resultText.trim(),
                conclusion: currentData.conclusion.trim() || undefined,
                referenceRange: currentData.referenceRange.trim() || undefined,
                unit: currentData.unit.trim() || undefined,
                rowVersion: item?.rowVersion
            };
            if (parameters.length > 0) {
                req.values = parameters.map(parameter => ({ parameterId: parameter.id, value: Number(parameterValues[itemId][parameter.id]) }));
                req.referenceRange = undefined;
                req.unit = undefined;
            }
            const res = await diagnosticApi.recordTechnicianItemResult(order.id, itemId, req);
            if (res.success && res.data) {
                setOrder(res.data);
                showToast('Đã lưu kết quả dịch vụ thành công!', 'success');
                return res.data;
            }
        } catch (err: any) {
            if (err.message?.includes('phiên làm việc khác') || err.errorCode === 'CONCURRENCY_CONFLICT') {
                showAlert('Dữ liệu đã được cập nhật từ phiên khác. Đang làm mới...', 'Xung đột dữ liệu', 'warning');
                await loadOrder();
            } else {
                showAlert(err.message || 'Không thể lưu kết quả dịch vụ.', 'Lỗi', 'error');
            }
        } finally {
            setSavingItemId(null);
        }
    };

    const handleSpecimen = async (itemId: number, action: 'collect' | 'receive' | 'reject') => {
        if (!order || specimenBusy !== null || order.verificationStatus === 'Pending') return;
        const item = order.items.find(current => current.id === itemId);
        if (!item) return;
        if (action === 'reject' && rejectionReason === 'Other' && rejectionNote.trim().length < 5) {
            setRejectionError('Lý do Khác cần ghi chú ít nhất 5 ký tự.');
            return;
        }
        setSpecimenBusy(itemId); setSpecimenError(''); setRejectionError('');
        const version = requestVersion.current;
        try {
            const response = action === 'collect'
                ? await diagnosticApi.collectSpecimen(order.id, itemId, { rowVersion: item.rowVersion, note: specimenNotes[itemId]?.trim() || undefined })
                : action === 'receive' ? await diagnosticApi.receiveSpecimen(order.id, itemId, { rowVersion: item.rowVersion })
                : await diagnosticApi.rejectSpecimen(order.id, itemId, { rowVersion: item.rowVersion, reason: specimenRejectionValues[rejectionReason], note: rejectionNote.trim() || undefined });
            if (!isMounted.current || version !== requestVersion.current) return;
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể cập nhật mẫu bệnh phẩm.');
            setOrder(response.data);
            setRejectItemId(null);
            setSpecimenNotes(previous => ({ ...previous, [itemId]: '' }));
        } catch (err: unknown) {
            if (!isMounted.current || version !== requestVersion.current) return;
            const failure = err as { message?: string; errorCode?: string };
            if (failure.errorCode === 'STATE_CONFLICT' || failure.errorCode === 'CONCURRENCY_CONFLICT' || failure.message?.includes('phiên làm việc khác')) {
                setSpecimenError('Dữ liệu đã được cập nhật từ phiên khác. Phiếu đã được tải lại.');
                setRejectItemId(null);
                await loadOrder();
            } else {
                const message = failure.message || 'Không thể cập nhật mẫu bệnh phẩm.';
                if (action === 'reject') setRejectionError(message);
                else setSpecimenError(message);
            }
        } finally { if (isMounted.current) setSpecimenBusy(null); }
    };

    const handleVerification = async (action: 'verify' | 'return') => {
        if (!order || verificationBusy || !order.canCurrentUserVerify || order.verificationStatus !== 'Pending') return;
        if (action === 'return' && returnReason.trim().length < 5) {
            setVerificationError('Lý do trả lại phải có ít nhất 5 ký tự.');
            return;
        }
        setVerificationBusy(true); setVerificationError('');
        const version = requestVersion.current;
        try {
            const response = action === 'verify'
                ? await diagnosticApi.verifyOrder(order.id, { note: verificationNote.trim() || undefined, rowVersion: order.rowVersion })
                : await diagnosticApi.returnOrder(order.id, { reason: returnReason.trim(), rowVersion: order.rowVersion });
            if (!isMounted.current || version !== requestVersion.current) return;
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể cập nhật duyệt kết quả.');
            setOrder(response.data); setReturnOpen(false); setVerificationNote(''); setReturnReason('');
        } catch (failure: unknown) {
            if (!isMounted.current || version !== requestVersion.current) return;
            const problem = failure as { errorCode?: string; message?: string };
            if (problem.errorCode === 'STATE_CONFLICT' || problem.errorCode === 'CONCURRENCY_CONFLICT') {
                setVerificationError('Dữ liệu đã thay đổi. Phiếu đã được tải lại.');
                setReturnOpen(false); await loadOrder();
            } else setVerificationError(problem.message || 'Không thể cập nhật duyệt kết quả.');
        } finally { if (isMounted.current) setVerificationBusy(false); }
    };

    const handleCompleteOrder = async () => {
        if (!order) return;

        // Check if all active items have results
        const unfinished = order.items.filter(i => i.status !== 'Completed' && i.status !== 'Cancelled');
        if (unfinished.length > 0) {
            showAlert(`Còn ${unfinished.length} dịch vụ chưa lưu kết quả. Vui lòng nhập và lưu kết quả cho tất cả dịch vụ trước khi hoàn tất.`, 'Chưa đủ kết quả', 'warning');
            return;
        }

        showConfirm('Xác nhận hoàn tất phiếu chỉ định và gửi kết quả về cho Bác sĩ?', async () => {
            setActionLoading(true);
            try {
                let currentOrder = order;
                for (const item of order.items.filter(item => item.status !== 'Cancelled')) {
                    if ((itemResults[item.id]?.conclusion.trim() || '') === (item.result?.conclusion?.trim() || '')) continue;
                    const saved = await handleSaveItemResult(item.id);
                    if (!saved) return;
                    currentOrder = saved;
                }
                const res = await diagnosticApi.completeTechnicianOrder(order.id, { rowVersion: currentOrder.rowVersion });
                if (res.success && res.data) {
                    setOrder(res.data);
                    showToast('Đã hoàn tất, bác sĩ đã nhận kết quả.', 'success');
                }
            } catch (err: any) {
                showAlert(err.message || 'Không thể hoàn tất phiếu chỉ định.', 'Lỗi', 'error');
            } finally {
                setActionLoading(false);
            }
        });
    };

    if (loading) return <LoadingState message="Đang tải thông tin phiếu chỉ định..." />;
    if (error) return <InlineError message={error} onRetry={() => { void loadOrder(); }} />;
    if (!order) return <EmptyState title="Không tìm thấy phiếu chỉ định." action={<Button type="primary" onClick={() => navigate('/diagnostics')}>Quay lại danh sách</Button>} />;

    const allItemsCompleted = order.items.every(i => i.status === 'Completed' || i.status === 'Cancelled');
    const activeItems = order.items.filter(item => item.status !== 'Cancelled');
    const resultedItems = activeItems.filter(item => item.result).length;
    const specimenItems = activeItems.filter(item => item.specimenStatus && item.specimenStatus !== 'NotRequired');
    const verificationPending = order.verificationStatus === 'Pending';
    const milestones = [
        { label: 'Chỉ định', time: order.orderedAtUtc, actor: order.orderingDoctorName },
        { label: 'Tiếp nhận', time: order.startedAtUtc, actor: order.startedByUserName },
        { label: 'Hoàn tất', time: order.completedAtUtc, actor: order.completedByUserName },
        { label: 'Bác sĩ đã xem', time: order.reviewedAtUtc, actor: order.reviewedByDoctorName },
    ];

    return (
        <div className={styles.detail}>
            {returnOpen && <Modal title="Trả lại kết quả" open={true} getContainer={false} onCancel={() => { if (!verificationBusy) setReturnOpen(false); }} footer={<><Button disabled={verificationBusy} onClick={() => setReturnOpen(false)}>Hủy</Button><Button danger type="primary" disabled={verificationBusy} onClick={() => void handleVerification('return')}>{verificationBusy ? 'Đang trả lại...' : 'Xác nhận trả lại'}</Button></>}>
                {verificationError && <Alert type="error" showIcon title={verificationError} />}
                <label htmlFor="verification-return-reason" className={styles.fieldLabel}>Lý do trả lại (bắt buộc, ít nhất 5 ký tự)</label><Input.TextArea id="verification-return-reason" rows={3} maxLength={500} value={returnReason} disabled={verificationBusy} onChange={event => { setReturnReason(event.target.value); setVerificationError(''); }} />
            </Modal>}
            {rejectItemId !== null && <Modal title="Từ chối mẫu" open={true} getContainer={false} onCancel={() => { if (specimenBusy === null) setRejectItemId(null); }}
                footer={<><Button disabled={specimenBusy !== null || verificationPending} onClick={() => setRejectItemId(null)}>Hủy</Button><Button danger type="primary" disabled={specimenBusy !== null || verificationPending} onClick={() => { if (rejectItemId !== null) void handleSpecimen(rejectItemId, 'reject'); }}>{specimenBusy !== null ? 'Đang từ chối...' : 'Xác nhận từ chối'}</Button></>}>
                <div className={styles.rejectionForm}>
                    {rejectionError && <Alert type="error" showIcon title={rejectionError} />}
                    <label htmlFor="specimen-rejection-reason" className={styles.fieldLabel}>Lý do từ chối</label>
                    <Select id="specimen-rejection-reason" aria-label="Lý do từ chối" value={rejectionReason} disabled={specimenBusy !== null || verificationPending}
                        options={Object.entries(specimenRejectionLabels).map(([value, label]) => ({ value, label }))} onChange={value => { setRejectionReason(value); setRejectionError(''); }} />
                    <label htmlFor="specimen-rejection-note" className={styles.fieldLabel}>Ghi chú {rejectionReason === 'Other' ? '(bắt buộc, ít nhất 5 ký tự)' : '(không bắt buộc)'}</label>
                    <Input.TextArea id="specimen-rejection-note" aria-label="Ghi chú từ chối" rows={3} maxLength={500} disabled={specimenBusy !== null || verificationPending} value={rejectionNote} onChange={event => { setRejectionNote(event.target.value); setRejectionError(''); }} />
                </div>
            </Modal>}
            <Link to="/diagnostics" className={styles.backLink}><ArrowLeft size={14} aria-hidden="true" /> Quay lại danh sách</Link>
            <PageHeader title="Thực hiện phiếu chỉ định CLS" />
            <div className={styles.navigation}>
                <div className={styles.orderStatus}><span>Trạng thái phiếu:</span><StatusBadge status={diagnosticStatusTone[order.status]} label={diagnosticStatusLabels[order.status]} /></div>
            </div>
            <Card className={styles.orderCard}>
                <div className={styles.orderHeading}>
                    <div className={styles.orderCode}>{order.orderCode}</div>
                    <div className={styles.appointmentInfo}><div>{order.visitCode ? 'Mã lượt khám:' : 'Mã lịch hẹn:'} <strong>{order.visitCode || order.appointmentCode}</strong></div><div className={styles.secondary}>Chỉ định lúc: {formatDateTime(order.orderedAtUtc)}</div></div>
                </div>
                <div className={styles.patientGrid}>
                    <div><div className={styles.secondary}>Bệnh nhân:</div><div className={styles.patientName}>{order.patientName}</div><div className={styles.secondary}>{getGenderLabel(order.patientGender)} • {order.patientAge ? `${order.patientAge} tuổi` : '---'}</div></div>
                    <div><div className={styles.secondary}>Số điện thoại:</div><strong>{order.patientPhone || '---'}</strong></div>
                    <div><div className={styles.secondary}>Bác sĩ chỉ định:</div><strong>{order.orderingDoctorName}</strong><div className={styles.secondary}>{order.specialtyName || '---'}</div></div>
                </div>
                <div className={styles.clinicalNotes}>{order.clinicalIndication && <div><strong>Chỉ định lâm sàng:</strong> <span>{order.clinicalIndication}</span></div>}{order.note && <div className={styles.doctorNote}><strong>Ghi chú từ bác sĩ:</strong> <span>{order.note}</span></div>}</div>
                {order.status === 'Ordered' && <div className={styles.startActions}><Button type="primary" onClick={handleStartOrder} disabled={actionLoading}><PlayCircle size={18} aria-hidden="true" /> {actionLoading ? 'Đang tiếp nhận...' : 'Bắt đầu thực hiện phiếu chỉ định'}</Button></div>}
            </Card>
            <Card className={styles.progressCard}>
                <strong>{resultedItems}/{activeItems.length} dịch vụ đã có kết quả</strong>
                {specimenItems.length > 0 && <p className={styles.secondary}>{specimenItems.filter(item => item.specimenStatus === 'Received').length}/{specimenItems.length} mẫu đã nhận</p>}
                <div className={styles.resultProgress} role="progressbar" aria-label="Tiến độ kết quả dịch vụ" aria-valuemin={0} aria-valuemax={Math.max(1, activeItems.length)} aria-valuenow={resultedItems}>
                    {activeItems.map(item => <span key={item.id} className={`${styles.progressSegment} ${item.result ? styles.progressSegmentDone : ''}`} />)}
                </div>
                <ol className={styles.timeline}>{milestones.map(milestone => <li key={milestone.label} className={`${styles.milestone} ${milestone.time ? styles.milestoneReached : styles.milestonePending}`}>
                    <strong>{milestone.label}</strong><span>{milestone.time ? formatDateTime(milestone.time) : 'Chưa thực hiện'}</span>{milestone.time && milestone.actor && <span>{milestone.actor}</span>}
                </li>)}</ol>
            </Card>
            {order.status === 'InProgress' && <Card className={styles.completionBar}><div className={styles.completionActions}><div><strong>Hoàn tất phiếu chỉ định cận lâm sàng</strong><div className={styles.secondary}>{allItemsCompleted ? 'Tất cả các dịch vụ đã có kết quả. Bạn có thể chốt phiếu.' : 'Vui lòng lưu kết quả cho từng dịch vụ trước khi hoàn tất.'}</div></div><Button type="primary" className={styles.completeButton} onClick={handleCompleteOrder} disabled={!allItemsCompleted || actionLoading}><Check size={18} aria-hidden="true" /> {actionLoading ? 'Đang hoàn tất...' : 'Hoàn tất phiếu'}</Button></div></Card>}
            <section className={styles.services}>
                {!returnOpen && verificationError && <Alert type="error" showIcon title={verificationError} />}
                {order.verificationStatus === 'Returned' && <Alert type="error" showIcon title={`Bị trả lại bởi ${order.returnedByUserName || '—'} lúc ${order.returnedAtUtc ? diagnosticNotificationTime(order.returnedAtUtc) : '—'}: ${order.returnReason || '—'}`} />}
                {verificationPending && <Card className={styles.verificationPanel}><Alert type="info" showIcon title="Đang chờ kỹ thuật viên khác duyệt" />{order.canCurrentUserVerify ? <div className={styles.verificationActions}><div className={styles.verificationNote}><label htmlFor="verification-note" className={styles.fieldLabel}>Ghi chú duyệt (không bắt buộc)</label><Input id="verification-note" maxLength={500} disabled={verificationBusy} value={verificationNote} onChange={event => setVerificationNote(event.target.value)} /></div><Button type="primary" disabled={verificationBusy} onClick={() => void handleVerification('verify')}>{verificationBusy ? 'Đang cập nhật...' : 'Duyệt kết quả'}</Button><Button danger disabled={verificationBusy} onClick={() => { setReturnOpen(true); setVerificationError(''); }}>Trả lại</Button></div> : <p>Bạn là người nhập kết quả nên không thể tự duyệt.</p>}</Card>}
                {order.verificationStatus === 'Verified' && <Card className={styles.verificationPanel}><p>Đã duyệt bởi {order.verifiedByUserName || '—'} lúc {order.verifiedAtUtc ? diagnosticNotificationTime(order.verifiedAtUtc) : '—'}</p><Link className={styles.resultPrintLink} to={`/diagnostics/orders/${order.id}/result-print`}>In phiếu kết quả</Link></Card>}
                {specimenError && <Alert type="error" showIcon title={specimenError} />}
                <h2 className={styles.sectionTitle}>Danh sách dịch vụ chỉ định ({order.items.length})</h2>
                <div className={styles.serviceList}>
                    {order.items.map((item, idx) => {
                        const current = itemResults[item.id] || { resultText: '', conclusion: '', referenceRange: '', unit: '' };
                        const isCompleted = item.status === 'Completed';
                        const collapsed = order.status === 'InProgress' && !!item.result && !expandedResults[item.id] && !verificationPending;
                        const CategoryIcon = categoryIcons[item.category];
                        const parameters = item.parameters || [];
                        const hasParameters = parameters.length > 0;
                        const requiresSpecimen = !!item.specimenStatus && item.specimenStatus !== 'NotRequired';
                        const canEnterResults = !requiresSpecimen || item.specimenStatus === 'Received';
                        const criticalPreview = parameters.some(parameter => {
                            const raw = parameterValues[item.id]?.[parameter.id] ?? '';
                            return raw.trim() !== '' && Number.isFinite(Number(raw)) && isCriticalDiagnosticFlag(previewDiagnosticFlag(Number(raw), parameter));
                        });
                        return (
                            <Card key={item.id}>
                                <div className={styles.serviceHeader}>
                                    <div className={styles.serviceIdentity}><div className={styles.serviceNumber}>{idx + 1}</div><div><div className={styles.serviceName}>{item.serviceName}</div><div className={styles.secondary}>Mã: {item.serviceCode}</div><Tag className={styles.categoryTag}><CategoryIcon size={13} aria-hidden="true" />{diagnosticCategoryLabels[item.category]}</Tag></div></div>
                                    {!isCompleted && order.status !== 'Completed' && <Button htmlType="button" onClick={() => setCopilotItemId(item.id)} className={copilotItemId === item.id ? styles.assistantSelected : undefined} aria-label={`Chọn ${item.serviceName} cho Copilot`}>Hỏi trợ lý</Button>}
                                    <StatusBadge status={diagnosticStatusTone[item.status]} label={diagnosticStatusLabels[item.status]} />
                                </div>
                                {item.preparationInstructions && <div className={styles.preparationNote}>Chuẩn bị: {item.preparationInstructions}</div>}
                                {(item.category === 'Imaging' || item.category === 'Ultrasound') && <section className={styles.attachmentPanel} aria-label={`Tệp đính kèm: ${item.serviceName}`}>
                                    <h3>Tệp đính kèm</h3>
                                    {order.status === 'InProgress' && !verificationPending && item.status !== 'Cancelled' && <Upload
                                        accept=".jpg,.jpeg,.png,.webp,.pdf" showUploadList={false}
                                        disabled={attachmentBusy !== null || (item.attachments?.length ?? 0) >= 10}
                                        beforeUpload={file => {
                                            if (file.size > 10 * 1024 * 1024) { showToast('Mỗi tệp không được vượt quá 10 MB.', 'error'); return Upload.LIST_IGNORE; }
                                            if (!/\.(jpe?g|png|webp|pdf)$/i.test(file.name) || (file.type && !['image/jpeg', 'image/png', 'image/webp', 'application/pdf'].includes(file.type))) {
                                                showToast('Chỉ chấp nhận JPG, PNG, WebP hoặc PDF.', 'error'); return Upload.LIST_IGNORE;
                                            }
                                            return true;
                                        }}
                                        customRequest={({ file, onSuccess, onError }) => {
                                            void uploadAttachment(item.id, file as File).then(() => onSuccess?.({})).catch((failure: unknown) => onError?.(new Error((failure as { message?: string })?.message || 'Không thể tải tệp lên.')));
                                        }}>
                                        <Button disabled={attachmentBusy !== null || (item.attachments?.length ?? 0) >= 10}>{attachmentBusy?.itemId === item.id ? attachmentBusy.action === 'upload' ? 'Đang tải lên...' : 'Đang xử lý...' : 'Thêm tệp đính kèm'}</Button>
                                    </Upload>}
                                    <p className={styles.secondary}>JPG, PNG, WebP hoặc PDF • Tối đa 10 MB/tệp, 10 tệp/dịch vụ.</p>
                                    <DiagnosticAttachmentList attachments={item.attachments} canDelete={order.status === 'InProgress' && !verificationPending && item.status !== 'Cancelled'} onDelete={attachmentId => deleteAttachment(item.id, attachmentId)} />
                                </section>}
                                {requiresSpecimen && <section className={styles.specimenPanel} aria-label={`Mẫu bệnh phẩm: ${item.serviceName}`}>
                                    <div className={styles.specimenHeading}><h3>Mẫu bệnh phẩm</h3><Tag className={item.specimenStatus === 'Rejected' ? styles.rejectedSpecimenTag : styles.specimenTag}>{specimenStatusLabels[item.specimenStatus!]}</Tag></div>
                                    <p>{item.specimenType || '—'} • {item.containerType || '—'}</p>
                                    {item.specimenBarcode && <p>Mã vạch: <strong>{item.specimenBarcode}</strong></p>}
                                    <div className={styles.specimenTimeline}>
                                        {item.collectedAtUtc && <p>Lấy: {diagnosticNotificationTime(item.collectedAtUtc)} • {item.collectedByUserName}</p>}
                                        {item.receivedAtUtc && <p>Nhận: {diagnosticNotificationTime(item.receivedAtUtc)} • {item.receivedByUserName}</p>}
                                        {item.rejectedAtUtc && <p>Từ chối: {diagnosticNotificationTime(item.rejectedAtUtc)} • {item.rejectedByUserName}</p>}
                                    </div>
                                    <p>Số lần lấy lại: {item.recollectCount ?? 0}</p>
                                    {item.specimenStatus === 'Rejected' && <Alert type="error" showIcon title={`Mẫu bị từ chối: ${item.rejectionReason ? specimenRejectionLabels[item.rejectionReason] : '—'}${item.rejectionNote ? ` — ${item.rejectionNote}` : ''}`} />}
                                    {order.status === 'InProgress' && !item.result && item.status !== 'Cancelled' && <div className={styles.specimenActions}>
                                        {(item.specimenStatus === 'PendingCollection' || item.specimenStatus === 'Rejected') && <>
                                            <div className={styles.collectionNote}><label htmlFor={`specimen-note-${item.id}`} className={styles.fieldLabel}>Ghi chú lấy mẫu (không bắt buộc)</label><Input id={`specimen-note-${item.id}`} maxLength={500} value={specimenNotes[item.id] || ''} onChange={event => setSpecimenNotes(previous => ({ ...previous, [item.id]: event.target.value }))} disabled={specimenBusy !== null || verificationPending} /></div>
                                            <Button type="primary" disabled={specimenBusy !== null || verificationPending} onClick={() => void handleSpecimen(item.id, 'collect')}>{specimenBusy === item.id ? 'Đang xác nhận...' : item.specimenStatus === 'Rejected' ? 'Lấy lại mẫu' : 'Xác nhận đã lấy mẫu'}</Button>
                                        </>}
                                        {item.specimenStatus === 'Collected' && <>
                                            <Button type="primary" disabled={specimenBusy !== null || verificationPending} onClick={() => void handleSpecimen(item.id, 'receive')}>{specimenBusy === item.id ? 'Đang cập nhật...' : 'Nhận mẫu đạt'}</Button>
                                            <Button danger disabled={specimenBusy !== null || verificationPending} onClick={() => { setRejectItemId(item.id); setRejectionReason('Hemolyzed'); setRejectionNote(''); setRejectionError(''); }}>Từ chối mẫu</Button>
                                        </>}
                                    </div>}
                                    {item.specimenStatus === 'Collected' && <Link className={styles.specimenPrintLink} to={`/diagnostics/orders/${order.id}/labels`}>In nhãn</Link>}
                                </section>}
                                {!!item.result?.values?.length && <div className={styles.parameterTable}>
                                    {item.result.values.some(value => value.isDeltaFailure) && <Alert type="warning" showIcon title="Có chỉ số lệch delta so với lần trước, kiểm tra mẫu/nhãn trước khi hoàn tất phiếu." />}
                                    <DataTable ariaLabel={`Chỉ số đã lưu: ${item.serviceName}`} data={item.result.values} keyExtractor={value => value.parameterId} columns={[
                                        { header: 'Chỉ số', accessor: value => `${value.parameterName} (${value.parameterCode})` },
                                        { header: 'Kết quả', accessor: value => value.value },
                                        { header: 'Đơn vị', accessor: 'unit' },
                                        { header: 'Khoảng tham chiếu', accessor: 'referenceRangeText' },
                                        { header: 'Đánh giá', accessor: value => <span className={styles[diagnosticFlagTone(value.flag)]}>{diagnosticResultFlagLabels[value.flag]}</span> },
                                        { header: 'So với lần trước', accessor: value => <>{formatDelta(value)}{value.isDeltaFailure && <> <Tag color="warning">Lệch delta</Tag></>}</> },
                                    ]} />
                                    {item.result.hasCriticalValue && item.result.criticalNotifiedAtUtc && <p className={styles.critical}>Đã báo bác sĩ lúc {diagnosticNotificationTime(item.result.criticalNotifiedAtUtc)}</p>}
                                </div>}
                                {order.status === 'InProgress' && item.result && <Button className={styles.resultToggle} aria-expanded={!collapsed} aria-controls={`result-form-${item.id}`} onClick={() => setExpandedResults(previous => ({ ...previous, [item.id]: !previous[item.id] }))}>{collapsed ? 'Xem/Sửa kết quả' : 'Thu gọn kết quả'}</Button>}
                                {!canEnterResults ? <div className={styles.startHint}>Cần nhận mẫu đạt trước khi nhập kết quả</div> : order.status === 'Ordered' ? <div className={styles.startHint}>Nhấn "Bắt đầu thực hiện" ở trên để tiếp nhận và nhập kết quả cho dịch vụ này.</div> : !collapsed && (
                                    <div id={`result-form-${item.id}`} className={styles.resultForm}>
                                        {hasParameters && order.status !== 'Completed' && <>
                                            {criticalPreview && <Alert type="error" showIcon title="Có giá trị nguy hiểm — hệ thống sẽ báo ngay cho bác sĩ chỉ định khi lưu." />}
                                            {parameterErrors[item.id] && <Alert type="error" showIcon title={parameterErrors[item.id]} />}
                                            <div className={styles.parameterTable}><DataTable ariaLabel={`Nhập chỉ số: ${item.serviceName}`} data={parameters} keyExtractor={parameter => parameter.id} columns={[
                                                { header: 'Chỉ số', accessor: parameter => `${parameter.name} (${parameter.code})` },
                                                { header: 'Kết quả', accessor: parameter => <Input type="number" step="any" min={0} aria-label={`Kết quả ${parameter.name} (${parameter.code})`} disabled={savingItemId === item.id || verificationPending}
                                                    value={parameterValues[item.id]?.[parameter.id] ?? ''} onChange={event => {
                                                        setParameterValues(previous => ({ ...previous, [item.id]: { ...previous[item.id], [parameter.id]: event.target.value } }));
                                                        setParameterErrors(previous => ({ ...previous, [item.id]: '' }));
                                                    }} /> },
                                                { header: 'Đơn vị', accessor: 'unit' },
                                                { header: 'Khoảng tham chiếu', accessor: 'referenceRangeText' },
                                                { header: 'Đánh giá', accessor: parameter => {
                                                    const raw = parameterValues[item.id]?.[parameter.id] ?? '';
                                                    if (!raw.trim() || !Number.isFinite(Number(raw))) return '—';
                                                    const flag = previewDiagnosticFlag(Number(raw), parameter);
                                                    return <span className={styles[diagnosticFlagTone(flag)]}>{diagnosticResultFlagLabels[flag]}</span>;
                                                } },
                                            ]} /></div>
                                        </>}
                                        <div><label htmlFor={`result-text-${item.id}`} className={styles.fieldLabel}>{hasParameters ? 'Nhận xét (không bắt buộc)' : <>Kết quả chi tiết / Mô tả tổn thương <span className={styles.required}>*</span></>}</label>
                                            <Input.TextArea id={`result-text-${item.id}`} rows={3} disabled={order.status === 'Completed' || verificationPending} value={current.resultText} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], resultText: e.target.value }}))} placeholder="Nhập chi tiết thông số xét nghiệm, mô tả hình ảnh siêu âm..." />
                                        </div>
                                        <div className={styles.resultFields}>
                                            <div><label htmlFor={`conclusion-${item.id}`} className={styles.fieldLabel}>Kết luận / Đánh giá</label><Input id={`conclusion-${item.id}`} type="text" disabled={order.status === 'Completed' || verificationPending} value={current.conclusion} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], conclusion: e.target.value }}))} placeholder="VD: Bình thường, Gan nhiễm mỡ độ 1..." />
                                                {order.status !== 'Completed' && <div className={styles.conclusionChips}>{quickConclusions.map(conclusion => <Button key={conclusion} size="small" disabled={savingItemId === item.id || verificationPending} onClick={() => setItemResults(previous => ({ ...previous, [item.id]: { ...previous[item.id], conclusion } }))}>{conclusion}</Button>)}</div>}
                                            </div>
                                            {!hasParameters && <><div><label htmlFor={`reference-range-${item.id}`} className={styles.fieldLabel}>Chỉ số bình thường</label><Input id={`reference-range-${item.id}`} type="text" disabled={order.status === 'Completed' || verificationPending} value={current.referenceRange} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], referenceRange: e.target.value }}))} placeholder="VD: 70 - 100 mg/dL" /></div>
                                            <div><label htmlFor={`unit-${item.id}`} className={styles.fieldLabel}>Đơn vị tính</label><Input id={`unit-${item.id}`} type="text" disabled={order.status === 'Completed' || verificationPending} value={current.unit} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], unit: e.target.value }}))} placeholder="VD: U/L, mg/dL" /></div></>}
                                        </div>
                                        {order.status !== 'Completed' && <div className={styles.saveActions}><Button type="primary" onClick={() => handleSaveItemResult(item.id)} disabled={savingItemId === item.id || verificationPending}><Save size={15} aria-hidden="true" /> {savingItemId === item.id ? 'Đang lưu...' : 'Lưu kết quả dịch vụ'}</Button></div>}
                                        {item.result && <div className={styles.resultAuthor}>Nhập bởi: {item.result.resultedByUserName} • {formatDateTime(item.result.resultedAtUtc)}</div>}
                                    </div>
                                )}
                            </Card>
                        );
                    })}
                </div>
            </section>
            {order.status === 'Completed' && <Card className={styles.completedCard}><div className={styles.completionActions}><div><div className={styles.completedTitle}><CheckCircle size={18} /> Phiếu chỉ định đã được hoàn tất</div><div className={styles.secondary}>Hoàn tất bởi: {order.completedByUserName} lúc {formatDateTime(order.completedAtUtc!)}</div></div>{order.reviewedAtUtc ? <div className={styles.reviewInfo}><strong>Bác sĩ đã xem:</strong> {order.reviewedByDoctorName} ({formatDateTime(order.reviewedAtUtc)})</div> : <div className={styles.reviewPending}>Chờ bác sĩ khám xác nhận đã xem</div>}</div></Card>}
        </div>
    );
};
