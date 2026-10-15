import React, { useState } from 'react';
import { Mail, Lock, KeyRound, X, CheckCircle, AlertCircle } from 'lucide-react';
import axiosClient from '../api/axiosClient';
import type { ApiResponse, ForgotPasswordRequest, ForgotPasswordResponse, ResetPasswordRequest } from '../types';
import styles from './ForgotPasswordModal.module.css';

interface ForgotPasswordModalProps {
    isOpen: boolean;
    onClose: () => void;
}

function getErrorMessage(err: unknown, fallback: string): string {
    if (typeof err === 'object' && err !== null) {
        if ('message' in err && typeof (err as { message: unknown }).message === 'string') {
            return (err as { message: string }).message;
        }
    }
    if (err instanceof Error) {
        return err.message;
    }
    return fallback;
}

export const ForgotPasswordModal: React.FC<ForgotPasswordModalProps> = ({ isOpen, onClose }) => {
    const [step, setStep] = useState<'request' | 'reset'>('request');
    const [email, setEmail] = useState('');
    const [token, setToken] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [loading, setLoading] = useState(false);
    const [errorMsg, setErrorMsg] = useState('');
    const [successMsg, setSuccessMsg] = useState('');

    if (!isOpen) return null;

    const handleRequestToken = async (e: React.FormEvent) => {
        e.preventDefault();
        setErrorMsg('');
        setSuccessMsg('');

        const trimmedEmail = email.trim();
        if (!trimmedEmail) {
            setErrorMsg('Vui lòng nhập địa chỉ email.');
            return;
        }

        setLoading(true);
        try {
            const res = await axiosClient.post<ForgotPasswordRequest, ApiResponse<ForgotPasswordResponse>>('/auth/forgot-password', {
                email: trimmedEmail
            });

            const resetToken = res.data?.resetToken;
            if (res.success && typeof resetToken === 'string' && resetToken.trim().length > 0) {
                setToken(resetToken.trim());
                setSuccessMsg(res.message || 'Mã xác thực đặt lại mật khẩu đã được tạo thành công.');
                setStep('reset');
            } else if (res.success) {
                setSuccessMsg(res.message || 'Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.');
            } else {
                setErrorMsg(res.message || 'Không thể gửi yêu cầu đặt lại mật khẩu.');
            }
        } catch (err: unknown) {
            setErrorMsg(getErrorMessage(err, 'Đã có lỗi xảy ra. Vui lòng kiểm tra lại email.'));
        } finally {
            setLoading(false);
        }
    };

    const handleResetPassword = async (e: React.FormEvent) => {
        e.preventDefault();
        setErrorMsg('');
        setSuccessMsg('');

        const trimmedToken = (token ?? '').trim();
        if (!trimmedToken) {
            setErrorMsg('Vui lòng nhập mã token đặt lại mật khẩu.');
            return;
        }

        if (!newPassword || newPassword.length < 8) {
            setErrorMsg('Mật khẩu mới phải có ít nhất 8 ký tự.');
            return;
        }

        if (newPassword !== confirmPassword) {
            setErrorMsg('Mật khẩu xác nhận không khớp.');
            return;
        }

        setLoading(true);
        try {
            const res = await axiosClient.post<ResetPasswordRequest, ApiResponse<void>>('/auth/reset-password', {
                email: email.trim(),
                token: trimmedToken,
                newPassword
            });

            if (res.success) {
                setSuccessMsg(res.message || 'Đặt lại mật khẩu thành công! Bạn có thể đóng cửa sổ và đăng nhập ngay.');
                setTimeout(() => {
                    handleClose();
                }, 2000);
            } else {
                setErrorMsg(res.message || 'Đặt lại mật khẩu thất bại.');
            }
        } catch (err: unknown) {
            setErrorMsg(getErrorMessage(err, 'Đặt lại mật khẩu thất bại. Token có thể đã hết hạn.'));
        } finally {
            setLoading(false);
        }
    };

    const handleClose = () => {
        setStep('request');
        setEmail('');
        setToken('');
        setNewPassword('');
        setConfirmPassword('');
        setErrorMsg('');
        setSuccessMsg('');
        onClose();
    };

    return (
        <div className={styles.overlay1}>
            <div className={styles.stackLayout1}>
                <div className={styles.flexLayout1}>
                    <div className={styles.flexLayout2}>
                        <div className={styles.flexLayout3}>
                            <KeyRound size={20} />
                        </div>
                        <div>
                            <h3 className={styles.heading1}>
                                {step === 'request' ? 'Quên mật khẩu' : 'Đặt lại mật khẩu'}
                            </h3>
                            <p className={styles.paragraph1}>
                                {step === 'request' ? 'Nhập email để nhận mã khôi phục' : 'Thiết lập mật khẩu mới cho tài khoản'}
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={handleClose}
                        className={styles.flexLayout4}
                    >
                        <X size={20} />
                    </button>
                </div>

                <div className={styles.spacing1}>
                    {errorMsg && (
                        <div className={styles.flexLayout5}>
                            <AlertCircle size={16} />
                            <span>{errorMsg}</span>
                        </div>
                    )}

                    {successMsg && (
                        <div className={styles.flexLayout6}>
                            <CheckCircle size={16} />
                            <span>{successMsg}</span>
                        </div>
                    )}

                    {step === 'request' ? (
                        <form onSubmit={handleRequestToken}>
                            <div className={styles.spacing2}>
                                <label htmlFor="forgot-email" className={styles.label1}>
                                    Địa chỉ Email tài khoản
                                </label>
                                <div className={styles.spacing3}>
                                    <Mail size={18} className={styles.icon1} />
                                    <input
                                        id="forgot-email"
                                        type="email"
                                        required
                                        value={email}
                                        onChange={(e) => setEmail(e.target.value)}
                                        placeholder="vidu@cliniccare.vn"
                                        className={styles.control1}
                                    />
                                </div>
                            </div>
                            <button
                                type="submit"
                                disabled={loading}
                                className={[styles.button1, (loading ? styles.state1 : styles.state2)].join(' ')}
                            >
                                {loading ? 'Đang gửi...' : 'Gửi mã đặt lại mật khẩu'}
                            </button>
                        </form>
                    ) : (
                        <form onSubmit={handleResetPassword}>
                            <div className={styles.spacing4}>
                                <label htmlFor="forgot-token" className={styles.label1}>
                                    Mã xác thực (Token)
                                </label>
                                <input
                                    id="forgot-token"
                                    type="text"
                                    required
                                    value={token}
                                    onChange={(e) => setToken(e.target.value)}
                                    placeholder="Dán mã token vào đây"
                                    className={styles.control2}
                                />
                            </div>

                            <div className={styles.spacing4}>
                                <label htmlFor="forgot-newPassword" className={styles.label1}>
                                    Mật khẩu mới (tối thiểu 8 ký tự)
                                </label>
                                <div className={styles.spacing3}>
                                    <Lock size={18} className={styles.icon1} />
                                    <input
                                        id="forgot-newPassword"
                                        type="password"
                                        required
                                        minLength={8}
                                        value={newPassword}
                                        onChange={(e) => setNewPassword(e.target.value)}
                                        placeholder="••••••••"
                                        className={styles.control1}
                                    />
                                </div>
                            </div>

                            <div className={styles.spacing5}>
                                <label htmlFor="forgot-confirmPassword" className={styles.label1}>
                                    Xác nhận mật khẩu mới
                                </label>
                                <div className={styles.spacing3}>
                                    <Lock size={18} className={styles.icon1} />
                                    <input
                                        id="forgot-confirmPassword"
                                        type="password"
                                        required
                                        minLength={8}
                                        value={confirmPassword}
                                        onChange={(e) => setConfirmPassword(e.target.value)}
                                        placeholder="••••••••"
                                        className={styles.control1}
                                    />
                                </div>
                            </div>

                            <div className={styles.flexLayout7}>
                                <button
                                    type="button"
                                    onClick={() => setStep('request')}
                                    className={styles.button2}
                                >
                                    Quay lại
                                </button>
                                <button
                                    type="submit"
                                    disabled={loading}
                                    className={[styles.button3, (loading ? styles.state1 : styles.state2)].join(' ')}
                                >
                                    {loading ? 'Đang xử lý...' : 'Đặt lại mật khẩu'}
                                </button>
                            </div>
                        </form>
                    )}
                </div>
            </div>
        </div>
    );
};
