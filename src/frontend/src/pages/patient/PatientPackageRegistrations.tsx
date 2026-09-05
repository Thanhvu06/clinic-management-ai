import React, { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Package, Clock, AlertTriangle, CheckCircle, XCircle, ChevronRight } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { formatVndCurrency, formatDisplayDate } from '../../utils/formatters';
import { Button, FormError } from '../../components/forms';

interface PackageRegistrationItem {
    id: number;
    registrationCode: string;
    healthPackageId: number;
    packageName: string;
    packageCode: string;
    packagePrice: number;
    preferredDate: string;
    contactPhone: string;
    notes?: string;
    status: 'Pending' | 'Confirmed' | 'Completed' | 'Cancelled';
    createdAt: string;
}

export const PatientPackageRegistrations: React.FC = () => {
    const [registrations, setRegistrations] = useState<PackageRegistrationItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Cancel modal state
    const [cancelModalItem, setCancelModalItem] = useState<PackageRegistrationItem | null>(null);
    const [cancelReason, setCancelReason] = useState('');
    const [isCancelling, setIsCancelling] = useState(false);
    const [cancelError, setCancelError] = useState<string | null>(null);

    const fetchRegistrations = async () => {
        try {
            setLoading(true);
            const res = await axiosClient.get<any, any>('/patient/health-package-registrations');
            if (res.success) {
                setRegistrations(res.data || []);
            } else {
                setError('Không thể tải danh sách gói khám đã đăng ký.');
            }
        } catch (err: any) {
            setError(err.response?.data?.message || 'Có lỗi xảy ra khi tải dữ liệu.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRegistrations();
    }, []);

    const handleConfirmCancel = async () => {
        if (!cancelModalItem || isCancelling) return;
        try {
            setIsCancelling(true);
            setCancelError(null);
            const res = await axiosClient.post<any, any>(`/patient/health-package-registrations/${cancelModalItem.id}/cancel`, {
                cancellationReason: cancelReason.trim() || 'Người bệnh chủ động hủy qua trang web'
            });

            if (res.success) {
                setCancelModalItem(null);
                setCancelReason('');
                fetchRegistrations();
            } else {
                setCancelError(res.message || 'Hủy đăng ký không thành công.');
            }
        } catch (err: any) {
            setCancelError(err.response?.data?.message || 'Có lỗi xảy ra khi hủy đăng ký.');
        } finally {
            setIsCancelling(false);
        }
    };

    const getStatusBadge = (status: string) => {
        switch (status) {
            case 'Pending':
                return (
                    <span style={{ background: '#fef3c7', color: '#92400e', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <Clock size={13} /> Chờ xác nhận
                    </span>
                );
            case 'Confirmed':
                return (
                    <span style={{ background: '#ecfdf5', color: '#065f46', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <CheckCircle size={13} /> Đã xác nhận
                    </span>
                );
            case 'Completed':
                return (
                    <span style={{ background: '#eff6ff', color: '#1e40af', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <CheckCircle size={13} /> Đã hoàn tất
                    </span>
                );
            case 'Cancelled':
                return (
                    <span style={{ background: '#fef2f2', color: '#991b1b', padding: '4px 10px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <XCircle size={13} /> Đã hủy
                    </span>
                );
            default:
                return <span>{status}</span>;
        }
    };

    return (
        <div style={{ maxWidth: '1040px', margin: '32px auto 80px auto', padding: '0 20px' }}>
            <div style={{ marginBottom: '28px' }}>
                <nav style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.875rem', color: 'var(--c-text-muted)', marginBottom: '8px' }}>
                    <Link to="/patient/profile" style={{ color: 'var(--c-primary)', textDecoration: 'none' }}>Hồ sơ cá nhân</Link>
                    <span>/</span>
                    <span>Gói khám đã đăng ký</span>
                </nav>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                    <div>
                        <h1 style={{ fontSize: '1.85rem', fontWeight: 800, color: 'var(--c-navy)', margin: 0 }}>
                            Gói Khám Sức Khỏe Đã Đăng Ký
                        </h1>
                        <p style={{ color: 'var(--c-text-muted)', fontSize: '0.95rem', margin: '4px 0 0 0' }}>
                            Theo dõi tình trạng xét duyệt và quản lý các yêu cầu đăng ký gói khám của bạn.
                        </p>
                    </div>
                    <Link to="/health-packages" className="btn btn-primary" style={{ textDecoration: 'none' }}>
                        <Package size={16} /> Đăng ký gói khám mới
                    </Link>
                </div>
            </div>

            {error && (
                <div style={{ marginBottom: '20px' }}>
                    <FormError message={error} />
                </div>
            )}

            {loading ? (
                <div style={{ padding: '60px 20px', background: '#ffffff', borderRadius: '12px', border: '1px solid var(--c-border)', textAlign: 'center' }}>
                    Đang tải danh sách đăng ký...
                </div>
            ) : registrations.length === 0 ? (
                <div style={{ 
                    padding: '60px 20px', background: '#ffffff', borderRadius: '16px', 
                    border: '1px dashed var(--c-border)', textAlign: 'center' 
                }}>
                    <Package size={52} style={{ opacity: 0.25, marginBottom: '16px', color: 'var(--c-primary)' }} />
                    <h2 style={{ fontSize: '1.25rem', fontWeight: 700, color: 'var(--c-navy)', marginBottom: '8px' }}>
                        Bạn chưa đăng ký gói khám sức khỏe nào
                    </h2>
                    <p style={{ color: 'var(--c-text-muted)', maxWidth: '480px', margin: '0 auto 24px auto', lineHeight: 1.5 }}>
                        ClinicCare cung cấp nhiều gói tầm soát tổng quát định kỳ phù hợp cho cá nhân và gia đình với chi phí hợp lý.
                    </p>
                    <Link to="/health-packages" className="btn btn-primary" style={{ textDecoration: 'none' }}>
                        Khám phá danh mục gói khám
                    </Link>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    {registrations.map(reg => (
                        <div 
                            key={reg.id}
                            style={{ 
                                background: '#ffffff', border: '1px solid var(--c-border)', borderRadius: '14px', 
                                padding: '24px', boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s' 
                            }}
                        >
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '12px', marginBottom: '16px' }}>
                                <div>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '6px' }}>
                                        <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--c-primary)', background: 'rgba(14, 116, 144, 0.08)', padding: '2px 8px', borderRadius: '4px' }}>
                                            {reg.registrationCode}
                                        </span>
                                        {getStatusBadge(reg.status)}
                                    </div>
                                    <h2 style={{ fontSize: '1.2rem', fontWeight: 700, color: 'var(--c-navy)', margin: 0 }}>
                                        {reg.packageName}
                                    </h2>
                                </div>
                                <div style={{ textAlign: 'right' }}>
                                    <span style={{ fontSize: '0.75rem', color: 'var(--c-text-muted)', display: 'block' }}>Chi phí trọn gói</span>
                                    <span style={{ fontSize: '1.25rem', fontWeight: 800, color: 'var(--c-primary)' }}>
                                        {formatVndCurrency(reg.packagePrice)}
                                    </span>
                                </div>
                            </div>

                            <div style={{ 
                                display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', 
                                gap: '12px', padding: '16px', background: '#f8fafc', borderRadius: '8px', 
                                fontSize: '0.875rem', marginBottom: '16px' 
                            }}>
                                <div>
                                    <span style={{ color: 'var(--c-text-muted)', display: 'block' }}>Ngày khám mong muốn:</span>
                                    <strong style={{ color: 'var(--c-navy)' }}>{reg.preferredDate}</strong>
                                </div>
                                <div>
                                    <span style={{ color: 'var(--c-text-muted)', display: 'block' }}>Số điện thoại liên hệ:</span>
                                    <span>{reg.contactPhone}</span>
                                </div>
                                <div>
                                    <span style={{ color: 'var(--c-text-muted)', display: 'block' }}>Thời gian gửi đăng ký:</span>
                                    <span>{formatDisplayDate(reg.createdAt)}</span>
                                </div>
                            </div>

                            {reg.notes && (
                                <div style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)', marginBottom: '16px' }}>
                                    <strong>Ghi chú:</strong> {reg.notes}
                                </div>
                            )}

                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: '12px', borderTop: '1px solid var(--c-border-light)' }}>
                                <Link 
                                    to={`/health-packages/${reg.healthPackageId}`}
                                    style={{ fontSize: '0.875rem', color: 'var(--c-primary)', fontWeight: 600, textDecoration: 'none', display: 'flex', alignItems: 'center', gap: '4px' }}
                                >
                                    Xem chi tiết gói khám <ChevronRight size={14} />
                                </Link>

                                {reg.status === 'Pending' && (
                                    <Button 
                                        variant="danger" 
                                        size="sm"
                                        onClick={() => {
                                            setCancelModalItem(reg);
                                            setCancelReason('');
                                            setCancelError(null);
                                        }}
                                    >
                                        Hủy đăng ký
                                    </Button>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {/* Cancel Modal */}
            {cancelModalItem && (
                <div style={{
                    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '20px'
                }}>
                    <div style={{
                        background: '#ffffff', borderRadius: '16px', maxWidth: '480px', width: '100%',
                        padding: '28px', boxShadow: 'var(--shadow-lg)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: 'var(--c-danger)', marginBottom: '12px' }}>
                            <AlertTriangle size={24} />
                            <h3 style={{ fontSize: '1.2rem', fontWeight: 700, margin: 0 }}>Xác nhận hủy đăng ký gói khám</h3>
                        </div>

                        <p style={{ fontSize: '0.9rem', color: 'var(--c-text-muted)', marginBottom: '16px', lineHeight: 1.5 }}>
                            Bạn có chắc chắn muốn hủy yêu cầu đăng ký gói khám <strong>{cancelModalItem.packageName}</strong> (Mã: {cancelModalItem.registrationCode})?
                        </p>

                        {cancelError && (
                            <div style={{ marginBottom: '12px' }}>
                                <FormError message={cancelError} />
                            </div>
                        )}

                        <div style={{ marginBottom: '20px' }}>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: 'var(--c-navy)', marginBottom: '6px' }}>
                                Lý do hủy (Không bắt buộc)
                            </label>
                            <textarea
                                value={cancelReason}
                                onChange={(e) => setCancelReason(e.target.value)}
                                placeholder="Thay đổi kế hoạch, dời ngày, hoặc lý do khác..."
                                rows={3}
                                style={{
                                    width: '100%', padding: '10px 12px', borderRadius: '8px',
                                    border: '1px solid var(--c-border)', fontSize: '0.9rem'
                                }}
                            />
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                            <Button variant="secondary" size="md" onClick={() => setCancelModalItem(null)} disabled={isCancelling}>
                                Đóng
                            </Button>
                            <Button variant="danger" size="md" onClick={handleConfirmCancel} loading={isCancelling}>
                                Xác nhận hủy
                            </Button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
