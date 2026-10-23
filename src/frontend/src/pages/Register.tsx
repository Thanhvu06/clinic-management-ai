import { getApiErrorMessage } from '../utils/apiError';
import React, { useState } from 'react';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { User, Mail, Phone } from 'lucide-react';
import { useDialog } from '../contexts/DialogContext';
import { AuthShell, FormField, TextInput, PasswordInput, Button, FormError } from '../components/forms';
import { sanitizeReturnUrl } from '../utils/roleRoutes';
import styles from './Register.module.css';

export const Register: React.FC = () => {
    const { showAlert, showToast } = useDialog();
    const navigate = useNavigate();
    const location = useLocation();

    // Parse returnUrl if user was trying to access a protected page
    const searchParams = new URLSearchParams(location.search);
    const returnUrl = searchParams.get('returnUrl');
    const safeReturnUrl = sanitizeReturnUrl(returnUrl);

    const [formData, setFormData] = useState({
        fullName: '',
        email: '',
        phoneNumber: '',
        password: ''
    });

    const [fieldErrors, setFieldErrors] = useState<{ [key: string]: string }>({});
    const [generalError, setGeneralError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));
        if (fieldErrors[name]) {
            setFieldErrors(prev => ({ ...prev, [name]: '' }));
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFieldErrors({});
        setGeneralError(null);

        // Basic client validation
        const errors: { [key: string]: string } = {};
        if (!formData.fullName.trim()) errors.fullName = 'Vui lòng nhập họ và tên.';
        if (!formData.email.trim()) errors.email = 'Vui lòng nhập địa chỉ email.';
        if (!formData.phoneNumber.trim()) errors.phoneNumber = 'Vui lòng nhập số điện thoại.';
        if (!formData.password || formData.password.length < 8) {
            errors.password = 'Mật khẩu phải có ít nhất 8 ký tự.';
        }

        if (Object.keys(errors).length > 0) {
            setFieldErrors(errors);
            return;
        }

        setLoading(true);

        try {
            const res = await axiosClient.post<any, any>('/auth/register', formData);
            if (res.success) {
                showToast('Tài khoản đã được tạo thành công!', 'success');
                showAlert('Tài khoản đã được tạo thành công. Vui lòng đăng nhập để bắt đầu sử dụng dịch vụ.', 'Đăng ký thành công', 'success');
                const targetLogin = safeReturnUrl ? `/login?returnUrl=${encodeURIComponent(safeReturnUrl)}` : '/login';
                navigate(targetLogin);
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
                setGeneralError(getApiErrorMessage(err, 'Đăng ký thất bại. Vui lòng kiểm tra lại thông tin.'));
            }
        } finally {
            setLoading(false);
        }
    };

    const loginUrl = safeReturnUrl ? `/login?returnUrl=${encodeURIComponent(safeReturnUrl)}` : '/login';

    return (
        <AuthShell
            title="Tạo tài khoản"
            subtitle="Tài khoản dùng để đặt lịch, xem đơn thuốc và kết quả khám."
            footerLink={
                <span>
                    Đã có tài khoản?{' '}
                    <Link to={loginUrl} className={styles.icon1}>Đăng nhập</Link>
                </span>
            }
        >
            {generalError && (
                <div className={styles.spacing1}>
                    <FormError message={generalError} />
                </div>
            )}

            <form onSubmit={handleSubmit} className={styles.stackLayout1}>
                <FormField id="fullName" label="Họ và tên" required error={fieldErrors.fullName}>
                    <TextInput
                        id="fullName"
                        name="fullName"
                        type="text"
                        placeholder="Nguyễn Văn A"
                        value={formData.fullName}
                        onChange={handleChange}
                        required
                        hasError={!!fieldErrors.fullName}
                        icon={<User size={18} />}
                    />
                </FormField>

                <FormField id="email" label="Địa chỉ email" required error={fieldErrors.email}>
                    <TextInput
                        id="email"
                        name="email"
                        type="email"
                        placeholder="name@example.com"
                        value={formData.email}
                        onChange={handleChange}
                        required
                        hasError={!!fieldErrors.email}
                        icon={<Mail size={18} />}
                    />
                </FormField>

                <FormField id="phoneNumber" label="Số điện thoại" required error={fieldErrors.phoneNumber}>
                    <TextInput
                        id="phoneNumber"
                        name="phoneNumber"
                        type="tel"
                        placeholder="0901234567"
                        value={formData.phoneNumber}
                        onChange={handleChange}
                        required
                        hasError={!!fieldErrors.phoneNumber}
                        icon={<Phone size={18} />}
                    />
                </FormField>

                <FormField 
                    id="password" 
                    label="Mật khẩu" 
                    required 
                    error={fieldErrors.password}
                    helpText="Tối thiểu 8 ký tự, nên có chữ hoa, chữ thường, số và ký tự đặc biệt."
                >
                    <PasswordInput
                        id="password"
                        name="password"
                        placeholder="Ít nhất 8 ký tự"
                        value={formData.password}
                        onChange={handleChange}
                        required
                        hasError={!!fieldErrors.password}
                    />
                </FormField>

                <div className={styles.spacing2}>
                    <Button
                        type="submit"
                        variant="primary"
                        size="lg"
                        fullWidth
                        disabled={loading}
                    >
                        {loading ? 'Đang tạo tài khoản...' : 'Đăng ký'}
                    </Button>
                </div>
            </form>
        </AuthShell>
    );
};
