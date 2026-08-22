import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { User, Mail, Phone, Calendar, MapPin, Save, AlertCircle, CheckCircle2 } from 'lucide-react';

interface PatientProfile {
    id: number;
    userId: string;
    fullName: string;
    email: string;
    phoneNumber: string;
    dateOfBirth: string | null;
    gender: 'Male' | 'Female' | 'Other' | null;
    address: string | null;
}

export const PatientProfile: React.FC = () => {
    const [profile, setProfile] = useState<PatientProfile | null>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [message, setMessage] = useState<{ type: 'success' | 'error', text: string } | null>(null);

    useEffect(() => {
        const fetchProfile = async () => {
            try {
                const res = await axiosClient.get<any, ApiResponse<PatientProfile>>('/patients/me');
                if (res.success && res.data) {
                    setProfile(res.data);
                }
            } catch (error) {
                setMessage({ type: 'error', text: 'Không thể tải thông tin hồ sơ.' });
            } finally {
                setLoading(false);
            }
        };
        fetchProfile();
    }, []);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
        if (!profile) return;
        setProfile({ ...profile, [e.target.name]: e.target.value });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!profile) return;
        
        setSaving(true);
        setMessage(null);
        try {
            const res = await axiosClient.put<any, ApiResponse<any>>('/patients/me', {
                fullName: profile.fullName,
                phoneNumber: profile.phoneNumber,
                dateOfBirth: profile.dateOfBirth,
                gender: profile.gender,
                address: profile.address
            });
            if (res.success) {
                setMessage({ type: 'success', text: 'Cập nhật hồ sơ thành công!' });
            }
        } catch (error: any) {
            setMessage({ type: 'error', text: error?.message || 'Có lỗi xảy ra khi cập nhật.' });
        } finally {
            setSaving(false);
        }
    };

    if (loading) {
        return <div style={{ color: 'var(--c-muted)', padding: '20px' }}>Đang tải thông tin...</div>;
    }

    if (!profile) {
        return <div className="card-panel">Không tìm thấy thông tin hồ sơ.</div>;
    }

    return (
        <div style={{ maxWidth: '800px', margin: '0 auto' }}>
            <h2 style={{ marginBottom: '24px', color: 'var(--c-navy-dark)' }}>Hồ sơ cá nhân</h2>
            
            <div className="card-panel">
                {message && (
                    <div style={{ 
                        padding: '16px', 
                        borderRadius: '8px',
                        marginBottom: '24px',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        backgroundColor: message.type === 'success' ? 'var(--c-success-bg)' : 'var(--c-danger-bg)',
                        color: message.type === 'success' ? 'var(--c-success)' : 'var(--c-danger)',
                        fontWeight: 500
                    }}>
                        {message.type === 'success' ? <CheckCircle2 size={20} /> : <AlertCircle size={20} />}
                        {message.text}
                    </div>
                )}

                <form onSubmit={handleSubmit} style={{ display: 'grid', gap: '20px' }}>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Họ và tên (*)</label>
                            <div style={{ position: 'relative' }}>
                                <User size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                                <input 
                                    type="text" 
                                    name="fullName"
                                    value={profile.fullName} 
                                    onChange={handleChange}
                                    className="form-input" 
                                    style={{ paddingLeft: '40px' }}
                                    required 
                                />
                            </div>
                        </div>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Email</label>
                            <div style={{ position: 'relative' }}>
                                <Mail size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                                <input 
                                    type="email" 
                                    value={profile.email} 
                                    className="form-input" 
                                    style={{ paddingLeft: '40px', backgroundColor: 'var(--c-bg)' }}
                                    disabled 
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Số điện thoại (*)</label>
                            <div style={{ position: 'relative' }}>
                                <Phone size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                                <input 
                                    type="text" 
                                    name="phoneNumber"
                                    value={profile.phoneNumber} 
                                    onChange={handleChange}
                                    className="form-input" 
                                    style={{ paddingLeft: '40px' }}
                                    required 
                                />
                            </div>
                        </div>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Ngày sinh</label>
                            <div style={{ position: 'relative' }}>
                                <Calendar size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                                <input 
                                    type="date" 
                                    name="dateOfBirth"
                                    value={profile.dateOfBirth || ''} 
                                    onChange={handleChange}
                                    className="form-input" 
                                    style={{ paddingLeft: '40px' }}
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Giới tính</label>
                            <select 
                                name="gender" 
                                value={profile.gender || ''} 
                                onChange={handleChange}
                                className="form-select"
                            >
                                <option value="">-- Chưa cập nhật --</option>
                                <option value="Male">Nam</option>
                                <option value="Female">Nữ</option>
                                <option value="Other">Khác</option>
                            </select>
                        </div>
                        <div>
                            <label style={{ display: 'block', marginBottom: '8px', fontWeight: 500 }}>Địa chỉ</label>
                            <div style={{ position: 'relative' }}>
                                <MapPin size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: 'var(--c-muted)' }} />
                                <input 
                                    type="text" 
                                    name="address"
                                    value={profile.address || ''} 
                                    onChange={handleChange}
                                    className="form-input" 
                                    style={{ paddingLeft: '40px' }}
                                />
                            </div>
                        </div>
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '16px', paddingTop: '20px', borderTop: '1px solid var(--c-border)' }}>
                        <button type="submit" className="btn-primary" disabled={saving}>
                            <Save size={18} />
                            {saving ? 'Đang lưu...' : 'Lưu thay đổi'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
};
