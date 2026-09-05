import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { 
    Clock, Users, Stethoscope, CheckCircle, 
    Calendar, ArrowRight, RefreshCw, AlertCircle, HeartPulse, UserCheck 
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorDashboardDto, DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';

export const DoctorDashboard: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert, showConfirm, showToast } = useDialog();

    const [dashboardData, setDashboardData] = useState<DoctorDashboardDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

    const loadDashboard = useCallback(async () => {
        setLoading(true);
        try {
            const res = await doctorApi.getDashboard();
            if (res.success && res.data) {
                setDashboardData(res.data);
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tải dữ liệu bàn làm việc bác sĩ.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    }, [showAlert]);

    useEffect(() => {
        loadDashboard();
    }, [loadDashboard]);

    const handleCheckIn = async (appointmentId: number) => {
        setActionLoadingId(appointmentId);
        try {
            const res = await doctorApi.checkInAppointment(appointmentId);
            if (res.success) {
                showToast('Đã tiếp nhận bệnh nhân vào phòng khám!', 'success');
                await loadDashboard();
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
                showToast('Bắt đầu phiên khám lâm sàng.', 'success');
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
                    const res = await doctorApi.markNoShow(item.appointmentId, 'Bệnh nhân không có mặt tại phòng khám.');
                    if (res.success) {
                        showToast('Đã đánh dấu bệnh nhân vắng mặt.', 'info');
                        await loadDashboard();
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

    const getStatusBadge = (status: string) => {
        switch (status) {
            case 'Confirmed':
                return <span className="badge badge-warning" style={{ backgroundColor: '#fef3c7', color: '#b45309', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Chờ tiếp nhận</span>;
            case 'CheckedIn':
                return <span className="badge badge-info" style={{ backgroundColor: '#dbeafe', color: '#1d4ed8', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Đã tiếp nhận</span>;
            case 'InConsultation':
                return <span className="badge badge-primary" style={{ backgroundColor: '#e0e7ff', color: '#4338ca', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 700 }}>Đang khám</span>;
            case 'Completed':
                return <span className="badge badge-success" style={{ backgroundColor: '#dcfce7', color: '#15803d', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Đã hoàn tất</span>;
            case 'NoShow':
                return <span className="badge badge-danger" style={{ backgroundColor: '#fee2e2', color: '#b91c1c', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Vắng mặt</span>;
            case 'Cancelled':
                return <span className="badge" style={{ backgroundColor: '#f1f5f9', color: '#64748b', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem', fontWeight: 600 }}>Đã hủy</span>;
            default:
                return <span className="badge" style={{ backgroundColor: '#f1f5f9', color: '#475569', padding: '4px 8px', borderRadius: '4px', fontSize: '0.75rem' }}>{status}</span>;
        }
    };

    const kpis = dashboardData?.kpis || {
        totalAppointmentsToday: (dashboardData as any)?.totalToday ?? 0,
        waitingCount: (dashboardData as any)?.checkedInCount ?? 0,
        inConsultationCount: (dashboardData as any)?.inConsultationCount ?? 0,
        completedCount: (dashboardData as any)?.completedTodayCount ?? 0,
        noShowCount: (dashboardData as any)?.noShowTodayCount ?? 0,
    };

    const currentShift = dashboardData?.currentShift;
    const nextPatient = dashboardData?.nextPatient;
    const queue: DoctorQueueItemDto[] = dashboardData?.todayQueue || (dashboardData as any)?.queue || [];

    return (
        <div style={{ padding: '8px 0' }}>
            {/* Top Bar */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px', flexWrap: 'wrap', gap: '12px' }}>
                <div>
                    <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--c-navy-dark, #0f172a)', margin: 0 }}>
                        Bàn làm việc Bác sĩ
                    </h1>
                    <p style={{ color: 'var(--c-muted, #64748b)', margin: '4px 0 0 0', fontSize: '0.9rem' }}>
                        Hệ thống điều phối khám lâm sàng và quản lý hồ sơ y tế bệnh nhân hôm nay.
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '8px' }}>
                    <button 
                        className="btn-secondary" 
                        onClick={loadDashboard} 
                        disabled={loading}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', cursor: 'pointer' }}
                    >
                        <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                        <span>Làm mới</span>
                    </button>
                    <Link 
                        to="/doctor/schedule" 
                        className="btn-primary" 
                        style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', textDecoration: 'none' }}
                    >
                        <Calendar size={16} />
                        <span>Xem lịch trực tuần</span>
                    </Link>
                </div>
            </div>

            {/* Current Shift Info Banner */}
            <div className="card" style={{ padding: '16px 20px', marginBottom: '24px', backgroundColor: currentShift ? '#f8fafc' : '#fffbeb', borderLeft: currentShift ? '4px solid #0284c7' : '4px solid #f59e0b', borderRadius: '8px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <div style={{ width: '40px', height: '40px', borderRadius: '8px', backgroundColor: currentShift ? '#e0f2fe' : '#fef3c7', display: 'flex', alignItems: 'center', justifyContent: 'center', color: currentShift ? '#0284c7' : '#d97706' }}>
                            <Clock size={22} />
                        </div>
                        <div>
                            <div style={{ fontSize: '0.8rem', color: '#64748b', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.5px' }}>
                                Ca trực hôm nay ({new Date().toLocaleDateString('vi-VN')})
                            </div>
                            <div style={{ fontSize: '1.05rem', fontWeight: 700, color: '#0f172a', marginTop: '2px' }}>
                                {typeof currentShift === 'string' ? (
                                    currentShift || 'Hôm nay bạn không có ca trực phòng khám được phân bổ'
                                ) : currentShift ? (
                                    <>
                                        {currentShift.shiftName} ({currentShift.startTime?.substring(0, 5) || ''} - {currentShift.endTime?.substring(0, 5) || ''}){currentShift.room ? ` • ${currentShift.room}` : ''}
                                    </>
                                ) : (
                                    'Hôm nay bạn không có ca trực phòng khám được phân bổ'
                                )}
                            </div>
                        </div>
                    </div>
                    {currentShift && typeof currentShift === 'object' && (
                        <span style={{ backgroundColor: '#dcfce7', color: '#15803d', padding: '4px 10px', borderRadius: '20px', fontSize: '0.8rem', fontWeight: 600 }}>
                            {currentShift.status === 'InProgress' ? 'Đang trong ca trực' : 'Ca trực hôm nay'}
                        </span>
                    )}
                </div>
            </div>

            {/* KPIs Grid */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px', marginBottom: '28px' }}>
                <div className="card" style={{ padding: '18px', borderLeft: '4px solid #0284c7', borderRadius: '8px' }}>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', fontWeight: 600 }}>TỔNG SỐ CA HÔM NAY</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#0f172a', marginTop: '4px' }}>
                        {loading ? '...' : (kpis?.totalAppointmentsToday ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', marginTop: '4px' }}>Tất cả lịch khám</div>
                </div>

                <div className="card" style={{ padding: '18px', borderLeft: '4px solid #f59e0b', borderRadius: '8px' }}>
                    <div style={{ fontSize: '0.8rem', color: '#b45309', fontWeight: 600 }}>ĐANG CHỜ KHÁM</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#b45309', marginTop: '4px' }}>
                        {loading ? '...' : (kpis?.waitingCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', marginTop: '4px' }}>Đã xác nhận & check-in</div>
                </div>

                <div className="card" style={{ padding: '18px', borderLeft: '4px solid #6366f1', borderRadius: '8px' }}>
                    <div style={{ fontSize: '0.8rem', color: '#4338ca', fontWeight: 600 }}>ĐANG THĂM KHÁM</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#4338ca', marginTop: '4px' }}>
                        {loading ? '...' : (kpis?.inConsultationCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', marginTop: '4px' }}>Phiên khám đang mở</div>
                </div>

                <div className="card" style={{ padding: '18px', borderLeft: '4px solid #10b981', borderRadius: '8px' }}>
                    <div style={{ fontSize: '0.8rem', color: '#15803d', fontWeight: 600 }}>ĐÃ HOÀN TẤT</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#15803d', marginTop: '4px' }}>
                        {loading ? '...' : (kpis?.completedCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', marginTop: '4px' }}>Đã chốt kết quả & đơn thuốc</div>
                </div>

                <div className="card" style={{ padding: '18px', borderLeft: '4px solid #ef4444', borderRadius: '8px' }}>
                    <div style={{ fontSize: '0.8rem', color: '#b91c1c', fontWeight: 600 }}>VẮNG MẶT (NO-SHOW)</div>
                    <div style={{ fontSize: '1.8rem', fontWeight: 800, color: '#b91c1c', marginTop: '4px' }}>
                        {loading ? '...' : (kpis?.noShowCount ?? 0)}
                    </div>
                    <div style={{ fontSize: '0.8rem', color: '#64748b', marginTop: '4px' }}>Không có mặt đúng giờ</div>
                </div>
            </div>

            {/* Next Patient Spotlight Banner */}
            <div className="card" style={{ padding: '20px', marginBottom: '28px', backgroundColor: '#f0f9ff', border: '1px solid #bae6fd', borderRadius: '10px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                    <div style={{ flex: 1, minWidth: '280px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '6px' }}>
                            <span style={{ backgroundColor: '#0284c7', color: 'white', fontSize: '0.75rem', fontWeight: 700, padding: '2px 8px', borderRadius: '4px' }}>
                                BỆNH NHÂN TIẾP THEO
                            </span>
                            {nextPatient && getStatusBadge(nextPatient.status)}
                        </div>

                        {nextPatient ? (
                            <div>
                                <div style={{ fontSize: '1.3rem', fontWeight: 800, color: '#0f172a' }}>
                                    {nextPatient.patientName}
                                    <span style={{ fontSize: '0.9rem', fontWeight: 500, color: '#64748b', marginLeft: '8px' }}>
                                        (#{nextPatient.appointmentCode}) • {nextPatient.patientGender === 'Male' ? 'Nam' : nextPatient.patientGender === 'Female' ? 'Nữ' : 'Khác'} {nextPatient.patientAge ? `• ${nextPatient.patientAge} tuổi` : ''}
                                    </span>
                                </div>
                                <div style={{ fontSize: '0.9rem', color: '#334155', marginTop: '4px' }}>
                                    <strong>Khung giờ:</strong> {nextPatient.startTime?.substring(0, 5) || '--:--'} - {nextPatient.endTime?.substring(0, 5) || '--:--'} | <strong>SĐT:</strong> {nextPatient.patientPhone}
                                </div>
                                <div style={{ fontSize: '0.9rem', color: '#475569', marginTop: '2px' }}>
                                    <strong>Lý do khám:</strong> {nextPatient.reason || 'Khám tổng quát theo lịch hẹn'}
                                </div>
                                {nextPatient.vitalSummaryText && (
                                    <div style={{ fontSize: '0.85rem', color: '#0369a1', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                                        <HeartPulse size={14} />
                                        <span>Dấu hiệu sinh tồn: {nextPatient.vitalSummaryText}</span>
                                    </div>
                                )}
                            </div>
                        ) : (
                            <div style={{ color: '#64748b', fontStyle: 'italic', padding: '8px 0' }}>
                                Hiện không có bệnh nhân nào đang chờ hoặc tiếp nhận trong hàng đợi hôm nay.
                            </div>
                        )}
                    </div>

                    {nextPatient && (
                        <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                            {nextPatient.status === 'Confirmed' && (
                                <button
                                    className="btn-secondary"
                                    onClick={() => handleCheckIn(nextPatient.appointmentId)}
                                    disabled={actionLoadingId === nextPatient.appointmentId}
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '10px 16px', borderRadius: '6px', cursor: 'pointer' }}
                                >
                                    <UserCheck size={18} />
                                    <span>Tiếp nhận</span>
                                </button>
                            )}

                            {nextPatient.status === 'CheckedIn' && (
                                <button
                                    className="btn-primary"
                                    onClick={() => handleStartConsultation(nextPatient.appointmentId)}
                                    disabled={actionLoadingId === nextPatient.appointmentId}
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '10px 20px', borderRadius: '6px', fontWeight: 600, cursor: 'pointer' }}
                                >
                                    <Stethoscope size={18} />
                                    <span>Bắt đầu khám</span>
                                </button>
                            )}

                            {nextPatient.status === 'InConsultation' && (
                                <button
                                    className="btn-primary"
                                    onClick={() => navigate(`/doctor/appointments/${nextPatient.appointmentId}/examination`)}
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '10px 20px', borderRadius: '6px', fontWeight: 600, backgroundColor: '#4f46e5', cursor: 'pointer' }}
                                >
                                    <ArrowRight size={18} />
                                    <span>Tiếp tục khám</span>
                                </button>
                            )}
                        </div>
                    )}
                </div>
            </div>

            {/* Today's Queue Section */}
            <div className="card" style={{ padding: '20px', borderRadius: '10px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '10px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <Users size={20} style={{ color: '#0284c7' }} />
                        <h2 style={{ fontSize: '1.2rem', fontWeight: 700, color: '#0f172a', margin: 0 }}>
                            Hàng đợi khám hôm nay ({queue.length} bệnh nhân)
                        </h2>
                    </div>
                    <Link to="/doctor/queue" style={{ fontSize: '0.9rem', color: '#0284c7', fontWeight: 600, textDecoration: 'none' }}>
                        Xem chế độ hàng đợi đầy đủ →
                    </Link>
                </div>

                {loading ? (
                    <div style={{ textAlign: 'center', padding: '32px 0', color: '#64748b' }}>
                        <RefreshCw size={24} className="animate-spin" style={{ margin: '0 auto 8px auto' }} />
                        <p>Đang tải danh sách hàng đợi...</p>
                    </div>
                ) : queue.length === 0 ? (
                    <div style={{ textAlign: 'center', padding: '40px 0', color: '#64748b' }}>
                        <AlertCircle size={36} style={{ margin: '0 auto 10px auto', color: '#94a3b8' }} />
                        <p style={{ fontWeight: 600 }}>Không có lịch khám nào được ghi nhận hôm nay.</p>
                        <p style={{ fontSize: '0.85rem' }}>Các lịch hẹn mới của bệnh nhân sẽ tự động xuất hiện tại đây khi được đặt.</p>
                    </div>
                ) : (
                    <div style={{ overflowX: 'auto' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
                            <thead>
                                <tr style={{ borderBottom: '2px solid #e2e8f0', textAlign: 'left', color: '#475569', fontSize: '0.8rem', textTransform: 'uppercase' }}>
                                    <th style={{ padding: '12px 10px' }}>#</th>
                                    <th style={{ padding: '12px 10px' }}>Giờ hẹn</th>
                                    <th style={{ padding: '12px 10px' }}>Mã lịch</th>
                                    <th style={{ padding: '12px 10px' }}>Bệnh nhân</th>
                                    <th style={{ padding: '12px 10px' }}>Lý do khám</th>
                                    <th style={{ padding: '12px 10px' }}>Sinh hiệu</th>
                                    <th style={{ padding: '12px 10px' }}>Trạng thái</th>
                                    <th style={{ padding: '12px 10px', textAlign: 'right' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                                {queue.map((item, idx) => (
                                    <tr key={item.appointmentId} style={{ borderBottom: '1px solid #f1f5f9', backgroundColor: item.status === 'InConsultation' ? '#f5f3ff' : 'transparent' }}>
                                        <td style={{ padding: '12px 10px', fontWeight: 600, color: '#64748b' }}>
                                            {item.queueOrder || idx + 1}
                                        </td>
                                        <td style={{ padding: '12px 10px', fontWeight: 700, color: '#0f172a', whiteSpace: 'nowrap' }}>
                                            {item.startTime?.substring(0, 5) || '--:--'} - {item.endTime?.substring(0, 5) || '--:--'}
                                        </td>
                                        <td style={{ padding: '12px 10px', fontFamily: 'monospace', fontSize: '0.85rem', color: '#475569' }}>
                                            {item.appointmentCode}
                                        </td>
                                        <td style={{ padding: '12px 10px' }}>
                                            <div style={{ fontWeight: 600, color: '#0f172a' }}>{item.patientName}</div>
                                            <div style={{ fontSize: '0.8rem', color: '#64748b' }}>
                                                {item.patientPhone} {item.patientGender === 'Male' ? '• Nam' : item.patientGender === 'Female' ? '• Nữ' : ''} {item.patientAge ? `• ${item.patientAge}t` : ''}
                                            </div>
                                        </td>
                                        <td style={{ padding: '12px 10px', maxWidth: '200px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: '#334155' }}>
                                            {item.reason || 'Khám tổng quát'}
                                        </td>
                                        <td style={{ padding: '12px 10px' }}>
                                            {item.isVitalsRecorded ? (
                                                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: '#059669', fontSize: '0.8rem', fontWeight: 600 }}>
                                                    <CheckCircle size={14} />
                                                    <span>Đã đo</span>
                                                </span>
                                            ) : (
                                                <span style={{ color: '#94a3b8', fontSize: '0.8rem' }}>Chưa đo</span>
                                            )}
                                        </td>
                                        <td style={{ padding: '12px 10px' }}>
                                            {getStatusBadge(item.status)}
                                        </td>
                                        <td style={{ padding: '12px 10px', textAlign: 'right', whiteSpace: 'nowrap' }}>
                                            <div style={{ display: 'inline-flex', gap: '6px' }}>
                                                {item.status === 'Confirmed' && (
                                                    <>
                                                        <button 
                                                            className="btn-secondary"
                                                            onClick={() => handleCheckIn(item.appointmentId)}
                                                            disabled={actionLoadingId === item.appointmentId}
                                                            style={{ padding: '6px 10px', fontSize: '0.8rem', borderRadius: '4px', cursor: 'pointer' }}
                                                            title="Xác nhận bệnh nhân đã đến phòng khám"
                                                        >
                                                            Tiếp nhận
                                                        </button>
                                                        <button
                                                            onClick={() => handleMarkNoShow(item)}
                                                            style={{ padding: '6px 8px', fontSize: '0.8rem', borderRadius: '4px', border: '1px solid #fecaca', backgroundColor: '#fff5f5', color: '#dc2626', cursor: 'pointer' }}
                                                            title="Đánh dấu vắng mặt"
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
                                                        style={{ padding: '6px 12px', fontSize: '0.8rem', borderRadius: '4px', fontWeight: 600, backgroundColor: '#4f46e5', cursor: 'pointer' }}
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
                                                        Xem hồ sơ
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
