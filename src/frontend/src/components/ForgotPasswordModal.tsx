import React, { useState } from 'react';
import { Mail, Lock, KeyRound, X, CheckCircle, AlertCircle } from 'lucide-react';
import axiosClient from '../api/axiosClient';
import type { ApiResponse } from '../types';

interface ForgotPasswordModalProps {
    isOpen: boolean;
    onClose: () => void;
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

        if (!email.trim()) {
            setErrorMsg('Vui lòng nhập địa chỉ email.');
            return;
        }

        setLoading(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<{ message: string; resetToken: string }>>('/auth/forgot-password', {
                email: email.trim()
            });

            if (res.success && res.data) {
                setToken(res.data.resetToken);
                setSuccessMsg('Mã xác thực đặt lại mật khẩu đã được tạo thành công.');
                setStep('reset');
            } else {
                setErrorMsg(res.message || 'Không thể gửi yêu cầu đặt lại mật khẩu.');
            }
        } catch (err: any) {
            setErrorMsg(err?.message || 'Đã có lỗi xảy ra. Vui lòng kiểm tra lại email.');
        } finally {
            setLoading(false);
        }
    };

    const handleResetPassword = async (e: React.FormEvent) => {
        e.preventDefault();
        setErrorMsg('');
        setSuccessMsg('');

        if (!token.trim()) {
            setErrorMsg('Vui lòng nhập mã token đặt lại mật khẩu.');
            return;
        }

        if (!newPassword || newPassword.length < 6) {
            setErrorMsg('Mật khẩu mới phải có ít nhất 6 ký tự.');
            return;
        }

        if (newPassword !== confirmPassword) {
            setErrorMsg('Mật khẩu xác nhận không khớp.');
            return;
        }

        setLoading(true);
        try {
            const res = await axiosClient.post<any, ApiResponse<boolean>>('/auth/reset-password', {
                email: email.trim(),
                token: token.trim(),
                newPassword
            });

            if (res.success) {
                setSuccessMsg('Đặt lại mật khẩu thành công! Bạn có thể đóng cửa sổ và đăng nhập ngay.');
                setTimeout(() => {
                    handleClose();
                }, 2000);
            } else {
                setErrorMsg(res.message || 'Đặt lại mật khẩu thất bại.');
            }
        } catch (err: any) {
            setErrorMsg(err?.message || 'Đặt lại mật khẩu thất bại. Token có thể đã hết hạn.');
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
        <div style={{
            position: 'fixed',
            inset: 0,
            backgroundColor: 'rgba(15, 23, 42, 0.65)',
            backdropFilter: 'blur(4px)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 9999,
            padding: '16px'
        }}>
            <div style={{
                backgroundColor: 'white',
                borderRadius: '16px',
                width: '100%',
                maxWidth: '480px',
                boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
                display: 'flex',
                flexDirection: 'column',
                overflow: 'hidden'
            }}>
                <div style={{
                    padding: '20px 24px',
                    borderBottom: '1px solid #e2e8f0',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    backgroundColor: '#f8fafc'
                }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <div style={{
                            width: '38px',
                            height: '38px',
                            borderRadius: '10px',
                            backgroundColor: '#e0f2fe',
                            color: '#0284c7',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center'
                        }}>
                            <KeyRound size={20} />
                        </div>
                        <div>
                            <h3 style={{ margin: 0, color: '#0f172a', fontSize: '1.2rem', fontWeight: 700 }}>
                                {step === 'request' ? 'Quên mật khẩu' : 'Đặt lại mật khẩu'}
                            </h3>
                            <p style={{ margin: '2px 0 0', color: '#64748b', fontSize: '0.825rem' }}>
                                {step === 'request' ? 'Nhập email để nhận mã khôi phục' : 'Thiết lập mật khẩu mới cho tài khoản'}
                            </p>
                        </div>
                    </div>
                    <button
                        onClick={handleClose}
                        style={{
                            background: 'none',
                            border: 'none',
                            color: '#64748b',
                            cursor: 'pointer',
                            padding: '6px',
                            borderRadius: '8px',
                            display: 'flex',
                            alignItems: 'center'
                        }}
                    >
                        <X size={20} />
                    </button>
                </div>

                <div style={{ padding: '24px' }}>
                    {errorMsg && (
                        <div style={{
                            padding: '10px 14px',
                            backgroundColor: '#fef2f2',
                            border: '1px solid #fecaca',
                            borderRadius: '8px',
                            color: '#991b1b',
                            fontSize: '0.85rem',
                            marginBottom: '16px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px'
                        }}>
                            <AlertCircle size={16} />
                            <span>{errorMsg}</span>
                        </div>
                    )}

                    {successMsg && (
                        <div style={{
                            padding: '10px 14px',
                            backgroundColor: '#f0fdf4',
                            border: '1px solid #bbf7d0',
                            borderRadius: '8px',
                            color: '#166534',
                            fontSize: '0.85rem',
                            marginBottom: '16px',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px'
                        }}>
                            <CheckCircle size={16} />
                            <span>{successMsg}</span>
                        </div>
                    )}

                    {step === 'request' ? (
                        <form onSubmit={handleRequestToken}>
                            <div style={{ marginBottom: '20px' }}>
                                <label htmlFor="forgot-email" style={{ display: 'block', marginBottom: '6px', fontSize: '0.9rem', fontWeight: 600, color: '#334155' }}>
                                    Địa chỉ Email tài khoản
                                </label>
                                <div style={{ position: 'relative' }}>
                                    <Mail size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: '#94a3b8' }} />
                                    <input
                                        id="forgot-email"
                                        type="email"
                                        required
                                        value={email}
                                        onChange={(e) => setEmail(e.target.value)}
                                        placeholder="vidu@cliniccare.vn"
                                        style={{
                                            width: '100%',
                                            padding: '10px 12px 10px 38px',
                                            borderRadius: '8px',
                                            border: '1.5px solid #cbd5e1',
                                            fontSize: '0.95rem',
                                            boxSizing: 'border-box'
                                        }}
                                    />
                                </div>
                            </div>
                            <button
                                type="submit"
                                disabled={loading}
                                style={{
                                    width: '100%',
                                    padding: '12px',
                                    backgroundColor: '#0284c7',
                                    color: 'white',
                                    border: 'none',
                                    borderRadius: '8px',
                                    fontWeight: 600,
                                    cursor: loading ? 'not-allowed' : 'pointer',
                                    fontSize: '0.95rem'
                                }}
                            >
                                {loading ? 'Đang gửi...' : 'Gửi mã đặt lại mật khẩu'}
                            </button>
                        </form>
                    ) : (
                        <form onSubmit={handleResetPassword}>
                            <div style={{ marginBottom: '16px' }}>
                                <label htmlFor="forgot-token" style={{ display: 'block', marginBottom: '6px', fontSize: '0.9rem', fontWeight: 600, color: '#334155' }}>
                                    Mã xác thực (Token)
                                </label>
                                <input
                                    id="forgot-token"
                                    type="text"
                                    required
                                    value={token}
                                    onChange={(e) => setToken(e.target.value)}
                                    placeholder="Dán mã token vào đây"
                                    style={{
                                        width: '100%',
                                        padding: '10px 12px',
                                        borderRadius: '8px',
                                        border: '1.5px solid #cbd5e1',
                                        fontSize: '0.95rem',
                                        boxSizing: 'border-box',
                                        fontFamily: 'monospace'
                                    }}
                                />
                            </div>

                            <div style={{ marginBottom: '16px' }}>
                                <label htmlFor="forgot-newPassword" style={{ display: 'block', marginBottom: '6px', fontSize: '0.9rem', fontWeight: 600, color: '#334155' }}>
                                    Mật khẩu mới (tối thiểu 6 ký tự)
                                </label>
                                <div style={{ position: 'relative' }}>
                                    <Lock size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: '#94a3b8' }} />
                                    <input
                                        id="forgot-newPassword"
                                        type="password"
                                        required
                                        minLength={6}
                                        value={newPassword}
                                        onChange={(e) => setNewPassword(e.target.value)}
                                        placeholder="••••••••"
                                        style={{
                                            width: '100%',
                                            padding: '10px 12px 10px 38px',
                                            borderRadius: '8px',
                                            border: '1.5px solid #cbd5e1',
                                            fontSize: '0.95rem',
                                            boxSizing: 'border-box'
                                        }}
                                    />
                                </div>
                            </div>

                            <div style={{ marginBottom: '22px' }}>
                                <label htmlFor="forgot-confirmPassword" style={{ display: 'block', marginBottom: '6px', fontSize: '0.9rem', fontWeight: 600, color: '#334155' }}>
                                    Xác nhận mật khẩu mới
                                </label>
                                <div style={{ position: 'relative' }}>
                                    <Lock size={18} style={{ position: 'absolute', left: '12px', top: '12px', color: '#94a3b8' }} />
                                    <input
                                        id="forgot-confirmPassword"
                                        type="password"
                                        required
                                        minLength={6}
                                        value={confirmPassword}
                                        onChange={(e) => setConfirmPassword(e.target.value)}
                                        placeholder="••••••••"
                                        style={{
                                            width: '100%',
                                            padding: '10px 12px 10px 38px',
                                            borderRadius: '8px',
                                            border: '1.5px solid #cbd5e1',
                                            fontSize: '0.95rem',
                                            boxSizing: 'border-box'
                                        }}
                                    />
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '10px' }}>
                                <button
                                    type="button"
                                    onClick={() => setStep('request')}
                                    style={{
                                        flex: 1,
                                        padding: '12px',
                                        backgroundColor: '#f1f5f9',
                                        color: '#475569',
                                        border: 'none',
                                        borderRadius: '8px',
                                        fontWeight: 600,
                                        cursor: 'pointer',
                                        fontSize: '0.95rem'
                                    }}
                                >
                                    Quay lại
                                </button>
                                <button
                                    type="submit"
                                    disabled={loading}
                                    style={{
                                        flex: 2,
                                        padding: '12px',
                                        backgroundColor: '#0284c7',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '8px',
                                        fontWeight: 600,
                                        cursor: loading ? 'not-allowed' : 'pointer',
                                        fontSize: '0.95rem'
                                    }}
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
