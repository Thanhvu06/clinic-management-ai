import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Flex, Input, Segmented } from 'antd';
import {
    Users, RefreshCw, Calendar, Clock,
    CheckCircle2, PlayCircle, Eye, UserX, Stethoscope
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import {
    PageHeader, FilterBar, StatusBadge, DataTable,
    InlineError, LoadingState, EmptyState
} from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './DoctorQueue.module.css';

export const DoctorQueue: React.FC = () => {
    const navigate = useNavigate();
    const { showConfirm, showToast } = useDialog();

    const todayStr = toLocalDateString();
    const [selectedDate, setSelectedDate] = useState(todayStr);
    const [queue, setQueue] = useState<DoctorQueueItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [statusFilter, setStatusFilter] = useState('ALL');
    const [searchTerm, setSearchTerm] = useState('');
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

    const loadQueue = useCallback(async (silent = false) => {
        if (!silent) setLoading(true);
        setError(null);
        try {
            const res = await doctorApi.getQueue(selectedDate);
            if (res && res.success && res.data) {
                setQueue(res.data);
            } else {
                setQueue([]);
            }
        } catch (err: any) {
            if (!silent) {
                const msg = err?.message || (typeof err === 'string' ? err : 'Không thể tải danh sách hàng đợi.');
                setError(msg);
            }
        } finally {
            if (!silent) setLoading(false);
        }
    }, [selectedDate]);

    useEffect(() => {
        loadQueue();
    }, [loadQueue]);

    // Background polling (every 25s)
    useEffect(() => {
        const timer = setInterval(() => {
            loadQueue(true);
        }, 25000);
        return () => clearInterval(timer);
    }, [loadQueue]);

    const handleCheckIn = async (appointmentId: number) => {
        setActionLoadingId(appointmentId);
        try {
            const res = await doctorApi.checkInAppointment(appointmentId);
            if (res.success) {
                const queueDisplay = typeof res.data?.queueDisplay === 'string' ? res.data.queueDisplay : null;
                showToast(queueDisplay ? `Đã tiếp nhận bệnh nhân vào phòng khám! Số thứ tự: ${queueDisplay}.` : 'Đã tiếp nhận bệnh nhân vào phòng khám!', 'success');
                await loadQueue();
            }
        } catch (err: any) {
            showToast(err?.message || 'Không thể tiếp nhận bệnh nhân.', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    const handleStartConsultation = async (item: DoctorQueueItemDto) => {
        const targetId = item.appointmentId || item.patientVisitId;
        if (!targetId) return;
        setActionLoadingId(targetId);
        try {
            if (item.appointmentId) {
                const res = await doctorApi.startConsultation(item.appointmentId);
                if (res.success) {
                    showToast('Bắt đầu phiên khám lâm sàng.', 'success');
                    navigate(`/doctor/appointments/${item.appointmentId}/examination`);
                }
            } else if (item.patientVisitId) {
                const res = await doctorApi.startVisitConsultation(item.patientVisitId);
                if (res.success) {
                    showToast('Bắt đầu phiên khám lâm sàng.', 'success');
                    navigate(`/doctor/visits/${item.patientVisitId}/examination`);
                }
            }
        } catch (err: any) {
            showToast(err?.message || 'Không thể bắt đầu phiên khám.', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    const getExaminationUrl = (item: DoctorQueueItemDto) => {
        if (item.appointmentId) {
            return `/doctor/appointments/${item.appointmentId}/examination`;
        }
        if (item.patientVisitId) {
            return `/doctor/visits/${item.patientVisitId}/examination`;
        }
        return '/doctor/queue';
    };

    const handleMarkNoShow = (item: DoctorQueueItemDto) => {
        if (!item.appointmentId) {
            showToast('Chỉ áp dụng vắng mặt cho lịch hẹn có đặt trước.', 'warning');
            return;
        }
        showConfirm(
            `Xác nhận đánh dấu bệnh nhân "${item.patientName}" vắng mặt cho ca khám lúc ${item.startTime.substring(0, 5)}?`,
            async () => {
                setActionLoadingId(item.appointmentId!);
                try {
                    const res = await doctorApi.markNoShow(item.appointmentId!, 'Bệnh nhân vắng mặt tại phòng khám.');
                    if (res.success) {
                        showToast('Đã đánh dấu bệnh nhân vắng mặt.', 'info');
                        await loadQueue();
                    }
                } catch (err: any) {
                    showToast(err?.message || 'Không thể đánh dấu vắng mặt.', 'error');
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

    const statusCounts = {
        ALL: queue.length,
        Confirmed: queue.filter(i => i.status === 'Confirmed').length,
        CheckedIn: queue.filter(i => i.status === 'CheckedIn').length,
        InConsultation: queue.filter(i => i.status === 'InConsultation').length,
        ResultsReady: queue.filter(i => i.status === 'ResultsReady').length,
        InDiagnostics: queue.filter(i => i.status === 'InDiagnostics').length,
        Completed: queue.filter(i => i.status === 'Completed').length,
        NoShow: queue.filter(i => i.status === 'NoShow').length,
    };

    const statusTabOptions = [
        { value: 'ALL', label: `Tất cả (${statusCounts.ALL})` },
        { value: 'Confirmed', label: `Chờ tiếp nhận (${statusCounts.Confirmed})` },
        { value: 'CheckedIn', label: `Đã tiếp nhận (${statusCounts.CheckedIn})` },
        { value: 'InConsultation', label: `Đang khám (${statusCounts.InConsultation})` },
        { value: 'InDiagnostics', label: `Chờ CLS (${statusCounts.InDiagnostics})` },
        { value: 'ResultsReady', label: `Có kết quả CLS (${statusCounts.ResultsReady})` },
        { value: 'Completed', label: `Đã xong (${statusCounts.Completed})` },
        { value: 'NoShow', label: `Vắng mặt (${statusCounts.NoShow})` },
    ];

    const columns: DataTableColumn<DoctorQueueItemDto>[] = [
        { header: 'STT', align: 'center', width: 60, accessor: (item) => item.queueOrder || filteredQueue.indexOf(item) + 1 },
        {
            header: 'Khung giờ',
            width: 130,
            accessor: (item) => (
                <Flex align="center" gap={6} style={{ fontWeight: 600, color: 'var(--cc-color-text-dark)' }}>
                    <Clock size={14} style={{ color: 'var(--cc-color-teal)' }} />
                    <span>{item.startTime.substring(0, 5)} - {item.endTime.substring(0, 5)}</span>
                </Flex>
            )
        },
        { header: 'Mã lịch', width: 130, accessor: (item) => <span style={{ fontFamily: 'monospace', fontSize: '0.84rem', color: 'var(--cc-color-text-muted)' }}>{item.appointmentCode}</span> },
        {
            header: 'Bệnh nhân',
            accessor: (item) => (
                <div>
                    <div style={{ fontWeight: 600, color: 'var(--cc-color-text-dark)' }}>{item.patientName}</div>
                    <div className={styles.patientMeta}>
                        {item.patientPhone}
                        {item.patientGender === 'Male' ? ' • Nam' : item.patientGender === 'Female' ? ' • Nữ' : ''}
                        {item.patientAge ? ` • ${item.patientAge} tuổi` : ''}
                    </div>
                </div>
            )
        },
        {
            header: 'Lý do khám / Chẩn đoán sơ bộ',
            accessor: (item) => <div className={styles.reasonCell}>{item.chiefComplaint || item.reason || 'Khám tổng quát theo lịch hẹn'}</div>
        },
        {
            header: 'Dấu hiệu sinh tồn',
            width: 180,
            accessor: (item) => item.isVitalsRecorded ? (
                <div>
                    <span className={styles.vitalsOk}><CheckCircle2 size={13} /><span>Đã ghi nhận</span></span>
                    {item.vitalSummaryText && <div className={styles.vitalsSummary}>{item.vitalSummaryText}</div>}
                </div>
            ) : (
                <span className={styles.vitalsPending}>Chưa ghi nhận</span>
            )
        },
        { header: 'Trạng thái', width: 130, accessor: (item) => <StatusBadge status={item.status} /> },
        {
            header: 'Thao tác',
            align: 'right',
            width: 220,
            accessor: (item) => (
                <Flex gap={6} justify="flex-end" wrap>
                    {item.status === 'Confirmed' && (
                        <>
                            <Button
                                size="small"
                                style={{ minHeight: 40 }}
                                onClick={() => item.appointmentId && handleCheckIn(item.appointmentId)}
                                loading={actionLoadingId === item.appointmentId}
                                disabled={item.appointmentDate !== toLocalDateString()}
                                title={item.appointmentDate !== toLocalDateString() ? `Chỉ tiếp nhận vào ngày khám ${item.appointmentDate.split('-').reverse().join('/')}` : undefined}
                            >
                                Tiếp nhận
                            </Button>
                            <Button
                                size="small"
                                danger
                                icon={<UserX size={14} />}
                                onClick={() => handleMarkNoShow(item)}
                                disabled={actionLoadingId === item.appointmentId}
                                title="Đánh dấu vắng mặt"
                            />
                        </>
                    )}

                    {(item.status === 'CheckedIn' || item.status === 'WaitingDoctor') && (
                        <Button
                            type="primary"
                            size="small"
                            icon={<PlayCircle size={14} />}
                            onClick={() => handleStartConsultation(item)}
                            loading={actionLoadingId === (item.appointmentId || item.patientVisitId)}
                        >
                            Vào khám
                        </Button>
                    )}

                    {item.status === 'InConsultation' && (
                        <Button type="primary" size="small" icon={<Stethoscope size={14} />} onClick={() => navigate(getExaminationUrl(item))}>
                            Tiếp tục khám
                        </Button>
                    )}

                    {item.status === 'InDiagnostics' && (
                        <Button size="small" icon={<Eye size={14} />} onClick={() => navigate(getExaminationUrl(item))}>
                            Chờ CLS
                        </Button>
                    )}

                    {item.status === 'ResultsReady' && (
                        <Button type="primary" size="small" icon={<CheckCircle2 size={14} />} onClick={() => navigate(getExaminationUrl(item))}>
                            Có kết quả CLS
                        </Button>
                    )}

                    {item.status === 'Completed' && (
                        <Button
                            size="small"
                            icon={<Eye size={14} />}
                            onClick={() => {
                                if (item.appointmentId) {
                                    navigate(`/doctor/appointments/${item.appointmentId}`);
                                } else {
                                    navigate(getExaminationUrl(item));
                                }
                            }}
                        >
                            Hồ sơ
                        </Button>
                    )}
                </Flex>
            )
        }
    ];

    return (
        <div>
            <PageHeader
                title="Hàng đợi khám lâm sàng"
                subtitle="Theo dõi thứ tự tiếp nhận, đo sinh hiệu và điều phối bệnh nhân vào phòng khám."
                actions={
                    <Flex align="center" gap="small">
                        <Input
                            type="date"
                            className={styles.dateInput}
                            prefix={<Calendar size={16} style={{ color: 'var(--cc-color-primary)' }} />}
                            value={selectedDate}
                            onChange={(e) => setSelectedDate(e.target.value)}
                        />
                        <Button icon={<RefreshCw size={15} className={loading ? 'animate-spin' : ''} />} onClick={() => loadQueue(false)} disabled={loading} title="Tải lại danh sách">
                            Làm mới
                        </Button>
                    </Flex>
                }
            />

            {error && (
                <InlineError title="Không thể tải danh sách hàng đợi" message={error} onRetry={loadQueue} />
            )}

            <FilterBar
                searchTerm={searchTerm}
                onSearchChange={setSearchTerm}
                searchPlaceholder="Tìm theo tên bệnh nhân, mã khám, SĐT..."
                onReset={() => {
                    setStatusFilter('ALL');
                    setSearchTerm('');
                    setSelectedDate(todayStr);
                }}
            >
                <Segmented options={statusTabOptions} value={statusFilter} onChange={(val) => setStatusFilter(String(val))} />
            </FilterBar>

            {loading ? (
                <LoadingState message="Đang tải danh sách hàng đợi khám..." height="280px" />
            ) : filteredQueue.length === 0 ? (
                <EmptyState
                    icon={<Users size={44} />}
                    title="Không tìm thấy bệnh nhân nào trong hàng đợi"
                    description={searchTerm || statusFilter !== 'ALL'
                        ? "Thử thay đổi bộ lọc trạng thái hoặc từ khóa tìm kiếm."
                        : "Chưa có lịch khám cho ngày đã chọn."}
                />
            ) : (
                <DataTable
                    columns={columns}
                    data={filteredQueue}
                    keyExtractor={(item) => item.appointmentId ? `apt-${item.appointmentId}` : `visit-${item.patientVisitId}`}
                />
            )}
        </div>
    );
};
