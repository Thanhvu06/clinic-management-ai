import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Checkbox, Form, Input, Result } from 'antd';
import { useParams, Link } from 'react-router-dom';
import { ArrowLeft, ArrowRight, Check, CheckCircle2 } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { parseIncludedServices, formatVndCurrency, formatDateOnly, toLocalDateString } from '../../utils/formatters';
import { PageHeader } from '../../components/common/PageHeader';
import { StatusBadge } from '../../components/common/StatusBadge';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';
import styles from './PackageRegistration.module.css';

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

interface RegistrationResult {
    registrationCode: string;
    healthPackageName?: string;
    packageName?: string;
    healthPackagePrice?: number;
    packagePrice?: number;
    preferredDate: string;
    contactPhone: string;
}

export const PackageRegistration: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const [pkg, setPkg] = useState<HealthPackage | null>(null);
    const [loadingPkg, setLoadingPkg] = useState(true);
    const [pkgError, setPkgError] = useState<string | null>(null);
    const [step, setStep] = useState<1 | 2 | 3>(1);
    const [preferredDate, setPreferredDate] = useState('');
    const [contactPhone, setContactPhone] = useState('');
    const [notes, setNotes] = useState('');
    const [agreedTerms, setAgreedTerms] = useState(false);
    const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
    const [submitError, setSubmitError] = useState<string | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const submitting = useRef(false);
    const [registrationResult, setRegistrationResult] = useState<RegistrationResult | null>(null);
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const tomorrowStr = toLocalDateString(tomorrow);

    const fetchPackage = useCallback(async () => {
        setLoadingPkg(true);
        setPkgError(null);
        try {
            if (!id) { setPkgError('Gói khám không tồn tại'); return; }
            const res = await axiosClient.get<unknown, ApiResponse<HealthPackage>>(`/health-packages/${id}`);
            if (res.success && res.data) setPkg(res.data);
            else setPkgError('Không tìm thấy thông tin gói khám.');
        } catch (err: any) {
            setPkgError(err?.message || 'Có lỗi xảy ra khi tải gói khám.');
        } finally { setLoadingPkg(false); }
    }, [id]);
    useEffect(() => { void fetchPackage(); }, [fetchPackage]);

    const validateStep2 = () => {
        const errors: Record<string, string> = {};
        if (!preferredDate) errors.preferredDate = 'Vui lòng chọn ngày khám mong muốn.';
        else if (preferredDate < tomorrowStr) errors.preferredDate = 'Ngày khám mong muốn phải từ ngày mai trở đi.';
        if (!contactPhone.trim()) errors.contactPhone = 'Vui lòng nhập số điện thoại liên hệ.';
        else if (!/^0[35789][0-9]{8}$/.test(contactPhone.replace(/\s+/g, ''))) errors.contactPhone = 'Số điện thoại không hợp lệ (10 chữ số, bắt đầu bằng 03/05/07/08/09).';
        setFieldErrors(errors);
        return Object.keys(errors).length === 0;
    };
    const handleNext = () => {
        if (step === 1) setStep(2);
        else if (step === 2 && validateStep2()) setStep(3);
    };
    const handleBack = () => {
        if (step === 2) setStep(1);
        if (step === 3) setStep(2);
    };
    const handleSubmitRegistration = async () => {
        if (!pkg || submitting.current) return;
        if (!agreedTerms) {
            setFieldErrors(prev => ({ ...prev, terms: 'Vui lòng xác nhận đồng ý với điều khoản đăng ký.' }));
            return;
        }
        submitting.current = true;
        setIsSubmitting(true);
        setSubmitError(null);
        try {
            const payload = { healthPackageId: pkg.id, preferredDate, contactPhone: contactPhone.replace(/\s+/g, ''), note: notes.trim() || undefined };
            const res = await axiosClient.post<unknown, ApiResponse<RegistrationResult>>('/patient/health-package-registrations', payload);
            if (res.success && res.data) setRegistrationResult(res.data);
            else setSubmitError(res.message || 'Đăng ký không thành công. Vui lòng thử lại.');
        } catch (err: any) {
            setSubmitError(err?.message || 'Có lỗi xảy ra trong quá trình xử lý đăng ký.');
        } finally {
            submitting.current = false;
            setIsSubmitting(false);
        }
    };

    if (loadingPkg) return <div className={styles.page}><LoadingState message="Đang tải thông tin gói khám..." /></div>;
    if (pkgError || !pkg) return <div className={styles.page}>
        <InlineError message={pkgError || 'Gói khám không tồn tại'} onRetry={fetchPackage} />
        <p>Vui lòng chọn gói khám khác trong danh mục.</p><Link to="/health-packages">Xem danh sách gói khám</Link>
    </div>;
    if (registrationResult) return <div className={styles.page}><Card>
        <Result status="success" title="Đăng ký gói khám thành công!" subTitle="Yêu cầu đăng ký gói khám của bạn đã được tiếp nhận. Bộ phận lễ tân sẽ liên hệ xác nhận lịch thăm khám theo thông tin bên dưới." />
        <div className={styles.summary}>
            <div className={styles.summaryLine}>Mã đăng ký: <strong>{registrationResult.registrationCode}</strong></div>
            <div className={styles.summaryLine}>Gói khám: <strong>{registrationResult.healthPackageName || registrationResult.packageName || pkg.name}</strong></div>
            <div className={styles.summaryLine}>Ngày khám mong muốn: <strong>{formatDateOnly(registrationResult.preferredDate)}</strong></div>
            <div className={styles.summaryLine}>Số điện thoại liên hệ: <strong>{registrationResult.contactPhone}</strong></div>
            <div className={styles.summaryLine}>Chi phí trọn gói: <strong>{formatVndCurrency(registrationResult.healthPackagePrice ?? registrationResult.packagePrice ?? pkg.price)}</strong></div>
            <div className={styles.summaryLine}>Trạng thái: <StatusBadge status="Pending" label="Chờ xác nhận" /></div>
        </div>
        <div className={styles.successActions}><Link to="/patient/health-package-registrations">Quản lý gói khám đã đăng ký</Link><Link to="/">Về trang chủ</Link></div>
    </Card></div>;

    const services = pkg.includedServices?.length ? pkg.includedServices : parseIncludedServices(pkg.includedServicesJson);
    return <div className={styles.page}>
        <Link className={styles.backLink} to={`/health-packages/${pkg.id}`}><ArrowLeft size={16} /> Quay lại chi tiết gói khám</Link>
        <PageHeader title="Đăng ký gói khám" subtitle="Quy trình 3 bước đăng ký tiện lợi, giúp bạn tiết kiệm thời gian chờ đợi tại phòng khám." />
        <ol className={styles.steps} aria-label="Các bước đăng ký">
            {['Gói khám & Quyền lợi', 'Thông tin & Ngày khám', 'Xác nhận đăng ký'].map((label, index) => <li key={label} className={step >= index + 1 ? styles.reachedStep : styles.upcomingStep} aria-current={step === index + 1 ? 'step' : undefined}><span className={styles.stepNumber}>{step > index + 1 ? <Check size={18} /> : index + 1}</span><span>{label}</span></li>)}
        </ol>
        {submitError && <Alert className={styles.submitError} type="error" showIcon title={submitError} />}
        {step === 1 && <Card>
            <div className={styles.packageHeader}>
                <div><span className={styles.packageCode}>{pkg.code}</span><h2 className={styles.packageName}>{pkg.name}</h2><span>Đối tượng phù hợp: {pkg.targetAudience}</span></div>
                <div className={styles.packagePrice}><span>Chi phí trọn gói</span><strong>{formatVndCurrency(pkg.price)}</strong></div>
            </div>
            <p className={styles.description}>{pkg.description}</p>
            <h3 className={styles.servicesTitle}><CheckCircle2 size={20} /> Danh mục xét nghiệm & dịch vụ ({services.length})</h3>
            <div className={styles.services}>{services.map((service, index) => <div className={styles.service} key={`${service}-${index}`}><Check size={16} /><span>{service}</span></div>)}</div>
            <div className={styles.nextAction}><Button type="primary" size="large" onClick={handleNext}>Tiếp tục bước 2 <ArrowRight size={18} /></Button></div>
        </Card>}
        {step === 2 && <Card>
            <h2 className={styles.stepTitle}>Thông tin liên hệ & Thời gian mong muốn</h2>
            <Form layout="vertical">
                <Form.Item label="Ngày khám mong muốn" htmlFor="preferredDate" required validateStatus={fieldErrors.preferredDate ? 'error' : undefined} help={fieldErrors.preferredDate || undefined} extra="Quý khách vui lòng chọn ngày khám cách thời điểm đăng ký ít nhất 1 ngày để phòng khám chuẩn bị chu đáo.">
                    <Input type="date" id="preferredDate" min={tomorrowStr} value={preferredDate} onChange={event => { setPreferredDate(event.target.value); setFieldErrors(prev => ({ ...prev, preferredDate: '' })); }} />
                </Form.Item>
                <Form.Item label="Số điện thoại liên hệ" htmlFor="contactPhone" required validateStatus={fieldErrors.contactPhone ? 'error' : undefined} help={fieldErrors.contactPhone || undefined} extra="Số điện thoại dùng để nhân viên lễ tân gọi điện xác nhận và gửi tin nhắn hướng dẫn.">
                    <Input type="tel" id="contactPhone" placeholder="0901234567" value={contactPhone} onChange={event => { setContactPhone(event.target.value); setFieldErrors(prev => ({ ...prev, contactPhone: '' })); }} />
                </Form.Item>
                <Form.Item label="Ghi chú hoặc yêu cầu đặc biệt (Không bắt buộc)" htmlFor="notes" extra="Ví dụ: Tiền sử dị ứng thuốc, cần xuất hóa đơn công ty, người lớn tuổi cần hỗ trợ di chuyển...">
                    <Input.TextArea id="notes" placeholder="Nhập ghi chú thêm nếu có..." value={notes} onChange={event => setNotes(event.target.value)} rows={3} />
                </Form.Item>
            </Form>
            <div className={styles.actions}><Button onClick={handleBack}><ArrowLeft size={16} /> Quay lại</Button><Button type="primary" size="large" onClick={handleNext}>Xem lại thông tin <ArrowRight size={18} /></Button></div>
        </Card>}
        {step === 3 && <Card>
            <h2 className={styles.stepTitle}>Xác nhận thông tin đăng ký</h2>
            <div className={styles.summary}>
                <div className={styles.summaryLine}>Gói khám lựa chọn: <strong>{pkg.name}</strong></div>
                <div className={styles.summaryLine}>Ngày khám mong muốn: <strong>{formatDateOnly(preferredDate)}</strong></div>
                <div className={styles.summaryLine}>Số điện thoại liên hệ: <span>{contactPhone}</span></div>
                {notes && <div className={styles.summaryLine}>Ghi chú: <span>{notes}</span></div>}
                <div className={styles.summaryLine}>Tổng chi phí gói: <strong>{formatVndCurrency(pkg.price)}</strong></div>
            </div>
            <Form layout="vertical"><Form.Item validateStatus={fieldErrors.terms ? 'error' : undefined} help={fieldErrors.terms || undefined}>
                <Checkbox checked={agreedTerms} disabled={isSubmitting} onChange={event => { setAgreedTerms(event.target.checked); if (event.target.checked) setFieldErrors(prev => ({ ...prev, terms: '' })); }}>Tôi xác nhận thông tin đăng ký trên là chính xác và đồng ý với hướng dẫn chuẩn bị (nhịn ăn xét nghiệm) của phòng khám.</Checkbox>
            </Form.Item></Form>
            <div className={styles.actions}><Button onClick={handleBack} disabled={isSubmitting}><ArrowLeft size={16} /> Quay lại</Button><Button type="primary" size="large" onClick={handleSubmitRegistration} disabled={!agreedTerms || isSubmitting}>{isSubmitting ? 'Đang gửi...' : 'Xác nhận đăng ký gói khám'}</Button></div>
        </Card>}
    </div>;
};
