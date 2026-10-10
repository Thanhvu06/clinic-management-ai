import React, { useState, useEffect, useRef, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Button, Card, Input, Select } from 'antd';
import { PageHeader, StatCard, DataTable, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common/DataTable';
import {
    Users, UserPlus, Search, Clock, Calendar, CheckCircle2,
    Printer, RefreshCw, CreditCard, Package,
    Building2, Eye, AlertTriangle
} from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { organizationApi, type FacilityDto } from '../../api/organizationApi';
import { patientVisitApi } from '../../api/patientVisitApi';
import type { ApiResponse, CheckInTicketDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { MpiPatientSearchModal } from './MpiPatientSearchModal';
import { CheckInTicketModal } from '../../components/CheckInTicketModal';
import { useCopilotResource } from '../../components/copilot/copilotResourceContext';
import styles from './ReceptionWorkspace.module.css';
import { toLocalDateString } from '../../utils/formatters';

interface AppointmentItem {
    id: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    medicalRecordNumber?: string | null;
    nationalId?: string | null;
    doctorId: number;
    doctorName: string;
    specialtyId: number;
    specialtyName: string;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string | null;
    status: string;
    patientVisitId?: number | null;
}

export const ReceptionWorkspace: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert } = useDialog();
    const { setSelection } = useCopilotResource();

    // Facilities
    const [facilities, setFacilities] = useState<FacilityDto[]>([]);
    const [selectedFacilityId, setSelectedFacilityId] = useState<number | undefined>(undefined);
    const [facilityLoading, setFacilityLoading] = useState<boolean>(true);
    const [facilityError, setFacilityError] = useState<string | null>(null);

    // Live clock
    const [currentTime, setCurrentTime] = useState<string>('');

    // Metrics
    const [stats, setStats] = useState<{
        appointmentsToday: number;
        pendingAppointmentsToday: number;
        confirmedAppointmentsToday: number;
        completedAppointmentsToday: number;
        unbilledCount?: number;
    } | null>(null);

    // Search
    const [searchTerm, setSearchTerm] = useState('');

    // Worklist
    const [activeTab, setActiveTab] = useState<'today' | 'pending' | 'upcoming' | 'recent' | 'history'>('today');
    const [worklistItems, setWorklistItems] = useState<AppointmentItem[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [worklistError, setWorklistError] = useState<string | null>(null);
    const [isStale, setIsStale] = useState<boolean>(false);
    const [page, setPage] = useState<number>(1);
    const [totalItems, setTotalItems] = useState<number>(0);
    const pageSize = 10;

    // Search MPI Modal
    const [mpiModalOpen, setMpiModalOpen] = useState(false);

    // Ticket Modal
    const [ticketModalOpen, setTicketModalOpen] = useState(false);
    const [currentTicket, setCurrentTicket] = useState<CheckInTicketDto | null>(null);
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

    const selectAppointmentForCopilot = useCallback((item: AppointmentItem) => {
        setSelection({
            context: { appointmentId: item.id },
            source: 'reception-worklist',
            label: `Lịch hẹn ${item.appointmentCode}`
        });
    }, [setSelection]);

    // Stale request tracking
    const fetchIdRef = useRef(0);
    const statsFetchIdRef = useRef(0);

    // Update live clock
    useEffect(() => {
        const updateClock = () => {
            const now = new Date();
            const dateStr = now.toLocaleDateString('vi-VN', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' });
            const timeStr = now.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
            setCurrentTime(`${dateStr} • ${timeStr}`);
        };
        updateClock();
        const timer = setInterval(updateClock, 1000);
        return () => clearInterval(timer);
    }, []);

    // Fetch user-assigned facilities on mount
    const loadFacilities = useCallback(async () => {
        setFacilityLoading(true);
        setFacilityError(null);
        try {
            const res = await organizationApi.getMyFacilities();
            const data = res.data;
            if (res.success && data && data.length > 0) {
                setFacilities(data);
                setSelectedFacilityId(prev => prev && data.some(f => f.id === prev) ? prev : data[0].id);
            } else {
                setFacilities([]);
                setSelectedFacilityId(undefined);
            }
        } catch (err) {
            console.error('Lỗi khi tải danh sách cơ sở:', err);
            setFacilityError('Không thể tải danh sách cơ sở làm việc.');
        } finally {
            setFacilityLoading(false);
        }
    }, []);

    useEffect(() => {
        loadFacilities();
    }, [loadFacilities]);

    useEffect(() => {
        // A facility switch invalidates the currently selected appointment context.
        setSelection(null);
    }, [selectedFacilityId, activeTab, page, searchTerm, setSelection]);

    const fetchStats = useCallback(async (facId?: number) => {
        const currentFetchId = ++statsFetchIdRef.current;
        try {
            const query = facId ? `?facilityId=${facId}` : '';
            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/stats${query}`);
            if (currentFetchId !== statsFetchIdRef.current) return;
            if (res.success && res.data) {
                setStats(res.data);
            }
        } catch (err) {
            if (currentFetchId !== statsFetchIdRef.current) return;
            console.error('Lỗi tải thống kê tiếp nhận:', err);
        }
    }, []);

    const fetchWorklist = useCallback(async (silent = false) => {
        const currentFetchId = ++fetchIdRef.current;
        if (!silent) {
            setLoading(true);
            setWorklistError(null);
        }
        try {
            const params = new URLSearchParams({
                tab: activeTab,
                status: activeTab,
                page: page.toString(),
                pageSize: pageSize.toString()
            });
            if (selectedFacilityId) params.append('facilityId', selectedFacilityId.toString());
            if (searchTerm.trim()) params.append('search', searchTerm.trim());

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments?${params.toString()}`);
            if (currentFetchId !== fetchIdRef.current) return;
            if (res.success && res.data) {
                setWorklistItems(res.data.items || []);
                setTotalItems(res.data.totalItems || res.data.totalCount || 0);
                setWorklistError(null);
                setIsStale(false);
            }
        } catch (err) {
            if (currentFetchId !== fetchIdRef.current) return;
            console.error('Lỗi khi tải danh sách lịch tiếp nhận:', err);
            if (silent) {
                // Background polling error: preserve existing worklist and show stale banner
                setIsStale(true);
            } else {
                setWorklistError('Không thể tải danh sách lịch tiếp nhận. Vui lòng thử lại.');
                setWorklistItems([]);
                setTotalItems(0);
            }
        } finally {
            if (currentFetchId === fetchIdRef.current) {
                setLoading(false);
            }
        }
    }, [activeTab, page, pageSize, selectedFacilityId, searchTerm]);

    // Refresh worklist and stats when selected facility or active tab changes
    useEffect(() => {
        setWorklistError(null);
        setIsStale(false);
        if (selectedFacilityId) {
            fetchStats(selectedFacilityId);
        }
        fetchWorklist();
    }, [selectedFacilityId, activeTab, fetchStats, fetchWorklist]);

    // Background polling (every 25s)
    useEffect(() => {
        const timer = setInterval(() => {
            if (selectedFacilityId) {
                fetchStats(selectedFacilityId);
            }
            fetchWorklist(true);
        }, 25000);
        return () => clearInterval(timer);
    }, [selectedFacilityId, fetchStats, fetchWorklist]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        setWorklistError(null);
        setIsStale(false);
        fetchWorklist();
    };

    const handleRefreshAll = useCallback(() => {
        setWorklistError(null);
        setIsStale(false);
        if (selectedFacilityId) fetchStats(selectedFacilityId);
        fetchWorklist(false);
    }, [selectedFacilityId, fetchStats, fetchWorklist]);

    useEffect(() => {
        window.addEventListener('cliniccare:copilot-action-completed', handleRefreshAll);
        return () => window.removeEventListener('cliniccare:copilot-action-completed', handleRefreshAll);
    }, [handleRefreshAll]);

    // Fast check-in action from appointment
    const handleFastCheckIn = async (item: AppointmentItem) => {
        selectAppointmentForCopilot(item);
        setActionLoadingId(item.id);
        try {
            const res = await patientVisitApi.receptionCheckInAppointment(item.id);
            if (res.success && res.data) {
                setCurrentTicket(res.data);
                setTicketModalOpen(true);
                showAlert(
                    'Tiếp nhận ca khám thành công!',
                    `Đã cấp STT ${res.data.queueNumber} tại ${res.data.departmentName || 'phòng khám'} cho bệnh nhân ${res.data.patientName}.`,
                    'success'
                );
                handleRefreshAll();
            } else {
                showAlert('Lỗi tiếp nhận', res.message || 'Không thể tiếp nhận ca khám.', 'error');
            }
        } catch (err: any) {
            showAlert('Lỗi tiếp nhận', err.response?.data?.message || 'Không thể tiếp nhận ca khám.', 'error');
        } finally {
            setActionLoadingId(null);
        }
    };

    // View ticket modal
    const handleViewTicket = async (visitId: number) => {
        try {
            const res = await patientVisitApi.getCheckInTicket(visitId);
            if (res.success && res.data) {
                setCurrentTicket(res.data);
                setTicketModalOpen(true);
            }
        } catch {
            showAlert('Thông báo', 'Không thể lấy thông tin phiếu khám.', 'error');
        }
    };

    const renderStatusBadge = (status: string) => {
        switch (status) {
            case 'Pending': return <StatusBadge status={status} label="Chờ xác nhận" />;
            case 'Confirmed': return <StatusBadge status={status} label="Đã xác nhận (Chờ tiếp nhận)" />;
            case 'CheckedIn':
            case 'InProgress': return <StatusBadge status="inconsultation" label="Đang khám" />;
            case 'Completed': return <StatusBadge status={status} label="Đã hoàn thành" />;
            case 'Cancelled': return <StatusBadge status={status} label="Đã hủy" />;
            default: return <StatusBadge status={status} label="Chưa rõ" />;
        }
    };
    const totalPages = Math.ceil(totalItems / pageSize) || 1;
    const queueColumns: DataTableColumn<AppointmentItem>[] = [
        { header: 'Mã / Giờ hẹn', accessor: item => <><span className={styles.appointmentCode}>{item.appointmentCode}</span><div className={styles.appointmentTime}>{item.startTime} - {item.endTime}</div></> },
        { header: 'Người bệnh', accessor: item => <><div className={styles.patientName}>{item.patientName}</div><div className={styles.patientIdentifiers}>{item.medicalRecordNumber ? `MRN: ${item.medicalRecordNumber}` : ''}{item.patientPhone ? ` • ${item.patientPhone}` : ''}</div></> },
        { header: 'Chuyên khoa / Bác sĩ', accessor: item => <><div className={styles.specialtyName}>{item.specialtyName}</div><div className={styles.doctorName}>BS: {item.doctorName || 'Chưa phân công'}</div></> },
        { header: 'Lý do khám', accessor: item => item.reason || '-', className: styles.visitReason },
        { header: 'Trạng thái', accessor: item => renderStatusBadge(item.status) },
        { header: 'Thao tác', align: 'right', className: styles.queueActionsCell, accessor: item => (
            <>
                {item.status === 'Confirmed' && (
                    <Button type="primary" htmlType="button" className={styles.checkInButton} style={{ minHeight: '40px' }}
                        onClick={() => handleFastCheckIn(item)}
                        disabled={actionLoadingId === item.id || item.appointmentDate !== toLocalDateString()}
                        title={item.appointmentDate !== toLocalDateString() ? `Chỉ tiếp nhận vào ngày khám ${item.appointmentDate.split('-').reverse().join('/')}` : 'Tiếp nhận ngay và cấp số thứ tự vào hàng đợi bác sĩ'}>
                        {actionLoadingId === item.id ? <RefreshCw className="spin" size={13} /> : <CheckCircle2 size={13} />} Tiếp nhận
                    </Button>
                )}
                <Button htmlType="button" onClick={() => selectAppointmentForCopilot(item)} title="Chọn đúng lịch hẹn này cho Copilot">Chọn Copilot</Button>
                {item.patientVisitId && <Button htmlType="button" onClick={() => handleViewTicket(item.patientVisitId!)} title="Xem lại phiếu khám và in vé số thứ tự"><Printer size={13} /> Vé khám</Button>}
                <Link to={`/reception/appointments`} className={styles.detailLink} title="Xem chi tiết lịch hẹn"><Eye size={13} /></Link>
            </>
        ) }
    ];
    return (
        <div className={styles.workspace}>
            <Card className={styles.workspaceHeader}>
                <PageHeader title="Bàn Làm Việc Lễ Tân" badge={<Building2 size={24} className={styles.primaryIcon} />}
                    actions={<><div className={styles.liveClock}><Clock size={16} /><span>{currentTime}</span></div><Button htmlType="button" onClick={handleRefreshAll}><RefreshCw size={14} className={loading ? 'spin' : ''} /> Làm mới</Button></>} />
                <div className={styles.facilityPicker}>
                    <span className={styles.facilityLabel}>Cơ sở trực:</span>
                    {facilityLoading ? <LoadingState message="Đang tải cơ sở..." height="40px" /> : facilityError ? <InlineError title="" message={facilityError} onRetry={loadFacilities} /> : facilities.length === 0 ? (
                        <span className={styles.unassignedFacility}>Chưa được phân công cơ sở trực</span>
                    ) : facilities.length === 1 ? <span className={styles.assignedFacility}>{facilities[0].name} ({facilities[0].code})</span> : (
                        <Select className={styles.facilitySelect} value={selectedFacilityId || undefined}
                            onChange={(value) => { setWorklistError(null); setIsStale(false); setWorklistItems([]); setPage(1); setSelectedFacilityId(Number(value)); }}
                            options={facilities.map(f => ({ value: f.id, label: `${f.name} (${f.code})` }))} />
                    )}
                </div>
            </Card>
            {!facilityLoading && !facilityError && facilities.length === 0 && <div className={styles.facilityNotice}><AlertTriangle size={20} className={styles.noticeIcon} /><div><strong>Tài khoản chưa được phân công cơ sở trực:</strong> Bạn chưa có phân công làm việc tại cơ sở y tế nào. Vui lòng liên hệ quản trị viên hệ thống để được gán cơ sở trước khi thực hiện tiếp nhận và quản lý hàng đợi.</div></div>}
            <div className={styles.metricGrid}>
                <StatCard title="Lịch khám hôm nay" value={stats?.appointmentsToday ?? 0} subtitle="Lịch hẹn đã đặt trong ngày" icon={<Calendar size={24} />} color="info" />
                <StatCard title="Chờ xác nhận" value={stats?.pendingAppointmentsToday ?? 0} subtitle="Cần lễ tân duyệt hoặc gọi xác nhận" icon={<Clock size={24} />} color="warning" />
                <StatCard title="Chờ tiếp đón / Đang khám" value={stats?.confirmedAppointmentsToday ?? 0} subtitle="Sẵn sàng tiếp nhận vào hàng đợi" icon={<Users size={24} />} color="primary" />
                <StatCard title="Đã khám xong hôm nay" value={stats?.completedAppointmentsToday ?? 0} subtitle="Đã hoàn thành lượt khám" icon={<CheckCircle2 size={24} />} color="success" />
            </div>
            <Card className={styles.searchCard}>
                <form onSubmit={handleSearchSubmit} className={styles.searchForm}>
                    <Input className={styles.searchInput} prefix={<Search size={18} />} placeholder="Tìm kiếm nhanh: Mã bệnh nhân (MRN), Số CCCD, Mã lịch hẹn, Tên người bệnh, Số điện thoại..." value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} />
                    <Button type="primary" htmlType="submit"><Search size={16} /> Tra cứu</Button>
                    {searchTerm && <Button htmlType="button" onClick={() => { setSearchTerm(''); setPage(1); fetchWorklist(); }}>Xóa tìm kiếm</Button>}
                </form>
            </Card>
                <section className={styles.worklistCard}>
                    <div className={styles.worklistTabs}>
                        <Button htmlType="button" className={`${styles.worklistTab} ${activeTab === 'today' ? styles.activeWorklistTab : ''}`} onClick={() => { setWorklistError(null); setIsStale(false); setActiveTab('today'); setPage(1); }}><span>Hôm nay (Ưu tiên tiếp nhận)</span><span className={styles.tabCount}>{activeTab === 'today' ? totalItems : (stats?.appointmentsToday ?? 0)}</span></Button>
                        <Button htmlType="button" className={`${styles.worklistTab} ${activeTab === 'pending' ? styles.activeWorklistTab : ''}`} onClick={() => { setWorklistError(null); setIsStale(false); setActiveTab('pending'); setPage(1); }}><span>Chờ xác nhận</span><span className={styles.tabCount}>{stats?.pendingAppointmentsToday ?? 0}</span></Button>
                        <Button htmlType="button" className={`${styles.worklistTab} ${activeTab === 'upcoming' ? styles.activeWorklistTab : ''}`} onClick={() => { setWorklistError(null); setIsStale(false); setActiveTab('upcoming'); setPage(1); }}><span>Sắp tới</span></Button>
                        <Button htmlType="button" className={`${styles.worklistTab} ${activeTab === 'recent' ? styles.activeWorklistTab : ''}`} onClick={() => { setWorklistError(null); setIsStale(false); setActiveTab('recent'); setPage(1); }}><span>Gần đây</span></Button>
                        <Button htmlType="button" className={`${styles.worklistTab} ${activeTab === 'history' ? styles.activeWorklistTab : ''}`} onClick={() => { setWorklistError(null); setIsStale(false); setActiveTab('history'); setPage(1); }}><span>Toàn bộ lịch sử</span></Button>
                    </div>
                    {isStale && <div className={styles.staleNotice}><div className={styles.noticeMessage}><AlertTriangle size={16} /><span>Dữ liệu có thể chưa mới nhất do kết nối mạng trong lần đồng bộ gần nhất. Đang tự động thử lại...</span></div><Button htmlType="button" size="small" onClick={() => fetchWorklist(false)}>Thử lại ngay</Button></div>}
                    {loading ? <LoadingState message="Đang tải danh sách hàng đợi tiếp nhận..." /> : worklistError ? <InlineError title="" message={worklistError} onRetry={() => fetchWorklist(false)} /> : worklistItems.length === 0 ? <EmptyState title="Không có lịch hẹn nào trong mục này." /> : <div className={styles.queueTable}><DataTable columns={queueColumns} data={worklistItems} keyExtractor={item => item.id} /></div>}
                    {totalItems > pageSize && <div className={styles.queuePagination}><span>Hiển thị trang {page} / {totalPages} (Tổng {totalItems} lượt)</span><div className={styles.paginationButtons}><Button htmlType="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Trang trước</Button><Button htmlType="button" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>Trang sau</Button></div></div>}
                </section>
                <div className={styles.actionPanel}>
                    <Card className={styles.intakeCard}><div className={styles.actionCardHeader}><UserPlus size={20} className={styles.primaryIcon} /><h3 className={styles.actionCardTitle}>Tiếp nhận người bệnh</h3></div><p className={styles.actionCardDescription}>Quy trình 3 bước chuẩn bệnh viện: Tìm kiếm hồ sơ cũ qua MRN/CCCD/SĐT, chọn bác sĩ & phòng khám, xác nhận in phiếu khám A5/nhiệt.</p><Link to="/reception/walk-in" className={styles.intakeLink}><UserPlus size={18} /> Tiếp nhận người bệnh mới</Link></Card>
                    <Card className={styles.actionCard}><div className={styles.actionCardHeader}><Search size={20} className={styles.searchActionIcon} /><h3 className={styles.actionCardTitle}>Tra cứu hồ sơ y bạ (MPI)</h3></div><p className={styles.actionCardDescription}>Kiểm tra lịch sử khám, thẻ BHYT, CCCD, thông tin liên lạc và tiền sử dị ứng người bệnh trên toàn hệ thống.</p><Button htmlType="button" block onClick={() => setMpiModalOpen(true)}><Search size={16} /> Mở tra cứu hồ sơ bệnh nhân</Button></Card>
                    <Card className={styles.actionCard}><div className={styles.actionCardHeader}><CreditCard size={20} className={styles.billingActionIcon} /><h3 className={styles.actionCardTitle}>Hàng đợi viện phí & Thu ngân</h3>{stats?.unbilledCount !== undefined && stats.unbilledCount > 0 && <span className={styles.unbilledCount}>{stats.unbilledCount} chờ thu</span>}</div><p className={styles.actionCardDescription}>Thu tiền công khám, dịch vụ cận lâm sàng và đơn thuốc đã xác nhận mua theo cơ chế khóa chống thu trùng.</p><Link to="/reception/billing" className={styles.actionLink}><CreditCard size={16} /> Mở thu ngân viện phí</Link></Card>
                    <Card className={styles.actionCard}><div className={styles.actionCardHeader}><Package size={20} className={styles.packageActionIcon} /><h3 className={styles.actionCardTitle}>Gói khám sức khỏe</h3></div><p className={styles.actionCardDescription}>Tiếp đón và kích hoạt lượt khám cho người bệnh đăng ký gói khám sức khỏe tổng quát.</p><Link to="/reception/package-registrations" className={styles.actionLink}><Package size={16} /> Quản lý đăng ký gói khám</Link></Card>
                </div>
            <MpiPatientSearchModal isOpen={mpiModalOpen} onClose={() => setMpiModalOpen(false)} onSelectPatient={(patient) => { setMpiModalOpen(false); navigate(`/reception/walk-in?existingPatientId=${patient.id}`); }} />
            <CheckInTicketModal isOpen={ticketModalOpen} ticket={currentTicket} onClose={() => setTicketModalOpen(false)} />
        </div>
    );
};
