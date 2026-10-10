import { Button, Input, Select, Modal } from 'antd';
import { PageHeader, DataTable, StatusBadge, LoadingState, EmptyState, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import styles from './ReceptionAppointments.module.css';
import React, { useState, useEffect, useRef } from 'react';
import axiosClient from '../../api/axiosClient';
import { patientVisitApi } from '../../api/patientVisitApi';
import { organizationApi, type DepartmentDto } from '../../api/organizationApi';
import type { ApiResponse, CheckInTicketDto } from '../../types';
import { Search, CalendarDays, Eye, Clock, RefreshCw, UserCheck } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import { CheckInTicketModal } from '../../components/CheckInTicketModal';
import { useCopilotResource } from '../../components/copilot/copilotResourceContext';
import { toLocalDateString } from '../../utils/formatters';

interface ReceptionAppointment {
    id: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    doctorId: number;
    doctorName: string;
    specialtyId: number;
    specialtyName: string;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string | null;
    status: string;
    facilityId?: number | null;
    facilityName?: string | null;
}

export const ReceptionAppointments: React.FC = () => {
    const { setSelection } = useCopilotResource();
    const [appointments, setAppointments] = useState<ReceptionAppointment[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Detail Modal
    const [modal, setModal] = useState<{ isOpen: boolean, apt: ReceptionAppointment | null }>({ isOpen: false, apt: null });
    const [actionLoading, setActionLoading] = useState(false);
    const [history, setHistory] = useState<any[]>([]);
    const [historyLoading, setHistoryLoading] = useState(false);
    const [historyError, setHistoryError] = useState('');
    const [departments, setDepartments] = useState<DepartmentDto[]>([]);
    const [departmentsLoading, setDepartmentsLoading] = useState(false);
    const [departmentsError, setDepartmentsError] = useState('');
    const [selectedDepartmentId, setSelectedDepartmentId] = useState<number | null>(null);
    const departmentRequestRef = useRef<{ generation: number; controller: AbortController | null }>({ generation: 0, controller: null });

    // Check-in Ticket Modal state
    const [ticketModalOpen, setTicketModalOpen] = useState(false);
    const [currentTicket, setCurrentTicket] = useState<CheckInTicketDto | null>(null);
    const [checkingInId, setCheckingInId] = useState<number | null>(null);

    const fetchAppointments = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments?${params.toString()}`);
            if (res.success && res.data) {
                setAppointments(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            // Error handling
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchAppointments();
    }, [page, statusFilter]);

    useEffect(() => {
        setSelection(null);
    }, [page, statusFilter, search, setSelection]);

    useEffect(() => {
        const refresh = () => { void fetchAppointments(); };
        window.addEventListener('cliniccare:copilot-action-completed', refresh);
        return () => window.removeEventListener('cliniccare:copilot-action-completed', refresh);
    }, [page, statusFilter, search]);

    useEffect(() => () => {
        departmentRequestRef.current.controller?.abort();
        departmentRequestRef.current.generation += 1;
    }, []);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchAppointments();
    };

    const fetchHistory = async (id: number) => {
        setHistoryLoading(true);
        setHistoryError('');
        setHistory([]);
        try {
            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/appointments/${id}/history`);
            if (res.success && res.data) {
                setHistory(res.data);
            }
        } catch (error: any) {
            console.error("Lỗi tải lịch sử:", error);
            setHistoryError('Không thể tải lịch sử.');
        } finally {
            setHistoryLoading(false);
        }
    };

    const cancelDepartmentRequest = () => {
        departmentRequestRef.current.controller?.abort();
        departmentRequestRef.current = {
            generation: departmentRequestRef.current.generation + 1,
            controller: null
        };
    };

    const openDetail = (apt: ReceptionAppointment) => {
        cancelDepartmentRequest();
        const generation = departmentRequestRef.current.generation;
        const facilityId = apt.facilityId && apt.facilityId > 0 ? apt.facilityId : null;
        setSelectedDepartmentId(null);
        setDepartments([]);
        setDepartmentsError(facilityId ? '' : 'Không xác định được cơ sở của lịch hẹn nên Copilot chưa thể chuẩn bị thao tác.');
        setDepartmentsLoading(Boolean(facilityId));
        setSelection({
            context: { appointmentId: apt.id },
            source: 'reception-appointments',
            label: `Lịch hẹn ${apt.appointmentCode}`
        });
        setModal({ isOpen: true, apt });
        fetchHistory(apt.id);
        if (facilityId) {
            const controller = new AbortController();
            departmentRequestRef.current.controller = controller;
            void organizationApi.getDepartments(facilityId, controller.signal)
                .then(result => {
                    if (controller.signal.aborted || departmentRequestRef.current.generation !== generation) return;
                    if (result.success && result.data) {
                        const available = result.data.filter(department => department.isActive && department.facilityId === facilityId);
                        const matching = available.filter(department => department.specialtyId === apt.specialtyId);
                        setDepartments(available);
                        setSelectedDepartmentId(matching.length === 1 ? matching[0].id : null);
                        setDepartmentsError('');
                    } else {
                        setDepartments([]);
                        setDepartmentsError(result.message || 'Không thể tải khoa thuộc cơ sở của lịch hẹn.');
                    }
                })
                .catch(error => {
                    if (controller.signal.aborted || departmentRequestRef.current.generation !== generation) return;
                    setDepartments([]);
                    setDepartmentsError(error?.message || 'Không thể tải khoa thuộc cơ sở của lịch hẹn.');
                })
                .finally(() => {
                    if (!controller.signal.aborted && departmentRequestRef.current.generation === generation) {
                        setDepartmentsLoading(false);
                        departmentRequestRef.current.controller = null;
                    }
                });
        }
    };

    const closeDetail = () => {
        cancelDepartmentRequest();
        setSelection(null);
        setSelectedDepartmentId(null);
        setDepartments([]);
        setDepartmentsError('');
        setDepartmentsLoading(false);
        setModal({ isOpen: false, apt: null });
    };

    useEffect(() => {
        if (!modal.isOpen || !modal.apt) {
            setSelection(null);
            return;
        }
        const department = selectedDepartmentId === null ? undefined : departments.find(item =>
            item.id === selectedDepartmentId && item.isActive && item.facilityId === modal.apt?.facilityId
        );
        if (!department) {
            setSelection({
                context: { appointmentId: modal.apt.id },
                source: 'reception-appointments',
                label: `Lịch hẹn ${modal.apt.appointmentCode}`
            });
            return;
        }
        setSelection({
            context: { appointmentId: modal.apt.id, departmentId: department.id },
            source: 'reception-appointments',
            label: `Lịch hẹn ${modal.apt.appointmentCode} · ${department.name}`
        });
    }, [departments, modal, selectedDepartmentId, setSelection]);

    const { showAlert, showConfirm } = useDialog();

    const handleConfirm = async () => {
        const apt = modal.apt;
        if (!apt) return;
        
        showConfirm('Xác nhận lịch hẹn này hợp lệ và đã sẵn sàng cho bác sĩ?', async () => {
            setActionLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/reception/appointments/${apt.id}/confirm`, {});
                if (res.success) {
                    showAlert('Đã xác nhận lịch hẹn.', 'Thành công', 'success');
                    // Update local state temporarily for fast UI or refetch all
                    setModal({ ...modal, apt: { ...apt, status: 'Confirmed' } });
                    fetchHistory(apt.id);
                    fetchAppointments();
                }
            } catch (error: any) {
                if (error?.errorCode === 'INVALID_APPOINTMENT_STATUS') {
                    showAlert('Trạng thái lịch hẹn không hợp lệ (có thể đã được người khác xử lý). Dữ liệu sẽ được làm mới.', 'Thông báo', 'warning');
                    fetchAppointments();
                    setModal({ isOpen: false, apt: null });
                } else {
                    showAlert(error?.message || 'Có lỗi xảy ra.', 'Lỗi', 'error');
                }
            } finally {
                setActionLoading(false);
            }
        });
    };

    const handleCheckIn = async (apt: ReceptionAppointment) => {
        setSelection({
            context: { appointmentId: apt.id },
            source: 'reception-appointments',
            label: `Lịch hẹn ${apt.appointmentCode}`
        });
        setCheckingInId(apt.id);
        try {
            const res = await patientVisitApi.receptionCheckInAppointment(apt.id);
            if (res.success && res.data) {
                setCurrentTicket(res.data);
                setTicketModalOpen(true);
                showAlert(
                    'Tiếp nhận thành công!',
                    `Bệnh nhân ${res.data.patientName} đã được cấp STT ${res.data.queueNumber} tại ${res.data.departmentName || 'phòng khám'}.`,
                    'success'
                );
                if (modal.isOpen && modal.apt?.id === apt.id) {
                    setModal({ ...modal, apt: { ...apt, status: 'CheckedIn' } });
                    fetchHistory(apt.id);
                }
                fetchAppointments();
            } else {
                showAlert(res.message || 'Không thể tiếp nhận bệnh nhân.', 'Lỗi tiếp nhận', 'error');
            }
        } catch (error: any) {
            showAlert(error?.message || 'Có lỗi xảy ra khi tiếp nhận bệnh nhân.', 'Lỗi tiếp nhận', 'error');
        } finally {
            setCheckingInId(null);
        }
    };

    const formatDate = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN').format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    const formatDateTime = (dateString: string) => {
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit'
            }).format(new Date(dateString));
        } catch {
            return dateString;
        }
    };

    const translateStatus = (status: string) => {
        switch (status) {
            case 'Pending': return 'Chờ xác nhận';
            case 'Confirmed': return 'Đã xác nhận';
            case 'CheckedIn': return 'Đã tiếp nhận (Chờ khám)';
            case 'Completed': return 'Đã hoàn thành';
            case 'Cancelled': return 'Đã hủy';
            case 'NoShow': return 'Không đến khám';
            default: return '—';
        }
    };

    const getStatusBadge = (status: string) => <StatusBadge status={status} label={translateStatus(status)} />;

    const translateAction = (action: string) => {
        switch (action) {
            case 'Created': return 'Tạo lịch hẹn';
            case 'Confirmed': return 'Xác nhận lịch';
            case 'CheckedIn': return 'Tiếp nhận khám';
            case 'Cancelled': return 'Hủy lịch';
            case 'Rescheduled': return 'Đổi lịch';
            case 'Completed': return 'Hoàn thành khám';
            default: return action;
        }
    };

    const columns: DataTableColumn<ReceptionAppointment>[] = [
        { header: 'Lịch khám', accessor: apt => <><div className={styles.primaryText}>{apt.appointmentCode}</div><div className={styles.appointmentTime}><Clock size={12} /> {formatDate(apt.appointmentDate)} {apt.startTime.substring(0,5)}</div></> },
        { header: 'Bệnh nhân', accessor: apt => <><div className={styles.primaryText}>{apt.patientName}</div><div className={styles.secondaryText}>{apt.patientPhone}</div></> },
        { header: 'Bác sĩ & Chuyên khoa', accessor: apt => <><div className={styles.primaryText}>{apt.doctorName}</div><div className={styles.secondaryText}>{apt.specialtyName}</div></> },
        { header: 'Trạng thái', accessor: apt => getStatusBadge(apt.status) },
        { header: 'Thao tác', align: 'right', className: styles.actionsCell, accessor: apt => <>
            {apt.status === 'Confirmed' && <Button type="primary" className={styles.checkInButton} style={{ minHeight: '40px' }} onClick={() => handleCheckIn(apt)} disabled={checkingInId === apt.id || apt.appointmentDate !== toLocalDateString()} title={apt.appointmentDate !== toLocalDateString() ? `Chỉ tiếp nhận vào ngày khám ${apt.appointmentDate.split('-').reverse().join('/')}` : undefined} icon={<UserCheck size={14} />}>{checkingInId === apt.id ? 'Đang tiếp nhận...' : 'Tiếp nhận'}</Button>}
            <Button onClick={() => openDetail(apt)} icon={<Eye size={14} />}>Chi tiết</Button>
        </> },
    ];

    return (
        <div className={styles.page}>
            <PageHeader title="Quản lý lịch hẹn" badge={<CalendarDays size={24} className={styles.primaryIcon} />} actions={<Button onClick={() => fetchAppointments()} icon={<RefreshCw size={14} />}>Làm mới</Button>} />
            <form onSubmit={handleSearchSubmit} className={styles.filterForm}>
                <Input className={styles.searchInput} prefix={<Search size={18} />} placeholder="Tìm theo mã lịch, tên, SĐT..." value={search} onChange={e => setSearch(e.target.value)} />
                <Select className={styles.statusFilter} value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả trạng thái'},{value:'Pending',label:'Chờ xác nhận'},{value:'Confirmed',label:'Đã xác nhận'},{value:'Completed',label:'Đã hoàn thành'},{value:'Cancelled',label:'Đã hủy'}]} />
                <Button htmlType="submit">Tìm kiếm</Button>
            </form>
            {loading ? <LoadingState message="Đang tải dữ liệu..." /> : appointments.length === 0 ? <EmptyState title="Không tìm thấy lịch hẹn nào." /> : <div className={styles.tableScroll}><DataTable columns={columns} data={appointments} keyExtractor={apt => apt.id} /></div>}
            <div className={styles.secondaryText}>Tổng cộng: {totalItems} lịch hẹn</div>
            {modal.isOpen && modal.apt && <Modal open onCancel={closeDetail} width={600} className={styles.detailModal} mask={{closable:false}} keyboard={false} closable={{'aria-label':'Đóng chi tiết lịch hẹn'}} title={<>Chi tiết lịch hẹn <span className={styles.detailCode}>#{modal.apt.appointmentCode}</span></>} footer={<>
                <Button onClick={closeDetail}>Đóng</Button>
                {modal.apt.status === 'Pending' && <Button type="primary" onClick={handleConfirm} disabled={actionLoading}>{actionLoading ? 'Đang xử lý...' : 'Xác nhận lịch hẹn'}</Button>}
                {modal.apt.status === 'Confirmed' && <Button type="primary" className={styles.checkInButton} style={{ minHeight: '40px' }} onClick={() => handleCheckIn(modal.apt!)} disabled={checkingInId === modal.apt.id || modal.apt.appointmentDate !== toLocalDateString()} title={modal.apt.appointmentDate !== toLocalDateString() ? `Chỉ tiếp nhận vào ngày khám ${modal.apt.appointmentDate.split('-').reverse().join('/')}` : undefined} icon={<UserCheck size={16} />}>{checkingInId === modal.apt.id ? 'Đang tiếp nhận...' : 'Tiếp nhận & Cấp phiếu STT'}</Button>}
            </>}>
                <div className={styles.detailGrid}>
                    <div className={styles.factCard}><div className={styles.factLabel}>Bệnh nhân</div><div className={styles.primaryText}>{modal.apt.patientName}</div><div>{modal.apt.patientPhone}</div></div>
                    <div className={styles.factCard}><div className={styles.factLabel}>Bác sĩ & Chuyên khoa</div><div className={styles.primaryText}>{modal.apt.doctorName}</div><div>{modal.apt.specialtyName}</div></div>
                    <div className={styles.timeCard}><div className={styles.factLabel}>Thời gian khám</div><div className={styles.appointmentTime}><Clock size={16} className={styles.primaryIcon} />{formatDate(modal.apt.appointmentDate)} ({modal.apt.startTime.substring(0,5)} - {modal.apt.endTime.substring(0,5)})</div></div>
                </div>
                <section className={styles.detailSection}><div className={styles.sectionLabel}>Lý do khám:</div><div className={styles.notePanel}>{modal.apt.reason || <span className={styles.secondaryText}>Không có ghi chú</span>}</div></section>
                <section className={styles.departmentPanel}>
                    <label htmlFor="copilot-checkin-department" className={styles.sectionLabel}>Khoa tiếp nhận cho Copilot *</label>
                    <select id="copilot-checkin-department" className={`form-select ${styles.departmentSelect}`} value={selectedDepartmentId ?? ''} onChange={event => setSelectedDepartmentId(event.target.value ? Number(event.target.value) : null)} disabled={!modal.apt.facilityId || departmentsLoading}>
                        <option value="">{departmentsLoading ? 'Đang tải khoa theo cơ sở…' : 'Chọn khoa từ cơ sở của lịch hẹn'}</option>
                        {departments.map(department => <option key={department.id} value={department.id}>{department.name} ({department.code})</option>)}
                    </select>
                    {departmentsError && <div role="alert" className={styles.departmentError}>{departmentsError}</div>}
                    <small className={styles.departmentHint}>Đã chọn sẵn khoa theo chuyên khoa của lịch hẹn; bạn có thể đổi. Backend vẫn kiểm tra lại cơ sở, quyền và lịch hẹn.</small>
                </section>
                <section className={styles.detailSection}>
                    <div className={styles.workflowHeading}><span>Tiến trình xử lý</span>{getStatusBadge(modal.apt.status)}</div>
                    {historyLoading ? <LoadingState message="Đang tải lịch sử..." /> : historyError ? <InlineError title="" message={historyError} /> : history.length === 0 ? <EmptyState title="Không có dữ liệu lịch sử." /> : <div className={styles.timeline}>{history.map(h => <div key={h.id} className={styles.timelineEntry}><div className={styles.secondaryText}>{formatDateTime(h.createdAt)}</div><div className={styles.primaryText}>{translateAction(h.action)}</div>{h.note && <div className={styles.historyNote}>{h.note}</div>}</div>)}</div>}
                </section>
            </Modal>}
            <CheckInTicketModal isOpen={ticketModalOpen} ticket={currentTicket} onClose={() => setTicketModalOpen(false)} />
        </div>
    );
};
