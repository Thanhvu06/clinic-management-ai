import React, { useState, useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import type { ApiResponse } from "../../types";
import {
    CalendarDays, Clock, Stethoscope, User, AlertTriangle,
    XCircle, History as HistoryIcon, RotateCcw, Calendar, Check, RefreshCw
} from "lucide-react";
import { AppModal } from "../../components/AppModal";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";
import { getNextWorkingDateString, isSundayDateString } from "../../utils/doctorNameHelper";

export const PatientAppointments: React.FC = () => {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const { showAlert, showConfirm } = useDialog();

    const [appointments, setAppointments] = useState<any[]>([]);
    const [specialtiesMap, setSpecialtiesMap] = useState<Record<number, string>>({});
    const [pendingRequests, setPendingRequests] = useState<Record<number, any>>({});
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [activeTab, setActiveTab] = useState("All");
    const [highlightedAppId, setHighlightedAppId] = useState<number | null>(null);

    // Cancel modal state
    const [cancelModal, setCancelModal] = useState<{
        isOpen: boolean;
        app: any | null;
        reason: string;
        inlineError: string;
    }>({ isOpen: false, app: null, reason: "", inlineError: "" });
    const [canceling, setCanceling] = useState(false);

    // Reschedule modal state
    const [rescheduleModal, setRescheduleModal] = useState<{
        isOpen: boolean;
        app: any | null;
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
        appCode: string;
        histories: any[];
        loading: boolean;
    }>({ isOpen: false, appCode: "", histories: [], loading: false });

    const fetchData = async () => {
        setLoading(true);
        setError(null);
        try {
            const [specRes, appRes, chgRes] = await Promise.all([
                axiosClient.get<any, ApiResponse<any[]>>("/specialties"),
                axiosClient.get<any, ApiResponse<any>>("/appointments/my?page=1&pageSize=100"),
                axiosClient.get<any, ApiResponse<any>>("/appointment-change-requests?status=Pending&page=1&pageSize=100")
            ]);

            if (specRes.success && specRes.data) {
                const sm: Record<number, string> = {};
                specRes.data.forEach((s: any) => sm[s.id] = s.specialtyName || s.name || "");
                setSpecialtiesMap(sm);
            }

            if (appRes.success && appRes.data?.items) {
                setAppointments(appRes.data.items);
            }

            if (chgRes.success && chgRes.data?.items) {
                const prMap: Record<number, any> = {};
                chgRes.data.items.forEach((r: any) => {
                    prMap[r.appointmentId] = r;
                });
                setPendingRequests(prMap);
            }
        } catch (err: any) {
            setError(err?.response?.data?.message || err?.message || "Không thể tải danh sách lịch hẹn.");
        }
        finally { setLoading(false); }
    };

    useEffect(() => { fetchData(); }, []);

    const getStatusBadge = (status: string) => {
        switch (status) {
            case "Pending": return <span className="badge badge-warning">Chờ xác nhận</span>;
            case "Confirmed": return <span className="badge badge-info">Đã xác nhận</span>;
            case "PendingCancellation": return <span className="badge badge-warning">Chờ hủy</span>;
            case "PendingReschedule": return <span className="badge badge-warning">Chờ đổi lịch</span>;
            case "Completed": return <span className="badge badge-success">Đã hoàn thành</span>;
            case "Cancelled": return <span className="badge badge-danger">Đã hủy</span>;
            case "NoShow": return <span className="badge badge-muted">Vắng mặt</span>;
            default: return <span className="badge badge-muted">{status}</span>;
        }
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

    const handleCancelOpen = (app: any) => {
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
                handleCancelClose();
                showAlert('Đã gửi yêu cầu hủy lịch thành công! Vui lòng chờ lễ tân xử lý.', 'Thành công', 'success');
                fetchData();
            } else {
                setCancelModal(p => ({ ...p, inlineError: res.message || "Không thể hủy lịch" }));
            }
        } catch (error: any) {
            const msg = error?.response?.data?.message || error?.message || "Có lỗi xảy ra khi hủy lịch.";
            setCancelModal(p => ({ ...p, inlineError: msg }));
        } finally {
            setCanceling(false);
        }
    };

    const loadSlotsForDate = async (doctorId: number, dateStr: string, specialtyId?: number, currentSlotId?: number) => {
        setRescheduleModal(p => ({ ...p, loadingSlots: true, inlineError: "", availableSlots: [], selectedSlotId: null }));
        try {
            const specParam = specialtyId ? `&specialtyId=${specialtyId}` : "";
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/doctors/${doctorId}/available-slots?fromDate=${dateStr}&toDate=${dateStr}${specParam}`);
            if (res.success && res.data) {
                // Filter out current appointment's slot
                const slots = res.data.filter((s: any) => s.slotId !== currentSlotId);
                setRescheduleModal(p => ({ ...p, availableSlots: slots, loadingSlots: false }));
            } else {
                setRescheduleModal(p => ({ ...p, availableSlots: [], loadingSlots: false }));
            }
        } catch {
            setRescheduleModal(p => ({ ...p, loadingSlots: false, inlineError: "Không thể tải danh sách ca khám khả dụng." }));
        }
    };

    const handleRescheduleOpen = (app: any) => {
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
        if (isSundayDateString(newDate)) {
            setRescheduleModal(p => ({
                ...p,
                targetDate: newDate,
                availableSlots: [],
                selectedSlotId: null,
                inlineError: "Phòng khám không làm việc vào Chủ nhật. Vui lòng chọn ngày khác (Thứ 2 - Thứ 7)."
            }));
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
                fetchData();
            } else {
                setRescheduleModal(p => ({ ...p, inlineError: res.message || "Không thể dời lịch khám." }));
            }
        } catch (err: any) {
            const msg = err?.response?.data?.message || err?.message || "Có lỗi xảy ra khi gửi yêu cầu đổi lịch.";
            setRescheduleModal(p => ({ ...p, inlineError: msg }));
        } finally {
            setRescheduling(false);
        }
    };

    const handleWithdraw = (app: any) => {
        const pendingReq = pendingRequests[app.id];
        if (!pendingReq) {
            showAlert("Không tìm thấy yêu cầu chờ xử lý của lịch hẹn này.", "Thông báo", "warning");
            return;
        }

        showConfirm(
            "Bạn có chắc chắn muốn rút yêu cầu thay đổi và tiếp tục giữ lịch khám ban đầu?",
            async () => {
                try {
                    const res = await axiosClient.post<any, ApiResponse<any>>(`/appointment-change-requests/${pendingReq.id}/withdraw`);
                    if (res.success) {
                        showAlert("Đã rút yêu cầu thành công!", "Thành công", "success");
                        fetchData();
                    } else {
                        showAlert(res.message || "Không thể rút yêu cầu.", "Lỗi", "error");
                    }
                } catch (err: any) {
                    const msg = err?.response?.data?.message || err?.message || "Có lỗi xảy ra khi rút yêu cầu.";
                    showAlert(msg, "Lỗi", "error");
                }
            },
            "Xác nhận rút yêu cầu"
        );
    };

    const handleViewHistory = async (appId: number, code: string) => {
        setHistoryModal({ isOpen: true, appCode: code, histories: [], loading: true });
        try {
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/appointments/${appId}/history`);
            if (res.success && res.data) {
                setHistoryModal({ isOpen: true, appCode: code, histories: res.data, loading: false });
            } else {
                setHistoryModal(p => ({ ...p, loading: false }));
            }
        } catch {
            setHistoryModal(p => ({ ...p, loading: false }));
        }
    };

    const filtered = appointments.filter(a => {
        if (activeTab === "Upcoming") return ["Pending", "Confirmed", "PendingReschedule", "PendingCancellation"].includes(a.status);
        if (activeTab === "Past") return ["Completed", "Cancelled", "NoShow"].includes(a.status);
        return true;
    });

    const tomorrowStr = (() => {
        const d = new Date();
        d.setDate(d.getDate() + 1);
        return d.toISOString().split("T")[0];
    })();

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto' }}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Lịch sử khám' }
            ]} />
            
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "24px" }}>
                <h2 style={{ color: "var(--c-navy)", margin: 0 }}>Lịch hẹn của tôi</h2>
                <button className="btn-primary" onClick={() => navigate("/patient/book")}>
                    + Đặt lịch mới
                </button>
            </div>

            <div className="tabs" style={{ marginBottom: "24px" }}>
                <button
                    className={`tab-btn ${activeTab === "All" ? "active" : ""}`}
                    onClick={() => setActiveTab("All")}
                >
                    Tất cả
                </button>
                <button
                    className={`tab-btn ${activeTab === "Upcoming" ? "active" : ""}`}
                    onClick={() => setActiveTab("Upcoming")}
                >
                    Sắp tới
                </button>
                <button
                    className={`tab-btn ${activeTab === "Past" ? "active" : ""}`}
                    onClick={() => setActiveTab("Past")}
                >
                    Đã qua
                </button>
            </div>

            {error ? (
                <div className="card-panel" style={{ textAlign: "center", padding: "40px 20px" }}>
                    <AlertTriangle size={36} color="var(--c-danger)" style={{ marginBottom: "12px" }} />
                    <p style={{ color: "var(--c-danger)", marginBottom: "16px" }}>{error}</p>
                    <button className="btn-primary" onClick={() => fetchData()} style={{ display: "inline-flex", alignItems: "center", gap: "6px" }}>
                        <RefreshCw size={14} /> Thử lại
                    </button>
                </div>
            ) : loading ? (
                <div style={{ padding: "40px", textAlign: "center", color: "var(--c-muted)" }}>Đang tải danh sách lịch hẹn...</div>
            ) : filtered.length === 0 ? (
                <div className="card-panel" style={{ textAlign: "center", padding: "60px 20px" }}>
                    <CalendarDays size={48} color="var(--c-muted)" style={{ marginBottom: "16px" }} />
                    <h3 style={{ margin: "0 0 8px 0", color: "var(--c-navy-dark)" }}>Bạn chưa có lịch hẹn nào</h3>
                    <p style={{ margin: 0, color: "var(--c-text)" }}>Hãy đặt lịch khám để được bác sĩ tư vấn nhé.</p>
                    <button className="btn-primary" style={{ marginTop: "24px" }} onClick={() => navigate("/patient/book")}>
                        Đặt lịch ngay
                    </button>
                </div>
            ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                    {filtered.map(app => {
                        const hasPendingReq = app.status === "PendingReschedule" || app.status === "PendingCancellation";
                        const pendingReq = pendingRequests[app.id];

                        return (
                            <div
                                key={app.id}
                                id={`appointment-card-${app.id}`}
                                className="card-panel"
                                style={{
                                    padding: "0",
                                    border: highlightedAppId === app.id ? "2px solid var(--c-primary)" : undefined,
                                    boxShadow: highlightedAppId === app.id ? "0 0 12px rgba(13, 148, 136, 0.35)" : undefined,
                                    transition: "all 0.3s ease"
                                }}
                            >
                                <div style={{ padding: "16px 20px", display: "flex", justifyContent: "space-between", alignItems: "flex-start", borderBottom: "1px solid var(--c-border)" }}>
                                    <div>
                                        <div style={{ display: "flex", alignItems: "center", gap: "12px", marginBottom: "8px" }}>
                                            <span style={{ fontWeight: 600, color: "var(--c-primary)", fontSize: "1.1rem" }}>#{app.appointmentCode}</span>
                                            {getStatusBadge(app.status)}
                                        </div>
                                        <div style={{ display: "flex", alignItems: "center", gap: "6px", color: "var(--c-text)", fontSize: "0.95rem" }}>
                                            <CalendarDays size={16} color="var(--c-muted)" />
                                            <span>Ngày: <strong>{app.slotDate ? app.slotDate.split("T")[0] : ""}</strong></span>
                                            <span style={{ margin: "0 8px", color: "var(--c-border)" }}>|</span>
                                            <Clock size={16} color="var(--c-muted)" />
                                            <span>Giờ: <strong>{app.startTime && app.startTime.substring(0, 5)} - {app.endTime && app.endTime.substring(0, 5)}</strong></span>
                                        </div>
                                    </div>

                                    <div style={{ display: "flex", gap: "8px" }}>
                                        <button
                                            className="btn-secondary"
                                            style={{ padding: "6px 12px", fontSize: "0.85rem" }}
                                            onClick={() => handleViewHistory(app.id, app.appointmentCode)}
                                        >
                                            <HistoryIcon size={14} style={{ marginRight: "4px" }}/>
                                            Lịch sử
                                        </button>

                                        {(app.status === "Pending" || app.status === "Confirmed") && (
                                            <>
                                                <button
                                                    className="btn-secondary"
                                                    style={{ padding: "6px 12px", fontSize: "0.85rem", borderColor: "var(--c-primary)", color: "var(--c-primary)" }}
                                                    onClick={() => handleRescheduleOpen(app)}
                                                >
                                                    <Calendar size={14} style={{ marginRight: "4px" }}/>
                                                    Đổi lịch
                                                </button>
                                                <button
                                                    className="btn-danger"
                                                    style={{ padding: "6px 12px", fontSize: "0.85rem" }}
                                                    onClick={() => handleCancelOpen(app)}
                                                >
                                                    <XCircle size={14} style={{ marginRight: "4px" }}/>
                                                    Hủy lịch
                                                </button>
                                            </>
                                        )}

                                        {hasPendingReq && (
                                            <button
                                                className="btn-secondary"
                                                style={{ padding: "6px 12px", fontSize: "0.85rem", color: "var(--c-warning)", borderColor: "var(--c-warning)" }}
                                                onClick={() => handleWithdraw(app)}
                                            >
                                                <RotateCcw size={14} style={{ marginRight: "4px" }}/>
                                                Rút yêu cầu
                                            </button>
                                        )}
                                    </div>
                                </div>

                                {hasPendingReq && (
                                    <div style={{ padding: "10px 20px", backgroundColor: "var(--c-warning-bg)", borderBottom: "1px solid var(--c-border)", display: "flex", alignItems: "center", gap: "8px", fontSize: "0.9rem", color: "var(--c-warning)" }}>
                                        <AlertTriangle size={16} />
                                        <span>
                                            Lịch hẹn đang có yêu cầu <strong>{app.status === "PendingReschedule" ? "đổi lịch" : "hủy lịch"}</strong> chờ lễ tân duyệt.
                                            {pendingReq?.reason && <span style={{ marginLeft: "6px", fontStyle: "italic", color: "var(--c-text)" }}>(Lý do: {pendingReq.reason})</span>}
                                        </span>
                                    </div>
                                )}

                                <div style={{ padding: "16px 20px", display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px", backgroundColor: "#f8fafc" }}>
                                    <div>
                                        <p style={{ margin: "0 0 8px 0", color: "var(--c-muted)", fontSize: "0.85rem", textTransform: "uppercase", fontWeight: 600 }}>Thông tin khám</p>
                                        <div style={{ display: "flex", alignItems: "center", gap: "8px", marginBottom: "8px" }}>
                                            <Stethoscope size={16} color="var(--c-primary)" />
                                            <span>Chuyên khoa: <strong>{specialtiesMap[app.specialtyId] || "..."}</strong></span>
                                        </div>
                                        <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                                            <User size={16} color="var(--c-primary)" />
                                            <span>Bác sĩ: <strong>{app.doctorName || "..."}</strong></span>
                                        </div>
                                    </div>
                                    <div>
                                        <p style={{ margin: "0 0 8px 0", color: "var(--c-muted)", fontSize: "0.85rem", textTransform: "uppercase", fontWeight: 600 }}>Triệu chứng / Ghi chú</p>
                                        <p style={{ margin: 0, fontSize: "0.95rem", lineHeight: 1.5, color: "var(--c-text-dark)" }}>
                                            {app.symptoms || "Không có ghi chú"}
                                        </p>
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}

            {/* Cancel Modal */}
            <AppModal
                isOpen={cancelModal.isOpen}
                onClose={handleCancelClose}
                title={`Hủy lịch hẹn #${cancelModal.app?.appointmentCode}`}
                actions={
                    <>
                        <button type="button" className="btn-secondary" onClick={handleCancelClose} disabled={canceling}>
                            Đóng
                        </button>
                        <button type="button" className="btn-danger" onClick={submitCancel} disabled={canceling}>
                            {canceling ? "Đang xử lý..." : "Xác nhận gửi yêu cầu"}
                        </button>
                    </>
                }
            >
                <div style={{ display: "flex", alignItems: "flex-start", gap: "12px", padding: "12px", backgroundColor: "var(--c-danger-bg)", color: "var(--c-danger)", borderRadius: "8px", marginBottom: "16px" }}>
                    <AlertTriangle size={24} style={{ flexShrink: 0 }} />
                    <p style={{ margin: 0, fontSize: "0.95rem" }}>
                        Yêu cầu hủy sẽ được gửi đến lễ tân duyệt. Bạn có chắc chắn muốn yêu cầu hủy lịch khám này?
                    </p>
                </div>
                <label style={{ display: "block", marginBottom: "8px", fontWeight: 500, color: "var(--c-text-dark)" }}>
                    Lý do hủy lịch (*)
                </label>
                <textarea
                    className="form-input"
                    rows={3}
                    placeholder="Vui lòng nhập lý do (ít nhất 5 ký tự)..."
                    value={cancelModal.reason}
                    onChange={(e) => setCancelModal(p => ({ ...p, reason: e.target.value }))}
                    disabled={canceling}
                    style={{ resize: "none" }}
                />
                {cancelModal.inlineError && (
                    <p style={{ color: "var(--c-danger)", fontSize: "0.85rem", marginTop: "8px", marginBottom: 0 }}>
                        {cancelModal.inlineError}
                    </p>
                )}
            </AppModal>

            {/* Reschedule Modal */}
            <AppModal
                isOpen={rescheduleModal.isOpen}
                onClose={() => !rescheduling && setRescheduleModal(p => ({ ...p, isOpen: false }))}
                title={`Đổi lịch khám #${rescheduleModal.app?.appointmentCode}`}
                actions={
                    <>
                        <button type="button" className="btn-secondary" onClick={() => setRescheduleModal(p => ({ ...p, isOpen: false }))} disabled={rescheduling}>
                            Đóng
                        </button>
                        <button type="button" className="btn-primary" onClick={submitReschedule} disabled={rescheduling || rescheduleModal.loadingSlots}>
                            {rescheduling ? "Đang gửi..." : "Gửi yêu cầu dời lịch"}
                        </button>
                    </>
                }
            >
                <div style={{ marginBottom: "16px", padding: "12px", backgroundColor: "#f0fdf4", border: "1px solid #bbf7d0", borderRadius: "8px", fontSize: "0.9rem" }}>
                    <div>Bác sĩ: <strong>{rescheduleModal.app?.doctorName}</strong></div>
                    <div>Chuyên khoa: <strong>{specialtiesMap[rescheduleModal.app?.specialtyId]}</strong></div>
                    <div>Lịch hiện tại: <strong>{rescheduleModal.app?.slotDate?.split("T")[0]} ({rescheduleModal.app?.startTime?.substring(0, 5)} - {rescheduleModal.app?.endTime?.substring(0, 5)})</strong></div>
                </div>

                <div style={{ marginBottom: "16px" }}>
                    <label style={{ display: "block", marginBottom: "6px", fontWeight: 500 }}>Chọn ngày khám mới (*)</label>
                    <input
                        type="date"
                        className="form-input"
                        min={tomorrowStr}
                        value={rescheduleModal.targetDate}
                        onChange={(e) => handleTargetDateChange(e.target.value)}
                        disabled={rescheduling}
                    />
                </div>

                <div style={{ marginBottom: "16px" }}>
                    <label style={{ display: "block", marginBottom: "6px", fontWeight: 500 }}>Chọn ca khám mới (*)</label>
                    {rescheduleModal.loadingSlots ? (
                        <div style={{ padding: "12px", textAlign: "center", color: "var(--c-muted)" }}>Đang tìm ca khám trống...</div>
                    ) : rescheduleModal.availableSlots.length === 0 ? (
                        <div style={{ padding: "12px", background: "#fef2f2", color: "#dc2626", borderRadius: "6px", fontSize: "0.9rem" }}>
                            Bác sĩ không có ca khám trống trong ngày này. Vui lòng chọn ngày khác.
                        </div>
                    ) : (
                        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(120px, 1fr))", gap: "8px", maxHeight: "160px", overflowY: "auto", padding: "4px" }}>
                            {rescheduleModal.availableSlots.map(slot => {
                                const isSelected = rescheduleModal.selectedSlotId === slot.slotId;
                                return (
                                    <button
                                        key={slot.slotId}
                                        type="button"
                                        onClick={() => setRescheduleModal(p => ({ ...p, selectedSlotId: slot.slotId, inlineError: "" }))}
                                        style={{
                                            padding: "8px",
                                            borderRadius: "6px",
                                            border: `1.5px solid ${isSelected ? "var(--c-primary)" : "var(--c-border)"}`,
                                            backgroundColor: isSelected ? "var(--c-primary-light, #e0f2fe)" : "white",
                                            color: isSelected ? "var(--c-primary)" : "var(--c-text)",
                                            fontWeight: isSelected ? 600 : 400,
                                            cursor: "pointer",
                                            display: "flex",
                                            alignItems: "center",
                                            justifyContent: "center",
                                            gap: "4px"
                                        }}
                                    >
                                        {isSelected && <Check size={14} />}
                                        {slot.startTime?.substring(0, 5)} - {slot.endTime?.substring(0, 5)}
                                    </button>
                                );
                            })}
                        </div>
                    )}
                </div>

                <div>
                    <label style={{ display: "block", marginBottom: "6px", fontWeight: 500 }}>Lý do đổi lịch (*)</label>
                    <textarea
                        className="form-input"
                        rows={2}
                        placeholder="Vui lòng nhập lý do (ít nhất 5 ký tự)..."
                        value={rescheduleModal.reason}
                        onChange={(e) => setRescheduleModal(p => ({ ...p, reason: e.target.value }))}
                        disabled={rescheduling}
                        style={{ resize: "none" }}
                    />
                </div>

                {rescheduleModal.inlineError && (
                    <p style={{ color: "var(--c-danger)", fontSize: "0.85rem", marginTop: "8px", marginBottom: 0 }}>
                        {rescheduleModal.inlineError}
                    </p>
                )}
            </AppModal>

            {/* History Modal */}
            <AppModal
                isOpen={historyModal.isOpen}
                onClose={() => setHistoryModal({ isOpen: false, appCode: "", histories: [], loading: false })}
                title={`Lịch sử thay đổi #${historyModal.appCode}`}
                actions={
                    <button type="button" className="btn-primary" onClick={() => setHistoryModal({ isOpen: false, appCode: "", histories: [], loading: false })}>
                        Đóng
                    </button>
                }
            >
                {historyModal.loading ? (
                    <div style={{ padding: "20px", textAlign: "center", color: "var(--c-text-light)" }}>Đang tải lịch sử...</div>
                ) : historyModal.histories.length === 0 ? (
                    <div style={{ padding: "20px", textAlign: "center", color: "var(--c-text-light)" }}>Không có lịch sử thay đổi.</div>
                ) : (
                    <div style={{ display: "flex", flexDirection: "column", gap: "12px", maxHeight: "300px", overflowY: "auto", paddingRight: "4px" }}>
                        {historyModal.histories.map((h, i) => (
                            <div key={i} style={{ padding: "12px", borderRadius: "8px", backgroundColor: "#f8fafc", border: "1px solid var(--c-border)", fontSize: "0.9rem" }}>
                                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                                    <strong style={{ color: "var(--c-primary)" }}>{getActionLabel(h.action)}</strong>
                                    <span style={{ color: "var(--c-text-light)", fontSize: "0.8rem" }}>
                                        {new Date(h.createdAt).toLocaleString("vi-VN")}
                                    </span>
                                </div>
                                <div style={{ color: "var(--c-text)" }}>
                                    <p style={{ margin: "0 0 4px 0" }}>Từ <strong>{getStatusBadge(h.oldStatus)}</strong> thành <strong>{getStatusBadge(h.newStatus)}</strong></p>
                                    {h.note && <p style={{ margin: 0, fontStyle: "italic", color: "var(--c-text-light)" }}>Ghi chú: {h.note}</p>}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </AppModal>

        </div>
    );
};
