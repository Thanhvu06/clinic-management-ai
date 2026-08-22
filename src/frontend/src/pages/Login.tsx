import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { useAuth } from '../auth/AuthContext';
import styles from './Auth.module.css';
import { ShieldPlus } from 'lucide-react';

export const Login: React.FC = () => {
    const [emailOrPhone, setEmailOrPhone] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    
    const { login } = useAuth();
    const navigate = useNavigate();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setLoading(true);

        try {
            const res = await axiosClient.post<any, any>('/auth/login', { emailOrPhone, password });
            if (res.success && res.data) {
                login(res.data.accessToken, res.data.user);
                switch (res.data.user.role) {
                    case 'Admin': navigate('/admin'); break;
                    case 'Doctor': navigate('/doctor'); break;
                    case 'Receptionist': navigate('/reception'); break;
                    default: navigate('/patient'); break;
                }
            }
        } catch (err: any) {
            setError(err?.message || 'Đăng nhập thất bại');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className={styles.authWrapper}>
            <div className={styles.authLeft}>
                <div className={styles.authBrand}>
                    <ShieldPlus size={32} className={styles.authBrandIcon} />
                    ClinicCare AI
                </div>
                <div className={styles.authLeftContent}>
                    <h1>Chăm sóc sức khỏe thông minh</h1>
                    <p>Trải nghiệm dịch vụ y tế hiện đại, đặt lịch khám nhanh chóng và quản lý hồ sơ cá nhân an toàn.</p>
                </div>
            </div>
            
            <div className={styles.authRight}>
                <div className={styles.card}>
                    <h2>Đăng nhập</h2>
                    {error && <div className={styles.error}>{error}</div>}
                    <form onSubmit={handleSubmit}>
                        <div className={styles.formGroup}>
                            <label>Email hoặc Số điện thoại</label>
                            <input 
                                type="text" 
                                className="form-input"
                                value={emailOrPhone} 
                                onChange={e => setEmailOrPhone(e.target.value)} 
                                required 
                                placeholder="Nhập email hoặc SĐT"
                            />
                        </div>
                        <div className={styles.formGroup}>
                            <label>Mật khẩu</label>
                            <input 
                                type="password" 
                                className="form-input"
                                value={password} 
                                onChange={e => setPassword(e.target.value)} 
                                required 
                                placeholder="••••••••"
                            />
                        </div>
                        <button type="submit" className={`btn-primary ${styles.submitBtn}`} disabled={loading}>
                            {loading ? 'Đang xử lý...' : 'Đăng nhập vào hệ thống'}
                        </button>
                    </form>
                    <div className={styles.links}>
                        Chưa có tài khoản? <Link to="/register">Đăng ký bệnh nhân mới</Link>
                    </div>
                </div>
            </div>
        </div>
    );
};
