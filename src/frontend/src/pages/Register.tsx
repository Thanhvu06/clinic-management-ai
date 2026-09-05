import React, { useState } from 'react';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { User, Mail, Phone, ArrowRight } from 'lucide-react';
import { useDialog } from '../contexts/DialogContext';
import { AuthShell, FormField, TextInput, PasswordInput, Button, FormError } from '../components/forms';

export const Register: React.FC = () => {
    const { showAlert, showToast } = useDialog();
    const navigate = useNavigate();
    const location = useLocation();

    // Parse returnUrl if user was trying to access a protected page
    const searchParams = new URLSearchParams(location.search);
    const returnUrl = searchParams.get('returnUrl');

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
                const targetLogin = returnUrl ? `/login?returnUrl=${encodeURIComponent(returnUrl)}` : '/login';
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
                setGeneralError(err?.response?.data?.message || err?.message || 'Đăng ký thất bại. Vui lòng kiểm tra lại thông tin.');
            }
        } finally {
            setLoading(false);
        }
    };

    const loginUrl = returnUrl ? `/login?returnUrl=${encodeURIComponent(returnUrl)}` : '/login';

    return (
        <AuthShell
            title="Tạo tài khoản mới"
            subtitle="Đăng ký tài khoản để khám chữa bệnh và quản lý hồ sơ gia đình thuận tiện"
            heroTitle="Đồng hành cùng sức khỏe gia đình bạn"
            heroDescription="Hệ thống quản lý phòng khám thông minh ClinicCare giúp bạn tiếp cận dịch vụ y tế chuẩn mực, minh bạch chi phí và an tâm điều trị."
            heroImageUrl="https://images.unsplash.com/photo-1551076805-e18690c5e561?auto=format&fit=crop&w=800&q=80"
            footerLink={
                <span>
                    Đã có tài khoản?{' '}
                    <Link to={loginUrl} style={{ color: 'var(--c-primary)', fontWeight: 600 }}>
                        Đăng nhập ngay &rarr;
                    </Link>
                </span>
            }
        >
            {generalError && (
                <div style={{ marginBottom: '20px' }}>
                    <FormError message={generalError} />
                </div>
            )}

            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '18px' }}>
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

                <FormField id="email" label="Địa chỉ Email" required error={fieldErrors.email}>
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
                    helpText="Mật khẩu tối thiểu 8 ký tự, nên bao gồm chữ hoa, chữ thường, số và ký tự đặc biệt."
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

                <div style={{ marginTop: '10px' }}>
                    <Button
                        type="submit"
                        variant="secondary"
                        size="lg"
                        fullWidth
                        loading={loading}
                        disabled={loading}
                    >
                        Đăng ký tài khoản <ArrowRight size={18} />
                    </Button>
                </div>
            </form>
        </AuthShell>
    );
};
