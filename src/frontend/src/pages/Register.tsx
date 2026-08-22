import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import styles from './Auth.module.css';
import { ShieldPlus } from 'lucide-react';

export const Register: React.FC = () => {
    const [formData, setFormData] = useState({
        fullName: '',
        email: '',
        phoneNumber: '',
        password: ''
    });
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    
    const navigate = useNavigate();

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setFormData({ ...formData, [e.target.name]: e.target.value });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setLoading(true);

        try {
            const res = await axiosClient.post<any, any>('/auth/register', formData);
            if (res.success) {
                alert('Đăng ký thành công. Vui lòng đăng nhập.');
                navigate('/login');
            }
        } catch (err: any) {
            setError(err?.message || 'Đăng ký thất bại');
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
                    <h1>Bắt đầu chăm sóc sức khỏe ngay hôm nay</h1>
                    <p>Tạo tài khoản miễn phí để kết nối với đội ngũ bác sĩ hàng đầu.</p>
                </div>
            </div>
            
            <div className={styles.authRight}>
                <div className={styles.card}>
                    <h2>Tạo tài khoản mới</h2>
                    {error && <div className={styles.error}>{error}</div>}
                    <form onSubmit={handleSubmit}>
                        <div className={styles.formGroup}>
                            <label>Họ và tên</label>
                            <input type="text" className="form-input" name="fullName" value={formData.fullName} onChange={handleChange} required placeholder="Nguyễn Văn A" />
                        </div>
                        <div className={styles.formGroup}>
                            <label>Email</label>
                            <input type="email" className="form-input" name="email" value={formData.email} onChange={handleChange} required placeholder="email@example.com" />
                        </div>
                        <div className={styles.formGroup}>
                            <label>Số điện thoại</label>
                            <input type="text" className="form-input" name="phoneNumber" value={formData.phoneNumber} onChange={handleChange} required placeholder="09xxxxxxxxx" />
                        </div>
                        <div className={styles.formGroup}>
                            <label>Mật khẩu</label>
                            <input type="password" className="form-input" name="password" value={formData.password} onChange={handleChange} required minLength={8} placeholder="Ít nhất 8 ký tự" />
                        </div>
                        <button type="submit" className={`btn-primary ${styles.submitBtn}`} disabled={loading}>
                            {loading ? 'Đang xử lý...' : 'Đăng ký'}
                        </button>
                    </form>
                    <div className={styles.links}>
                        Đã có tài khoản? <Link to="/login">Đăng nhập ngay</Link>
                    </div>
                </div>
            </div>
        </div>
    );
};
