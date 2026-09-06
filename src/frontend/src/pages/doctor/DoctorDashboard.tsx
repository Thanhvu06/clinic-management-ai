import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { 
    Clock, Users, Stethoscope, CheckCircle, 
    Calendar, ArrowRight, RefreshCw, HeartPulse, UserCheck, 
    CalendarDays, AlertOctagon
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorDashboardDto, DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, StatCard, StatusBadge, InlineError, EmptyState } from '../../components/common';

export const DoctorDashboard: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert, showConfirm, showToast } = useDialog();

    const [dashboardData, setDashboardData] = useState<DoctorDashboardDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

    const loadDashboard = useCallback(async () => {
        setLoading(true);
        setLoadError(null);
        try {
            const res = await doctorApi.getDashboard();
            if (res.success && res.data) {
                setDashboardData(res.data);
            }
        } catch (err: any) {
            setLoadError(err.response?.data?.message || err.message || 'Không thể tải dữ liệu bàn làm việc bác sĩ.');
        } finally {
            setLoading(false);
        }
    }, []);

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
        <div>
            {/* Page Header */}
            <PageHeader
                title="Bàn làm việc Bác sĩ"
                subtitle="Hệ thống điều phối khám lâm sàng và quản lý hồ sơ y tế bệnh nhân hôm nay"
                actions={
                    <div style={{ display: 'flex', gap: '8px' }}>
                        <button 
                            className="btn-secondary" 
                            onClick={loadDashboard} 
                            disabled={loading}
                        >
                            <RefreshCw size={15} className={loading ? 'animate-spin' : ''} />
                            <span>Làm mới</span>
                        </button>
                        <Link to="/doctor/schedule" className="btn-primary" style={{ textDecoration: 'none' }}>
                            <Calendar size={15} />
                            <span>Lịch trực tuần</span>
                        </Link>
                    </div>
                }
            />

            {/* Load error inline banner */}
            {loadError && (
                <div style={{ marginBottom: '20px' }}>
                    <InlineError 
                        message={loadError} 
                        onRetry={loadDashboard} 
                    />
                </div>
            )}

            {/* Current Shift Info Banner */}
            <div className="card" style={{ 
                padding: '16px 20px', 
                marginBottom: '24px', 
                backgroundColor: currentShift ? '#f8fafc' : '#fffbeb', 
                borderLeft: currentShift ? '4px solid var(--c-teal)' : '4px solid #f59e0b'
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '12px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
                        <div style={{ 
                            width: '42px', 
                            height: '42px', 
                            borderRadius: '10px', 
                            backgroundColor: currentShift ? 'var(--c-teal-light)' : '#fef3c7', 
                            display: 'flex', 
                            alignItems: 'center', 
                            justifyContent: 'center', 
                            color: currentShift ? 'var(--c-teal)' : '#d97706' 
                        }}>
                            <Clock size={22} />
                        </div>
                        <div>
                            <div style={{ fontSize: '0.78rem', color: 'var(--c-text-light)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                                Ca trực hôm nay ({new Date().toLocaleDateString('vi-VN')})
                            </div>
                            <div style={{ fontSize: '1.05rem', fontWeight: 700, color: 'var(--c-text-dark)', marginTop: '2px' }}>
                                {typeof currentShift === 'string' ? (
                                    currentShift || 'Hôm nay bạn không có ca trực phòng khám được phân bổ'
                                ) : currentShift ? (
                                    <>
                                        {currentShift.shiftName} ({currentShift.startTime?.substring(0, 5) || ''} - {currentShift.endTime?.substring(0, 5) || ''}){currentShift.room ? ` • Phòng ${currentShift.room}` : ''}
                                    </>
                                ) : (
                                    'Hôm nay bạn không có ca trực phòng khám được phân bổ'
                                )}
                            </div>
                        </div>
                    </div>
                    {currentShift && typeof currentShift === 'object' && (
                        <StatusBadge 
                            status={currentShift.status === 'InProgress' ? 'Đang trong ca trực' : 'Ca trực hôm nay'} 
                            label={currentShift.status === 'InProgress' ? 'Đang trong ca trực' : 'Ca trực hôm nay'}
                        />
                    )}
                </div>
            </div>

            {/* KPIs Grid */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '16px', marginBottom: '28px' }}>
                <StatCard
                    title="Tổng số ca hôm nay"
                    value={loading ? '...' : (kpis?.totalAppointmentsToday ?? 0)}
                    subtitle="Tất cả lịch khám"
                    icon={<CalendarDays size={22} />}
                    color="primary"
                    onClick={() => navigate('/doctor/appointments')}
                />
                <StatCard
                    title="Đang chờ khám"
                    value={loading ? '...' : (kpis?.waitingCount ?? 0)}
                    subtitle="Đã xác nhận & tiếp nhận"
                    icon={<Users size={22} />}
                    color="warning"
                    onClick={() => navigate('/doctor/queue')}
                />
                <StatCard
                    title="Đang thăm khám"
                    value={loading ? '...' : (kpis?.inConsultationCount ?? 0)}
                    subtitle="Phiên khám đang mở"
                    icon={<Stethoscope size={22} />}
                    color="teal"
                />
                <StatCard
                    title="Đã hoàn tất"
                    value={loading ? '...' : (kpis?.completedCount ?? 0)}
                    subtitle="Đã chốt kết quả & đơn"
                    icon={<CheckCircle size={22} />}
                    color="success"
                />
                <StatCard
                    title="Vắng mặt (No-Show)"
                    value={loading ? '...' : (kpis?.noShowCount ?? 0)}
                    subtitle="Không đến khám"
                    icon={<AlertOctagon size={22} />}
                    color="danger"
                />
            </div>

            {/* Next Patient Spotlight Banner */}
            <div className="card" style={{ 
                padding: '20px 24px', 
                marginBottom: '28px', 
                backgroundColor: '#f0f9ff', 
                border: '1px solid #bae6fd', 
                borderRadius: '12px' 
            }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                    <div style={{ flex: 1, minWidth: '280px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                            <span style={{ 
                                backgroundColor: 'var(--c-primary)', 
                                color: 'white', 
                                fontSize: '0.72rem', 
                                fontWeight: 700, 
                                padding: '3px 8px', 
                                borderRadius: '4px',
                                letterSpacing: '0.04em'
                            }}>
                                BỆNH NHÂN TIẾP THEO
                            </span>
                            {nextPatient && <StatusBadge status={nextPatient.status} />}
                        </div>

                        {nextPatient ? (
                            <div>
                                <div style={{ fontSize: '1.25rem', fontWeight: 800, color: 'var(--c-text-dark)' }}>
                                    {nextPatient.patientName}
                                    <span style={{ fontSize: '0.85rem', fontWeight: 500, color: 'var(--c-text-light)', marginLeft: '10px' }}>
                                        (#{nextPatient.appointmentCode}) • {nextPatient.patientGender === 'Male' ? 'Nam' : nextPatient.patientGender === 'Female' ? 'Nữ' : 'Khác'} {nextPatient.patientAge ? `• ${nextPatient.patientAge} tuổi` : ''}
                                    </span>
                                </div>
                                <div style={{ fontSize: '0.88rem', color: 'var(--c-text)', marginTop: '4px' }}>
                                    <strong>Khung giờ:</strong> {nextPatient.startTime?.substring(0, 5) || '--:--'} - {nextPatient.endTime?.substring(0, 5) || '--:--'} | <strong>SĐT:</strong> {nextPatient.patientPhone}
                                </div>
                                <div style={{ fontSize: '0.88rem', color: 'var(--c-text-light)', marginTop: '2px' }}>
                                    <strong>Lý do khám:</strong> {nextPatient.reason || 'Khám tổng quát theo lịch hẹn'}
                                </div>
                                {nextPatient.vitalSummaryText && (
                                    <div style={{ fontSize: '0.82rem', color: 'var(--c-teal)', marginTop: '6px', display: 'flex', alignItems: 'center', gap: '5px', fontWeight: 500 }}>
                                        <HeartPulse size={14} />
                                        <span>Dấu hiệu sinh tồn: {nextPatient.vitalSummaryText}</span>
                                    </div>
                                )}
                            </div>
                        ) : (
                            <div style={{ color: 'var(--c-text-light)', fontStyle: 'italic', padding: '6px 0', fontSize: '0.9rem' }}>
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
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '9px 16px', borderRadius: '6px', cursor: 'pointer' }}
                                >
                                    <UserCheck size={16} />
                                    <span>Tiếp nhận</span>
                                </button>
                            )}

                            {nextPatient.status === 'CheckedIn' && (
                                <button
                                    className="btn-primary"
                                    onClick={() => handleStartConsultation(nextPatient.appointmentId)}
                                    disabled={actionLoadingId === nextPatient.appointmentId}
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '9px 20px', borderRadius: '6px', fontWeight: 600, cursor: 'pointer' }}
                                >
                                    <Stethoscope size={16} />
                                    <span>Bắt đầu khám</span>
                                </button>
                            )}

                            {nextPatient.status === 'InConsultation' && (
                                <button
                                    className="btn-primary"
                                    onClick={() => navigate(`/doctor/appointments/${nextPatient.appointmentId}/examination`)}
                                    style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '9px 20px', borderRadius: '6px', fontWeight: 600, cursor: 'pointer' }}
                                >
                                    <ArrowRight size={16} />
                                    <span>Tiếp tục khám</span>
                                </button>
                            )}
                        </div>
                    )}
                </div>
            </div>

            {/* Today's Queue Section */}
            <div className="card" style={{ padding: '24px', borderRadius: '12px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '18px', flexWrap: 'wrap', gap: '10px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{ width: '32px', height: '32px', borderRadius: '8px', backgroundColor: 'var(--c-primary-light)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--c-primary)' }}>
                            <Users size={18} />
                        </div>
                        <h2 style={{ fontSize: '1.15rem', fontWeight: 700, color: 'var(--c-text-dark)', margin: 0 }}>
                            Hàng đợi khám hôm nay ({queue.length} bệnh nhân)
                        </h2>
                    </div>
                    <Link to="/doctor/queue" style={{ fontSize: '0.88rem', color: 'var(--c-primary)', fontWeight: 600, textDecoration: 'none', display: 'flex', alignItems: 'center', gap: '4px' }}>
                        <span>Xem chế độ hàng đợi đầy đủ</span>
                        <ArrowRight size={14} />
                    </Link>
                </div>

                {loading ? (
                    <div style={{ textAlign: 'center', padding: '40px 0', color: 'var(--c-text-light)' }}>
                        <RefreshCw size={24} className="animate-spin" style={{ margin: '0 auto 8px auto' }} />
                        <p style={{ fontSize: '0.9rem' }}>Đang tải danh sách hàng đợi...</p>
                    </div>
                ) : queue.length === 0 ? (
                    <EmptyState
                        title="Không có lịch khám nào được ghi nhận hôm nay."
                        description="Các lịch hẹn mới của bệnh nhân sẽ tự động xuất hiện tại đây khi được đặt."
                    />
                ) : (
                    <div style={{ overflowX: 'auto' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.88rem' }}>
                            <thead>
                                <tr style={{ borderBottom: '2px solid var(--c-border)', textAlign: 'left', color: 'var(--c-text-light)', fontSize: '0.78rem', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
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
                                    <tr key={item.appointmentId} style={{ 
                                        borderBottom: '1px solid var(--c-border-light)', 
                                        backgroundColor: item.status === 'InConsultation' ? 'rgba(8, 145, 178, 0.04)' : 'transparent' 
                                    }}>
                                        <td style={{ padding: '14px 10px', fontWeight: 600, color: 'var(--c-text-light)' }}>
                                            {item.queueOrder || idx + 1}
                                        </td>
                                        <td style={{ padding: '14px 10px', fontWeight: 700, color: 'var(--c-text-dark)', whiteSpace: 'nowrap' }}>
                                            {item.startTime?.substring(0, 5) || '--:--'} - {item.endTime?.substring(0, 5) || '--:--'}
                                        </td>
                                        <td style={{ padding: '14px 10px', fontFamily: 'var(--font-mono, monospace)', fontSize: '0.82rem', color: 'var(--c-text-light)' }}>
                                            {item.appointmentCode}
                                        </td>
                                        <td style={{ padding: '14px 10px' }}>
                                            <div style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>{item.patientName}</div>
                                            <div style={{ fontSize: '0.78rem', color: 'var(--c-text-light)', marginTop: '2px' }}>
                                                {item.patientPhone} {item.patientGender === 'Male' ? '• Nam' : item.patientGender === 'Female' ? '• Nữ' : ''} {item.patientAge ? `• ${item.patientAge}t` : ''}
                                            </div>
                                        </td>
                                        <td style={{ padding: '14px 10px', maxWidth: '220px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--c-text)' }}>
                                            {item.reason || 'Khám tổng quát'}
                                        </td>
                                        <td style={{ padding: '14px 10px' }}>
                                            {item.isVitalsRecorded ? (
                                                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '4px', color: 'var(--c-success)', fontSize: '0.78rem', fontWeight: 600 }}>
                                                    <CheckCircle size={14} />
                                                    <span>Đã đo</span>
                                                </span>
                                            ) : (
                                                <span style={{ color: 'var(--c-text-light)', fontSize: '0.78rem' }}>Chưa đo</span>
                                            )}
                                        </td>
                                        <td style={{ padding: '14px 10px' }}>
                                            <StatusBadge status={item.status} size="sm" />
                                        </td>
                                        <td style={{ padding: '14px 10px', textAlign: 'right', whiteSpace: 'nowrap' }}>
                                            <div style={{ display: 'inline-flex', gap: '6px' }}>
                                                {item.status === 'Confirmed' && (
                                                    <>
                                                        <button 
                                                            className="btn-secondary"
                                                            onClick={() => handleCheckIn(item.appointmentId)}
                                                            disabled={actionLoadingId === item.appointmentId}
                                                            style={{ padding: '5px 10px', fontSize: '0.78rem' }}
                                                            title="Xác nhận bệnh nhân đã đến phòng khám"
                                                        >
                                                            Tiếp nhận
                                                        </button>
                                                        <button
                                                            onClick={() => handleMarkNoShow(item)}
                                                            style={{ 
                                                                padding: '5px 8px', 
                                                                fontSize: '0.78rem', 
                                                                borderRadius: '6px', 
                                                                border: '1px solid rgba(220, 38, 38, 0.2)', 
                                                                backgroundColor: 'var(--c-danger-bg)', 
                                                                color: 'var(--c-danger)', 
                                                                cursor: 'pointer',
                                                                fontWeight: 500
                                                            }}
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
                                                        style={{ padding: '5px 12px', fontSize: '0.78rem', fontWeight: 600 }}
                                                    >
                                                        Vào khám
                                                    </button>
                                                )}

                                                {item.status === 'InConsultation' && (
                                                    <button 
                                                        className="btn-primary"
                                                        onClick={() => navigate(`/doctor/appointments/${item.appointmentId}/examination`)}
                                                        style={{ padding: '5px 12px', fontSize: '0.78rem', fontWeight: 600 }}
                                                    >
                                                        Tiếp tục khám →
                                                    </button>
                                                )}

                                                {item.status === 'Completed' && (
                                                    <button 
                                                        className="btn-secondary"
                                                        onClick={() => navigate(`/doctor/appointments/${item.appointmentId}`)}
                                                        style={{ padding: '5px 10px', fontSize: '0.78rem' }}
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
