import { getApiErrorMessage } from '../utils/apiError';
import React, { useState } from 'react';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { useAuth } from '../auth/AuthContext';
import { Mail, ArrowRight } from 'lucide-react';
import { useDialog } from '../contexts/DialogContext';
import { ForgotPasswordModal } from '../components/ForgotPasswordModal';
import { AuthShell, FormField, TextInput, PasswordInput, Button, FormError } from '../components/forms';
import { sanitizeReturnUrl, getRedirectAfterLogin } from '../utils/roleRoutes';
import styles from './Login.module.css';

export const Login: React.FC = () => {
    const [emailOrPhone, setEmailOrPhone] = useState('');
    const [password, setPassword] = useState('');
    const [loading, setLoading] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const [showForgotModal, setShowForgotModal] = useState(false);
    
    const { login } = useAuth();
    const navigate = useNavigate();
    const location = useLocation();
    const { showToast } = useDialog();

    // Parse returnUrl from query params
    const searchParams = new URLSearchParams(location.search);
    const returnUrl = searchParams.get('returnUrl');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);
        setErrorMessage(null);

        try {
            const res = await axiosClient.post<any, any>('/auth/login', { 
                emailOrPhone: emailOrPhone.trim(), 
                password 
            });

            if (res.success && res.data) {
                showToast('Đăng nhập thành công', 'success');
                login(res.data.accessToken, res.data.user);

                const targetPath = getRedirectAfterLogin(res.data.user?.role, returnUrl);
                navigate(targetPath, { replace: true });
            }
        } catch (err: any) {
            let errorMsg = 'Đăng nhập thất bại. Vui lòng kiểm tra lại email/SĐT và mật khẩu.';
            if (err === 'Network Error' || err?.message === 'Network Error') {
                errorMsg = 'Không thể kết nối đến máy chủ. Vui lòng thử lại sau.';
            } else {
                errorMsg = getApiErrorMessage(err, errorMsg);
            }
            setErrorMessage(errorMsg);
        } finally {
            setLoading(false);
        }
    };

    const safeReturnUrl = sanitizeReturnUrl(returnUrl);
    const registerUrl = safeReturnUrl ? `/register?returnUrl=${encodeURIComponent(safeReturnUrl)}` : '/register';

    return (
        <AuthShell
            title="Chào mừng trở lại"
            subtitle="Đăng nhập để đặt lịch khám, xem đơn thuốc và hồ sơ bệnh án"
            heroTitle="Chăm sóc sức khỏe thông minh và tin cậy"
            heroDescription="ClinicCare AI giúp bạn kết nối nhanh chóng với đội ngũ bác sĩ chuyên khoa, đặt hẹn dễ dàng và quản lý toàn diện lịch sử khám bệnh trực tuyến."
            heroImageUrl="https://images.unsplash.com/photo-1576091160399-112ba8d25d1d?auto=format&fit=crop&w=800&q=80"
            footerLink={
                <span>
                    Chưa có tài khoản?{' '}
                    <Link to={registerUrl} className={styles.icon1}>
                        Đăng ký tài khoản mới &rarr;
                    </Link>
                </span>
            }
        >
            {errorMessage && (
                <div className={styles.spacing1}>
                    <FormError message={errorMessage} />
                </div>
            )}

            <form onSubmit={handleSubmit}>
                <FormField id="emailOrPhone" label="Email hoặc Số điện thoại" required>
                    <TextInput
                        id="emailOrPhone"
                        type="text"
                        value={emailOrPhone}
                        onChange={e => setEmailOrPhone(e.target.value)}
                        placeholder="example@gmail.com hoặc 0901234567"
                        autoComplete="username"
                        required
                        icon={<Mail size={18} />}
                    />
                </FormField>

                <div className={styles.spacing2}>
                    <div className={styles.flexLayout1}>
                        <label 
                            htmlFor="password" 
                            className={styles.label1}
                        >
                            Mật khẩu <span className={styles.text1}>*</span>
                        </label>
                        <button
                            type="button"
                            onClick={() => setShowForgotModal(true)}
                            className={styles.button1}
                        >
                            Quên mật khẩu?
                        </button>
                    </div>

                    <PasswordInput
                        id="password"
                        value={password}
                        onChange={e => setPassword(e.target.value)}
                        placeholder="••••••••"
                        autoComplete="current-password"
                        required
                    />
                </div>

                <div className={styles.spacing3}>
                    <Button
                        type="submit"
                        variant="primary"
                        size="lg"
                        fullWidth
                        loading={loading}
                        disabled={loading}
                    >
                        Đăng nhập vào hệ thống <ArrowRight size={18} />
                    </Button>
                </div>
            </form>

            <ForgotPasswordModal
                isOpen={showForgotModal}
                onClose={() => setShowForgotModal(false)}
            />
        </AuthShell>
    );
};
