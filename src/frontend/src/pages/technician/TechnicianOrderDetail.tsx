import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { 
    ArrowLeft, CheckCircle, PlayCircle, Save, Check, FlaskConical, Waves, Scan, Stethoscope
} from 'lucide-react';
import { Button, Card, Input, Tag } from 'antd';
import { EmptyState, LoadingState, StatusBadge, InlineError } from '../../components/common';
import { diagnosticCategoryLabels, diagnosticStatusLabels, diagnosticStatusTone } from '../../utils/diagnosticLabels';
import styles from './TechnicianOrderDetail.module.css';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto, RecordDiagnosticResultRequest } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { useCopilotResource } from '../../components/copilot/copilotResourceContext';

const categoryIcons = { Laboratory: FlaskConical, Ultrasound: Waves, Imaging: Scan, Other: Stethoscope };
const quickConclusions = ['Bình thường', 'Bất thường — đề nghị bác sĩ xem', 'Cần làm lại'];

export const TechnicianOrderDetail: React.FC = () => {
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
                res.data.items.forEach(item => {
                    initial[item.id] = {
                        resultText: item.result?.resultText || '',
                        conclusion: item.result?.conclusion || '',
                        referenceRange: item.result?.referenceRange || '',
                        unit: item.result?.unit || ''
                    };
                });
                setItemResults(initial);
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
        if (!order) return;
        const currentData = itemResults[itemId];
        if (!currentData || !currentData.resultText.trim()) {
            showAlert('Vui lòng nhập kết quả chi tiết cho dịch vụ này.', 'Thiếu dữ liệu', 'warning');
            return;
        }

        const item = order.items.find(i => i.id === itemId);
        setSavingItemId(itemId);
        try {
            const req: RecordDiagnosticResultRequest = {
                resultText: currentData.resultText.trim(),
                conclusion: currentData.conclusion.trim() || undefined,
                referenceRange: currentData.referenceRange.trim() || undefined,
                unit: currentData.unit.trim() || undefined,
                rowVersion: item?.rowVersion
            };
            const res = await diagnosticApi.recordTechnicianItemResult(order.id, itemId, req);
            if (res.success && res.data) {
                setOrder(res.data);
                showToast('Đã lưu kết quả dịch vụ thành công!', 'success');
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
                const res = await diagnosticApi.completeTechnicianOrder(order.id, { rowVersion: order.rowVersion });
                if (res.success && res.data) {
                    setOrder(res.data);
                    showToast('Đã hoàn tất phiếu chỉ định cận lâm sàng!', 'success');
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
    const milestones = [
        { label: 'Chỉ định', time: order.orderedAtUtc, actor: order.orderingDoctorName },
        { label: 'Tiếp nhận', time: order.startedAtUtc, actor: order.startedByUserName },
        { label: 'Hoàn tất', time: order.completedAtUtc, actor: order.completedByUserName },
        { label: 'Bác sĩ đã xem', time: order.reviewedAtUtc, actor: order.reviewedByDoctorName },
    ];

    return (
        <div className={styles.detail}>
            <div className={styles.navigation}>
                <Button onClick={() => navigate('/diagnostics')}><ArrowLeft size={16} aria-hidden="true" /> Quay lại danh sách</Button>
                <div className={styles.orderStatus}><span>Trạng thái phiếu:</span><StatusBadge status={diagnosticStatusTone[order.status]} label={diagnosticStatusLabels[order.status]} /></div>
            </div>
            <Card className={styles.orderCard}>
                <div className={styles.orderHeading}>
                    <div><div className={styles.secondary}>PHIẾU CHỈ ĐỊNH CẬN LÂM SÀNG</div><div className={styles.orderCode}>{order.orderCode}</div></div>
                    <div className={styles.appointmentInfo}><div>Mã lịch hẹn: <strong>#{order.appointmentCode}</strong></div><div className={styles.secondary}>Chỉ định lúc: {new Date(order.orderedAtUtc).toLocaleString('vi-VN')}</div></div>
                </div>
                <div className={styles.patientGrid}>
                    <div><div className={styles.secondary}>Bệnh nhân:</div><div className={styles.patientName}>{order.patientName}</div><div className={styles.secondary}>{order.patientGender?.toLowerCase() === 'female' ? 'Nữ' : order.patientGender?.toLowerCase() === 'male' ? 'Nam' : 'Chưa cập nhật'} • {order.patientAge ? `${order.patientAge} tuổi` : '---'}</div></div>
                    <div><div className={styles.secondary}>Số điện thoại:</div><strong>{order.patientPhone || '---'}</strong></div>
                    <div><div className={styles.secondary}>Bác sĩ chỉ định:</div><strong>{order.orderingDoctorName}</strong><div className={styles.secondary}>{order.specialtyName || '---'}</div></div>
                </div>
                <div className={styles.clinicalNotes}><div><strong>Chỉ định lâm sàng:</strong> <span>{order.clinicalIndication}</span></div>{order.note && <div className={styles.doctorNote}><strong>Ghi chú từ bác sĩ:</strong> <span>{order.note}</span></div>}</div>
                {order.status === 'Ordered' && <div className={styles.startActions}><Button type="primary" onClick={handleStartOrder} disabled={actionLoading}><PlayCircle size={18} aria-hidden="true" /> {actionLoading ? 'Đang tiếp nhận...' : 'Bắt đầu thực hiện phiếu chỉ định'}</Button></div>}
            </Card>
            <Card className={styles.progressCard}>
                <strong>{resultedItems}/{activeItems.length} dịch vụ đã có kết quả</strong>
                <div className={styles.resultProgress} role="progressbar" aria-label="Tiến độ kết quả dịch vụ" aria-valuemin={0} aria-valuemax={Math.max(1, activeItems.length)} aria-valuenow={resultedItems}>
                    {activeItems.map(item => <span key={item.id} className={`${styles.progressSegment} ${item.result ? styles.progressSegmentDone : ''}`} />)}
                </div>
                <ol className={styles.timeline}>{milestones.map(milestone => <li key={milestone.label} className={`${styles.milestone} ${milestone.time ? styles.milestoneReached : styles.milestonePending}`}>
                    <strong>{milestone.label}</strong><span>{milestone.time ? new Date(milestone.time).toLocaleString('vi-VN') : 'Chưa thực hiện'}</span>{milestone.time && milestone.actor && <span>{milestone.actor}</span>}
                </li>)}</ol>
            </Card>
            <section className={styles.services}>
                <h2 className={styles.sectionTitle}>Danh sách dịch vụ chỉ định ({order.items.length})</h2>
                <div className={styles.serviceList}>
                    {order.items.map((item, idx) => {
                        const current = itemResults[item.id] || { resultText: '', conclusion: '', referenceRange: '', unit: '' };
                        const isCompleted = item.status === 'Completed';
                        const collapsed = order.status === 'InProgress' && !!item.result && !expandedResults[item.id];
                        const CategoryIcon = categoryIcons[item.category];
                        return (
                            <Card key={item.id}>
                                <div className={styles.serviceHeader}>
                                    <div className={styles.serviceIdentity}><div className={styles.serviceNumber}>{idx + 1}</div><div><div className={styles.serviceName}>{item.serviceName}</div><div className={styles.secondary}>Mã: {item.serviceCode}</div><Tag className={styles.categoryTag}><CategoryIcon size={13} aria-hidden="true" />{diagnosticCategoryLabels[item.category]}</Tag></div></div>
                                    {!isCompleted && order.status !== 'Completed' && <Button htmlType="button" type={copilotItemId === item.id ? 'primary' : 'default'} onClick={() => setCopilotItemId(item.id)} aria-label={`Chọn ${item.serviceName} cho Copilot`}>{copilotItemId === item.id ? 'Đã chọn Copilot' : 'Chọn cho Copilot'}</Button>}
                                    <StatusBadge status={diagnosticStatusTone[item.status]} label={diagnosticStatusLabels[item.status]} />
                                </div>
                                {item.preparationInstructions && <div className={styles.preparationNote}><strong>Chuẩn bị:</strong> {item.preparationInstructions}</div>}
                                {order.status === 'InProgress' && item.result && <Button className={styles.resultToggle} aria-expanded={!collapsed} aria-controls={`result-form-${item.id}`} onClick={() => setExpandedResults(previous => ({ ...previous, [item.id]: !previous[item.id] }))}>{collapsed ? 'Xem/Sửa kết quả' : 'Thu gọn kết quả'}</Button>}
                                {order.status === 'Ordered' ? <div className={styles.startHint}>Nhấn "Bắt đầu thực hiện" ở trên để tiếp nhận và nhập kết quả cho dịch vụ này.</div> : !collapsed && (
                                    <div id={`result-form-${item.id}`} className={styles.resultForm}>
                                        <div><label htmlFor={`result-text-${item.id}`} className={styles.fieldLabel}>Kết quả chi tiết / Mô tả tổn thương <span className={styles.required}>*</span></label>
                                            <Input.TextArea id={`result-text-${item.id}`} rows={3} disabled={order.status === 'Completed'} value={current.resultText} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], resultText: e.target.value }}))} placeholder="Nhập chi tiết thông số xét nghiệm, mô tả hình ảnh siêu âm..." />
                                        </div>
                                        <div className={styles.resultFields}>
                                            <div><label htmlFor={`conclusion-${item.id}`} className={styles.fieldLabel}>Kết luận / Đánh giá</label><Input id={`conclusion-${item.id}`} type="text" disabled={order.status === 'Completed'} value={current.conclusion} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], conclusion: e.target.value }}))} placeholder="VD: Bình thường, Gan nhiễm mỡ độ 1..." />
                                                {order.status !== 'Completed' && <div className={styles.conclusionChips}>{quickConclusions.map(conclusion => <Button key={conclusion} size="small" disabled={savingItemId === item.id} onClick={() => setItemResults(previous => ({ ...previous, [item.id]: { ...previous[item.id], conclusion } }))}>{conclusion}</Button>)}</div>}
                                            </div>
                                            <div><label htmlFor={`reference-range-${item.id}`} className={styles.fieldLabel}>Chỉ số bình thường</label><Input id={`reference-range-${item.id}`} type="text" disabled={order.status === 'Completed'} value={current.referenceRange} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], referenceRange: e.target.value }}))} placeholder="VD: 70 - 100 mg/dL" /></div>
                                            <div><label htmlFor={`unit-${item.id}`} className={styles.fieldLabel}>Đơn vị tính</label><Input id={`unit-${item.id}`} type="text" disabled={order.status === 'Completed'} value={current.unit} onChange={e => setItemResults(prev => ({...prev,[item.id]: { ...prev[item.id], unit: e.target.value }}))} placeholder="VD: U/L, mg/dL" /></div>
                                        </div>
                                        {order.status !== 'Completed' && <div className={styles.saveActions}><Button type="primary" onClick={() => handleSaveItemResult(item.id)} disabled={savingItemId === item.id}><Save size={15} aria-hidden="true" /> {savingItemId === item.id ? 'Đang lưu...' : 'Lưu kết quả dịch vụ'}</Button></div>}
                                        {item.result && <div className={styles.resultAuthor}>Nhập bởi: {item.result.resultedByUserName} • {new Date(item.result.resultedAtUtc).toLocaleString('vi-VN')}</div>}
                                    </div>
                                )}
                            </Card>
                        );
                    })}
                </div>
            </section>
            {order.status === 'InProgress' && <Card className={styles.completionBar}><div className={styles.completionActions}><div><strong>Hoàn tất phiếu chỉ định cận lâm sàng</strong><div className={styles.secondary}>{allItemsCompleted ? 'Tất cả các dịch vụ đã có kết quả. Bạn có thể chốt phiếu.' : 'Vui lòng lưu kết quả cho từng dịch vụ trước khi hoàn tất.'}</div></div><Button type="primary" className={styles.completeButton} onClick={handleCompleteOrder} disabled={!allItemsCompleted || actionLoading}><Check size={18} aria-hidden="true" /> {actionLoading ? 'Đang gửi kết quả...' : 'Hoàn tất và gửi kết quả'}</Button></div></Card>}
            {order.status === 'Completed' && <Card className={styles.completedCard}><div className={styles.completionActions}><div><div className={styles.completedTitle}><CheckCircle size={18} /> Phiếu chỉ định đã được hoàn tất</div><div className={styles.secondary}>Hoàn tất bởi: {order.completedByUserName} lúc {new Date(order.completedAtUtc!).toLocaleString('vi-VN')}</div></div>{order.reviewedAtUtc ? <div className={styles.reviewInfo}><strong>Bác sĩ đã xem:</strong> {order.reviewedByDoctorName} ({new Date(order.reviewedAtUtc).toLocaleString('vi-VN')})</div> : <div className={styles.reviewPending}>Chờ bác sĩ khám xác nhận đã xem</div>}</div></Card>}
        </div>
    );
};
