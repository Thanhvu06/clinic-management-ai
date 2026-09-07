import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Printer, ArrowLeft, ShieldPlus } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { DiagnosticOrderDto } from '../../types';

export const DiagnosticOrderPrint: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const orderId = Number(id);
    const navigate = useNavigate();

    const [order, setOrder] = useState<DiagnosticOrderDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!orderId) return;
        setLoading(true);
        diagnosticApi.getDoctorOrderById(orderId)
            .then(res => {
                if (res.success && res.data) {
                    setOrder(res.data);
                } else {
                    setError('Không tìm thấy thông tin phiếu chỉ định.');
                }
            })
            .catch(err => {
                setError(err.message || 'Lỗi khi tải phiếu chỉ định.');
            })
            .finally(() => setLoading(false));
    }, [orderId]);

    const handlePrint = () => {
        window.print();
    };

    if (loading) {
        return (
            <div style={{ padding: '60px', textAlign: 'center', fontFamily: 'sans-serif' }}>
                <p>Đang tải thông tin phiếu chỉ định...</p>
            </div>
        );
    }

    if (error || !order) {
        return (
            <div style={{ padding: '60px', textAlign: 'center', fontFamily: 'sans-serif' }}>
                <p style={{ color: '#dc2626' }}>{error || 'Không tìm thấy phiếu chỉ định.'}</p>
                <button 
                    onClick={() => navigate(-1)} 
                    style={{ padding: '8px 16px', background: '#0284c7', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer' }}
                >
                    Quay lại
                </button>
            </div>
        );
    }

    const categoryMap: Record<string, string> = {
        Laboratory: 'Xét nghiệm',
        Ultrasound: 'Siêu âm',
        Imaging: 'Chẩn đoán hình ảnh',
        Other: 'Khác'
    };

    const statusMap: Record<string, string> = {
        Ordered: 'Chờ thực hiện',
        InProgress: 'Đang thực hiện',
        Completed: 'Đã có kết quả',
        Cancelled: 'Đã hủy'
    };

    const formattedDate = new Date(order.orderedAtUtc).toLocaleString('vi-VN', {
        hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit', year: 'numeric'
    });

    return (
        <div style={{ minHeight: '100vh', background: '#f8fafc', padding: '24px' }}>
            <style>{`
                @media print {
                    body { background: #fff !important; margin: 0 !important; }
                    .no-print { display: none !important; }
                    .print-container { 
                        box-shadow: none !important; 
                        border: none !important; 
                        margin: 0 !important; 
                        padding: 0 !important; 
                        width: 100% !important; 
                        max-width: 100% !important; 
                    }
                }
            `}</style>

            {/* Action Bar (Screen Only) */}
            <div className="no-print" style={{ maxWidth: '800px', margin: '0 auto 16px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <button
                    onClick={() => navigate(-1)}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 16px', background: '#e2e8f0', color: '#334155', border: 'none', borderRadius: '6px', cursor: 'pointer', fontWeight: 500 }}
                >
                    <ArrowLeft size={16} /> Quay lại
                </button>
                <button
                    onClick={handlePrint}
                    style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '8px 20px', background: '#0284c7', color: '#ffffff', border: 'none', borderRadius: '6px', cursor: 'pointer', fontWeight: 600, boxShadow: '0 2px 4px rgba(2,132,199,0.2)' }}
                >
                    <Printer size={16} /> In phiếu chỉ định
                </button>
            </div>

            {/* Print Slip Document */}
            <div className="print-container" style={{ maxWidth: '800px', margin: '0 auto', background: '#ffffff', padding: '40px', borderRadius: '8px', border: '1px solid #cbd5e1', boxShadow: '0 4px 6px -1px rgba(0,0,0,0.1)', color: '#0f172a', fontFamily: 'Arial, sans-serif' }}>
                {/* Header */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', borderBottom: '2px solid #0284c7', paddingBottom: '16px', marginBottom: '20px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <div style={{ width: '48px', height: '48px', borderRadius: '8px', background: '#e0f2fe', color: '#0284c7', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                            <ShieldPlus size={32} />
                        </div>
                        <div>
                            <div style={{ fontSize: '18px', fontWeight: 'bold', color: '#0369a1', textTransform: 'uppercase' }}>Phòng Khám Đa Khoa ClinicCare</div>
                            <div style={{ fontSize: '12px', color: '#64748b' }}>123 Nguyễn Văn Cừ, Quận 5, TP. Hồ Chí Minh • Hotline: 1900 1234</div>
                        </div>
                    </div>
                    <div style={{ textAlign: 'right' }}>
                        <div style={{ fontSize: '13px', fontWeight: 'bold', color: '#0f172a' }}>MÃ PHIẾU: {order.orderCode}</div>
                        <div style={{ fontSize: '12px', color: '#64748b' }}>Mã lịch hẹn: #{order.appointmentCode}</div>
                        <div style={{ fontSize: '12px', color: '#64748b' }}>Ngày lập: {formattedDate}</div>
                    </div>
                </div>

                {/* Slip Title */}
                <div style={{ textAlign: 'center', margin: '20px 0' }}>
                    <h1 style={{ fontSize: '22px', fontWeight: 'bold', textTransform: 'uppercase', margin: 0, color: '#0f172a', letterSpacing: '0.5px' }}>
                        Phiếu Chỉ Định Cận Lâm Sàng
                    </h1>
                    <div style={{ fontSize: '12px', color: '#64748b', fontStyle: 'italic', marginTop: '4px' }}>
                        (Vui lòng mang phiếu này đến phòng xét nghiệm / chẩn đoán hình ảnh)
                    </div>
                </div>

                {/* Patient Information Box */}
                <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '6px', padding: '14px 18px', marginBottom: '20px', fontSize: '13px', lineHeight: '1.6' }}>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '10px' }}>
                        <div>
                            <strong>Họ và tên:</strong> <span style={{ textTransform: 'uppercase', fontWeight: 600 }}>{order.patientName}</span>
                        </div>
                        <div>
                            <strong>Giới tính:</strong> {order.patientGender === 'Female' ? 'Nữ' : 'Nam'}
                        </div>
                        <div>
                            <strong>Tuổi:</strong> {order.patientAge ? `${order.patientAge} tuổi` : '---'}
                        </div>
                        <div>
                            <strong>Số điện thoại:</strong> {order.patientPhone || '---'}
                        </div>
                        <div>
                            <strong>Bác sĩ chỉ định:</strong> {order.orderingDoctorName}
                        </div>
                        <div>
                            <strong>Chuyên khoa:</strong> {order.specialtyName}
                        </div>
                    </div>
                    <div style={{ marginTop: '8px', paddingTop: '8px', borderTop: '1px dashed #cbd5e1' }}>
                        <div><strong>Chỉ định lâm sàng / Lý do:</strong> {order.clinicalIndication}</div>
                        {order.note && <div style={{ marginTop: '4px' }}><strong>Ghi chú bổ sung:</strong> {order.note}</div>}
                    </div>
                </div>

                {/* Ordered Items Table */}
                <div style={{ marginBottom: '24px' }}>
                    <div style={{ fontSize: '14px', fontWeight: 'bold', marginBottom: '8px', color: '#0369a1', textTransform: 'uppercase' }}>
                        Danh Mục Dịch Vụ Chỉ Định ({order.items.length})
                    </div>
                    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                        <thead>
                            <tr style={{ background: '#f1f5f9', borderTop: '1px solid #cbd5e1', borderBottom: '2px solid #94a3b8' }}>
                                <th style={{ padding: '8px 10px', textAlign: 'center', width: '40px' }}>STT</th>
                                <th style={{ padding: '8px 10px', textAlign: 'left', width: '110px' }}>Mã dịch vụ</th>
                                <th style={{ padding: '8px 10px', textAlign: 'left' }}>Tên dịch vụ cận lâm sàng</th>
                                <th style={{ padding: '8px 10px', textAlign: 'left', width: '140px' }}>Phân loại</th>
                                <th style={{ padding: '8px 10px', textAlign: 'center', width: '120px' }}>Trạng thái</th>
                            </tr>
                        </thead>
                        <tbody>
                            {order.items.map((item, idx) => (
                                <tr key={item.id} style={{ borderBottom: '1px solid #e2e8f0' }}>
                                    <td style={{ padding: '10px', textAlign: 'center', color: '#64748b' }}>{idx + 1}</td>
                                    <td style={{ padding: '10px', fontWeight: 600, color: '#334155' }}>{item.serviceCode}</td>
                                    <td style={{ padding: '10px', fontWeight: 500 }}>{item.serviceName}</td>
                                    <td style={{ padding: '10px', color: '#475569' }}>{categoryMap[item.category] || item.category}</td>
                                    <td style={{ padding: '10px', textAlign: 'center' }}>
                                        <span style={{ 
                                            display: 'inline-block', 
                                            padding: '2px 8px', 
                                            borderRadius: '12px', 
                                            fontSize: '11px', 
                                            fontWeight: 600,
                                            background: item.status === 'Completed' ? '#dcfce7' : item.status === 'InProgress' ? '#e0e7ff' : '#fef9c3',
                                            color: item.status === 'Completed' ? '#15803d' : item.status === 'InProgress' ? '#4338ca' : '#a16207'
                                        }}>
                                            {statusMap[item.status] || item.status}
                                        </span>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>

                {/* Signatures */}
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '40px', marginTop: '40px', textAlign: 'center', pageBreakInside: 'avoid' }}>
                    <div>
                        <div style={{ fontSize: '13px', fontWeight: 'bold' }}>KỸ THUẬT VIÊN THỰC HIỆN</div>
                        <div style={{ fontSize: '11px', color: '#64748b', fontStyle: 'italic', marginBottom: '60px' }}>(Ký và ghi rõ họ tên)</div>
                        <div style={{ fontSize: '13px', fontWeight: 600, color: '#334155' }}>
                            {order.completedByUserName || '................................................'}
                        </div>
                    </div>
                    <div>
                        <div style={{ fontSize: '13px', fontWeight: 'bold' }}>BÁC SĨ CHỈ ĐỊNH</div>
                        <div style={{ fontSize: '11px', color: '#64748b', fontStyle: 'italic', marginBottom: '60px' }}>(Ký và ghi rõ họ tên)</div>
                        <div style={{ fontSize: '13px', fontWeight: 600, color: '#0369a1' }}>
                            {order.orderingDoctorName}
                        </div>
                    </div>
                </div>

                {/* Footer Note */}
                <div style={{ marginTop: '30px', paddingTop: '10px', borderTop: '1px solid #e2e8f0', fontSize: '11px', color: '#94a3b8', textAlign: 'center' }}>
                    Phiếu chỉ định được khởi tạo từ Hệ thống Quản trị Phòng khám Thông minh ClinicCare AI.
                </div>
            </div>
        </div>
    );
};
