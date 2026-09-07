import React, { useState, useEffect } from 'react';
import { 
    FlaskConical, HeartPulse, FileText, ChevronDown, ChevronUp 
} from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

export const PatientDiagnosticResults: React.FC = () => {
    const { showAlert } = useDialog();

    const [activeTab, setActiveTab] = useState<'orders' | 'vitals'>('orders');
    const [orders, setOrders] = useState<DiagnosticOrderDto[]>([]);
    const [vitals, setVitals] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [expandedOrderId, setExpandedOrderId] = useState<number | null>(null);

    useEffect(() => {
        setLoading(true);
        Promise.all([
            diagnosticApi.getPatientOrders(1, 50),
            diagnosticApi.getPatientVitals(30)
        ])
        .then(([ordersRes, vitalsRes]) => {
            if (ordersRes.success && ordersRes.data) {
                setOrders(ordersRes.data.items);
                if (ordersRes.data.items.length > 0) {
                    setExpandedOrderId(ordersRes.data.items[0].id);
                }
            }
            if (vitalsRes.success && vitalsRes.data) {
                setVitals(vitalsRes.data);
            }
        })
        .catch(err => {
            showAlert(err.message || 'Không thể tải kết quả cận lâm sàng của bạn.', 'Lỗi', 'error');
        })
        .finally(() => setLoading(false));
    }, [showAlert]);

    return (
        <div style={{ maxWidth: '1000px', margin: '0 auto', padding: '30px 16px' }}>
            {/* Page Header */}
            <div style={{ marginBottom: '24px' }}>
                <h1 style={{ fontSize: '26px', fontWeight: 'bold', color: '#0f172a', margin: '0 0 6px', display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <FlaskConical size={28} color="#0284c7" /> Kết Quả Cận Lâm Sàng & Sinh Hiệu
                </h1>
                <p style={{ margin: 0, color: '#64748b', fontSize: '14px' }}>
                    Theo dõi lịch sử xét nghiệm, siêu âm và chỉ số sức khỏe của bạn tại ClinicCare
                </p>
            </div>

            {/* Tab selector */}
            <div style={{ display: 'flex', gap: '8px', borderBottom: '2px solid #e2e8f0', marginBottom: '24px' }}>
                <button
                    onClick={() => setActiveTab('orders')}
                    style={{
                        padding: '10px 20px',
                        fontWeight: 600,
                        fontSize: '14px',
                        cursor: 'pointer',
                        border: 'none',
                        background: 'none',
                        borderBottom: activeTab === 'orders' ? '2px solid #0284c7' : '2px solid transparent',
                        color: activeTab === 'orders' ? '#0284c7' : '#64748b',
                        marginBottom: '-2px',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px'
                    }}
                >
                    <FileText size={16} /> Phiếu cận lâm sàng ({orders.length})
                </button>

                <button
                    onClick={() => setActiveTab('vitals')}
                    style={{
                        padding: '10px 20px',
                        fontWeight: 600,
                        fontSize: '14px',
                        cursor: 'pointer',
                        border: 'none',
                        background: 'none',
                        borderBottom: activeTab === 'vitals' ? '2px solid #0284c7' : '2px solid transparent',
                        color: activeTab === 'vitals' ? '#0284c7' : '#64748b',
                        marginBottom: '-2px',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px'
                    }}
                >
                    <HeartPulse size={16} /> Chỉ số sinh hiệu ({vitals.length})
                </button>
            </div>

            {loading ? (
                <div style={{ padding: '60px 0', textAlign: 'center', color: '#64748b' }}>Đang tải dữ liệu y tế...</div>
            ) : activeTab === 'orders' ? (
                orders.length === 0 ? (
                    <div style={{ padding: '60px 0', textAlign: 'center', background: '#ffffff', borderRadius: '8px', border: '1px solid #e2e8f0' }}>
                        <FlaskConical size={48} style={{ margin: '0 auto 12px', opacity: 0.3, color: '#64748b' }} />
                        <div style={{ fontSize: '16px', fontWeight: 600, color: '#334155' }}>Chưa có kết quả cận lâm sàng nào</div>
                        <p style={{ fontSize: '13px', color: '#64748b', marginTop: '4px' }}>
                            Các kết quả xét nghiệm, siêu âm sau khi hoàn tất sẽ được cập nhật tại đây.
                        </p>
                    </div>
                ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                        {orders.map(order => {
                            const isExpanded = expandedOrderId === order.id;
                            const isReviewed = !!order.reviewedAtUtc;

                            return (
                                <div key={order.id} style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: '8px', overflow: 'hidden', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
                                    {/* Order Card Header */}
                                    <div 
                                        onClick={() => setExpandedOrderId(isExpanded ? null : order.id)}
                                        style={{ padding: '18px 20px', cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: isExpanded ? '#f8fafc' : '#ffffff', borderBottom: isExpanded ? '1px solid #e2e8f0' : 'none' }}
                                    >
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
                                            <div style={{ width: '40px', height: '40px', borderRadius: '8px', background: '#e0f2fe', color: '#0369a1', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                                                <FlaskConical size={22} />
                                            </div>
                                            <div>
                                                <div style={{ fontWeight: 'bold', fontSize: '15px', color: '#0f172a' }}>{order.orderCode}</div>
                                                <div style={{ fontSize: '12px', color: '#64748b', marginTop: '2px' }}>
                                                    Khám: #{order.appointmentCode} • Bác sĩ: {order.orderingDoctorName} ({order.specialtyName})
                                                </div>
                                            </div>
                                        </div>

                                        <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
                                            <span style={{ 
                                                padding: '4px 12px', 
                                                borderRadius: '12px', 
                                                fontSize: '12px', 
                                                fontWeight: 600,
                                                background: isReviewed ? '#dcfce7' : '#e0e7ff',
                                                color: isReviewed ? '#15803d' : '#4338ca'
                                            }}>
                                                {isReviewed ? 'Đã có kết luận bác sĩ' : 'Đã có kết quả'}
                                            </span>

                                            <span style={{ fontSize: '12px', color: '#64748b' }}>
                                                {new Date(order.orderedAtUtc).toLocaleDateString('vi-VN')}
                                            </span>

                                            {isExpanded ? <ChevronUp size={18} color="#64748b" /> : <ChevronDown size={18} color="#64748b" />}
                                        </div>
                                    </div>

                                    {/* Order Details Body */}
                                    {isExpanded && (
                                        <div style={{ padding: '20px' }}>
                                            <div style={{ background: '#f8fafc', padding: '12px 16px', borderRadius: '6px', marginBottom: '18px', fontSize: '13px' }}>
                                                <div><strong>Chỉ định:</strong> {order.clinicalIndication}</div>
                                                {order.note && <div style={{ marginTop: '4px' }}><strong>Ghi chú:</strong> {order.note}</div>}
                                                {order.reviewedAtUtc && (
                                                    <div style={{ marginTop: '6px', color: '#0369a1', fontWeight: 500 }}>
                                                        ✓ Bác sĩ {order.reviewedByDoctorName} đã xác nhận xem kết quả lúc {new Date(order.reviewedAtUtc).toLocaleString('vi-VN')}
                                                    </div>
                                                )}
                                            </div>

                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                                {order.items.map((item, idx) => (
                                                    <div key={item.id} style={{ border: '1px solid #e2e8f0', borderRadius: '6px', padding: '14px 16px', background: '#ffffff' }}>
                                                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                                                            <div style={{ fontWeight: 600, fontSize: '14px', color: '#0f172a' }}>
                                                                {idx + 1}. {item.serviceName} ({item.serviceCode})
                                                            </div>
                                                            <span style={{ fontSize: '11px', color: '#64748b', background: '#f1f5f9', padding: '2px 8px', borderRadius: '4px' }}>
                                                                {item.category}
                                                            </span>
                                                        </div>

                                                        {item.result ? (
                                                            <div style={{ fontSize: '13px', lineHeight: '1.6' }}>
                                                                <div style={{ background: '#fafafa', padding: '10px 12px', borderRadius: '4px', border: '1px solid #f1f5f9', whiteSpace: 'pre-line' }}>
                                                                    <strong>Kết quả:</strong> {item.result.resultText}
                                                                </div>

                                                                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '10px', marginTop: '8px', fontSize: '12px', color: '#475569' }}>
                                                                    {item.result.conclusion && (
                                                                        <div><strong>Kết luận:</strong> {item.result.conclusion}</div>
                                                                    )}
                                                                    {item.result.referenceRange && (
                                                                        <div><strong>Chỉ số bình thường:</strong> {item.result.referenceRange}</div>
                                                                    )}
                                                                    {item.result.unit && (
                                                                        <div><strong>Đơn vị:</strong> {item.result.unit}</div>
                                                                    )}
                                                                </div>

                                                                <div style={{ fontSize: '11px', color: '#94a3b8', marginTop: '6px', textAlign: 'right' }}>
                                                                    Thực hiện bởi: {item.result.resultedByUserName} • {new Date(item.result.resultedAtUtc).toLocaleString('vi-VN')}
                                                                </div>
                                                            </div>
                                                        ) : (
                                                            <div style={{ fontSize: '12px', color: '#b45309', fontStyle: 'italic' }}>
                                                                Đang chờ kỹ thuật viên trả kết quả...
                                                            </div>
                                                        )}
                                                    </div>
                                                ))}
                                            </div>
                                        </div>
                                    )}
                                </div>
                            );
                        })}
                    </div>
                )
            ) : (
                /* Vitals History Tab */
                vitals.length === 0 ? (
                    <div style={{ padding: '60px 0', textAlign: 'center', background: '#ffffff', borderRadius: '8px', border: '1px solid #e2e8f0' }}>
                        <HeartPulse size={48} style={{ margin: '0 auto 12px', opacity: 0.3, color: '#64748b' }} />
                        <div style={{ fontSize: '16px', fontWeight: 600, color: '#334155' }}>Chưa có lịch sử sinh hiệu nào</div>
                    </div>
                ) : (
                    <div style={{ background: '#ffffff', borderRadius: '8px', border: '1px solid #e2e8f0', overflow: 'hidden', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                            <thead>
                                <tr style={{ background: '#f8fafc', borderBottom: '1px solid #e2e8f0', color: '#475569' }}>
                                    <th style={{ padding: '12px 16px', textAlign: 'left' }}>Ngày khám</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>Cân nặng</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>Chiều cao</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>BMI</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>Huyết áp</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>Nhịp tim</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>Thân nhiệt</th>
                                    <th style={{ padding: '12px 16px', textAlign: 'center' }}>SpO2</th>
                                </tr>
                            </thead>
                            <tbody>
                                {vitals.map((v, idx) => (
                                    <tr key={idx} style={{ borderBottom: '1px solid #f1f5f9' }}>
                                        <td style={{ padding: '12px 16px', fontWeight: 500 }}>
                                            {new Date(v.recordedAtUtc).toLocaleDateString('vi-VN')}
                                            <div style={{ fontSize: '11px', color: '#94a3b8' }}>#{v.appointmentCode}</div>
                                        </td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>{v.weight ? `${v.weight} kg` : '---'}</td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>{v.height ? `${v.height} cm` : '---'}</td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center', fontWeight: 600 }}>{v.bmi ?? '---'}</td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>
                                            {v.bloodPressureSystolic && v.bloodPressureDiastolic ? `${v.bloodPressureSystolic}/${v.bloodPressureDiastolic}` : '---'}
                                        </td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>{v.heartRate ? `${v.heartRate} bpm` : '---'}</td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>{v.temperature ? `${v.temperature}°C` : '---'}</td>
                                        <td style={{ padding: '12px 16px', textAlign: 'center' }}>{v.spO2 ? `${v.spO2}%` : '---'}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )
            )}
        </div>
    );
};
