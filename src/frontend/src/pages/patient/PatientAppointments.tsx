import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import type { ApiResponse } from "../../types";
import {
    CalendarDays, Clock, Stethoscope, User, AlertTriangle,
    XCircle, History as HistoryIcon,  
} from "lucide-react";
import { AppModal } from "../../components/AppModal";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";

export const PatientAppointments: React.FC = () => {
    const navigate = useNavigate();
    const { showAlert,  } = useDialog();

    const [appointments, setAppointments] = useState<any[]>([]);
    const [specialtiesMap, setSpecialtiesMap] = useState<Record<number, string>>({});
    const [loading, setLoading] = useState(true);
    const [activeTab, setActiveTab] = useState("All");

    // Cancel modal state
    const [cancelModal, setCancelModal] = useState<{
        isOpen: boolean;
        app: any | null;
        reason: string;
        inlineError: string;
    }>({ isOpen: false, app: null, reason: "", inlineError: "" });
    const [canceling, setCanceling] = useState(false);

    // History modal state
    const [historyModal, setHistoryModal] = useState<{
        isOpen: boolean;
        appCode: string;
        histories: any[];
        loading: boolean;
    }>({ isOpen: false, appCode: "", histories: [], loading: false });

    const fetchData = async () => {
        setLoading(true);
        try {
            const [specRes, appRes] = await Promise.all([
                axiosClient.get<any, ApiResponse<any[]>>("/specialties"),
                axiosClient.get<any, ApiResponse<any>>("/appointments/my?page=1&pageSize=100")
            ]);

            if (specRes.success && specRes.data) {
                const sm: Record<number, string> = {};
                specRes.data.forEach((s: any) => sm[s.id] = s.specialtyName || s.name || "");
                setSpecialtiesMap(sm);
            }

            if (appRes.success && appRes.data?.items) {
                setAppointments(appRes.data.items);
            }
        } catch (_) {}
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
        if (reason.trim().length < 5) {
            setCancelModal(p => ({ ...p, inlineError: "Vui lòng nhập lý do hủy (ít nhất 5 ký tự)." }));
            return;
        }

        setCanceling(true);
        setCancelModal(p => ({ ...p, inlineError: "" }));
        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/appointments/${app.id}/cancellation-requests`, { reason });
            if (res.success) {
                handleCancelClose();
                showAlert('Đã gửi yêu cầu hủy lịch thành công!', 'Thành công', 'success');
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

    const handleViewHistory = async (appId: number, code: string) => {
        setHistoryModal({ isOpen: true, appCode: code, histories: [], loading: true });
        try {
            const res = await axiosClient.get<any, ApiResponse<any[]>>(`/appointments/${appId}/history`);
            if (res.success && res.data) {
                setHistoryModal({ isOpen: true, appCode: code, histories: res.data, loading: false });
            } else {
                setHistoryModal(p => ({ ...p, loading: false }));
            }
        } catch (error) {
            setHistoryModal(p => ({ ...p, loading: false }));
        }
    };

    const filtered = appointments.filter(a => {
        if (activeTab === "Upcoming") return ["Pending", "Confirmed", "PendingReschedule", "PendingCancellation"].includes(a.status);
        if (activeTab === "Past") return ["Completed", "Cancelled", "NoShow"].includes(a.status);
        return true;
    });

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

            {loading ? (
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
                    {filtered.map(app => (
                        <div key={app.id} className="card-panel" style={{ padding: "0" }}>
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
                                        <button
                                            className="btn-danger"
                                            style={{ padding: "6px 12px", fontSize: "0.85rem" }}
                                            onClick={() => handleCancelOpen(app)}
                                        >
                                            <XCircle size={14} style={{ marginRight: "4px" }}/>
                                            Hủy lịch
                                        </button>
                                    )}
                                </div>
                            </div>
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
                    ))}
                </div>
            )}

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
                            {canceling ? "Đang xử lý..." : "Xác nhận hủy"}
                        </button>
                    </>
                }
            >
                <div style={{ display: "flex", alignItems: "flex-start", gap: "12px", padding: "12px", backgroundColor: "var(--c-danger-bg)", color: "var(--c-danger)", borderRadius: "8px", marginBottom: "16px" }}>
                    <AlertTriangle size={24} style={{ flexShrink: 0 }} />
                    <p style={{ margin: 0, fontSize: "0.95rem" }}>
                        Bạn có chắc chắn muốn hủy lịch khám này? Hãy cho chúng tôi biết lý do (bắt buộc).
                    </p>
                </div>
                <label style={{ display: "block", marginBottom: "8px", fontWeight: 500, color: "var(--c-text-dark)" }}>
                    Lý do hủy lịch (*)
                </label>
                <textarea
                    className="form-input"
                    rows={3}
                    placeholder="Vui lòng nhập lý do..."
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
