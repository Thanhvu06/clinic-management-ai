import { toLocalDateString, formatDateOnly } from '../../utils/formatters';
import React, { useState, useEffect, useRef } from "react";
import { Alert, Button, Card, Form, Input, Modal } from 'antd';
import { useNavigate, useSearchParams } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import type { ApiResponse } from "../../types";
import {
    CalendarDays, Clock, Stethoscope, User,
    XCircle, History as HistoryIcon, RotateCcw, Calendar, Check
} from "lucide-react";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";
import { getNextWorkingDateString, isSundayDateString } from "../../utils/doctorNameHelper";
import styles from './PatientAppointments.module.css';
import { PageHeader } from '../../components/common/PageHeader';
import { StatusBadge } from '../../components/common/StatusBadge';
import { EmptyState } from '../../components/common/EmptyState';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';

interface Appointment {
    id: number;
    appointmentCode: string;
    doctorId: number;
    doctorName: string;
    specialtyId: number;
    specialtyName: string;
    appointmentSlotId: number;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string | null;
    status: string;
}

export const PatientAppointments: React.FC = () => {
    const navigate = useNavigate();
    const [searchParams, setSearchParams] = useSearchParams();
    const { showAlert, showConfirm } = useDialog();

    const [appointments, setAppointments] = useState<Appointment[]>([]);
    const [pendingRequests, setPendingRequests] = useState<Record<number, any>>({});
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [activeTab, setActiveTab] = useState(() => ({ upcoming: 'Upcoming', past: 'Past', all: 'All' }[searchParams.get('tab') ?? ''] ?? 'All'));
    const [highlightedAppId, setHighlightedAppId] = useState<number | null>(null);
    const handledDeepLink = useRef<string | null>(null);
    const slotsRequest = useRef(0);
    const withdrawing = useRef<number | null>(null);
    const [withdrawingId, setWithdrawingId] = useState<number | null>(null);

    // Cancel modal state
    const [cancelModal, setCancelModal] = useState<{
        isOpen: boolean;
        app: Appointment | null;
        reason: string;
        inlineError: string;
    }>({ isOpen: false, app: null, reason: "", inlineError: "" });
    const [canceling, setCanceling] = useState(false);

    // Reschedule modal state
    const [rescheduleModal, setRescheduleModal] = useState<{
        isOpen: boolean;
        app: Appointment | null;
        targetDate: string;
        availableSlots: any[];
        selectedSlotId: number | null;
        reason: string;
        loadingSlots: boolean;
        inlineError: string;
    }>({
        isOpen: false,
        app: null,
        targetDate: "",
        availableSlots: [],
        selectedSlotId: null,
        reason: "",
        loadingSlots: false,
        inlineError: ""
    });
    const [rescheduling, setRescheduling] = useState(false);

    // History modal state
    const [historyModal, setHistoryModal] = useState<{
        isOpen: boolean;
        appId: number | null;
        appCode: string;
        histories: any[];
        loading: boolean;
        error: string;
    }>({ isOpen: false, appId: null, appCode: "", histories: [], loading: false, error: '' });

    const fetchData = async () => {
        setLoading(true);
        setError(null);
        try {
            const appRes = await axiosClient.get<unknown, ApiResponse<{ items: Appointment[] }>>("/appointments/my?page=1&pageSize=100");
            if (appRes.success && appRes.data?.items) {
                setAppointments(appRes.data.items);
            } else {
                setError(appRes.message || "Không thể tải danh sách lịch hẹn.");
            }
        } catch (err: any) {
            setError(err?.message || "Không thể tải danh sách lịch hẹn.");
        }
        finally { setLoading(false); }
    };

    const fetchPendingRequests = async () => {
        setPendingRequests({});
        try {
            const res = await axiosClient.get<any, ApiResponse<any>>("/appointment-change-requests?status=Pending&page=1&pageSize=100");
            const requests: Record<number, any> = {};
            if (res.success && res.data?.items) {
                res.data.items.forEach((request: any) => { requests[request.appointmentId] = request; });
            }
            setPendingRequests(requests);
        } catch {
            setPendingRequests({});
        }
    };

    const refreshData = () => {
        void fetchData();
        void fetchPendingRequests();
    };

    useEffect(() => {
        setActiveTab({ upcoming: 'Upcoming', past: 'Past', all: 'All' }[searchParams.get('tab') ?? ''] ?? 'All');
        refreshData();
    }, [searchParams]);

    const getStatusBadge = (status: string | null) => {
        if (!status) return '—';
        const labels: Record<string, string> = { Pending: 'Chờ xác nhận', Confirmed: 'Đã xác nhận', PendingCancellation: 'Chờ hủy', PendingReschedule: 'Chờ đổi lịch', Completed: 'Đã hoàn thành', Cancelled: 'Đã hủy', NoShow: 'Vắng mặt' };
        return <StatusBadge status={status.startsWith('Pending') ? 'Pending' : status} label={labels[status] || 'Chưa xác định'} />;
    };

    const getActionLabel = (action: string) => {
        switch (action) {
            case "Created": return "Tạo mới";
            case "StatusChanged": return "Cập nhật trạng thái";
            case "Cancelled": return "Hủy lịch";
            case "Rescheduled": return "Đổi lịch";
            case "Confirmed": return "Xác nhận";
            case "Completed": return "Hoàn thành";
            case "NoShow": return "Đánh dấu vắng";
            case "RescheduleRequested": return "Yêu cầu đổi lịch";
            case "CancelRequested": return "Yêu cầu hủy";
            default: return action;
        }
    };

    const handleCancelOpen = (app: Appointment) => {
        setCancelModal({ isOpen: true, app, reason: "", inlineError: "" });
    };

    const handleCancelClose = () => {
        if (canceling) return;
        setCancelModal({ isOpen: false, app: null, reason: "", inlineError: "" });
    };

    const submitCancel = async () => {
        const { app, reason } = cancelModal;
        if (!app) return;
        const trimmedReason = reason.trim();
        if (trimmedReason.length < 5) {
            setCancelModal(p => ({ ...p, inlineError: "Vui lòng nhập lý do hủy (ít nhất 5 ký tự)." }));
            return;
        }

        setCanceling(true);
        setCancelModal(p => ({ ...p, inlineError: "" }));
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/appointments/${app.id}/cancellation-requests`, { reason: trimmedReason });
            if (res.success) {
                setCancelModal(p => ({ ...p, isOpen: false }));
                showAlert('Đã gửi yêu cầu hủy lịch thành công! Vui lòng chờ lễ tân xử lý.', 'Thành công', 'success');
                refreshData();
            } else {
                setCancelModal(p => ({ ...p, inlineError: res.message || "Không thể hủy lịch" }));
            }
        } catch (error: any) {
            const msg = error?.message || "Có lỗi xảy ra khi hủy lịch.";
            setCancelModal(p => ({ ...p, inlineError: msg }));
        } finally {
            setCanceling(false);
        }
    };

    const loadSlotsForDate = async (doctorId: number, dateStr: string, specialtyId?: number, currentSlotId?: number) => {
        const requestId = ++slotsRequest.current;
        setRescheduleModal(p => ({ ...p, loadingSlots: true, inlineError: "", availableSlots: [], selectedSlotId: null }));
        try {
            const specParam = specialtyId ? `&specialtyId=${specialtyId}` : "";
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/doctors/${doctorId}/available-slots?fromDate=${dateStr}&toDate=${dateStr}${specParam}`);
            if (requestId !== slotsRequest.current) return;
            if (res.success && res.data) {
                // Filter out current appointment's slot
                const slots = res.data.filter((s: any) => s.slotId !== currentSlotId);
                setRescheduleModal(p => ({ ...p, availableSlots: slots, loadingSlots: false }));
            } else {
                setRescheduleModal(p => ({ ...p, availableSlots: [], loadingSlots: false }));
            }
        } catch (err: any) {
            if (requestId !== slotsRequest.current) return;
            setRescheduleModal(p => ({ ...p, loadingSlots: false, inlineError: err?.message || "Không thể tải danh sách ca khám khả dụng." }));
        }
    };

    const handleRescheduleOpen = (app: Appointment) => {
        const dateStr = getNextWorkingDateString(new Date());

        setRescheduleModal({
            isOpen: true,
            app,
            targetDate: dateStr,
            availableSlots: [],
            selectedSlotId: null,
            reason: "",
            loadingSlots: false,
            inlineError: ""
        });

        loadSlotsForDate(app.doctorId, dateStr, app.specialtyId, app.appointmentSlotId);
    };

    const handleTargetDateChange = (newDate: string) => {
        ++slotsRequest.current;
        if (isSundayDateString(newDate)) {
            setRescheduleModal(p => ({
                ...p,
                targetDate: newDate,
                availableSlots: [],
                selectedSlotId: null,
                loadingSlots: false,
                inlineError: "Phòng khám không làm việc vào Chủ nhật. Vui lòng chọn ngày khác (Thứ 2 - Thứ 7)."
            }));
            return;
        }

        if (!newDate || newDate < tomorrowStr) {
            setRescheduleModal(p => ({ ...p, targetDate: newDate, availableSlots: [], selectedSlotId: null, loadingSlots: false, inlineError: "Vui lòng chọn ngày từ ngày mai trở đi." }));
            return;
        }

        setRescheduleModal(p => ({ ...p, targetDate: newDate, inlineError: "" }));
        if (rescheduleModal.app) {
            loadSlotsForDate(rescheduleModal.app.doctorId, newDate, rescheduleModal.app.specialtyId, rescheduleModal.app.appointmentSlotId);
        }
    };

    // Deep-link handling for ?appointmentId=...&action=...
    useEffect(() => {
        const appointmentIdParam = searchParams.get("appointmentId");
        const actionParam = searchParams.get("action");
        if (!appointmentIdParam || appointments.length === 0) return;

        const targetId = parseInt(appointmentIdParam, 10);
        if (isNaN(targetId)) return;

        const targetApp = appointments.find(a => a.id === targetId);
        if (!targetApp) return;

        const key = `${appointmentIdParam}|${actionParam ?? ''}`;
        if (handledDeepLink.current === key) return;
        handledDeepLink.current = key;

        setHighlightedAppId(targetId);

        setTimeout(() => {
            const el = document.getElementById(`appointment-card-${targetId}`);
            el?.scrollIntoView({ behavior: "smooth", block: "center" });
        }, 150);

        if (actionParam === "reschedule" && (targetApp.status === "Pending" || targetApp.status === "Confirmed")) {
            handleRescheduleOpen(targetApp);
        } else if (actionParam === "cancel" && (targetApp.status === "Pending" || targetApp.status === "Confirmed")) {
            handleCancelOpen(targetApp);
        }
    }, [appointments, searchParams]);

    const submitReschedule = async () => {
        const { app, selectedSlotId, reason } = rescheduleModal;
        if (!app) return;
        if (!selectedSlotId) {
            setRescheduleModal(p => ({ ...p, inlineError: "Vui lòng chọn một ca khám mới." }));
            return;
        }
        if (reason.trim().length < 5) {
            setRescheduleModal(p => ({ ...p, inlineError: "Vui lòng nhập lý do đổi lịch (ít nhất 5 ký tự)." }));
            return;
        }

        setRescheduling(true);
        setRescheduleModal(p => ({ ...p, inlineError: "" }));
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/appointments/${app.id}/reschedule-requests`, {
                requestedSlotId: selectedSlotId,
                reason: reason.trim()
            });
            if (res.success) {
                setRescheduleModal(p => ({ ...p, isOpen: false }));
                showAlert("Đã gửi yêu cầu dời lịch khám thành công! Vui lòng chờ lễ tân xác nhận.", "Thành công", "success");
                refreshData();
            } else {
                setRescheduleModal(p => ({ ...p, inlineError: res.message || "Không thể dời lịch khám." }));
            }
        } catch (err: any) {
            const msg = err?.message || "Có lỗi xảy ra khi gửi yêu cầu đổi lịch.";
            setRescheduleModal(p => ({ ...p, inlineError: msg }));
        } finally {
            setRescheduling(false);
        }
    };

    const handleWithdraw = (app: Appointment) => {
        if (withdrawing.current !== null) return;
        const pendingReq = pendingRequests[app.id];
        if (!pendingReq) {
            showAlert("Không tìm thấy yêu cầu chờ xử lý của lịch hẹn này.", "Thông báo", "warning");
            return;
        }

        showConfirm(
            "Bạn có chắc chắn muốn rút yêu cầu thay đổi và tiếp tục giữ lịch khám ban đầu?",
            async () => {
                if (withdrawing.current !== null) return;
                withdrawing.current = app.id;
                setWithdrawingId(app.id);
                try {
                    const res = await axiosClient.post<any, ApiResponse<any>>(`/appointment-change-requests/${pendingReq.id}/withdraw`);
                    if (res.success) {
                        showAlert("Đã rút yêu cầu thành công!", "Thành công", "success");
                        refreshData();
                    } else {
                        showAlert(res.message || "Không thể rút yêu cầu.", "Lỗi", "error");
                    }
                } catch (err: any) {
                    const msg = err?.message || "Có lỗi xảy ra khi rút yêu cầu.";
                    showAlert(msg, "Lỗi", "error");
                } finally {
                    withdrawing.current = null;
                    setWithdrawingId(null);
                }
            },
            "Xác nhận rút yêu cầu"
        );
    };

    const handleViewHistory = async (appId: number, code: string) => {
        setHistoryModal({ isOpen: true, appId, appCode: code, histories: [], loading: true, error: '' });
        try {
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/appointments/${appId}/history`);
            if (res.success && res.data) {
                const histories = res.data;
                setHistoryModal(p => ({ ...p, histories, loading: false }));
            } else {
                setHistoryModal(p => ({ ...p, loading: false, error: res.message || 'Không thể tải lịch sử thay đổi.' }));
            }
        } catch (err: any) {
            setHistoryModal(p => ({ ...p, loading: false, error: err?.message || 'Không thể tải lịch sử thay đổi.' }));
        }
    };

    const filtered = appointments.filter(a => {
        if (activeTab === "Upcoming") return ["Pending", "Confirmed", "PendingReschedule", "PendingCancellation"].includes(a.status);
        if (activeTab === "Past") return ["Completed", "Cancelled", "NoShow"].includes(a.status);
        return true;
    });
    if (activeTab === 'Upcoming') filtered.sort((a, b) => `${a.appointmentDate}T${a.startTime}`.localeCompare(`${b.appointmentDate}T${b.startTime}`));

    const tomorrowStr = (() => {
        const d = new Date();
        d.setDate(d.getDate() + 1);
        return toLocalDateString(d);
    })();

    const closeReschedule = () => {
        if (rescheduling) return;
        ++slotsRequest.current;
        setRescheduleModal(p => ({ ...p, isOpen: false }));
    };
    const closeHistory = () => setHistoryModal(p => ({ ...p, isOpen: false }));
    const changeTab = (tab: string) => {
        setActiveTab(tab);
        const next = new URLSearchParams(searchParams);
        next.set('tab', tab.toLowerCase());
        setSearchParams(next);
    };

    return (
        <div className={styles.page}>
            <Breadcrumb items={[{ label: 'Trang chủ', path: '/patient' }, { label: 'Lịch sử khám' }]} />
            <PageHeader title="Lịch hẹn của tôi" actions={<Button type="primary" onClick={() => navigate('/patient/book')}>+ Đặt lịch mới</Button>} />
            <div className={styles.filters} role="group" aria-label="Lọc lịch hẹn">
                {[{ key: 'All', label: 'Tất cả' }, { key: 'Upcoming', label: 'Sắp tới' }, { key: 'Past', label: 'Đã qua' }].map(tab => <Button key={tab.key} type={activeTab === tab.key ? 'primary' : 'default'} className={activeTab === tab.key ? 'active' : undefined} aria-pressed={activeTab === tab.key} onClick={() => changeTab(tab.key)}>{tab.label}</Button>)}
            </div>
            {error ? <InlineError message={error} onRetry={refreshData} /> : loading ? <LoadingState message="Đang tải danh sách lịch hẹn..." /> : filtered.length === 0 ? (
                <EmptyState icon={<CalendarDays size={48} />} title="Bạn chưa có lịch hẹn nào" description="Hãy đặt lịch khám để được bác sĩ tư vấn nhé." action={<Button type="primary" onClick={() => navigate('/patient/book')}>Đặt lịch ngay</Button>} />
            ) : <div className={styles.appointments}>
                {filtered.map(app => {
                    const hasPendingReq = app.status === 'PendingReschedule' || app.status === 'PendingCancellation';
                    const pendingReq = pendingRequests[app.id];
                    return <Card key={app.id} id={`appointment-card-${app.id}`} className={highlightedAppId === app.id ? styles.highlightedCard : styles.appointmentCard}>
                        <div className={styles.cardHeader}>
                            <div>
                                <div className={styles.identity}><strong>#{app.appointmentCode}</strong>{getStatusBadge(app.status)}</div>
                                <div className={styles.schedule}>
                                    <span className={styles.infoLine}><CalendarDays size={16} /><span>Ngày: <strong>{formatDateOnly(app.appointmentDate)}</strong></span></span>
                                    <span className={styles.infoLine}><Clock size={16} /><span>Giờ: <strong>{app.startTime?.substring(0, 5)} - {app.endTime?.substring(0, 5)}</strong></span></span>
                                </div>
                            </div>
                            <div className={styles.actions}>
                                <Button icon={<HistoryIcon size={14} />} onClick={() => handleViewHistory(app.id, app.appointmentCode)}>Lịch sử</Button>
                                {(app.status === 'Pending' || app.status === 'Confirmed') && <>
                                    <Button icon={<Calendar size={14} />} onClick={() => handleRescheduleOpen(app)}>Đổi lịch</Button>
                                    <Button danger icon={<XCircle size={14} />} onClick={() => handleCancelOpen(app)}>Hủy lịch</Button>
                                </>}
                                {hasPendingReq && <Button icon={<RotateCcw size={14} />} onClick={() => handleWithdraw(app)} disabled={withdrawingId === app.id}>{withdrawingId === app.id ? 'Đang xử lý...' : 'Rút yêu cầu'}</Button>}
                            </div>
                        </div>
                        {hasPendingReq && <Alert type="warning" showIcon className={styles.pendingAlert} title={<span>Lịch hẹn đang có yêu cầu <strong>{app.status === 'PendingReschedule' ? 'đổi lịch' : 'hủy lịch'}</strong> chờ lễ tân duyệt.{pendingReq?.reason && <> <span className={styles.requestReason}>(Lý do: {pendingReq.reason})</span></>}</span>} />}
                        <div className={styles.details}>
                            <div>
                                <h3 className={styles.sectionTitle}>Thông tin khám</h3>
                                <div className={styles.infoLine}><Stethoscope size={16} /><span>Chuyên khoa: <strong>{app.specialtyName || '...'}</strong></span></div>
                                <div className={styles.infoLine}><User size={16} /><span>Bác sĩ: <strong>{app.doctorName || '...'}</strong></span></div>
                            </div>
                            <div><h3 className={styles.sectionTitle}>Triệu chứng / Ghi chú</h3><p className={styles.notes}>{app.reason || 'Không có ghi chú'}</p></div>
                        </div>
                    </Card>;
                })}
            </div>}
            <Modal getContainer={false} open={cancelModal.isOpen} title={`Hủy lịch hẹn #${cancelModal.app?.appointmentCode}`} onCancel={handleCancelClose} maskClosable={!canceling} closable={!canceling} keyboard={!canceling} footer={<>
                <Button onClick={handleCancelClose} disabled={canceling}>Đóng</Button>
                <Button type="primary" danger onClick={submitCancel} disabled={canceling}>{canceling ? 'Đang xử lý...' : 'Xác nhận gửi yêu cầu'}</Button>
            </>}>
                <Alert type="warning" showIcon className={styles.modalAlert} title="Yêu cầu hủy sẽ được gửi đến lễ tân duyệt. Bạn có chắc chắn muốn yêu cầu hủy lịch khám này?" />
                <Form layout="vertical"><Form.Item label="Lý do hủy lịch (*)" htmlFor="cancel-reason"><Input.TextArea id="cancel-reason" rows={3} placeholder="Vui lòng nhập lý do (ít nhất 5 ký tự)..." value={cancelModal.reason} onChange={event => setCancelModal(p => ({ ...p, reason: event.target.value }))} disabled={canceling} /></Form.Item></Form>
                {cancelModal.inlineError && <Alert type="error" title={cancelModal.inlineError} showIcon />}
            </Modal>
            <Modal getContainer={false} open={rescheduleModal.isOpen} title={`Đổi lịch khám #${rescheduleModal.app?.appointmentCode}`} onCancel={closeReschedule} maskClosable={!rescheduling} closable={!rescheduling} keyboard={!rescheduling} footer={<>
                <Button onClick={closeReschedule} disabled={rescheduling}>Đóng</Button>
                <Button type="primary" onClick={submitReschedule} disabled={rescheduling || rescheduleModal.loadingSlots}>{rescheduling ? 'Đang gửi...' : 'Gửi yêu cầu dời lịch'}</Button>
            </>}>
                <div className={styles.currentAppointment}>
                    <div>Bác sĩ: <strong>{rescheduleModal.app?.doctorName}</strong></div>
                    <div>Chuyên khoa: <strong>{rescheduleModal.app?.specialtyName}</strong></div>
                    <div>Lịch hiện tại: <strong>{formatDateOnly(rescheduleModal.app?.appointmentDate)} ({rescheduleModal.app?.startTime?.substring(0, 5)} - {rescheduleModal.app?.endTime?.substring(0, 5)})</strong></div>
                </div>
                <Form layout="vertical">
                    <Form.Item label="Chọn ngày khám mới (*)" htmlFor="reschedule-date"><Input type="date" id="reschedule-date" className={styles.dateInput} min={tomorrowStr} value={rescheduleModal.targetDate} onChange={event => handleTargetDateChange(event.target.value)} disabled={rescheduling} /></Form.Item>
                    <Form.Item label="Chọn ca khám mới (*)" htmlFor="reschedule-slots">
                        <div id="reschedule-slots">
                            {rescheduleModal.loadingSlots ? <LoadingState message="Đang tìm ca khám trống..." height="120px" /> : rescheduleModal.inlineError ? null : rescheduleModal.availableSlots.length === 0 ? <Alert type="error" title="Bác sĩ không có ca khám trống trong ngày này. Vui lòng chọn ngày khác." /> : <div className={styles.slots}>
                                {rescheduleModal.availableSlots.map(slot => <Button key={slot.slotId} type={rescheduleModal.selectedSlotId === slot.slotId ? 'primary' : 'default'} disabled={rescheduling} icon={rescheduleModal.selectedSlotId === slot.slotId ? <Check size={14} /> : undefined} onClick={() => setRescheduleModal(p => ({ ...p, selectedSlotId: slot.slotId, inlineError: '' }))}>{slot.startTime?.substring(0, 5)} - {slot.endTime?.substring(0, 5)}</Button>)}
                            </div>}
                        </div>
                    </Form.Item>
                    <Form.Item label="Lý do đổi lịch (*)" htmlFor="reschedule-reason"><Input.TextArea id="reschedule-reason" rows={2} placeholder="Vui lòng nhập lý do (ít nhất 5 ký tự)..." value={rescheduleModal.reason} onChange={event => setRescheduleModal(p => ({ ...p, reason: event.target.value }))} disabled={rescheduling} /></Form.Item>
                </Form>
                {rescheduleModal.inlineError && <Alert type="error" title={rescheduleModal.inlineError} showIcon />}
            </Modal>
            <Modal getContainer={false} open={historyModal.isOpen} title={`Lịch sử thay đổi #${historyModal.appCode}`} onCancel={closeHistory} maskClosable={!historyModal.loading} closable={!historyModal.loading} keyboard={!historyModal.loading} footer={<Button type="primary" onClick={closeHistory} disabled={historyModal.loading}>Đóng</Button>}>
                {historyModal.loading ? <LoadingState message="Đang tải lịch sử..." height="120px" /> : historyModal.error ? <InlineError message={historyModal.error} onRetry={() => historyModal.appId !== null && handleViewHistory(historyModal.appId, historyModal.appCode)} /> : historyModal.histories.length === 0 ? <EmptyState title="Không có lịch sử thay đổi." /> : <div className={styles.history}>
                    {historyModal.histories.map((history, index) => <Card key={history.id ?? index} size="small">
                        <div className={styles.historyHeader}><strong>{getActionLabel(history.action)}</strong><span>{new Date(history.createdAt).toLocaleString('vi-VN')}</span></div>
                        <p className={styles.statusChange}>Từ {getStatusBadge(history.oldStatus)} thành {getStatusBadge(history.newStatus)}</p>
                        {history.note && <p className={styles.historyNote}>Ghi chú: {history.note}</p>}
                    </Card>)}
                </div>}
            </Modal>
        </div>
    );
};
