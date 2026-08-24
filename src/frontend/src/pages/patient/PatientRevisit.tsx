import React, { useState, useEffect } from "react";
import axiosClient from "../../api/axiosClient";
import type { ApiResponse } from "../../types";
import { CalendarDays, Stethoscope, CheckCircle, XCircle, AlertCircle,  } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { AppModal } from "../../components/AppModal";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";

export const PatientRevisit: React.FC = () => {
    const [requests, setRequests] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [actionLoading, setActionLoading] = useState<number | null>(null);
    const { showAlert,  } = useDialog();
    
    // Reject Modal state
    const [rejectModal, setRejectModal] = useState<{isOpen: boolean, id: number | null, reason: string, error: string}>({
        isOpen: false, id: null, reason: "", error: ""
    });

    const navigate = useNavigate();

    const fetchRequests = async () => {
        try {
            const res = await axiosClient.get<any, ApiResponse<any>>("/revisit-requests");
            if (res.success && res.data) {
                setRequests(Array.isArray(res.data) ? res.data : (res.data.items || []));
            }
        } catch (error) {
            // Ignore
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRequests();
    }, []);

    const handleAccept = () => {
        showAlert("Vui lòng đặt lịch khám mới theo ngày gợi ý.", "Chấp nhận tái khám", "success");
        navigate("/patient/book");
    };

    const openRejectModal = (id: number) => {
        setRejectModal({ isOpen: true, id, reason: "", error: "" });
    };

    const closeRejectModal = () => {
        if (actionLoading !== null) return;
        setRejectModal({ isOpen: false, id: null, reason: "", error: "" });
    };

    const submitReject = async () => {
        const { id, reason } = rejectModal;
        if (!id) return;
        if (reason.trim().length < 5) {
            setRejectModal(p => ({ ...p, error: "Vui lòng nhập lý do (ít nhất 5 ký tự)." }));
            return;
        }

        setActionLoading(id);
        setRejectModal(p => ({ ...p, error: "" }));

        try {
            const res = await axiosClient.post<any, ApiResponse<any>>(`/revisit-requests/${id}/reject`, { reason });
            if (res.success) {
                closeRejectModal();
                showAlert('Đã từ chối lời mời tái khám.', 'Thành công', 'success');
                fetchRequests();
            } else {
                setRejectModal(p => ({ ...p, error: res.message || "Không thể từ chối" }));
            }
        } catch (err: any) {
            const msg = err?.response?.data?.message || err?.message || "Có lỗi xảy ra";
            setRejectModal(p => ({ ...p, error: msg }));
        } finally {
            setActionLoading(null);
        }
    };

    const pendingRequests = requests.filter(r => r.status === "PendingPatientResponse");
    const historyRequests = requests.filter(r => r.status !== "PendingPatientResponse");

    if (loading) {
        return <div style={{ padding: "40px", textAlign: "center", color: "var(--c-muted)" }}>Đang tải lời mời tái khám...</div>;
    }

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto' }}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Tái khám' }
            ]} />
            <h2 style={{ marginBottom: "24px", color: "var(--c-navy-dark)" }}>Lời mời tái khám</h2>

            {pendingRequests.length === 0 ? (
                <div className="card-panel" style={{ textAlign: "center", padding: "60px 20px", marginBottom: "32px" }}>
                    <CheckCircle size={48} color="var(--c-success)" style={{ marginBottom: "16px", opacity: 0.8 }} />
                    <h3 style={{ margin: "0 0 8px 0", color: "var(--c-navy-dark)" }}>Không có lời mời mới</h3>
                    <p style={{ margin: 0, color: "var(--c-text)" }}>Sức khỏe của bạn đang rất tốt, hãy duy trì nhé!</p>
                </div>
            ) : (
                <div style={{ display: "grid", gap: "20px", marginBottom: "40px" }}>
                    {pendingRequests.map(r => (
                        <div key={r.id} className="card-panel" style={{ padding: "0", border: "1px solid var(--c-primary)", overflow: "hidden" }}>
                            <div style={{ backgroundColor: "#EFF6FF", padding: "16px 20px", borderBottom: "1px solid #BFDBFE", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                <div style={{ display: "flex", alignItems: "center", gap: "8px", color: "var(--c-primary-dark)", fontWeight: 600 }}>
                                    <AlertCircle size={20} />
                                    Bác sĩ yêu cầu tái khám
                                </div>
                                <span className="badge badge-warning">Chờ phản hồi</span>
                            </div>

                            <div style={{ padding: "20px" }}>
                                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "20px", marginBottom: "20px" }}>
                                    <div>
                                        <p style={{ margin: "0 0 4px 0", color: "var(--c-muted)", fontSize: "0.9rem" }}>Ngày gợi ý</p>
                                        <div style={{ display: "flex", alignItems: "center", gap: "8px", fontWeight: 500 }}>
                                            <CalendarDays size={18} color="var(--c-primary)" />
                                            {r.suggestedDate ? r.suggestedDate.split("T")[0] : ""}
                                        </div>
                                    </div>
                                    <div>
                                        <p style={{ margin: "0 0 4px 0", color: "var(--c-muted)", fontSize: "0.9rem" }}>Bác sĩ phụ trách</p>
                                        <div style={{ display: "flex", alignItems: "center", gap: "8px", fontWeight: 500 }}>
                                            <Stethoscope size={18} color="var(--c-primary)" />
                                            {r.doctorName || "..."}
                                        </div>
                                    </div>
                                </div>

                                <div style={{ backgroundColor: "var(--c-bg)", padding: "12px 16px", borderRadius: "8px", borderLeft: "4px solid var(--c-primary)", marginBottom: "24px" }}>
                                    <p style={{ margin: "0 0 4px 0", fontSize: "0.85rem", color: "var(--c-muted)", fontWeight: 600, textTransform: "uppercase" }}>Lời nhắn từ bác sĩ</p>
                                    <p style={{ margin: 0, fontStyle: "italic" }}>"{r.note}"</p>
                                </div>

                                <div style={{ display: "flex", gap: "12px", justifyContent: "flex-end" }}>
                                    <button
                                        className="btn-danger"
                                        onClick={() => openRejectModal(r.id)}
                                        disabled={actionLoading === r.id}
                                    >
                                        <XCircle size={18} /> Từ chối
                                    </button>
                                    <button
                                        className="btn-primary"
                                        onClick={handleAccept}
                                        disabled={actionLoading === r.id}
                                    >
                                        <CheckCircle size={18} /> Đặt lịch ngay
                                    </button>
                                </div>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {historyRequests.length > 0 && (
                <div>
                    <h3 style={{ marginBottom: "16px", color: "var(--c-navy-dark)" }}>Lịch sử tái khám</h3>
                    <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                        {historyRequests.map(r => (
                            <div key={r.id} className="card-panel" style={{ padding: "16px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                <div>
                                    <p style={{ margin: "0 0 8px 0", fontWeight: 500 }}>Ngày hẹn: {r.suggestedDate ? r.suggestedDate.split("T")[0] : ""}</p>
                                    <p style={{ margin: "0 0 4px 0", fontSize: "0.9rem", color: "var(--c-text)" }}>Bác sĩ: <strong>{r.doctorName}</strong></p>
                                    <p style={{ margin: 0, fontSize: "0.9rem", color: "var(--c-muted)" }}>Lý do: {r.note}</p>
                                </div>
                                <div>
                                    {r.status === "PatientRejected" ? (
                                        <span className="badge badge-danger">Đã từ chối</span>
                                    ) : r.status === "Accepted" ? (
                                        <span className="badge badge-success">Đã chấp nhận</span>
                                    ) : (
                                        <span className="badge badge-muted">{r.status}</span>
                                    )}
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            <AppModal
                isOpen={rejectModal.isOpen}
                onClose={closeRejectModal}
                title="Từ chối tái khám"
                actions={
                    <>
                        <button type="button" className="btn-secondary" onClick={closeRejectModal} disabled={actionLoading !== null}>
                            Đóng
                        </button>
                        <button type="button" className="btn-danger" onClick={submitReject} disabled={actionLoading !== null}>
                            {actionLoading !== null ? "Đang xử lý..." : "Xác nhận từ chối"}
                        </button>
                    </>
                }
            >
                <div style={{ marginBottom: "16px", color: "var(--c-text-dark)" }}>
                    Xin hãy cho chúng tôi biết lý do bạn không thể tham gia tái khám đợt này.
                </div>
                <label style={{ display: "block", marginBottom: "8px", fontWeight: 500, color: "var(--c-text-dark)" }}>
                    Lý do từ chối (*)
                </label>
                <textarea
                    className="form-input"
                    rows={3}
                    placeholder="Vui lòng nhập lý do (ví dụ: Đã khỏe lại, Bận công tác...)"
                    value={rejectModal.reason}
                    onChange={(e) => setRejectModal(p => ({ ...p, reason: e.target.value }))}
                    disabled={actionLoading !== null}
                    style={{ resize: "none" }}
                />
                {rejectModal.error && (
                    <p style={{ color: "var(--c-danger)", fontSize: "0.85rem", marginTop: "8px", marginBottom: 0 }}>
                        {rejectModal.error}
                    </p>
                )}
            </AppModal>

        </div>
    );
};
