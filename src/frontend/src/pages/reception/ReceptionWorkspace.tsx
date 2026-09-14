import React, { useState, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
    Users, UserPlus, Search, Clock, Calendar, CheckCircle2,
    Printer, RefreshCw, CreditCard, Package,
    Building2, Eye
} from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { organizationApi, type FacilityDto } from '../../api/organizationApi';
import { patientVisitApi } from '../../api/patientVisitApi';
import type { ApiResponse, CheckInTicketDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { MpiPatientSearchModal } from './MpiPatientSearchModal';
import { CheckInTicketModal } from '../../components/CheckInTicketModal';
import styles from './ReceptionWorkspace.module.css';

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

    // Facilities
    const [facilities, setFacilities] = useState<FacilityDto[]>([]);
    const [selectedFacilityId, setSelectedFacilityId] = useState<number | undefined>(undefined);

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
    const [page, setPage] = useState<number>(1);
    const [totalItems, setTotalItems] = useState<number>(0);
    const pageSize = 10;

    // Search MPI Modal
    const [mpiModalOpen, setMpiModalOpen] = useState(false);

    // Ticket Modal
    const [ticketModalOpen, setTicketModalOpen] = useState(false);
    const [currentTicket, setCurrentTicket] = useState<CheckInTicketDto | null>(null);
    const [actionLoadingId, setActionLoadingId] = useState<number | null>(null);

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

    // Fetch facilities on mount
    useEffect(() => {
        organizationApi.getFacilities(false)
            .then(res => {
                if (res.success && res.data && res.data.length > 0) {
                    setFacilities(res.data);
                    setSelectedFacilityId(res.data[0].id);
                }
            })
            .catch(err => console.error('Lỗi khi tải danh sách cơ sở:', err));
    }, []);

    const fetchStats = async (facId?: number) => {
        try {
            const query = facId ? `?facilityId=${facId}` : '';
            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/stats${query}`);
            if (res.success && res.data) {
                setStats(res.data);
            }
        } catch (err) {
            console.error('Lỗi tải thống kê tiếp nhận:', err);
        }
    };

    const fetchWorklist = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                tab: activeTab,
                page: page.toString(),
                pageSize: pageSize.toString()
            });
            if (selectedFacilityId) params.append('facilityId', selectedFacilityId.toString());
            if (searchTerm.trim()) params.append('search', searchTerm.trim());

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments?${params.toString()}`);
            if (res.success && res.data) {
                setWorklistItems(res.data.items || []);
                setTotalItems(res.data.totalItems || 0);
            }
        } catch (err) {
            console.error('Lỗi khi tải danh sách lịch tiếp nhận:', err);
            setWorklistItems([]);
            setTotalItems(0);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        if (selectedFacilityId) {
            fetchStats(selectedFacilityId);
        }
        fetchWorklist();
    }, [selectedFacilityId, activeTab, page]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchWorklist();
    };

    const handleRefreshAll = () => {
        if (selectedFacilityId) fetchStats(selectedFacilityId);
        fetchWorklist();
    };

    // Fast check-in action from appointment
    const handleFastCheckIn = async (item: AppointmentItem) => {
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
            case 'Pending':
                return <span className="badge badge-warning">Chờ xác nhận</span>;
            case 'Confirmed':
                return <span className="badge badge-info">Đã xác nhận (Chờ tiếp nhận)</span>;
            case 'CheckedIn':
            case 'InProgress':
                return <span className="badge badge-primary">Đang khám</span>;
            case 'Completed':
                return <span className="badge badge-success">Đã hoàn thành</span>;
            case 'Cancelled':
                return <span className="badge badge-danger">Đã hủy</span>;
            default:
                return <span className="badge">{status}</span>;
        }
    };

    const totalPages = Math.ceil(totalItems / pageSize) || 1;

    return (
        <div className={styles.container}>
            {/* 1. Top Bar */}
            <div className={styles.topBar}>
                <div className={styles.topBarLeft}>
                    <div className={styles.workspaceTitle}>
                        <Building2 size={24} color="var(--c-primary)" />
                        <h1>Bàn Làm Việc Lễ Tân</h1>
                    </div>

                    <div className={styles.facilitySelectWrapper}>
                        <span style={{ fontSize: '0.85rem', color: '#64748b' }}>Cơ sở trực:</span>
                        <select
                            className={styles.facilitySelect}
                            value={selectedFacilityId || ''}
                            onChange={(e) => setSelectedFacilityId(Number(e.target.value))}
                        >
                            {facilities.map((f) => (
                                <option key={f.id} value={f.id}>
                                    {f.name} ({f.code})
                                </option>
                            ))}
                        </select>
                    </div>
                </div>

                <div className={styles.topBarRight}>
                    <div className={styles.liveClock}>
                        <Clock size={16} />
                        <span>{currentTime}</span>
                    </div>

                    <button
                        type="button"
                        className="btn-secondary"
                        onClick={handleRefreshAll}
                        style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                    >
                        <RefreshCw size={14} className={loading ? 'spin' : ''} /> Làm mới
                    </button>
                </div>
            </div>

            {/* 2. Key Operational Metrics Cards */}
            <div className={styles.metricGrid}>
                <div className={styles.metricCard}>
                    <div className={styles.metricIcon} style={{ background: '#e0f2fe', color: '#0284c7' }}>
                        <Calendar size={24} />
                    </div>
                    <div className={styles.metricInfo}>
                        <span className={styles.metricLabel}>Lịch khám hôm nay</span>
                        <span className={styles.metricValue}>{stats?.appointmentsToday ?? 0}</span>
                        <span className={styles.metricSub}>Lịch hẹn đã đặt trong ngày</span>
                    </div>
                </div>

                <div className={styles.metricCard}>
                    <div className={styles.metricIcon} style={{ background: '#fef3c7', color: '#d97706' }}>
                        <Clock size={24} />
                    </div>
                    <div className={styles.metricInfo}>
                        <span className={styles.metricLabel}>Chờ xác nhận</span>
                        <span className={styles.metricValue}>{stats?.pendingAppointmentsToday ?? 0}</span>
                        <span className={styles.metricSub}>Cần lễ tân duyệt hoặc gọi xác nhận</span>
                    </div>
                </div>

                <div className={styles.metricCard}>
                    <div className={styles.metricIcon} style={{ background: '#e0e7ff', color: '#4338ca' }}>
                        <Users size={24} />
                    </div>
                    <div className={styles.metricInfo}>
                        <span className={styles.metricLabel}>Chờ tiếp đón / Đang khám</span>
                        <span className={styles.metricValue}>{stats?.confirmedAppointmentsToday ?? 0}</span>
                        <span className={styles.metricSub}>Sẵn sàng tiếp nhận vào hàng đợi</span>
                    </div>
                </div>

                <div className={styles.metricCard}>
                    <div className={styles.metricIcon} style={{ background: '#dcfce7', color: '#15803d' }}>
                        <CheckCircle2 size={24} />
                    </div>
                    <div className={styles.metricInfo}>
                        <span className={styles.metricLabel}>Đã khám xong hôm nay</span>
                        <span className={styles.metricValue}>{stats?.completedAppointmentsToday ?? 0}</span>
                        <span className={styles.metricSub}>Đã hoàn thành lượt khám</span>
                    </div>
                </div>
            </div>

            {/* 3. Search Bar Across All Fields */}
            <div className={styles.searchCard}>
                <form onSubmit={handleSearchSubmit} className={styles.searchForm}>
                    <div className={styles.searchBox}>
                        <Search size={18} className={styles.searchIcon} />
                        <input
                            type="text"
                            className={`form-input ${styles.searchInput}`}
                            placeholder="Tìm kiếm nhanh: Mã bệnh nhân (MRN), Số CCCD, Mã lịch hẹn, Tên người bệnh, Số điện thoại..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                        />
                    </div>
                    <button type="submit" className="btn-primary" style={{ height: '44px', padding: '0 20px' }}>
                        <Search size={16} /> Tra cứu
                    </button>
                    {searchTerm && (
                        <button
                            type="button"
                            className="btn-secondary"
                            style={{ height: '44px' }}
                            onClick={() => {
                                setSearchTerm('');
                                setPage(1);
                                fetchWorklist();
                            }}
                        >
                            Xóa tìm kiếm
                        </button>
                    )}
                </form>
            </div>

            {/* 4. Main 2/3 and 1/3 Work Area */}
            <div className={styles.mainLayout}>
                {/* 2/3 Worklist Table */}
                <div className={styles.worklistCard}>
                    <div className={styles.tabHeader}>
                        <button
                            type="button"
                            className={`${styles.tabButton} ${activeTab === 'today' ? styles.tabButtonActive : ''}`}
                            onClick={() => { setActiveTab('today'); setPage(1); }}
                        >
                            <span>Hôm nay (Ưu tiên tiếp nhận)</span>
                            <span className={`${styles.tabBadge} ${activeTab === 'today' ? styles.tabBadgeActive : ''}`}>
                                {activeTab === 'today' ? totalItems : (stats?.appointmentsToday ?? 0)}
                            </span>
                        </button>
                        <button
                            type="button"
                            className={`${styles.tabButton} ${activeTab === 'pending' ? styles.tabButtonActive : ''}`}
                            onClick={() => { setActiveTab('pending'); setPage(1); }}
                        >
                            <span>Chờ xác nhận</span>
                            <span className={`${styles.tabBadge} ${activeTab === 'pending' ? styles.tabBadgeActive : ''}`}>
                                {stats?.pendingAppointmentsToday ?? 0}
                            </span>
                        </button>
                        <button
                            type="button"
                            className={`${styles.tabButton} ${activeTab === 'upcoming' ? styles.tabButtonActive : ''}`}
                            onClick={() => { setActiveTab('upcoming'); setPage(1); }}
                        >
                            <span>Sắp tới</span>
                        </button>
                        <button
                            type="button"
                            className={`${styles.tabButton} ${activeTab === 'recent' ? styles.tabButtonActive : ''}`}
                            onClick={() => { setActiveTab('recent'); setPage(1); }}
                        >
                            <span>Gần đây</span>
                        </button>
                        <button
                            type="button"
                            className={`${styles.tabButton} ${activeTab === 'history' ? styles.tabButtonActive : ''}`}
                            onClick={() => { setActiveTab('history'); setPage(1); }}
                        >
                            <span>Toàn bộ lịch sử</span>
                        </button>
                    </div>

                    <div className={styles.tableResponsive}>
                        <table className={styles.table}>
                            <thead>
                                <tr>
                                    <th>Mã / Giờ hẹn</th>
                                    <th>Người bệnh</th>
                                    <th>Chuyên khoa / Bác sĩ</th>
                                    <th>Lý do khám</th>
                                    <th>Trạng thái</th>
                                    <th style={{ textAlign: 'right' }}>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                                {loading ? (
                                    <tr>
                                        <td colSpan={6} className={styles.emptyState}>
                                            <RefreshCw className="spin" size={24} style={{ margin: '0 auto 8px' }} />
                                            <div>Đang tải danh sách hàng đợi tiếp nhận...</div>
                                        </td>
                                    </tr>
                                ) : worklistItems.length === 0 ? (
                                    <tr>
                                        <td colSpan={6} className={styles.emptyState}>
                                            Không có lịch hẹn nào trong mục này.
                                        </td>
                                    </tr>
                                ) : (
                                    worklistItems.map((item) => (
                                        <tr key={item.id}>
                                            <td>
                                                <span className={styles.codeBadge}>{item.appointmentCode}</span>
                                                <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '4px' }}>
                                                    {item.startTime} - {item.endTime}
                                                </div>
                                            </td>
                                            <td>
                                                <div style={{ fontWeight: 600, color: 'var(--c-text)' }}>
                                                    {item.patientName}
                                                </div>
                                                <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>
                                                    {item.medicalRecordNumber ? `MRN: ${item.medicalRecordNumber}` : ''}
                                                    {item.patientPhone ? ` • ${item.patientPhone}` : ''}
                                                </div>
                                            </td>
                                            <td>
                                                <div style={{ fontWeight: 500, color: 'var(--c-text)' }}>
                                                    {item.specialtyName}
                                                </div>
                                                <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>
                                                    BS: {item.doctorName || 'Chưa phân công'}
                                                </div>
                                            </td>
                                            <td style={{ maxWidth: '160px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                                {item.reason || '-'}
                                            </td>
                                            <td>
                                                {renderStatusBadge(item.status)}
                                            </td>
                                            <td style={{ textAlign: 'right' }}>
                                                <div className={styles.actionBtnGroup}>
                                                    {item.status === 'Confirmed' && (
                                                        <button
                                                            type="button"
                                                            className="btn-primary"
                                                            style={{ padding: '5px 12px', fontSize: '0.82rem' }}
                                                            onClick={() => handleFastCheckIn(item)}
                                                            disabled={actionLoadingId === item.id}
                                                            title="Tiếp nhận ngay và cấp số thứ tự vào hàng đợi bác sĩ"
                                                        >
                                                            {actionLoadingId === item.id ? (
                                                                <RefreshCw className="spin" size={13} />
                                                            ) : (
                                                                <CheckCircle2 size={13} />
                                                            )}
                                                            Tiếp nhận
                                                        </button>
                                                    )}

                                                    {item.patientVisitId && (
                                                        <button
                                                            type="button"
                                                            className="btn-secondary"
                                                            style={{ padding: '5px 10px', fontSize: '0.82rem' }}
                                                            onClick={() => handleViewTicket(item.patientVisitId!)}
                                                            title="Xem lại phiếu khám và in vé số thứ tự"
                                                        >
                                                            <Printer size={13} /> Vé khám
                                                        </button>
                                                    )}

                                                    <Link
                                                        to={`/reception/appointments`}
                                                        className="btn-secondary"
                                                        style={{ padding: '5px 10px', fontSize: '0.82rem' }}
                                                        title="Xem chi tiết lịch hẹn"
                                                    >
                                                        <Eye size={13} />
                                                    </Link>
                                                </div>
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>

                    {/* Pagination */}
                    {totalItems > pageSize && (
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '14px 20px', borderTop: '1px solid var(--c-border)' }}>
                            <span style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>
                                Hiển thị trang {page} / {totalPages} (Tổng {totalItems} lượt)
                            </span>
                            <div style={{ display: 'flex', gap: '8px' }}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    style={{ padding: '4px 12px', fontSize: '0.85rem' }}
                                    disabled={page <= 1}
                                    onClick={() => setPage(p => p - 1)}
                                >
                                    Trang trước
                                </button>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    style={{ padding: '4px 12px', fontSize: '0.85rem' }}
                                    disabled={page >= totalPages}
                                    onClick={() => setPage(p => p + 1)}
                                >
                                    Trang sau
                                </button>
                            </div>
                        </div>
                    )}
                </div>

                {/* 1/3 Fast Action Panel */}
                <div className={styles.actionPanel}>
                    {/* Fast Action Card 1: Walk-In / Intake */}
                    <div className={styles.actionCard} style={{ borderTop: '4px solid #0284c7' }}>
                        <div className={styles.actionCardHeader}>
                            <UserPlus size={20} color="#0284c7" />
                            <h3 className={styles.actionCardTitle}>Tiếp nhận người bệnh</h3>
                        </div>
                        <p className={styles.actionCardDesc}>
                            Quy trình 3 bước chuẩn bệnh viện: Tìm kiếm hồ sơ cũ qua MRN/CCCD/SĐT, chọn bác sĩ & phòng khám, xác nhận in phiếu khám A5/nhiệt.
                        </p>
                        <Link to="/reception/walk-in" className={styles.btnPrimaryLarge}>
                            <UserPlus size={18} /> Tiếp nhận người bệnh mới
                        </Link>
                    </div>

                    {/* Fast Action Card 2: MPI Search */}
                    <div className={styles.actionCard}>
                        <div className={styles.actionCardHeader}>
                            <Search size={20} color="#0d9488" />
                            <h3 className={styles.actionCardTitle}>Tra cứu hồ sơ y bạ (MPI)</h3>
                        </div>
                        <p className={styles.actionCardDesc}>
                            Kiểm tra lịch sử khám, thẻ BHYT, CCCD, thông tin liên lạc và tiền sử dị ứng người bệnh trên toàn hệ thống.
                        </p>
                        <button
                            type="button"
                            className={styles.btnSecondaryAction}
                            onClick={() => setMpiModalOpen(true)}
                        >
                            <Search size={16} /> Mở tra cứu hồ sơ bệnh nhân
                        </button>
                    </div>

                    {/* Fast Action Card 3: Billing & Cashier */}
                    <div className={styles.actionCard}>
                        <div className={styles.actionCardHeader}>
                            <CreditCard size={20} color="#16a34a" />
                            <h3 className={styles.actionCardTitle}>Hàng đợi viện phí & Thu ngân</h3>
                        </div>
                        <p className={styles.actionCardDesc}>
                            Thu tiền công khám, dịch vụ cận lâm sàng và đơn thuốc đã xác nhận mua theo cơ chế khóa chống thu trùng.
                        </p>
                        <Link to="/reception/billing" className={styles.btnSecondaryAction}>
                            <CreditCard size={16} /> Mở thu ngân viện phí
                        </Link>
                    </div>

                    {/* Fast Action Card 4: Health Packages */}
                    <div className={styles.actionCard}>
                        <div className={styles.actionCardHeader}>
                            <Package size={20} color="#8b5cf6" />
                            <h3 className={styles.actionCardTitle}>Gói khám sức khỏe</h3>
                        </div>
                        <p className={styles.actionCardDesc}>
                            Tiếp đón và kích hoạt lượt khám cho người bệnh đăng ký gói khám sức khỏe tổng quát.
                        </p>
                        <Link to="/reception/package-registrations" className={styles.btnSecondaryAction}>
                            <Package size={16} /> Quản lý đăng ký gói khám
                        </Link>
                    </div>
                </div>
            </div>

            {/* MPI Patient Search Modal */}
            <MpiPatientSearchModal
                isOpen={mpiModalOpen}
                onClose={() => setMpiModalOpen(false)}
                onSelectPatient={(patient) => {
                    setMpiModalOpen(false);
                    // Navigate to walk-in with pre-selected patient id
                    navigate(`/reception/walk-in?existingPatientId=${patient.id}`);
                }}
            />

            {/* Check-In Ticket Modal */}
            <CheckInTicketModal
                isOpen={ticketModalOpen}
                ticket={currentTicket}
                onClose={() => setTicketModalOpen(false)}
            />
        </div>
    );
};
