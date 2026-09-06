import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { 
    Users, RefreshCw, Calendar, Clock, 
    CheckCircle2, PlayCircle, Eye, UserX, Stethoscope
} from 'lucide-react';
import { doctorApi } from '../../api/doctorApi';
import type { DoctorQueueItemDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { 
    PageHeader, FilterBar, StatusBadge, 
    InlineError, LoadingState, EmptyState 
} from '../../components/common';

export const DoctorQueue: React.FC = () => {
    const navigate = useNavigate();
    const { showConfirm, showToast } = useDialog();

    const todayStr = new Date().toISOString().split('T')[0];
    const [selectedDate, setSelectedDate] = useState(todayStr);
    const [queue, setQueue] = useState<DoctorQueueItemDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [statusFilter, setStatusFilter] = useState('ALL');
    const [searchTerm, setSearchTerm] = useState('');
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

    const loadQueue = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await doctorApi.getQueue(selectedDate);
            if (res && res.success && res.data) {
                setQueue(res.data);
            } else {
                setQueue([]);
            }
        } catch (err: any) {
            const msg = err?.message || (typeof err === 'string' ? err : 'Không thể tải danh sách hàng đợi.');
            setError(msg);
        } finally {
            setLoading(false);
        }
    }, [selectedDate]);

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
            showToast(err?.message || 'Không thể tiếp nhận bệnh nhân.', 'error');
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
            showToast(err?.message || 'Không thể bắt đầu phiên khám.', 'error');
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
        Completed: queue.filter(i => i.status === 'Completed').length,
        NoShow: queue.filter(i => i.status === 'NoShow').length,
    };

    return (
        <div>
            <PageHeader
                title="Hàng đợi khám lâm sàng"
                subtitle="Theo dõi thứ tự tiếp nhận, đo sinh hiệu và điều phối bệnh nhân vào phòng khám."
                actions={
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            backgroundColor: 'white',
                            padding: '6px 12px',
                            border: '1px solid var(--c-border)',
                            borderRadius: 'var(--radius-md)',
                            boxShadow: 'var(--shadow-sm)'
                        }}>
                            <Calendar size={16} style={{ color: 'var(--c-primary)' }} />
                            <input
                                type="date"
                                value={selectedDate}
                                onChange={(e) => setSelectedDate(e.target.value)}
                                style={{
                                    border: 'none',
                                    outline: 'none',
                                    fontSize: '0.88rem',
                                    color: 'var(--c-text)',
                                    cursor: 'pointer',
                                    fontWeight: 500
                                }}
                            />
                        </div>

                        <button 
                            type="button"
                            className="btn-secondary" 
                            onClick={loadQueue}
                            disabled={loading}
                            title="Tải lại danh sách"
                        >
                            <RefreshCw size={15} className={loading ? 'animate-spin' : ''} />
                            <span>Làm mới</span>
                        </button>
                    </div>
                }
            />

            {error && (
                <InlineError
                    title="Không thể tải danh sách hàng đợi"
                    message={error}
                    onRetry={loadQueue}
                />
            )}

            {/* Filter and Tab Navigation Bar */}
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
                <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                    {[
                        { key: 'ALL', label: `Tất cả (${statusCounts.ALL})` },
                        { key: 'Confirmed', label: `Chờ tiếp nhận (${statusCounts.Confirmed})` },
                        { key: 'CheckedIn', label: `Đã tiếp nhận (${statusCounts.CheckedIn})` },
                        { key: 'InConsultation', label: `Đang khám (${statusCounts.InConsultation})` },
                        { key: 'Completed', label: `Đã xong (${statusCounts.Completed})` },
                        { key: 'NoShow', label: `Vắng mặt (${statusCounts.NoShow})` },
                    ].map(tab => (
                        <button
                            key={tab.key}
                            type="button"
                            onClick={() => setStatusFilter(tab.key)}
                            style={{
                                padding: '6px 12px',
                                borderRadius: 'var(--radius-md)',
                                fontSize: '0.82rem',
                                fontWeight: 600,
                                border: '1px solid',
                                borderColor: statusFilter === tab.key ? 'var(--c-primary)' : 'var(--c-border)',
                                backgroundColor: statusFilter === tab.key ? 'var(--c-primary)' : 'white',
                                color: statusFilter === tab.key ? 'white' : 'var(--c-text)',
                                cursor: 'pointer',
                                transition: 'all 0.15s ease'
                            }}
                        >
                            {tab.label}
                        </button>
                    ))}
                </div>
            </FilterBar>

            {/* Queue Table */}
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
                {loading ? (
                    <LoadingState message="Đang tải danh sách hàng đợi khám..." height="280px" />
                ) : filteredQueue.length === 0 ? (
                    <EmptyState
                        icon={<Users size={44} style={{ color: 'var(--c-muted)' }} />}
                        title="Không tìm thấy bệnh nhân nào trong hàng đợi"
                        description={searchTerm || statusFilter !== 'ALL' 
                            ? "Thử thay đổi bộ lọc trạng thái hoặc từ khóa tìm kiếm."
                            : "Chưa có lịch khám cho ngày đã chọn."}
                    />
                ) : (
                    <div className="table-responsive" style={{ border: 'none', borderRadius: 0 }}>
                        <table className="table">
                            <thead>
                                <tr>
                                    <th style={{ width: '60px', textAlign: 'center' }}>STT</th>
                                    <th style={{ width: '130px' }}>Khung giờ</th>
                                    <th style={{ width: '130px' }}>Mã lịch</th>
                                    <th>Bệnh nhân</th>
                                    <th>Lý do khám / Chẩn đoán sơ bộ</th>
                                    <th style={{ width: '180px' }}>Dấu hiệu sinh tồn</th>
                                    <th style={{ width: '130px' }}>Trạng thái</th>
                                    <th style={{ textAlign: 'right', width: '180px' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                                {filteredQueue.map((item, idx) => {
                                    const isCurrent = item.status === 'InConsultation';
                                    const isWaiting = item.status === 'CheckedIn';
                                    return (
                                        <tr 
                                            key={item.appointmentId}
                                            style={{
                                                backgroundColor: isCurrent 
                                                    ? 'rgba(15, 76, 129, 0.05)' 
                                                    : isWaiting 
                                                    ? 'rgba(5, 150, 105, 0.04)' 
                                                    : undefined,
                                                borderLeft: isCurrent 
                                                    ? '3px solid var(--c-primary)' 
                                                    : isWaiting 
                                                    ? '3px solid var(--c-success)' 
                                                    : undefined
                                            }}
                                        >
                                            <td style={{ textAlign: 'center', fontWeight: 700, color: 'var(--c-text-light)' }}>
                                                {item.queueOrder || idx + 1}
                                            </td>

                                            <td style={{ whiteSpace: 'nowrap' }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 600, color: 'var(--c-text-dark)' }}>
                                                    <Clock size={14} style={{ color: 'var(--c-teal)' }} />
                                                    <span>{item.startTime.substring(0, 5)} - {item.endTime.substring(0, 5)}</span>
                                                </div>
                                            </td>

                                            <td style={{ fontFamily: 'monospace', fontSize: '0.84rem', color: 'var(--c-text-light)' }}>
                                                {item.appointmentCode}
                                            </td>

                                            <td>
                                                <div style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>
                                                    {item.patientName}
                                                </div>
                                                <div style={{ fontSize: '0.78rem', color: 'var(--c-text-light)', marginTop: '2px' }}>
                                                    {item.patientPhone}
                                                    {item.patientGender === 'Male' ? ' • Nam' : item.patientGender === 'Female' ? ' • Nữ' : ''}
                                                    {item.patientAge ? ` • ${item.patientAge} tuổi` : ''}
                                                </div>
                                            </td>

                                            <td style={{ maxWidth: '240px' }}>
                                                <div style={{
                                                    whiteSpace: 'nowrap',
                                                    overflow: 'hidden',
                                                    textOverflow: 'ellipsis',
                                                    color: 'var(--c-text)',
                                                    fontSize: '0.88rem'
                                                }}>
                                                    {item.chiefComplaint || item.reason || 'Khám tổng quát theo lịch hẹn'}
                                                </div>
                                            </td>

                                            <td>
                                                {item.isVitalsRecorded ? (
                                                    <div>
                                                        <span style={{
                                                            display: 'inline-flex',
                                                            alignItems: 'center',
                                                            gap: '4px',
                                                            color: 'var(--c-success)',
                                                            fontSize: '0.8rem',
                                                            fontWeight: 600
                                                        }}>
                                                            <CheckCircle2 size={13} />
                                                            <span>Đã ghi nhận</span>
                                                        </span>
                                                        {item.vitalSummaryText && (
                                                            <div style={{ fontSize: '0.74rem', color: 'var(--c-text-light)', marginTop: '2px' }}>
                                                                {item.vitalSummaryText}
                                                            </div>
                                                        )}
                                                    </div>
                                                ) : (
                                                    <span style={{ color: 'var(--c-muted)', fontSize: '0.8rem', fontStyle: 'italic' }}>
                                                        Chưa ghi nhận
                                                    </span>
                                                )}
                                            </td>

                                            <td>
                                                <StatusBadge status={item.status} />
                                            </td>

                                            <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                                                <div style={{ display: 'inline-flex', gap: '6px', alignItems: 'center' }}>
                                                    {item.status === 'Confirmed' && (
                                                        <>
                                                            <button 
                                                                type="button"
                                                                className="btn-secondary"
                                                                onClick={() => handleCheckIn(item.appointmentId)}
                                                                disabled={actionLoadingId === item.appointmentId}
                                                                style={{ padding: '6px 12px', fontSize: '0.8rem', height: '32px' }}
                                                            >
                                                                Tiếp nhận
                                                            </button>
                                                            <button
                                                                type="button"
                                                                onClick={() => handleMarkNoShow(item)}
                                                                disabled={actionLoadingId === item.appointmentId}
                                                                style={{
                                                                    padding: '6px 10px',
                                                                    fontSize: '0.8rem',
                                                                    height: '32px',
                                                                    borderRadius: 'var(--radius-md)',
                                                                    border: '1px solid rgba(220, 38, 38, 0.25)',
                                                                    backgroundColor: 'var(--c-danger-bg)',
                                                                    color: 'var(--c-danger)'
                                                                }}
                                                                title="Đánh dấu vắng mặt"
                                                            >
                                                                <UserX size={14} />
                                                            </button>
                                                        </>
                                                    )}

                                                    {item.status === 'CheckedIn' && (
                                                        <button 
                                                            type="button"
                                                            className="btn-primary"
                                                            onClick={() => handleStartConsultation(item.appointmentId)}
                                                            disabled={actionLoadingId === item.appointmentId}
                                                            style={{
                                                                padding: '6px 14px',
                                                                fontSize: '0.82rem',
                                                                height: '32px',
                                                                backgroundColor: 'var(--c-teal)'
                                                            }}
                                                        >
                                                            <PlayCircle size={14} />
                                                            <span>Vào khám</span>
                                                        </button>
                                                    )}

                                                    {item.status === 'InConsultation' && (
                                                        <button 
                                                            type="button"
                                                            className="btn-primary"
                                                            onClick={() => navigate(`/doctor/appointments/${item.appointmentId}/examination`)}
                                                            style={{
                                                                padding: '6px 14px',
                                                                fontSize: '0.82rem',
                                                                height: '32px'
                                                            }}
                                                        >
                                                            <Stethoscope size={14} />
                                                            <span>Tiếp tục khám</span>
                                                        </button>
                                                    )}

                                                    {item.status === 'Completed' && (
                                                        <button 
                                                            type="button"
                                                            className="btn-secondary"
                                                            onClick={() => navigate(`/doctor/appointments/${item.appointmentId}`)}
                                                            style={{ padding: '6px 12px', fontSize: '0.8rem', height: '32px' }}
                                                        >
                                                            <Eye size={14} />
                                                            <span>Hồ sơ</span>
                                                        </button>
                                                    )}
                                                </div>
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
};
