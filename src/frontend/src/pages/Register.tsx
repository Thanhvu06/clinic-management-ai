import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { ShieldPlus, User, Mail, Phone, Lock } from 'lucide-react';
import { useDialog } from '../contexts/DialogContext';

export const Register: React.FC = () => {
    const { showAlert, showToast } = useDialog();
    const [formData, setFormData] = useState({
        fullName: '',
        email: '',
        phoneNumber: '',
        password: ''
    });
    const [fieldErrors, setFieldErrors] = useState<{ [key: string]: string }>({});
    const [loading, setLoading] = useState(false);
    
    const navigate = useNavigate();

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setFormData({ ...formData, [e.target.name]: e.target.value });
        if (fieldErrors[e.target.name]) {
            setFieldErrors({ ...fieldErrors, [e.target.name]: '' });
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFieldErrors({});
        setLoading(true);

        try {
            const res = await axiosClient.post<any, any>('/auth/register', formData);
            if (res.success) {
                showToast('Tài khoản đã được tạo thành công!', 'success');
                showAlert('Tài khoản đã được tạo thành công. Vui lòng đăng nhập để sử dụng dịch vụ.', 'Đăng ký thành công', 'success');
                navigate('/login');
            }
        } catch (err: any) {
            if (err?.errors) {
                const newFieldErrors: { [key: string]: string } = {};
                for (const key in err.errors) {
                    const camelKey = key.charAt(0).toLowerCase() + key.slice(1);
                    newFieldErrors[camelKey] = Array.isArray(err.errors[key]) ? err.errors[key][0] : err.errors[key];
                }
                setFieldErrors(newFieldErrors);
            } else {
                showAlert(err?.message || err?.title || 'Đăng ký thất bại. Vui lòng thử lại.', 'Đã xảy ra lỗi', 'error');
            }
        } finally {
            setLoading(false);
        }
    };

    return (
        <div style={{ display: 'flex', width: '100%', minHeight: '80vh', backgroundColor: 'var(--c-bg)', flexWrap: 'wrap-reverse' }}>
            <div style={{ flex: '1 1 500px', display: 'flex', flexDirection: 'column', background: 'linear-gradient(135deg, var(--c-secondary-light) 0%, white 100%)', padding: '40px', justifyContent: 'center', alignItems: 'center' }}>
                <img src="https://images.unsplash.com/photo-1551076805-e18690c5e561?auto=format&fit=crop&w=800&q=80" alt="Register Visual" style={{ maxWidth: '80%', borderRadius: 'var(--radius-xl)', boxShadow: 'var(--shadow-xl)' }} />
                <div style={{ marginTop: '40px', textAlign: 'center', maxWidth: '80%' }}>
                    <h3 style={{ color: 'var(--c-navy)', fontSize: '2rem', fontWeight: 800, marginBottom: '16px' }}>Đồng hành cùng sức khỏe</h3>
                    <p style={{ color: 'var(--c-text)', fontSize: '1.1rem', lineHeight: 1.6 }}>Tạo tài khoản để dễ dàng quản lý sức khỏe của bạn và người thân trong gia đình một cách toàn diện.</p>
                </div>
            </div>
            
            <div style={{ flex: '1 1 500px', display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '40px 20px' }}>
                <div className="card" style={{ width: '100%', maxWidth: '480px', borderRadius: 'var(--radius-xl)', padding: '40px 32px', border: 'none', boxShadow: 'var(--shadow-lg)' }}>
                    <div style={{ textAlign: 'center', marginBottom: '32px' }}>
                        <div style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: '64px', height: '64px', borderRadius: '50%', backgroundColor: 'var(--c-secondary-light)', color: 'var(--c-secondary)', marginBottom: '16px' }}>
                            <ShieldPlus size={36} />
                        </div>
                        <h2 style={{ fontSize: '1.75rem', margin: 0, color: 'var(--c-navy)', fontWeight: 700 }}>Tạo tài khoản mới</h2>
                        <p style={{ color: 'var(--c-text-light)', marginTop: '8px', fontSize: '0.95rem' }}>Bắt đầu chăm sóc sức khỏe ngay hôm nay</p>
                    </div>
                    
                    <form onSubmit={handleSubmit}>
                        <div className="form-group" style={{ marginBottom: '20px' }}>
                            <label className="form-label" style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>Họ và tên</label>
                            <div style={{ position: 'relative' }}>
                                <div style={{ position: 'absolute', top: '50%', left: '14px', transform: 'translateY(-50%)', color: 'var(--c-text-light)' }}>
                                    <User size={20} />
                                </div>
                                <input 
                                    type="text" 
                                    className={`form-input ${fieldErrors.fullName ? 'is-invalid' : ''}`} 
                                    name="fullName" 
                                    value={formData.fullName} 
                                    onChange={handleChange} 
                                    required 
                                    placeholder="Nguyễn Văn A"
                                    style={{ paddingLeft: '44px' }}
                                />
                            </div>
                            {fieldErrors.fullName && <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '6px', fontWeight: 500 }}>{fieldErrors.fullName}</div>}
                        </div>
                        <div className="form-group" style={{ marginBottom: '20px' }}>
                            <label className="form-label" style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>Email</label>
                            <div style={{ position: 'relative' }}>
                                <div style={{ position: 'absolute', top: '50%', left: '14px', transform: 'translateY(-50%)', color: 'var(--c-text-light)' }}>
                                    <Mail size={20} />
                                </div>
                                <input 
                                    type="email" 
                                    className={`form-input ${fieldErrors.email ? 'is-invalid' : ''}`} 
                                    name="email" 
                                    value={formData.email} 
                                    onChange={handleChange} 
                                    required 
                                    placeholder="email@example.com"
                                    style={{ paddingLeft: '44px' }}
                                />
                            </div>
                            {fieldErrors.email && <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '6px', fontWeight: 500 }}>{fieldErrors.email}</div>}
                        </div>
                        <div className="form-group" style={{ marginBottom: '20px' }}>
                            <label className="form-label" style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>Số điện thoại</label>
                            <div style={{ position: 'relative' }}>
                                <div style={{ position: 'absolute', top: '50%', left: '14px', transform: 'translateY(-50%)', color: 'var(--c-text-light)' }}>
                                    <Phone size={20} />
                                </div>
                                <input 
                                    type="text" 
                                    className={`form-input ${fieldErrors.phoneNumber ? 'is-invalid' : ''}`} 
                                    name="phoneNumber" 
                                    value={formData.phoneNumber} 
                                    onChange={handleChange} 
                                    required 
                                    placeholder="09xxxxxxxxx"
                                    style={{ paddingLeft: '44px' }}
                                />
                            </div>
                            {fieldErrors.phoneNumber && <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '6px', fontWeight: 500 }}>{fieldErrors.phoneNumber}</div>}
                        </div>
                        <div className="form-group" style={{ marginBottom: '28px' }}>
                            <label className="form-label" style={{ fontWeight: 600, color: 'var(--c-text-dark)' }}>Mật khẩu</label>
                            <div style={{ position: 'relative' }}>
                                <div style={{ position: 'absolute', top: '50%', left: '14px', transform: 'translateY(-50%)', color: 'var(--c-text-light)' }}>
                                    <Lock size={20} />
                                </div>
                                <input 
                                    type="password" 
                                    className={`form-input ${fieldErrors.password ? 'is-invalid' : ''}`} 
                                    name="password" 
                                    value={formData.password} 
                                    onChange={handleChange} 
                                    required 
                                    placeholder="Ít nhất 8 ký tự"
                                    style={{ paddingLeft: '44px' }}
                                />
                            </div>
                            <div style={{ fontSize: '0.85rem', color: 'var(--c-text-light)', marginTop: '8px' }}>Mật khẩu yêu cầu ít nhất 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.</div>
                            {fieldErrors.password && <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '6px', fontWeight: 500 }}>{fieldErrors.password}</div>}
                        </div>
                        <button type="submit" className="btn-secondary" disabled={loading} style={{ width: '100%', padding: '14px', fontSize: '1.05rem', borderRadius: 'var(--radius-md)', fontWeight: 600, display: 'flex', justifyContent: 'center' }}>
                            {loading ? 'Đang xử lý...' : 'Đăng ký tài khoản'}
                        </button>
                    </form>
                    <div style={{ marginTop: '28px', textAlign: 'center', fontSize: '0.95rem', color: 'var(--c-text-light)' }}>
                        Đã có tài khoản? <Link to="/login" style={{ color: 'var(--c-primary)', fontWeight: 600, marginLeft: '4px' }}>Đăng nhập ngay</Link>
                    </div>
                </div>
            </div>
        </div>
    );
};
