import { formatVitalSummaryVi } from '../../utils/formatDecimalVi';
import { getApiErrorMessage } from '../../utils/apiError';
import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { Button, Card, Flex, Typography } from 'antd';
import {
    Stethoscope, CheckCircle,
    Calendar, ArrowRight, RefreshCw, UserCheck
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorDashboardDto, DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, MetricStrip, StatusBadge, InlineError, EmptyState, DataTable, LoadingState } from '../../components/common';
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
            setLoadError(getApiErrorMessage(err, 'Không thể tải dữ liệu bàn làm việc bác sĩ.'));
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
            showAlert(getApiErrorMessage(err, 'Không thể tiếp nhận bệnh nhân.'), 'Lỗi', 'error');
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
            showAlert(getApiErrorMessage(err, 'Không thể bắt đầu phiên khám.'), 'Lỗi', 'error');
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
                    showAlert(getApiErrorMessage(err, 'Không thể đánh dấu vắng mặt.'), 'Lỗi', 'error');
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
                    <div className={styles.text1}>{item.patientName}</div>
                    <Typography.Text type="secondary" className={styles.icon1}>
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
                                className={styles.button1}
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
                    <div className={styles.text1}>{item.patientName}</div>
                    <Typography.Text type="secondary" className={styles.icon1}>
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

    const shiftText = (() => {
        if (!currentShift || typeof currentShift === 'string') {
            const text = (currentShift || '').trim();
            if (!text || text === 'Không có ca trực') return 'Hôm nay không có ca trực';
            return text.replace(/\s*\((\d{2}:\d{2}) - (\d{2}:\d{2})\)/g, ' $1–$2').replace(/(\d{2}:\d{2}) - (\d{2}:\d{2})/g, '$1–$2');
        }
        const room = currentShift.room ? ` · ${/^phòng/i.test(currentShift.room) ? currentShift.room : `Phòng ${currentShift.room}`}` : '';
        return `${currentShift.shiftName} ${currentShift.startTime?.substring(0, 5) || ''}–${currentShift.endTime?.substring(0, 5) || ''}${room}`;
    })();

    return (
        <div>
            <PageHeader
                title="Bàn làm việc bác sĩ"
                badge={dashboardData && <span className={styles.titleCount}>· {kpis?.totalAppointmentsToday ?? 0} ca hôm nay</span>}
                subtitle={dashboardData ? shiftText : undefined}
                actions={
                    <Flex gap="small">
                        <Button
                            icon={<RefreshCw size={15} />}
                            onClick={loadDashboard}
                            disabled={loading}
                        >
                            {loading ? 'Đang tải...' : 'Làm mới'}
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

            <MetricStrip
                ariaLabel="Số liệu hôm nay"
                items={[
                    { label: 'Đang chờ khám', value: loading ? '...' : (kpis?.waitingCount ?? 0), tone: 'attention', to: '/doctor/queue' },
                    { label: 'Đang khám', value: loading ? '...' : (kpis?.inConsultationCount ?? 0) },
                    { label: 'Đã hoàn tất', value: loading ? '...' : (kpis?.completedCount ?? 0) },
                    { label: 'Vắng mặt', value: loading ? '...' : (kpis?.noShowCount ?? 0) },
                ]}
            />

            <Card className={styles.nextPatientBanner}>
                <Flex justify="space-between" align="center" wrap gap="middle">
                    <div className={styles.spacing1}>
                        <Flex align="center" gap="small" className={styles.spacing2}>
                            <h2 className={styles.nextPatientTag}>Bệnh nhân tiếp theo</h2>
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
                                <div className={styles.text2}>
                                    <strong>Khung giờ:</strong> {nextPatient.startTime?.substring(0, 5) || '--:--'} - {nextPatient.endTime?.substring(0, 5) || '--:--'} | <strong>SĐT:</strong> {nextPatient.patientPhone}
                                </div>
                                <div className={styles.text3}>
                                    <strong>Lý do khám:</strong> {nextPatient.reason || 'Khám tổng quát theo lịch hẹn'}
                                </div>
                                {nextPatient.vitalSummaryText && (
                                    <div className={styles.text4}>Sinh hiệu: {formatVitalSummaryVi(nextPatient.vitalSummaryText)}</div>
                                )}
                            </div>
                        ) : (
                            <Typography.Text type="secondary">
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
                <Flex justify="space-between" align="center" wrap gap="small" className={styles.spacing3}>
                    <h2 className={styles.sectionTitle}>
                        Hàng đợi khám hôm nay ({queue.length} bệnh nhân)
                    </h2>
                    <Link to="/doctor/queue" className={styles.text5}>Xem chế độ hàng đợi đầy đủ</Link>
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
                <Flex justify="space-between" align="center" wrap gap="small" className={styles.spacing3}>
                    <h2 className={styles.sectionTitle}>
                        Lịch khám 7 ngày tới ({upcoming.length} bệnh nhân)
                    </h2>
                    <Link to="/doctor/appointments" className={styles.text5}>Xem tất cả lịch hẹn</Link>
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
