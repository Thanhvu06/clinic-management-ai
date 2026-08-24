import React, { useState, useEffect } from "react";
import axiosClient from "../../api/axiosClient";
import { User, Phone, MapPin, Calendar, Mail, Save } from "lucide-react";
import type { ApiResponse } from "../../types";
import { useDialog } from "../../contexts/DialogContext";
import { Breadcrumb } from "../../components/Breadcrumb";

interface PatientProfile {
    id: number;
    fullName: string;
    email: string;
    phoneNumber: string;
    dateOfBirth?: string;
    gender?: string;
    address?: string;
}

export const PatientProfile: React.FC = () => {
    const [profile, setProfile] = useState<PatientProfile | null>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    
    const { showAlert } = useDialog();

    useEffect(() => {
        fetchProfile();
    }, []);

    const fetchProfile = async () => {
        try {
            const res = await axiosClient.get<any, ApiResponse<PatientProfile>>("/patients/me");
            if (res.success && res.data) {
                // Ensure date string is formatted correctly for input type="date"
                const data = res.data;
                if (data.dateOfBirth && data.dateOfBirth.includes("T")) {
                    data.dateOfBirth = data.dateOfBirth.split("T")[0];
                }
                setProfile(data);
            }
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
        if (!profile) return;
        setProfile({ ...profile, [e.target.name]: e.target.value });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!profile) return;
        const genderVal = profile.gender === "" || profile.gender === null ? null : Number(profile.gender);

        setSaving(true);
        try {
            const res = await axiosClient.put<any, ApiResponse<any>>("/patients/me", {
                fullName: profile.fullName,
                phoneNumber: profile.phoneNumber,
                dateOfBirth: profile.dateOfBirth,
                gender: genderVal,
                address: profile.address
            });
            if (res.success) {
                showAlert('Cập nhật hồ sơ thành công!', 'Thành công', 'success');
                fetchProfile();
            }
        } catch (error: any) {
            const msg = error?.response?.data?.message || error?.message || "Có lỗi xảy ra khi cập nhật.";
            showAlert(msg, 'Lỗi', 'error');
        } finally {
            setSaving(false);
        }
    };

    if (loading) {
        return <div style={{ color: "var(--c-muted)", padding: "20px" }}>Đang tải thông tin...</div>;
    }

    if (!profile) {
        return <div className="card">Không tìm thấy thông tin hồ sơ.</div>;
    }

    return (
        <div>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Hồ sơ cá nhân' }
            ]} />

            <div className="card" style={{ maxWidth: '1000px', margin: '0 auto', padding: '32px' }}>
                <h2 style={{ marginBottom: "24px", color: "var(--c-navy)", borderBottom: '1px solid var(--c-border)', paddingBottom: '16px' }}>Hồ sơ cá nhân</h2>

                <form onSubmit={handleSubmit} style={{ display: "grid", gap: "24px" }}>
                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "24px" }}>
                        <div className="form-group">
                            <label className="form-label">Họ và tên (*)</label>
                            <div style={{ position: "relative" }}>
                                <User size={18} style={{ position: "absolute", left: "12px", top: "10px", color: "var(--c-muted)" }} />
                                <input
                                    type="text"
                                    name="fullName"
                                    value={profile.fullName}
                                    onChange={handleChange}
                                    className="form-input"
                                    style={{ paddingLeft: "40px" }}
                                    required
                                />
                            </div>
                        </div>
                        <div className="form-group">
                            <label className="form-label">Email</label>
                            <div style={{ position: "relative" }}>
                                <Mail size={18} style={{ position: "absolute", left: "12px", top: "10px", color: "var(--c-muted)" }} />
                                <input
                                    type="email"
                                    value={profile.email}
                                    className="form-input"
                                    style={{ paddingLeft: "40px", backgroundColor: "var(--c-bg)" }}
                                    disabled
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "24px" }}>
                        <div className="form-group">
                            <label className="form-label">Số điện thoại (*)</label>
                            <div style={{ position: "relative" }}>
                                <Phone size={18} style={{ position: "absolute", left: "12px", top: "10px", color: "var(--c-muted)" }} />
                                <input
                                    type="text"
                                    name="phoneNumber"
                                    value={profile.phoneNumber}
                                    onChange={handleChange}
                                    className="form-input"
                                    style={{ paddingLeft: "40px" }}
                                    required
                                />
                            </div>
                        </div>
                        <div className="form-group">
                            <label className="form-label">Ngày sinh</label>
                            <div style={{ position: "relative" }}>
                                <Calendar size={18} style={{ position: "absolute", left: "12px", top: "10px", color: "var(--c-muted)" }} />
                                <input
                                    type="date"
                                    name="dateOfBirth"
                                    value={profile.dateOfBirth || ""}
                                    onChange={handleChange}
                                    className="form-input"
                                    style={{ paddingLeft: "40px" }}
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "24px" }}>
                        <div className="form-group">
                            <label className="form-label">Giới tính</label>
                            <select
                                name="gender"
                                value={profile.gender !== null && profile.gender !== undefined ? profile.gender.toString() : ""}
                                onChange={handleChange}
                                className="form-select"
                            >
                                <option value="">-- Chưa cập nhật --</option>
                                <option value="0">Nam</option>
                                <option value="1">Nữ</option>
                                <option value="2">Khác</option>
                            </select>
                        </div>
                        <div className="form-group">
                            <label className="form-label">Địa chỉ</label>
                            <div style={{ position: "relative" }}>
                                <MapPin size={18} style={{ position: "absolute", left: "12px", top: "10px", color: "var(--c-muted)" }} />
                                <input
                                    type="text"
                                    name="address"
                                    value={profile.address || ""}
                                    onChange={handleChange}
                                    className="form-input"
                                    style={{ paddingLeft: "40px" }}
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "16px", paddingTop: "24px", borderTop: "1px solid var(--c-border)" }}>
                        <button type="submit" className="btn-primary" disabled={saving}>
                            <Save size={18} style={{ marginRight: '8px' }} />
                            {saving ? "Đang lưu..." : "Lưu thay đổi"}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
};
