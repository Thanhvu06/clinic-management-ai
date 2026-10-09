import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { Button, Card, Col, Row, Flex, Typography } from 'antd';
import {
    Clock, Users, Stethoscope, CheckCircle,
    Calendar, ArrowRight, RefreshCw, HeartPulse, UserCheck,
    CalendarDays, AlertOctagon
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorDashboardDto, DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, StatCard, StatusBadge, InlineError, EmptyState, DataTable, LoadingState } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import { toLocalDateString } from '../../utils/formatters';
import styles from './DoctorDashboard.module.css';

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

        const interval = setInterval(() => {
            if (document.visibilityState === 'visible') {
                loadDashboard();
            }
        }, 30000);

        const onFocus = () => {
            loadDashboard();
        };
        window.addEventListener('focus', onFocus);

        return () => {
            clearInterval(interval);
            window.removeEventListener('focus', onFocus);
        };
    }, [loadDashboard]);

    const handleCheckIn = async (appointmentId?: number | null) => {
        if (!appointmentId) return;
        setActionLoadingId(appointmentId);
        try {
            const res = await doctorApi.checkInAppointment(appointmentId);
            if (res.success) {
                const queueDisplay = typeof res.data?.queueDisplay === 'string' ? res.data.queueDisplay : null;
                showToast(queueDisplay ? `Đã tiếp nhận bệnh nhân vào phòng khám! Số thứ tự: ${queueDisplay}.` : 'Đã tiếp nhận bệnh nhân vào phòng khám!', 'success');
                await loadDashboard();
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể tiếp nhận bệnh nhân.', 'Lỗi', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    const handleStartConsultation = async (item: DoctorQueueItemDto) => {
        const targetId = item.patientVisitId || item.appointmentId;
        if (!targetId) return;
        setActionLoadingId(targetId);
        try {
            if (item.patientVisitId) {
                const res = await doctorApi.startVisitConsultation(item.patientVisitId);
                if (res.success) {
                    showToast('Bắt đầu phiên khám lâm sàng.', 'success');
                    navigate(`/doctor/visits/${item.patientVisitId}/examination`);
                }
            } else if (item.appointmentId) {
                const res = await doctorApi.startConsultation(item.appointmentId);
                if (res.success) {
                    showToast('Bắt đầu phiên khám lâm sàng.', 'success');
                    navigate(`/doctor/appointments/${item.appointmentId}/examination`);
                }
            }
        } catch (err: any) {
            showAlert(err.response?.data?.message || 'Không thể bắt đầu phiên khám.', 'Lỗi', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    const handleMarkNoShow = (item: DoctorQueueItemDto) => {
        if (!item.appointmentId) return;
        showConfirm(
            `Xác nhận đánh dấu bệnh nhân "${item.patientName}" vắng mặt cho ca khám lúc ${item.startTime.substring(0, 5)}?`,
            async () => {
                setActionLoadingId(item.appointmentId!);
                try {
                    const res = await doctorApi.markNoShow(item.appointmentId!, 'Bệnh nhân không có mặt tại phòng khám.');
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
    const upcoming = dashboardData?.upcomingAppointments || [];

    const queueColumns: DataTableColumn<DoctorQueueItemDto>[] = [
        { header: '#', accessor: (item) => queue.indexOf(item) + 1, width: 50 },
        {
            header: 'Giờ hẹn',
            accessor: (item) => `${item.startTime?.substring(0, 5) || '--:--'} - ${item.endTime?.substring(0, 5) || '--:--'}`,
            width: 130
        },
        { header: 'Mã lịch', accessor: 'appointmentCode', width: 120 },
        {
            header: 'Bệnh nhân',
            accessor: (item) => (
                <div>
                    <div style={{ fontWeight: 600, color: 'var(--cc-color-text-dark)' }}>{item.patientName}</div>
                    <Typography.Text type="secondary" style={{ fontSize: '0.78rem' }}>
                        {item.patientPhone} {item.patientGender === 'Male' ? '• Nam' : item.patientGender === 'Female' ? '• Nữ' : ''} {item.patientAge ? `• ${item.patientAge}t` : ''}
                    </Typography.Text>
                </div>
            )
        },
        { header: 'Lý do khám', accessor: (item) => item.reason || 'Khám tổng quát' },
        {
            header: 'Sinh hiệu',
            accessor: (item) => item.isVitalsRecorded
                ? <span className={styles.vitalsOk}><CheckCircle size={14} /><span>Đã đo</span></span>
                : <span className={styles.vitalsPending}>Chưa đo</span>
        },
        { header: 'Trạng thái', accessor: (item) => <StatusBadge status={item.status} size="sm" /> },
        {
            header: 'Thao tác',
            align: 'right',
            accessor: (item) => (
                <Flex gap="small" justify="flex-end">
                    {item.status === 'Confirmed' && item.appointmentId && (
                        <>
                            <Button
                                size="small"
                                style={{ minHeight: 40 }}
                                onClick={() => handleCheckIn(item.appointmentId)}
                                loading={actionLoadingId === item.appointmentId}
                                disabled={item.appointmentDate !== toLocalDateString()}
                                title={item.appointmentDate !== toLocalDateString() ? `Chỉ tiếp nhận vào ngày khám ${item.appointmentDate.split('-').reverse().join('/')}` : 'Xác nhận bệnh nhân đã đến phòng khám'}
                            >
                                Tiếp nhận
                            </Button>
                            <Button size="small" danger onClick={() => handleMarkNoShow(item)} title="Đánh dấu vắng mặt">
                                Vắng
                            </Button>
                        </>
                    )}
                    {item.status === 'CheckedIn' && (
                        <Button
                            type="primary"
                            size="small"
                            onClick={() => handleStartConsultation(item)}
                            loading={actionLoadingId === (item.patientVisitId || item.appointmentId)}
                        >
                            Vào khám
                        </Button>
                    )}
                    {item.status === 'InConsultation' && (
                        <Button
                            type="primary"
                            size="small"
                            onClick={() => navigate(item.patientVisitId ? `/doctor/visits/${item.patientVisitId}/examination` : `/doctor/appointments/${item.appointmentId}/examination`)}
                        >
                            Tiếp tục khám →
                        </Button>
                    )}
                    {item.status === 'Completed' && (
                        <Button
                            size="small"
                            onClick={() => item.appointmentId ? navigate(`/doctor/appointments/${item.appointmentId}`) : navigate('/doctor/queue')}
                        >
                            Xem hồ sơ
                        </Button>
                    )}
                </Flex>
            )
        }
    ];

    const upcomingColumns: DataTableColumn<DoctorQueueItemDto>[] = [
        { header: 'Ngày khám', accessor: (item) => item.appointmentDate ? new Date(item.appointmentDate).toLocaleDateString('vi-VN') : '--', width: 120 },
        { header: 'Khung giờ', accessor: (item) => `${item.startTime?.substring(0, 5) || '--:--'} - ${item.endTime?.substring(0, 5) || '--:--'}`, width: 130 },
        { header: 'Mã lịch', accessor: 'appointmentCode', width: 120 },
        {
            header: 'Bệnh nhân',
            accessor: (item) => (
                <div>
                    <div style={{ fontWeight: 600, color: 'var(--cc-color-text-dark)' }}>{item.patientName}</div>
                    <Typography.Text type="secondary" style={{ fontSize: '0.78rem' }}>
                        {item.patientPhone} {item.patientGender === 'Male' ? '• Nam' : item.patientGender === 'Female' ? '• Nữ' : ''} {item.patientAge ? `• ${item.patientAge}t` : ''}
                    </Typography.Text>
                </div>
            )
        },
        { header: 'Lý do khám', accessor: (item) => item.reason || 'Khám theo lịch hẹn' },
        { header: 'Trạng thái', accessor: (item) => <StatusBadge status={item.status} size="sm" /> },
        {
            header: 'Thao tác',
            align: 'right',
            accessor: (item) => (
                <Button size="small" onClick={() => navigate(`/doctor/appointments/${item.appointmentId}`)}>
                    Chi tiết
                </Button>
            )
        }
    ];

    return (
        <div>
            <PageHeader
                title="Bàn làm việc Bác sĩ"
                subtitle="Hệ thống điều phối khám lâm sàng và quản lý hồ sơ y tế bệnh nhân hôm nay"
                actions={
                    <Flex gap="small">
                        <Button
                            icon={<RefreshCw size={15} className={loading ? 'animate-spin' : ''} />}
                            onClick={loadDashboard}
                            disabled={loading}
                        >
                            Làm mới
                        </Button>
                        <Link to="/doctor/schedule">
                            <Button type="primary" icon={<Calendar size={15} />}>
                                Lịch trực tuần
                            </Button>
                        </Link>
                    </Flex>
                }
            />

            {loadError && (
                <InlineError message={loadError} onRetry={loadDashboard} />
            )}

            <Card className={`${styles.shiftBanner} ${currentShift ? styles.shiftBannerActive : styles.shiftBannerIdle}`}>
                <Flex justify="space-between" align="center" wrap gap="middle">
                    <Flex align="center" gap="middle">
                        <div className={`${styles.shiftIcon} ${currentShift ? styles.shiftIconActive : styles.shiftIconIdle}`}>
                            <Clock size={22} />
                        </div>
                        <div>
                            <div className={styles.shiftLabel}>
                                Ca trực hôm nay ({new Date().toLocaleDateString('vi-VN')})
                            </div>
                            <div className={styles.shiftValue}>
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
                    </Flex>
                    {currentShift && typeof currentShift === 'object' && (
                        <StatusBadge
                            status={currentShift.status === 'InProgress' ? 'Đang trong ca trực' : 'Ca trực hôm nay'}
                            label={currentShift.status === 'InProgress' ? 'Đang trong ca trực' : 'Ca trực hôm nay'}
                        />
                    )}
                </Flex>
            </Card>

            <Row gutter={[16, 16]} className={styles.kpiGrid}>
                <Col xs={12} sm={12} md={8} lg={4}>
                    <StatCard
                        title="Tổng số ca hôm nay"
                        value={loading ? '...' : (kpis?.totalAppointmentsToday ?? 0)}
                        subtitle="Tất cả lịch khám"
                        icon={<CalendarDays size={22} />}
                        color="primary"
                        onClick={() => navigate('/doctor/appointments')}
                    />
                </Col>
                <Col xs={12} sm={12} md={8} lg={4}>
                    <StatCard
                        title="Đang chờ khám"
                        value={loading ? '...' : (kpis?.waitingCount ?? 0)}
                        subtitle="Đã xác nhận & tiếp nhận"
                        icon={<Users size={22} />}
                        color="warning"
                        onClick={() => navigate('/doctor/queue')}
                    />
                </Col>
                <Col xs={12} sm={12} md={8} lg={4}>
                    <StatCard
                        title="Đang thăm khám"
                        value={loading ? '...' : (kpis?.inConsultationCount ?? 0)}
                        subtitle="Phiên khám đang mở"
                        icon={<Stethoscope size={22} />}
                        color="teal"
                    />
                </Col>
                <Col xs={12} sm={12} md={8} lg={4}>
                    <StatCard
                        title="Đã hoàn tất"
                        value={loading ? '...' : (kpis?.completedCount ?? 0)}
                        subtitle="Đã chốt kết quả & đơn"
                        icon={<CheckCircle size={22} />}
                        color="success"
                    />
                </Col>
                <Col xs={12} sm={12} md={8} lg={4}>
                    <StatCard
                        title="Vắng mặt (No-Show)"
                        value={loading ? '...' : (kpis?.noShowCount ?? 0)}
                        subtitle="Không đến khám"
                        icon={<AlertOctagon size={22} />}
                        color="danger"
                    />
                </Col>
            </Row>

            <Card className={styles.nextPatientBanner}>
                <Flex justify="space-between" align="center" wrap gap="middle">
                    <div style={{ flex: 1, minWidth: 280 }}>
                        <Flex align="center" gap="small" style={{ marginBottom: 8 }}>
                            <span className={styles.nextPatientTag}>BỆNH NHÂN TIẾP THEO</span>
                            {nextPatient && <StatusBadge status={nextPatient.status} />}
                        </Flex>

                        {nextPatient ? (
                            <div>
                                <span className={styles.nextPatientName}>
                                    {nextPatient.patientName}
                                    <span className={styles.nextPatientMeta}>
                                        (#{nextPatient.appointmentCode}) • {nextPatient.patientGender === 'Male' ? 'Nam' : nextPatient.patientGender === 'Female' ? 'Nữ' : 'Khác'} {nextPatient.patientAge ? `• ${nextPatient.patientAge} tuổi` : ''}
                                    </span>
                                </span>
                                <div style={{ fontSize: '0.88rem', color: 'var(--cc-color-text)', marginTop: 4 }}>
                                    <strong>Khung giờ:</strong> {nextPatient.startTime?.substring(0, 5) || '--:--'} - {nextPatient.endTime?.substring(0, 5) || '--:--'} | <strong>SĐT:</strong> {nextPatient.patientPhone}
                                </div>
                                <div style={{ fontSize: '0.88rem', color: 'var(--cc-color-text-muted)', marginTop: 2 }}>
                                    <strong>Lý do khám:</strong> {nextPatient.reason || 'Khám tổng quát theo lịch hẹn'}
                                </div>
                                {nextPatient.vitalSummaryText && (
                                    <Flex align="center" gap={5} style={{ fontSize: '0.82rem', color: 'var(--cc-color-teal)', marginTop: 6, fontWeight: 500 }}>
                                        <HeartPulse size={14} />
                                        <span>Dấu hiệu sinh tồn: {nextPatient.vitalSummaryText}</span>
                                    </Flex>
                                )}
                            </div>
                        ) : (
                            <Typography.Text type="secondary" italic>
                                Hiện không có bệnh nhân nào đang chờ hoặc tiếp nhận trong hàng đợi hôm nay.
                            </Typography.Text>
                        )}
                    </div>

                    {nextPatient && (
                        <Flex gap="small" align="center">
                            {nextPatient.status === 'Confirmed' && nextPatient.appointmentId && (
                                <Button
                                    icon={<UserCheck size={16} />}
                                    onClick={() => handleCheckIn(nextPatient.appointmentId)}
                                    loading={actionLoadingId === nextPatient.appointmentId}
                                >
                                    Tiếp nhận
                                </Button>
                            )}

                            {nextPatient.status === 'CheckedIn' && (
                                <Button
                                    type="primary"
                                    icon={<Stethoscope size={16} />}
                                    onClick={() => handleStartConsultation(nextPatient)}
                                    loading={actionLoadingId === (nextPatient.patientVisitId || nextPatient.appointmentId)}
                                >
                                    Bắt đầu khám
                                </Button>
                            )}

                            {nextPatient.status === 'InConsultation' && (
                                <Button
                                    type="primary"
                                    icon={<ArrowRight size={16} />}
                                    onClick={() => navigate(nextPatient.patientVisitId ? `/doctor/visits/${nextPatient.patientVisitId}/examination` : `/doctor/appointments/${nextPatient.appointmentId}/examination`)}
                                >
                                    Tiếp tục khám
                                </Button>
                            )}
                        </Flex>
                    )}
                </Flex>
            </Card>

            <Card className={styles.sectionCard}>
                <Flex justify="space-between" align="center" wrap gap="small" style={{ marginBottom: 18 }}>
                    <Flex align="center" gap="small">
                        <div className={styles.sectionIcon}><Users size={18} /></div>
                        <h2 className={styles.sectionTitle}>
                            Hàng đợi khám hôm nay ({queue.length} bệnh nhân)
                        </h2>
                    </Flex>
                    <Link to="/doctor/queue">
                        <Flex align="center" gap={4} style={{ fontSize: '0.88rem', color: 'var(--cc-color-primary)', fontWeight: 600 }}>
                            <span>Xem chế độ hàng đợi đầy đủ</span>
                            <ArrowRight size={14} />
                        </Flex>
                    </Link>
                </Flex>

                {loading ? (
                    <LoadingState message="Đang tải danh sách hàng đợi..." height="200px" />
                ) : queue.length === 0 ? (
                    <EmptyState
                        title="Không có lịch khám nào được ghi nhận hôm nay."
                        description="Các lịch hẹn mới của bệnh nhân sẽ tự động xuất hiện tại đây khi được đặt."
                    />
                ) : (
                    <DataTable
                        columns={queueColumns}
                        data={queue}
                        keyExtractor={(item) => item.appointmentId ?? `visit-${item.patientVisitId}`}
                    />
                )}
            </Card>

            <Card className={styles.sectionCard}>
                <Flex justify="space-between" align="center" wrap gap="small" style={{ marginBottom: 18 }}>
                    <Flex align="center" gap="small">
                        <div className={`${styles.sectionIcon} ${styles.sectionIconInfo}`}><CalendarDays size={18} /></div>
                        <h2 className={styles.sectionTitle}>
                            Lịch khám 7 ngày tới ({upcoming.length} bệnh nhân)
                        </h2>
                    </Flex>
                    <Link to="/doctor/appointments">
                        <Flex align="center" gap={4} style={{ fontSize: '0.88rem', color: 'var(--cc-color-primary)', fontWeight: 600 }}>
                            <span>Xem tất cả lịch hẹn</span>
                            <ArrowRight size={14} />
                        </Flex>
                    </Link>
                </Flex>

                {loading ? (
                    <LoadingState message="Đang nạp lịch sắp tới..." height="160px" />
                ) : upcoming.length === 0 ? (
                    <EmptyState
                        title="Không có lịch hẹn nào được ghi nhận trong 7 ngày tới."
                    />
                ) : (
                    <DataTable
                        columns={upcomingColumns}
                        data={upcoming}
                        keyExtractor={(item) => item.appointmentId ?? `visit-${item.patientVisitId}`}
                    />
                )}
            </Card>
        </div>
    );
};
