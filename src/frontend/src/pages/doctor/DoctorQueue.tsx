import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { 
    Users, Search, RefreshCw, Calendar, Clock, 
    CheckCircle2, AlertCircle 
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

export const DoctorQueue: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert, showConfirm, showToast } = useDialog();

    const todayStr = new Date().toISOString().split('T')[0];
    const [selectedDate, setSelectedDate] = useState(todayStr);
    const [queue, setQueue] = useState<DoctorQueueItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [statusFilter, setStatusFilter] = useState('ALL');
    const [searchTerm, setSearchTerm] = useState('');
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

    const loadQueue = useCallback(async () => {
        setLoading(true);
        try {
            const res = await doctorApi.getQueue(selectedDate);
            if (res.success && res.data) {
                setQueue(res.data);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tải danh sách hàng đợi.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    }, [selectedDate, showAlert]);

    useEffect(() => {
        loadQueue();
    }, [loadQueue]);

    const handleCheckIn = async (appointmentId: number) => {
        setActionLoadingId(appointmentId);
        try {
            const res = await doctorApi.checkInAppointment(appointmentId);
            if (res.success) {
                showToast('Đã tiếp nhận bệnh nhân vào phòng khám!', 'success');
                await loadQueue();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tiếp nhận bệnh nhân.', 'Lỗi', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    const handleStartConsultation = async (appointmentId: number) => {
        setActionLoadingId(appointmentId);
        try {
            const res = await doctorApi.startConsultation(appointmentId);
            if (res.success) {
                showToast('Bắt đầu ca khám lâm sàng.', 'success');
                navigate(`/doctor/appointments/${appointmentId}/examination`);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể bắt đầu phiên khám.', 'Lỗi', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    const handleMarkNoShow = (item: DoctorQueueItemDto) => {
        showConfirm(
            `Xác nhận đánh dấu bệnh nhân "${item.patientName}" vắng mặt cho ca khám lúc ${item.startTime.substring(0, 5)}?`,
            async () => {
                setActionLoadingId(item.appointmentId);
                try {
                    const res = await doctorApi.markNoShow(item.appointmentId, 'Bệnh nhân vắng mặt tại phòng khám.');
                    if (res.success) {
                        showToast('Đã đánh dấu bệnh nhân vắng mặt.', 'info');
                        await loadQueue();
                    }
                } catch (err: any) {
                    showAlert(err.response?.data?.message || 'Không thể đánh dấu vắng mặt.', 'Lỗi', 'error');
                } finally {
                    setActionLoadingId(null);
                }
            },
            'Xác nhận vắng mặt'
        );
    };

    const filteredQueue = queue.filter(item => {
        const matchesStatus = statusFilter === 'ALL' || item.status === statusFilter;
        const matchesSearch = !searchTerm || 
            item.patientName.toLowerCase().includes(searchTerm.toLowerCase()) ||
            item.appointmentCode.toLowerCase().includes(searchTerm.toLowerCase()) ||
            item.patientPhone.includes(searchTerm);
        return matchesStatus && matchesSearch;
    });

    const getStatusBadge = (status: string) => {
        switch (status) {
            case 'Confirmed':
                return <span style={{ backgroundColor: '#fef3c7', color: '#b45309', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Chờ tiếp nhận</span>;
            case 'CheckedIn':
                return <span style={{ backgroundColor: '#dbeafe', color: '#1d4ed8', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Đã tiếp nhận</span>;
            case 'InConsultation':
                return <span style={{ backgroundColor: '#e0e7ff', color: '#4338ca', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 700 }}>Đang khám</span>;
            case 'Completed':
                return <span style={{ backgroundColor: '#dcfce7', color: '#15803d', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Đã hoàn tất</span>;
            case 'NoShow':
                return <span style={{ backgroundColor: '#fee2e2', color: '#b91c1c', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Vắng mặt</span>;
            case 'Cancelled':
                return <span style={{ backgroundColor: '#f1f5f9', color: '#64748b', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Đã hủy</span>;
            default:
                return <span style={{ backgroundColor: '#f1f5f9', color: '#475569', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem' }}>{status}</span>;
        }
    };

    return (
        <div style={{ padding: '8px 0' }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px', flexWrap: 'wrap', gap: '12px' }}>
                <div>
                    <h1 style={{ fontSize: '1.5rem', fontWeight: 700, color: '#0f172a', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Users size={24} style={{ color: '#0284c7' }} />
                        <span>Hàng đợi khám lâm sàng</span>
                    </h1>
                    <p style={{ color: '#64748b', margin: '4px 0 0 0', fontSize: '0.9rem' }}>
                        Theo dõi thứ tự tiếp nhận, đo sinh hiệu và điều phối bệnh nhân vào phòng khám.
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px', backgroundColor: 'white', padding: '6px 12px', border: '1px solid #cbd5e1', borderRadius: '6px' }}>
                        <Calendar size={16} style={{ color: '#64748b' }} />
                        <input
                            type="date"
                            value={selectedDate}
                            onChange={(e) => setSelectedDate(e.target.value)}
                            style={{ border: 'none', outline: 'none', fontSize: '0.9rem', color: '#0f172a', cursor: 'pointer' }}
                        />
                    </div>
                    <button 
                        className="btn-secondary" 
                        onClick={loadQueue}
                        disabled={loading}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                    >
                        <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                        <span>Tải lại</span>
                    </button>
                </div>
            </div>

            {/* Filter Bar */}
            <div className="card" style={{ padding: '16px', marginBottom: '20px', borderRadius: '8px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px' }}>
                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                        {[
                            { key: 'ALL', label: `Tất cả (${queue.length})` },
                            { key: 'Confirmed', label: `Chờ tiếp nhận (${queue.filter(i => i.status === 'Confirmed').length})` },
                            { key: 'CheckedIn', label: `Đã tiếp nhận (${queue.filter(i => i.status === 'CheckedIn').length})` },
                            { key: 'InConsultation', label: `Đang khám (${queue.filter(i => i.status === 'InConsultation').length})` },
                            { key: 'Completed', label: `Đã xong (${queue.filter(i => i.status === 'Completed').length})` },
                            { key: 'NoShow', label: `Vắng (${queue.filter(i => i.status === 'NoShow').length})` },
                        ].map(tab => (
                            <button
                                key={tab.key}
                                onClick={() => setStatusFilter(tab.key)}
                                style={{
                                    padding: '6px 12px',
                                    borderRadius: '6px',
                                    fontSize: '0.85rem',
                                    fontWeight: 600,
                                    border: 'none',
                                    cursor: 'pointer',
                                    backgroundColor: statusFilter === tab.key ? '#0284c7' : '#f1f5f9',
                                    color: statusFilter === tab.key ? 'white' : '#475569'
                                }}
                            >
                                {tab.label}
                            </button>
                        ))}
                    </div>

                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: '240px' }}>
                        <div style={{ position: 'relative', width: '100%' }}>
                            <Search size={16} style={{ position: 'absolute', left: '10px', top: '50%', transform: 'translateY(-50%)', color: '#94a3b8' }} />
                            <input
                                type="text"
                                placeholder="Tìm theo tên, mã, SĐT..."
                                value={searchTerm}
                                onChange={(e) => setSearchTerm(e.target.value)}
                                style={{ width: '100%', padding: '8px 10px 8px 34px', borderRadius: '6px', border: '1px solid #cbd5e1', fontSize: '0.85rem' }}
                            />
                        </div>
                    </div>
                </div>
            </div>

            {/* Queue List Table */}
            <div className="card" style={{ padding: '0', borderRadius: '8px', overflow: 'hidden' }}>
                {loading ? (
                    <div style={{ textAlign: 'center', padding: '40px 0', color: '#64748b' }}>
                        <RefreshCw size={24} className="animate-spin" style={{ margin: '0 auto 8px auto' }} />
                        <p>Đang tải hàng đợi...</p>
                    </div>
                ) : filteredQueue.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '48px 0', color: '#64748b' }}>
                        <AlertCircle size={40} style={{ margin: '0 auto 10px auto', color: '#94a3b8' }} />
                        <p style={{ fontWeight: 600 }}>Không tìm thấy lượt khám nào trong hàng đợi.</p>
                        <p style={{ fontSize: '0.85rem' }}>Hãy chọn ngày khác hoặc xóa bộ lọc để xem các lượt khác.</p>
                    </div>
                ) : (
                    <div style={{ overflowX: 'auto' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
                            <thead style={{ backgroundColor: '#f8fafc' }}>
                                <tr style={{ borderBottom: '1px solid #e2e8f0', textAlign: 'left', color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>
                                    <th style={{ padding: '12px 14px' }}>STT</th>
                                    <th style={{ padding: '12px 14px' }}>Khung giờ</th>
                                    <th style={{ padding: '12px 14px' }}>Mã lịch</th>
                                    <th style={{ padding: '12px 14px' }}>Bệnh nhân</th>
                                    <th style={{ padding: '12px 14px' }}>Lý do khám</th>
                                    <th style={{ padding: '12px 14px' }}>Dấu hiệu sinh tồn</th>
                                    <th style={{ padding: '12px 14px' }}>Trạng thái</th>
                                    <th style={{ padding: '12px 14px', textAlign: 'right' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                                {filteredQueue.map((item, idx) => (
                                    <tr 
                                        key={item.appointmentId} 
                                        style={{ 
                                            borderBottom: '1px solid #f1f5f9',
                                            backgroundColor: item.status === 'InConsultation' ? '#f5f3ff' : item.status === 'CheckedIn' ? '#f0fdf4' : 'transparent' 
                                        }}
                                    >
                                        <td style={{ padding: '12px 14px', fontWeight: 700, color: '#64748b' }}>
                                            {item.queueOrder || idx + 1}
                                        </td>
                                        <td style={{ padding: '12px 14px', fontWeight: 700, color: '#0f172a', whiteSpace: 'nowrap' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                <Clock size={15} style={{ color: '#0284c7' }} />
                                                <span>{item.startTime.substring(0, 5)} - {item.endTime.substring(0, 5)}</span>
                                            </div>
                                        </td>
                                        <td style={{ padding: '12px 14px', fontFamily: 'monospace', fontSize: '0.85rem', color: '#475569' }}>
                                            {item.appointmentCode}
                                        </td>
                                        <td style={{ padding: '12px 14px' }}>
                                            <div style={{ fontWeight: 600, color: '#0f172a' }}>{item.patientName}</div>
                                            <div style={{ fontSize: '0.8rem', color: '#64748b' }}>
                                                {item.patientPhone} {item.patientGender === 'Male' ? '• Nam' : item.patientGender === 'Female' ? '• Nữ' : ''} {item.patientAge ? `• ${item.patientAge} tuổi` : ''}
                                            </div>
                                        </td>
                                        <td style={{ padding: '12px 14px', maxWidth: '220px', color: '#334155' }}>
                                            <div style={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                {item.reason || 'Khám theo lịch hẹn'}
                                            </div>
                                        </td>
                                        <td style={{ padding: '12px 14px' }}>
                                            {item.isVitalsRecorded ? (
                                                <div>
                                                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: '#059669', fontSize: '0.8rem', fontWeight: 600 }}>
                                                        <CheckCircle2 size={14} />
                                                        <span>Đã ghi nhận</span>
                                                    </span>
                                                    {item.vitalSummaryText && (
                                                        <div style={{ fontSize: '0.75rem', color: '#64748b', marginTop: '2px' }}>
                                                            {item.vitalSummaryText}
                                                        </div>
                                                    )}
                                                </div>
                                            ) : (
                                                <span style={{ color: '#94a3b8', fontSize: '0.8rem' }}>Chưa đo</span>
                                            )}
                                        </td>
                                        <td style={{ padding: '12px 14px' }}>
                                            {getStatusBadge(item.status)}
                                        </td>
                                        <td style={{ padding: '12px 14px', textAlign: 'right', whiteSpace: 'nowrap' }}>
                                            <div style={{ display: 'inline-flex', gap: '6px' }}>
                                                {item.status === 'Confirmed' && (
                                                    <>
                                                        <button 
                                                            className="btn-secondary"
                                                            onClick={() => handleCheckIn(item.appointmentId)}
                                                            disabled={actionLoadingId === item.appointmentId}
                                                            style={{ padding: '6px 10px', fontSize: '0.8rem', borderRadius: '4px', cursor: 'pointer' }}
                                                        >
                                                            Tiếp nhận
                                                        </button>
                                                        <button
                                                            onClick={() => handleMarkNoShow(item)}
                                                            disabled={actionLoadingId === item.appointmentId}
                                                            style={{ padding: '6px 8px', fontSize: '0.8rem', borderRadius: '4px', border: '1px solid #fecaca', backgroundColor: '#fff5f5', color: '#dc2626', cursor: 'pointer' }}
                                                        >
                                                            Vắng
                                                        </button>
                                                    </>
                                                )}

                                                {item.status === 'CheckedIn' && (
                                                    <button 
                                                        className="btn-primary"
                                                        onClick={() => handleStartConsultation(item.appointmentId)}
                                                        disabled={actionLoadingId === item.appointmentId}
                                                        style={{ padding: '6px 12px', fontSize: '0.8rem', borderRadius: '4px', fontWeight: 600, cursor: 'pointer' }}
                                                    >
                                                        Vào khám
                                                    </button>
                                                )}

                                                {item.status === 'InConsultation' && (
                                                    <button 
                                                        className="btn-primary"
                                                        onClick={() => navigate(`/doctor/appointments/${item.appointmentId}/examination`)}
                                                        style={{ padding: '6px 14px', fontSize: '0.8rem', borderRadius: '4px', fontWeight: 600, backgroundColor: '#4f46e5', cursor: 'pointer' }}
                                                    >
                                                        Tiếp tục khám →
                                                    </button>
                                                )}

                                                {item.status === 'Completed' && (
                                                    <button 
                                                        className="btn-secondary"
                                                        onClick={() => navigate(`/doctor/appointments/${item.appointmentId}`)}
                                                        style={{ padding: '6px 10px', fontSize: '0.8rem', borderRadius: '4px', cursor: 'pointer' }}
                                                    >
                                                        Xem kết quả
                                                    </button>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
};
