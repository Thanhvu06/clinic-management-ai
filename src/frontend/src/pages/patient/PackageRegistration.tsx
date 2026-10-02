import React, { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { 
    CheckCircle2, ArrowLeft, ArrowRight, AlertCircle 
} from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { parseIncludedServices, formatVndCurrency } from '../../utils/formatters';
import { FormField, TextInput, Textarea, Button, FormError } from '../../components/forms';

interface HealthPackage {
    id: number;
    code: string;
    name: string;
    targetAudience: string;
    description: string;
    price: number;
    includedServices?: string[];
    includedServicesJson?: string;
    isActive: boolean;
}

export const PackageRegistration: React.FC = () => {
    const { id } = useParams<{ id: string }>();

    const [pkg, setPkg] = useState<HealthPackage | null>(null);
    const [loadingPkg, setLoadingPkg] = useState(true);
    const [pkgError, setPkgError] = useState<string | null>(null);

    const [step, setStep] = useState<1 | 2 | 3>(1);

    // Form inputs
    const [preferredDate, setPreferredDate] = useState('');
    const [contactPhone, setContactPhone] = useState('');
    const [notes, setNotes] = useState('');
    const [agreedTerms, setAgreedTerms] = useState(false);

    // Errors & Submitting state
    const [fieldErrors, setFieldErrors] = useState<{ [key: string]: string }>({});
    const [submitError, setSubmitError] = useState<string | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);

    // Success registration result
    const [registrationResult, setRegistrationResult] = useState<any | null>(null);

    // Tomorrow's date string YYYY-MM-DD for min date picker
    const tomorrowStr = new Date(Date.now() + 86400000).toISOString().split('T')[0];

    useEffect(() => {
        const fetchPackage = async () => {
            if (!id) return;
            try {
                setLoadingPkg(true);
                const res = await axiosClient.get<any, any>(`/health-packages/${id}`);
                if (res.success && res.data) {
                    setPkg(res.data);
                } else {
                    setPkgError('Không tìm thấy thông tin gói khám.');
                }
            } catch (err: any) {
                setPkgError(err.response?.data?.message || 'Có lỗi xảy ra khi tải gói khám.');
            } finally {
                setLoadingPkg(false);
            }
        };

        fetchPackage();
    }, [id]);

    const validateStep2 = () => {
        const errors: { [key: string]: string } = {};
        if (!preferredDate) {
            errors.preferredDate = 'Vui lòng chọn ngày khám mong muốn.';
        } else if (preferredDate < tomorrowStr) {
            errors.preferredDate = 'Ngày khám mong muốn phải từ ngày mai trở đi.';
        }

        const phoneRegex = /^[0-9]{10,11}$/;
        if (!contactPhone.trim()) {
            errors.contactPhone = 'Vui lòng nhập số điện thoại liên hệ.';
        } else if (!phoneRegex.test(contactPhone.replace(/\s+/g, ''))) {
            errors.contactPhone = 'Số điện thoại không hợp lệ (cần 10-11 chữ số).';
        }

        setFieldErrors(errors);
        return Object.keys(errors).length === 0;
    };

    const handleNext = () => {
        if (step === 1) {
            setStep(2);
        } else if (step === 2) {
            if (validateStep2()) {
                setStep(3);
            }
        }
    };

    const handleBack = () => {
        if (step === 2) setStep(1);
        if (step === 3) setStep(2);
    };

    const handleSubmitRegistration = async () => {
        if (!pkg || isSubmitting) return;
        if (!agreedTerms) {
            setFieldErrors(prev => ({ ...prev, terms: 'Vui lòng xác nhận đồng ý với điều khoản đăng ký.' }));
            return;
        }

        try {
            setIsSubmitting(true);
            setSubmitError(null);

            const payload = {
                healthPackageId: pkg.id,
                preferredDate,
                contactPhone: contactPhone.trim(),
                note: notes.trim() ? notes.trim() : undefined
            };

            const res = await axiosClient.post<any, any>('/patient/health-package-registrations', payload);
            if (res.success && res.data) {
                setRegistrationResult(res.data);
            } else {
                setSubmitError(res.message || 'Đăng ký không thành công. Vui lòng thử lại.');
            }
        } catch (err: any) {
            setSubmitError(err.response?.data?.message || 'Có lỗi xảy ra trong quá trình xử lý đăng ký.');
        } finally {
            setIsSubmitting(false);
        }
    };

    if (loadingPkg) {
        return (
            <div style={{ maxWidth: '800px', margin: '40px auto', padding: '0 20px' }}>
                <div style={{ padding: '40px', background: '#fff', borderRadius: '12px', textAlign: 'center', border: '1px solid var(--c-border)' }}>
                    Đang tải thông tin gói khám...
                </div>
            </div>
        );
    }

    if (pkgError || !pkg) {
        return (
            <div style={{ maxWidth: '800px', margin: '40px auto', padding: '0 20px' }}>
                <div style={{ padding: '40px', background: '#fff', borderRadius: '12px', textAlign: 'center', border: '1px solid var(--c-border)' }}>
                    <AlertCircle size={48} color="#ef4444" style={{ marginBottom: '16px' }} />
                    <h2 style={{ fontSize: '1.25rem', color: 'var(--c-navy)', marginBottom: '8px' }}>{pkgError || 'Gói khám không tồn tại'}</h2>
                    <p style={{ color: 'var(--c-text-muted)', marginBottom: '20px' }}>Vui lòng chọn gói khám khác trong danh mục.</p>
                    <Link to="/health-packages" className="btn btn-primary" style={{ textDecoration: 'none' }}>
                        Xem danh sách gói khám
                    </Link>
                </div>
            </div>
        );
    }

    // Success Screen
    if (registrationResult) {
        return (
            <div style={{ maxWidth: '700px', margin: '40px auto', padding: '0 20px' }}>
                <div style={{ 
                    background: '#ffffff', border: '1px solid var(--c-border)', borderRadius: '16px', 
                    padding: '40px', textAlign: 'center', boxShadow: 'var(--shadow-md)' 
                }}>
                    <div style={{ 
                        width: '72px', height: '72px', borderRadius: '50%', background: '#ecfdf5', 
                        color: '#10b981', display: 'flex', alignItems: 'center', justifyContent: 'center', 
                        margin: '0 auto 20px auto' 
                    }}>
                        <CheckCircle2 size={44} />
                    </div>

                    <h1 style={{ fontSize: '1.6rem', fontWeight: 800, color: 'var(--c-navy)', marginBottom: '8px' }}>
                        Đăng ký gói khám thành công!
                    </h1>
                    <p style={{ color: 'var(--c-text-muted)', fontSize: '0.95rem', marginBottom: '24px' }}>
                        Yêu cầu đăng ký gói khám của bạn đã được tiếp nhận. Bộ phận lễ tân sẽ liên hệ xác nhận lịch thăm khám theo thông tin bên dưới.
                    </p>

                    <div style={{ 
                        background: '#f8fafc', border: '1px solid var(--c-border-light)', borderRadius: '12px', 
                        padding: '24px', textAlign: 'left', marginBottom: '28px' 
                    }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px', paddingBottom: '12px', borderBottom: '1px solid #e2e8f0' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Mã đăng ký:</span>
                            <strong style={{ color: 'var(--c-primary)', fontSize: '1.1rem', letterSpacing: '0.05em' }}>
                                {registrationResult.registrationCode}
                            </strong>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Gói khám:</span>
                            <span style={{ fontWeight: 600, color: 'var(--c-navy)' }}>{registrationResult.packageName}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Ngày khám mong muốn:</span>
                            <span style={{ fontWeight: 600 }}>{registrationResult.preferredDate}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Số điện thoại liên hệ:</span>
                            <span>{registrationResult.contactPhone}</span>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Chi phí trọn gói:</span>
                            <strong style={{ color: 'var(--c-primary)', fontSize: '1.05rem' }}>
                                {formatVndCurrency(registrationResult.packagePrice)}
                            </strong>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                            <span style={{ color: 'var(--c-text-muted)', fontSize: '0.9rem' }}>Trạng thái:</span>
                            <span style={{ 
                                background: '#fef3c7', color: '#92400e', padding: '3px 10px', 
                                borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600 
                            }}>
                                Chờ xác nhận
                            </span>
                        </div>
                    </div>

                    <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', flexWrap: 'wrap' }}>
                        <Link 
                            to="/patient/health-package-registrations" 
                            className="btn btn-primary"
                            style={{ textDecoration: 'none', padding: '12px 24px' }}
                        >
                            Quản lý gói khám đã đăng ký
                        </Link>
                        <Link 
                            to="/" 
                            className="btn btn-secondary"
                            style={{ textDecoration: 'none', padding: '12px 24px' }}
                        >
                            Về trang chủ
                        </Link>
                    </div>
                </div>
            </div>
        );
    }

    const services = (pkg.includedServices && pkg.includedServices.length > 0)
        ? pkg.includedServices
        : parseIncludedServices(pkg.includedServicesJson);

    return (
        <div style={{ maxWidth: '860px', margin: '32px auto 80px auto', padding: '0 20px' }}>
            <div style={{ marginBottom: '24px' }}>
                <Link to={`/health-packages/${pkg.id}`} style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', color: 'var(--c-primary)', textDecoration: 'none', fontSize: '0.9rem', fontWeight: 500, marginBottom: '8px' }}>
                    <ArrowLeft size={16} /> Quay lại chi tiết gói khám
                </Link>
                <h1 style={{ fontSize: '1.85rem', fontWeight: 800, color: 'var(--c-navy)', margin: 0 }}>
                    Đăng ký Gói khám Sức khỏe
                </h1>
                <p style={{ color: 'var(--c-text-muted)', fontSize: '0.95rem', margin: '6px 0 0 0' }}>
                    Quy trình 3 bước đăng ký tiện lợi, giúp bạn tiết kiệm thời gian chờ đợi tại phòng khám.
                </p>
            </div>

            {/* Step Wizard Progress Bar */}
            <div style={{ 
                display: 'flex', justifyContent: 'space-between', alignItems: 'center', 
                background: '#ffffff', padding: '18px 24px', borderRadius: '12px', 
                border: '1px solid var(--c-border)', marginBottom: '24px', boxShadow: 'var(--shadow-sm)' 
            }}>
                {[
                    { num: 1, label: 'Gói khám & Quyền lợi' },
                    { num: 2, label: 'Thông tin & Ngày khám' },
                    { num: 3, label: 'Xác nhận đăng ký' }
                ].map((s) => (
                    <div 
                        key={s.num} 
                        style={{ 
                            display: 'flex', alignItems: 'center', gap: '10px',
                            opacity: step >= s.num ? 1 : 0.45,
                            fontWeight: step === s.num ? 700 : 500
                        }}
                    >
                        <div style={{ 
                            width: '32px', height: '32px', borderRadius: '50%', 
                            background: step >= s.num ? 'var(--c-primary)' : 'var(--c-border)',
                            color: '#ffffff', display: 'flex', alignItems: 'center', justifyContent: 'center',
                            fontSize: '0.9rem', fontWeight: 700 
                        }}>
                            {step > s.num ? <CheckCircle2 size={18} /> : s.num}
                        </div>
                        <span style={{ fontSize: '0.9rem', color: step === s.num ? 'var(--c-navy)' : 'var(--c-text)' }}>
                            {s.label}
                        </span>
                    </div>
                ))}
            </div>

            {submitError && (
                <div style={{ marginBottom: '20px' }}>
                    <FormError message={submitError} />
                </div>
            )}

            {/* Step 1: Confirm Package & Benefits */}
            {step === 1 && (
                <div style={{ background: '#ffffff', border: '1px solid var(--c-border)', borderRadius: '16px', padding: '32px', boxShadow: 'var(--shadow-sm)' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
                        <div>
                            <span style={{ background: 'var(--c-navy)', color: '#fff', fontSize: '0.75rem', fontWeight: 700, padding: '3px 8px', borderRadius: '4px' }}>
                                {pkg.code}
                            </span>
                            <h2 style={{ fontSize: '1.4rem', fontWeight: 700, color: 'var(--c-navy)', margin: '8px 0 4px 0' }}>
                                {pkg.name}
                            </h2>
                            <span style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)' }}>
                                Đối tượng phù hợp: {pkg.targetAudience}
                            </span>
                        </div>
                        <div style={{ textAlign: 'right' }}>
                            <span style={{ fontSize: '0.75rem', color: 'var(--c-text-muted)', display: 'block' }}>Chi phí trọn gói</span>
                            <span style={{ fontSize: '1.5rem', fontWeight: 800, color: 'var(--c-primary)' }}>
                                {formatVndCurrency(pkg.price)}
                            </span>
                        </div>
                    </div>

                    <p style={{ color: 'var(--c-text)', fontSize: '0.95rem', lineHeight: 1.6, marginBottom: '24px', paddingBottom: '20px', borderBottom: '1px solid var(--c-border-light)' }}>
                        {pkg.description}
                    </p>

                    <h3 style={{ fontSize: '1.1rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <CheckCircle2 size={20} color="var(--c-secondary)" /> Danh mục xét nghiệm & dịch vụ ({services.length})
                    </h3>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '10px', marginBottom: '32px' }}>
                        {services.map((srv, idx) => (
                            <div key={idx} style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', fontSize: '0.875rem', color: 'var(--c-text)' }}>
                                <span style={{ color: 'var(--c-primary)', fontWeight: 700 }}>•</span>
                                <span>{srv}</span>
                            </div>
                        ))}
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <Button variant="primary" size="lg" onClick={handleNext}>
                            Tiếp tục bước 2 <ArrowRight size={18} />
                        </Button>
                    </div>
                </div>
            )}

            {/* Step 2: Date & Contact Info */}
            {step === 2 && (
                <div style={{ background: '#ffffff', border: '1px solid var(--c-border)', borderRadius: '16px', padding: '32px', boxShadow: 'var(--shadow-sm)' }}>
                    <h2 style={{ fontSize: '1.3rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '20px' }}>
                        Thông tin liên hệ & Thời gian mong muốn
                    </h2>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                        <FormField 
                            id="preferredDate" 
                            label="Ngày khám mong muốn" 
                            required 
                            error={fieldErrors.preferredDate}
                            helpText="Quý khách vui lòng chọn ngày khám cách thời điểm đăng ký ít nhất 1 ngày để phòng khám chuẩn bị chu đáo."
                        >
                            <TextInput
                                id="preferredDate"
                                type="date"
                                min={tomorrowStr}
                                value={preferredDate}
                                onChange={(e) => {
                                    setPreferredDate(e.target.value);
                                    setFieldErrors(prev => ({ ...prev, preferredDate: '' }));
                                }}
                                hasError={!!fieldErrors.preferredDate}
                            />
                        </FormField>

                        <FormField 
                            id="contactPhone" 
                            label="Số điện thoại liên hệ" 
                            required 
                            error={fieldErrors.contactPhone}
                            helpText="Số điện thoại dùng để nhân viên lễ tân gọi điện xác nhận và gửi tin nhắn hướng dẫn."
                        >
                            <TextInput
                                id="contactPhone"
                                type="tel"
                                placeholder="0901234567"
                                value={contactPhone}
                                onChange={(e) => {
                                    setContactPhone(e.target.value);
                                    setFieldErrors(prev => ({ ...prev, contactPhone: '' }));
                                }}
                                hasError={!!fieldErrors.contactPhone}
                            />
                        </FormField>

                        <FormField 
                            id="notes" 
                            label="Ghi chú hoặc yêu cầu đặc biệt (Không bắt buộc)"
                            helpText="Ví dụ: Tiền sử dị ứng thuốc, cần xuất hóa đơn công ty, người lớn tuổi cần hỗ trợ di chuyển..."
                        >
                            <Textarea
                                id="notes"
                                placeholder="Nhập ghi chú thêm nếu có..."
                                value={notes}
                                onChange={(e) => setNotes(e.target.value)}
                                rows={3}
                            />
                        </FormField>
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: '32px' }}>
                        <Button variant="secondary" size="md" onClick={handleBack}>
                            <ArrowLeft size={16} /> Quay lại
                        </Button>
                        <Button variant="primary" size="lg" onClick={handleNext}>
                            Xem lại thông tin <ArrowRight size={18} />
                        </Button>
                    </div>
                </div>
            )}

            {/* Step 3: Review & Submit */}
            {step === 3 && (
                <div style={{ background: '#ffffff', border: '1px solid var(--c-border)', borderRadius: '16px', padding: '32px', boxShadow: 'var(--shadow-sm)' }}>
                    <h2 style={{ fontSize: '1.3rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '20px' }}>
                        Xác nhận thông tin đăng ký
                    </h2>

                    <div style={{ background: '#f8fafc', border: '1px solid var(--c-border-light)', borderRadius: '12px', padding: '24px', marginBottom: '24px' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px', paddingBottom: '12px', borderBottom: '1px solid #e2e8f0' }}>
                            <span style={{ color: 'var(--c-text-muted)' }}>Gói khám lựa chọn:</span>
                            <strong style={{ color: 'var(--c-navy)', fontSize: '1.05rem' }}>{pkg.name}</strong>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                            <span style={{ color: 'var(--c-text-muted)' }}>Ngày khám mong muốn:</span>
                            <strong style={{ color: 'var(--c-primary)' }}>{preferredDate}</strong>
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                            <span style={{ color: 'var(--c-text-muted)' }}>Số điện thoại liên hệ:</span>
                            <span>{contactPhone}</span>
                        </div>
                        {notes && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '12px' }}>
                                <span style={{ color: 'var(--c-text-muted)' }}>Ghi chú:</span>
                                <span>{notes}</span>
                            </div>
                        )}
                        <div style={{ display: 'flex', justifyContent: 'space-between', paddingTop: '12px', borderTop: '1px solid #e2e8f0' }}>
                            <span style={{ color: 'var(--c-text-muted)' }}>Tổng chi phí gói:</span>
                            <span style={{ fontSize: '1.3rem', fontWeight: 800, color: 'var(--c-primary)' }}>
                                {formatVndCurrency(pkg.price)}
                            </span>
                        </div>
                    </div>

                    <div style={{ marginBottom: '24px' }}>
                        <label style={{ display: 'flex', alignItems: 'flex-start', gap: '10px', cursor: 'pointer', fontSize: '0.9rem', color: 'var(--c-text)' }}>
                            <input
                                type="checkbox"
                                checked={agreedTerms}
                                onChange={(e) => {
                                    setAgreedTerms(e.target.checked);
                                    if (e.target.checked) setFieldErrors(prev => ({ ...prev, terms: '' }));
                                }}
                                style={{ marginTop: '3px', width: '16px', height: '16px' }}
                            />
                            <span>
                                Tôi xác nhận thông tin đăng ký trên là chính xác và đồng ý với hướng dẫn chuẩn bị (nhịn ăn xét nghiệm) của phòng khám.
                            </span>
                        </label>
                        {fieldErrors.terms && (
                            <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '6px' }}>
                                {fieldErrors.terms}
                            </div>
                        )}
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                        <Button variant="secondary" size="md" onClick={handleBack} disabled={isSubmitting}>
                            <ArrowLeft size={16} /> Quay lại
                        </Button>
                        <Button 
                            variant="primary" 
                            size="lg" 
                            onClick={handleSubmitRegistration} 
                            loading={isSubmitting}
                            disabled={!agreedTerms || isSubmitting}
                        >
                            Xác nhận đăng ký gói khám
                        </Button>
                    </div>
                </div>
            )}
        </div>
    );
};
