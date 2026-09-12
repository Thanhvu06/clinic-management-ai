import React, { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { 
    ArrowLeft, CheckCircle, PlayCircle, Save, Check 
} from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto, RecordDiagnosticResultRequest } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

export const TechnicianOrderDetail: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const orderId = Number(id);
    const navigate = useNavigate();
    const { showAlert, showToast, showConfirm } = useDialog();

    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [actionLoading, setActionLoading] = useState(false);

    // Form inputs state for items
    const [itemResults, setItemResults] = useState<Record<number, {
        resultText: string;
        conclusion: string;
        referenceRange: string;
        unit: string;
    }>>({});
    const [savingItemId, setSavingItemId] = useState<number | null>(null);

    const loadOrder = useCallback(async () => {
        if (!orderId) return;
        try {
            const res = await diagnosticApi.getTechnicianOrderById(orderId);
            if (res.success && res.data) {
                setOrder(res.data);
                
                // Prepopulate results
                const initial: Record<number, any> = {};
                res.data.items.forEach(item => {
                    initial[item.id] = {
                        resultText: item.result?.resultText || '',
                        conclusion: item.result?.conclusion || '',
                        referenceRange: item.result?.referenceRange || '',
                        unit: item.result?.unit || ''
                    };
                });
                setItemResults(initial);
            }
        } catch (err: any) {
            showAlert(err.message || 'Không thể tải chi tiết phiếu chỉ định.', 'Lỗi', 'error');
        }
    }, [orderId, showAlert]);

    useEffect(() => {
        if (!orderId) return;
        let isMounted = true;
        diagnosticApi.getTechnicianOrderById(orderId)
            .then(res => {
                if (!isMounted) return;
                if (res.success && res.data) {
                    setOrder(res.data);
                    const initial: Record<number, any> = {};
                    res.data.items.forEach(item => {
                        initial[item.id] = {
                            resultText: item.result?.resultText || '',
                            conclusion: item.result?.conclusion || '',
                            referenceRange: item.result?.referenceRange || '',
                            unit: item.result?.unit || ''
                        };
                    });
                    setItemResults(initial);
                }
            })
            .catch(err => {
                if (!isMounted) return;
                showAlert(err.message || 'Không thể tải chi tiết phiếu chỉ định.', 'Lỗi', 'error');
            })
            .finally(() => {
                if (isMounted) setLoading(false);
            });
        return () => { isMounted = false; };
    }, [orderId, showAlert]);

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

    if (loading) {
        return <div style={{ padding: '60px 0', textAlign: 'center', color: '#64748b' }}>Đang tải thông tin phiếu chỉ định...</div>;
    }

    if (!order) {
        return (
            <div style={{ padding: '60px 0', textAlign: 'center' }}>
                <p>Không tìm thấy phiếu chỉ định.</p>
                <button onClick={() => navigate('/diagnostics')} style={{ padding: '8px 16px', background: '#0284c7', color: '#fff', border: 'none', borderRadius: '6px' }}>
                    Quay lại danh sách
                </button>
            </div>
        );
    }

    const allItemsCompleted = order.items.every(i => i.status === 'Completed' || i.status === 'Cancelled');

    return (
        <div style={{ padding: '24px', maxWidth: '1100px', margin: '0 auto' }}>
            {/* Top Navigation */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                <button
                    onClick={() => navigate('/diagnostics')}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 14px', background: '#f1f5f9', border: '1px solid #cbd5e1', borderRadius: '6px', cursor: 'pointer', fontWeight: 500, color: '#334155' }}
                >
                    <ArrowLeft size={16} /> Quay lại danh sách
                </button>

                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <span style={{ fontSize: '14px', color: '#64748b' }}>Trạng thái phiếu:</span>
                    <span style={{ 
                        padding: '4px 12px', 
                        borderRadius: '12px', 
                        fontSize: '13px', 
                        fontWeight: 600,
                        background: order.status === 'Completed' ? '#dcfce7' : order.status === 'InProgress' ? '#e0e7ff' : '#fef3c7',
                        color: order.status === 'Completed' ? '#15803d' : order.status === 'InProgress' ? '#4338ca' : '#b45309'
                    }}>
                        {order.status === 'Ordered' ? 'Chờ thực hiện' : order.status === 'InProgress' ? 'Đang thực hiện' : order.status === 'Completed' ? 'Đã hoàn tất' : 'Đã hủy'}
                    </span>
                </div>
            </div>

            {/* Patient & Order Information Card */}
            <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '20px', marginBottom: '24px', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', borderBottom: '1px solid #f1f5f9', paddingBottom: '16px', marginBottom: '16px' }}>
                    <div>
                        <div style={{ fontSize: '13px', color: '#64748b', fontWeight: 500 }}>PHIẾU CHỈ ĐỊNH CẬN LÂM SÀNG</div>
                        <div style={{ fontSize: '20px', fontWeight: 'bold', color: '#0284c7', marginTop: '2px' }}>{order.orderCode}</div>
                    </div>
                    <div style={{ textAlign: 'right' }}>
                        <div style={{ fontSize: '13px', color: '#64748b' }}>Mã lịch hẹn: <strong>#{order.appointmentCode}</strong></div>
                        <div style={{ fontSize: '12px', color: '#94a3b8', marginTop: '2px' }}>
                            Chỉ định lúc: {new Date(order.orderedAtUtc).toLocaleString('vi-VN')}
                        </div>
                    </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '16px', fontSize: '13px' }}>
                    <div>
                        <div style={{ color: '#64748b' }}>Bệnh nhân:</div>
                        <div style={{ fontWeight: 600, fontSize: '15px', color: '#0f172a', marginTop: '2px' }}>{order.patientName}</div>
                        <div style={{ color: '#64748b', marginTop: '2px' }}>
                            {order.patientGender?.toLowerCase() === 'female' ? 'Nữ' : order.patientGender?.toLowerCase() === 'male' ? 'Nam' : 'Chưa cập nhật'} • {order.patientAge ? `${order.patientAge} tuổi` : '---'}
                        </div>
                    </div>

                    <div>
                        <div style={{ color: '#64748b' }}>Số điện thoại:</div>
                        <div style={{ fontWeight: 600, color: '#0f172a', marginTop: '2px' }}>{order.patientPhone || '---'}</div>
                    </div>

                    <div>
                        <div style={{ color: '#64748b' }}>Bác sĩ chỉ định:</div>
                        <div style={{ fontWeight: 600, color: '#0f172a', marginTop: '2px' }}>{order.orderingDoctorName}</div>
                        <div style={{ color: '#64748b', marginTop: '2px' }}>{order.specialtyName || '---'}</div>
                    </div>
                </div>

                <div style={{ marginTop: '14px', paddingTop: '12px', borderTop: '1px dashed #e2e8f0', fontSize: '13px' }}>
                    <div><strong>Chỉ định lâm sàng:</strong> <span style={{ color: '#0f172a' }}>{order.clinicalIndication}</span></div>
                    {order.note && <div style={{ marginTop: '4px' }}><strong>Ghi chú từ bác sĩ:</strong> <span style={{ color: '#475569' }}>{order.note}</span></div>}
                </div>

                {order.status === 'Ordered' && (
                    <div style={{ marginTop: '20px', paddingTop: '16px', borderTop: '1px solid #f1f5f9', display: 'flex', justifyContent: 'flex-end' }}>
                        <button
                            onClick={handleStartOrder}
                            disabled={actionLoading}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', padding: '10px 24px', background: '#0284c7', color: '#ffffff', border: 'none', borderRadius: '6px', fontWeight: 600, fontSize: '14px', cursor: 'pointer' }}
                        >
                            <PlayCircle size={18} /> Bắt đầu thực hiện phiếu chỉ định
                        </button>
                    </div>
                )}
            </div>

            {/* Diagnostic Services List & Result Input */}
            <div style={{ marginBottom: '24px' }}>
                <h2 style={{ fontSize: '16px', fontWeight: 'bold', color: '#0f172a', marginBottom: '14px' }}>
                    Danh sách dịch vụ chỉ định ({order.items.length})
                </h2>

                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    {order.items.map((item, idx) => {
                        const current = itemResults[item.id] || { resultText: '', conclusion: '', referenceRange: '', unit: '' };
                        const isCompleted = item.status === 'Completed';

                        return (
                            <div key={item.id} style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '20px', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px' }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                                        <div style={{ width: '28px', height: '28px', borderRadius: '50%', background: '#e0f2fe', color: '#0369a1', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 'bold', fontSize: '13px' }}>
                                            {idx + 1}
                                        </div>
                                        <div>
                                            <div style={{ fontWeight: 600, fontSize: '15px', color: '#0f172a' }}>{item.serviceName}</div>
                                            <div style={{ fontSize: '12px', color: '#64748b' }}>Mã: {item.serviceCode} • Phân loại: {item.category}</div>
                                        </div>
                                    </div>
                                    <span style={{ 
                                        padding: '3px 10px', 
                                        borderRadius: '12px', 
                                        fontSize: '11px', 
                                        fontWeight: 600,
                                        background: isCompleted ? '#dcfce7' : item.status === 'InProgress' ? '#e0e7ff' : '#fef3c7',
                                        color: isCompleted ? '#15803d' : item.status === 'InProgress' ? '#4338ca' : '#b45309'
                                    }}>
                                        {isCompleted ? 'Đã nhập kết quả' : item.status === 'InProgress' ? 'Đang thực hiện' : 'Chờ thực hiện'}
                                    </span>
                                </div>

                                {order.status === 'Ordered' ? (
                                    <div style={{ padding: '14px', background: '#f8fafc', borderRadius: '6px', fontSize: '13px', color: '#64748b', textAlign: 'center' }}>
                                        Nhấn "Bắt đầu thực hiện" ở trên để tiếp nhận và nhập kết quả cho dịch vụ này.
                                    </div>
                                ) : (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                        <div>
                                            <label style={{ display: 'block', fontSize: '13px', fontWeight: 600, color: '#334155', marginBottom: '4px' }}>
                                                Kết quả chi tiết / Mô tả tổn thương <span style={{ color: '#dc2626' }}>*</span>
                                            </label>
                                            <textarea
                                                rows={3}
                                                disabled={order.status === 'Completed'}
                                                value={current.resultText}
                                                onChange={e => setItemResults(prev => ({
                                                    ...prev,
                                                    [item.id]: { ...prev[item.id], resultText: e.target.value }
                                                }))}
                                                placeholder="Nhập chi tiết thông số xét nghiệm, mô tả hình ảnh siêu âm..."
                                                style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none', fontFamily: 'inherit' }}
                                            />
                                        </div>

                                        <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: '12px' }}>
                                            <div>
                                                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#475569', marginBottom: '4px' }}>
                                                    Kết luận / Đánh giá
                                                </label>
                                                <input
                                                    type="text"
                                                    disabled={order.status === 'Completed'}
                                                    value={current.conclusion}
                                                    onChange={e => setItemResults(prev => ({
                                                        ...prev,
                                                        [item.id]: { ...prev[item.id], conclusion: e.target.value }
                                                    }))}
                                                    placeholder="VD: Bình thường, Gan nhiễm mỡ độ 1..."
                                                    style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }}
                                                />
                                            </div>

                                            <div>
                                                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#475569', marginBottom: '4px' }}>
                                                    Chỉ số bình thường
                                                </label>
                                                <input
                                                    type="text"
                                                    disabled={order.status === 'Completed'}
                                                    value={current.referenceRange}
                                                    onChange={e => setItemResults(prev => ({
                                                        ...prev,
                                                        [item.id]: { ...prev[item.id], referenceRange: e.target.value }
                                                    }))}
                                                    placeholder="VD: 70 - 100 mg/dL"
                                                    style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }}
                                                />
                                            </div>

                                            <div>
                                                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, color: '#475569', marginBottom: '4px' }}>
                                                    Đơn vị tính
                                                </label>
                                                <input
                                                    type="text"
                                                    disabled={order.status === 'Completed'}
                                                    value={current.unit}
                                                    onChange={e => setItemResults(prev => ({
                                                        ...prev,
                                                        [item.id]: { ...prev[item.id], unit: e.target.value }
                                                    }))}
                                                    placeholder="VD: U/L, mg/dL"
                                                    style={{ width: '100%', padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }}
                                                />
                                            </div>
                                        </div>

                                        {order.status !== 'Completed' && (
                                            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '6px' }}>
                                                <button
                                                    onClick={() => handleSaveItemResult(item.id)}
                                                    disabled={savingItemId === item.id}
                                                    style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '7px 16px', background: '#0284c7', color: '#fff', border: 'none', borderRadius: '6px', fontSize: '13px', fontWeight: 500, cursor: 'pointer' }}
                                                >
                                                    <Save size={15} /> {savingItemId === item.id ? 'Đang lưu...' : 'Lưu kết quả dịch vụ'}
                                                </button>
                                            </div>
                                        )}

                                        {item.result && (
                                            <div style={{ fontSize: '11px', color: '#94a3b8', textAlign: 'right', marginTop: '4px' }}>
                                                Nhập bởi: {item.result.resultedByUserName} • {new Date(item.result.resultedAtUtc).toLocaleString('vi-VN')}
                                            </div>
                                        )}
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            </div>

            {/* Bottom Actions for Order Completion */}
            {order.status === 'InProgress' && (
                <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', boxShadow: '0 2px 4px rgba(0,0,0,0.05)' }}>
                    <div>
                        <div style={{ fontWeight: 600, color: '#0f172a' }}>Hoàn tất phiếu chỉ định cận lâm sàng</div>
                        <div style={{ fontSize: '13px', color: '#64748b' }}>
                            {allItemsCompleted ? 'Tất cả các dịch vụ đã có kết quả. Bạn có thể chốt phiếu.' : 'Vui lòng lưu kết quả cho từng dịch vụ trước khi hoàn tất.'}
                        </div>
                    </div>
                    <button
                        onClick={handleCompleteOrder}
                        disabled={!allItemsCompleted || actionLoading}
                        style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '8px',
                            padding: '10px 24px',
                            background: allItemsCompleted ? '#16a34a' : '#94a3b8',
                            color: '#ffffff',
                            border: 'none',
                            borderRadius: '6px',
                            fontWeight: 600,
                            fontSize: '14px',
                            cursor: allItemsCompleted ? 'pointer' : 'not-allowed'
                        }}
                    >
                        <Check size={18} /> Hoàn tất và gửi kết quả
                    </button>
                </div>
            )}

            {order.status === 'Completed' && (
                <div style={{ background: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '8px', padding: '16px 20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div>
                        <div style={{ fontWeight: 600, color: '#166534', display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <CheckCircle size={18} /> Phiếu chỉ định đã được hoàn tất
                        </div>
                        <div style={{ fontSize: '12px', color: '#15803d', marginTop: '2px' }}>
                            Hoàn tất bởi: {order.completedByUserName} lúc {new Date(order.completedAtUtc!).toLocaleString('vi-VN')}
                        </div>
                    </div>
                    {order.reviewedAtUtc ? (
                        <div style={{ textAlign: 'right', fontSize: '12px', color: '#0369a1' }}>
                            <strong>Bác sĩ đã xem:</strong> {order.reviewedByDoctorName} ({new Date(order.reviewedAtUtc).toLocaleString('vi-VN')})
                        </div>
                    ) : (
                        <div style={{ fontSize: '12px', color: '#b45309', fontWeight: 500 }}>
                            Chờ bác sĩ khám xác nhận đã xem
                        </div>
                    )}
                </div>
            )}
        </div>
    );
};
