import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { useAuth } from '../auth/AuthContext';
import { ShieldPlus, Mail, Lock } from 'lucide-react';
import { useDialog } from '../contexts/DialogContext';

export const Login: React.FC = () => {
    const [emailOrPhone, setEmailOrPhone] = useState('');
    const [password, setPassword] = useState('');
    const [loading, setLoading] = useState(false);
    
    const { login } = useAuth();
    const navigate = useNavigate();
    const { showAlert, showToast } = useDialog();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);

        try {
            const res = await axiosClient.post<any, any>('/auth/login', { emailOrPhone, password });
            if (res.success && res.data) {
                showToast('Đăng nhập thành công', 'success');
                login(res.data.accessToken, res.data.user);
                switch (res.data.user.role) {
                    case 'Admin': navigate('/admin'); break;
                    case 'Doctor': navigate('/doctor'); break;
                    case 'Receptionist': navigate('/reception'); break;
                    default: navigate('/patient'); break;
                }
            }
        } catch (err: any) {
            let errorMsg = 'Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.';
            if (err === 'Network Error' || err?.message === 'Network Error') {
                errorMsg = 'Không thể kết nối máy chủ. Vui lòng thử lại sau.';
            } else if (err?.message) {
                errorMsg = err.message;
            } else if (typeof err === 'string') {
                errorMsg = err;
            }
            showAlert(errorMsg, 'Đăng nhập thất bại', 'error');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div style={{ display: 'flex', width: '100%', minHeight: '80vh', backgroundColor: 'var(--c-bg)', flexWrap: 'wrap' }}>
            <div style={{ flex: '1 1 500px', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '40px 20px' }}>
                <div className="card" style={{ width: '100%', maxWidth: '440px', borderRadius: 'var(--radius-xl)', padding: '40px 32px', border: 'none', boxShadow: 'var(--shadow-lg)' }}>
                    <div style={{ textAlign: 'center', marginBottom: '32px' }}>
                        <div style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: '64px', height: '64px', borderRadius: '50%', backgroundColor: 'var(--c-primary-light)', color: 'var(--c-primary)', marginBottom: '16px' }}>
                            <ShieldPlus size={36} />
                        </div>
                        <h2 style={{ fontSize: '1.75rem', margin: 0, color: 'var(--c-navy)', fontWeight: 700 }}>Chào mừng trở lại</h2>
                        <p style={{ color: 'var(--c-text-light)', marginTop: '8px', fontSize: '0.95rem' }}>Đăng nhập để tiếp tục với ClinicCare AI</p>
                    </div>
                    
                    <form onSubmit={handleSubmit}>
                        <div className="form-group" style={{ marginBottom: '20px' }}>
                            <label className="form-label" style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>Email hoặc Số điện thoại</label>
                            <div style={{ position: 'relative' }}>
                                <div style={{ position: 'absolute', top: '50%', left: '14px', transform: 'translateY(-50%)', color: 'var(--c-text-light)' }}>
                                    <Mail size={20} />
                                </div>
                                <input 
                                    type="text" 
                                    className="form-input"
                                    value={emailOrPhone} 
                                    onChange={e => setEmailOrPhone(e.target.value)} 
                                    required 
                                    placeholder="Nhập email hoặc SĐT"
                                    style={{ paddingLeft: '44px' }}
                                />
                            </div>
                        </div>
                        <div className="form-group" style={{ marginBottom: '28px' }}>
                            <label className="form-label" style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>Mật khẩu</label>
                            <div style={{ position: 'relative' }}>
                                <div style={{ position: 'absolute', top: '50%', left: '14px', transform: 'translateY(-50%)', color: 'var(--c-text-light)' }}>
                                    <Lock size={20} />
                                </div>
                                <input 
                                    type="password" 
                                    className="form-input"
                                    value={password} 
                                    onChange={e => setPassword(e.target.value)} 
                                    required 
                                    placeholder="••••••••"
                                    style={{ paddingLeft: '44px' }}
                                />
                            </div>
                        </div>
                        <button type="submit" className="btn-primary" disabled={loading} style={{ width: '100%', padding: '14px', fontSize: '1.05rem', borderRadius: 'var(--radius-md)', fontWeight: 600, display: 'flex', justifyContent: 'center' }}>
                            {loading ? 'Đang xử lý...' : 'Đăng nhập vào hệ thống'}
                        </button>
                    </form>
                    <div style={{ marginTop: '28px', textAlign: 'center', fontSize: '0.95rem', color: 'var(--c-text-light)' }}>
                        Chưa có tài khoản? <Link to="/register" style={{ color: 'var(--c-secondary)', fontWeight: 600, marginLeft: '4px' }}>Đăng ký ngay</Link>
                    </div>
                </div>
            </div>
            <div style={{ flex: '1 1 500px', display: 'flex', flexDirection: 'column', background: 'linear-gradient(135deg, var(--c-primary-light) 0%, white 100%)', padding: '40px', justifyContent: 'center', alignItems: 'center' }}>
                <img src="https://images.unsplash.com/photo-1576091160399-112ba8d25d1d?auto=format&fit=crop&w=800&q=80" alt="Login Visual" style={{ maxWidth: '80%', borderRadius: 'var(--radius-xl)', boxShadow: 'var(--shadow-xl)' }} />
                <div style={{ marginTop: '40px', textAlign: 'center', maxWidth: '80%' }}>
                    <h3 style={{ color: 'var(--c-navy)', fontSize: '2rem', fontWeight: 800, marginBottom: '16px' }}>Khám chữa bệnh dễ dàng</h3>
                    <p style={{ color: 'var(--c-text)', fontSize: '1.1rem', lineHeight: 1.6 }}>Quản lý hồ sơ sức khỏe, đặt lịch khám và nhận tư vấn trực tuyến nhanh chóng với hệ thống ClinicCare AI.</p>
                </div>
            </div>
        </div>
    );
};
