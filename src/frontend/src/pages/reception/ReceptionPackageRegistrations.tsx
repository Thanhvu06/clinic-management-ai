import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Package, CheckCircle, XCircle, Clock, RefreshCw, AlertTriangle } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
import { formatVndCurrency, formatDisplayDate } from '../../utils/formatters';

interface PackageRegistrationItem {
    id: number;
    registrationCode: string;
    patientId: number;
    patientName: string;
    healthPackageId: number;
    healthPackageName?: string;
    packageName?: string;
    healthPackageCode?: string;
    packageCode?: string;
    healthPackagePrice?: number;
    packagePrice?: number;
    preferredDate: string;
    contactPhone: string;
    note?: string;
    notes?: string;
    adminNotes?: string;
    cancellationReason?: string;
    status: 'Pending' | 'Confirmed' | 'Completed' | 'Cancelled' | string;
    createdAt: string;
}

export const ReceptionPackageRegistrations: React.FC = () => {
    const [registrations, setRegistrations] = useState<PackageRegistrationItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    const [actionLoading, setActionLoading] = useState(false);
    const [selectedItem, setSelectedItem] = useState<PackageRegistrationItem | null>(null);
    const [actionType, setActionType] = useState<'confirm' | 'cancel' | null>(null);
    const [actionNote, setActionNote] = useState('');
    const [actionError, setActionError] = useState<string | null>(null);

    const { showToast } = useDialog();

    const fetchRegistrations = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/reception/health-package-registrations?${params.toString()}`);
            if (res.success && res.data) {
                setRegistrations(res.data.items || []);
                setTotalItems(res.data.totalItems || 0);
            }
        } catch (error) {
            console.error("Lỗi tải danh sách đăng ký gói khám", error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchRegistrations();
    }, [page, statusFilter]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchRegistrations();
    };

    const handleConfirmAction = async () => {
        if (!selectedItem || !actionType || actionLoading) return;
        setActionLoading(true);
        setActionError(null);

        try {
            let res: any;
            if (actionType === 'confirm') {
                res = await axiosClient.post(`/reception/health-package-registrations/${selectedItem.id}/confirm`, {
                    notes: actionNote.trim()
                });
            } else {
                res = await axiosClient.post(`/reception/health-package-registrations/${selectedItem.id}/cancel`, {
                    cancellationReason: actionNote.trim() || 'Hủy theo yêu cầu của phòng khám/khách hàng'
                });
            }

            if (res.success) {
                showToast(actionType === 'confirm' ? 'Đã xác nhận đăng ký gói khám!' : 'Đã hủy đăng ký gói khám.', 'success');
                setSelectedItem(null);
                setActionType(null);
                setActionNote('');
                fetchRegistrations();
            } else {
                setActionError(res.message || 'Thao tác không thành công.');
            }
        } catch (err: any) {
            setActionError(err?.response?.data?.message || 'Có lỗi xảy ra trong quá trình xử lý.');
        } finally {
            setActionLoading(false);
        }
    };

    const getStatusBadge = (status: string) => {
        switch (status) {
            case 'Pending':
                return (
                    <span className="badge badge-warning" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <Clock size={12} /> Chờ xác nhận
                    </span>
                );
            case 'Confirmed':
                return (
                    <span className="badge badge-success" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <CheckCircle size={12} /> Đã xác nhận
                    </span>
                );
            case 'Completed':
                return (
                    <span className="badge badge-info" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <CheckCircle size={12} /> Đã hoàn tất
                    </span>
                );
            case 'Cancelled':
                return (
                    <span className="badge badge-danger" style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
                        <XCircle size={12} /> Đã hủy
                    </span>
                );
            default:
                return <span>{status}</span>;
        }
    };

    return (
        <div style={{ padding: '24px', maxWidth: '1200px', margin: '0 auto' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                <div>
                    <h1 style={{ fontSize: '1.75rem', fontWeight: 800, color: 'var(--c-navy)', margin: 0 }}>
                        Quản lý Đăng ký Gói khám Sức khỏe
                    </h1>
                    <p style={{ color: 'var(--c-text-muted)', fontSize: '0.925rem', margin: '4px 0 0 0' }}>
                        Xác nhận và hỗ trợ bệnh nhân đã đăng ký các gói khám định kỳ trực tuyến.
                    </p>
                </div>
                <button 
                    type="button" 
                    className="btn btn-secondary btn-sm"
                    onClick={fetchRegistrations}
                    title="Làm mới danh sách"
                >
                    <RefreshCw size={14} /> Làm mới
                </button>
            </div>

            {/* Filter Toolbar */}
            <div style={{ display: 'flex', gap: '16px', background: '#ffffff', padding: '16px', borderRadius: '12px', border: '1px solid var(--c-border)', marginBottom: '24px', flexWrap: 'wrap' }}>
                <form onSubmit={handleSearchSubmit} style={{ display: 'flex', flex: 1, minWidth: '280px', gap: '8px' }}>
                    <div style={{ position: 'relative', flex: 1 }}>
                        <Search size={18} style={{ position: 'absolute', left: '12px', top: '50%', transform: 'translateY(-50%)', color: 'var(--c-text-muted)' }} />
                        <input
                            type="text"
                            placeholder="Tìm theo mã đăng ký, tên bệnh nhân, SĐT..."
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            className="form-input"
                            style={{ paddingLeft: '38px', minHeight: '40px' }}
                        />
                    </div>
                    <button type="submit" className="btn btn-primary" style={{ padding: '0 16px' }}>
                        Tìm kiếm
                    </button>
                </form>

                <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                    <select
                        className="form-select"
                        value={statusFilter}
                        onChange={(e) => {
                            setStatusFilter(e.target.value);
                            setPage(1);
                        }}
                        style={{ minHeight: '40px' }}
                    >
                        <option value="">Tất cả trạng thái</option>
                        <option value="Pending">Chờ xác nhận</option>
                        <option value="Confirmed">Đã xác nhận</option>
                        <option value="Completed">Đã hoàn tất</option>
                        <option value="Cancelled">Đã hủy</option>
                    </select>
                </div>
            </div>

            {/* Registrations Table */}
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
                {loading ? (
                    <div style={{ padding: '60px', textAlign: 'center', color: 'var(--c-text-muted)' }}>
                        Đang tải danh sách đăng ký gói khám...
                    </div>
                ) : registrations.length === 0 ? (
                    <div style={{ padding: '60px', textAlign: 'center', color: 'var(--c-text-muted)' }}>
                        <Package size={44} style={{ opacity: 0.3, marginBottom: '12px' }} />
                        <div>Không có dữ liệu đăng ký gói khám nào phù hợp.</div>
                    </div>
                ) : (
                    <div className="table-responsive">
                        <table className="table">
                            <thead>
                                <tr>
                                    <th>Mã đăng ký</th>
                                    <th>Bệnh nhân</th>
                                    <th>Gói khám</th>
                                    <th>Ngày mong muốn</th>
                                    <th>Chi phí</th>
                                    <th>Trạng thái</th>
                                    <th>Thao tác</th>
                                </tr>
                            </thead>
                            <tbody>
                                {registrations.map(reg => (
                                    <tr key={reg.id}>
                                        <td>
                                            <span style={{ fontWeight: 700, color: 'var(--c-primary)' }}>
                                                {reg.registrationCode}
                                            </span>
                                            <div style={{ fontSize: '0.75rem', color: 'var(--c-text-muted)' }}>
                                                {formatDisplayDate(reg.createdAt)}
                                            </div>
                                        </td>
                                        <td>
                                            <div style={{ fontWeight: 600, color: 'var(--c-navy)' }}>{reg.patientName}</div>
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)' }}>{reg.contactPhone}</div>
                                        </td>
                                        <td>
                                            <div style={{ fontWeight: 600 }}>{reg.healthPackageName || reg.packageName}</div>
                                            <div style={{ fontSize: '0.75rem', color: 'var(--c-text-muted)' }}>Mã: {reg.healthPackageCode || reg.packageCode}</div>
                                            {(reg.note || reg.notes) && (
                                                <div style={{ fontSize: '0.8rem', color: '#64748b', fontStyle: 'italic', maxWidth: '240px' }}>
                                                    "{reg.note || reg.notes}"
                                                </div>
                                            )}
                                            {reg.adminNotes && (
                                                <div style={{ fontSize: '0.75rem', color: '#065f46', marginTop: '2px' }}>
                                                    <strong>Lễ tân:</strong> {reg.adminNotes}
                                                </div>
                                            )}
                                            {reg.cancellationReason && (
                                                <div style={{ fontSize: '0.75rem', color: '#991b1b', marginTop: '2px' }}>
                                                    <strong>Lý do hủy:</strong> {reg.cancellationReason}
                                                </div>
                                            )}
                                        </td>
                                        <td>
                                            <strong style={{ color: 'var(--c-navy)' }}>{reg.preferredDate}</strong>
                                        </td>
                                        <td>
                                            <span style={{ fontWeight: 700, color: 'var(--c-primary)' }}>
                                                {formatVndCurrency(reg.healthPackagePrice ?? reg.packagePrice ?? 0)}
                                            </span>
                                        </td>
                                        <td>
                                            {getStatusBadge(reg.status)}
                                        </td>
                                        <td>
                                            {reg.status === 'Pending' ? (
                                                <div style={{ display: 'flex', gap: '6px' }}>
                                                    <button
                                                        type="button"
                                                        className="btn btn-success btn-sm"
                                                        onClick={() => {
                                                            setSelectedItem(reg);
                                                            setActionType('confirm');
                                                            setActionNote('');
                                                            setActionError(null);
                                                        }}
                                                    >
                                                        Xác nhận
                                                    </button>
                                                    <button
                                                        type="button"
                                                        className="btn btn-danger btn-sm"
                                                        onClick={() => {
                                                            setSelectedItem(reg);
                                                            setActionType('cancel');
                                                            setActionNote('');
                                                            setActionError(null);
                                                        }}
                                                    >
                                                        Hủy
                                                    </button>
                                                </div>
                                            ) : (
                                                <span style={{ fontSize: '0.85rem', color: 'var(--c-text-muted)' }}>—</span>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* Pagination */}
            {totalItems > 10 && (
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '16px' }}>
                    <span style={{ fontSize: '0.875rem', color: 'var(--c-text-muted)' }}>
                        Hiển thị {registrations.length} / {totalItems} bản ghi
                    </span>
                    <div style={{ display: 'flex', gap: '8px' }}>
                        <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled={page <= 1}
                            onClick={() => setPage(prev => prev - 1)}
                        >
                            Trang trước
                        </button>
                        <button
                            type="button"
                            className="btn btn-secondary btn-sm"
                            disabled={page * 10 >= totalItems}
                            onClick={() => setPage(prev => prev + 1)}
                        >
                            Trang sau
                        </button>
                    </div>
                </div>
            )}

            {/* Action Confirmation Modal */}
            {selectedItem && actionType && (
                <div style={{
                    position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '20px'
                }}>
                    <div style={{
                        background: '#ffffff', borderRadius: '16px', maxWidth: '480px', width: '100%',
                        padding: '28px', boxShadow: 'var(--shadow-lg)'
                    }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '14px', color: actionType === 'confirm' ? 'var(--c-success)' : 'var(--c-danger)' }}>
                            {actionType === 'confirm' ? <CheckCircle size={24} /> : <AlertTriangle size={24} />}
                            <h3 style={{ fontSize: '1.2rem', fontWeight: 700, margin: 0, color: 'var(--c-navy)' }}>
                                {actionType === 'confirm' ? 'Xác nhận đăng ký gói khám' : 'Hủy đăng ký gói khám'}
                            </h3>
                        </div>

                        <p style={{ fontSize: '0.9rem', color: 'var(--c-text)', marginBottom: '16px', lineHeight: 1.5 }}>
                            {actionType === 'confirm' ? (
                                <>Xác nhận thông tin gói khám <strong>{selectedItem.packageName}</strong> cho người bệnh <strong>{selectedItem.patientName}</strong> vào ngày <strong>{selectedItem.preferredDate}</strong>?</>
                            ) : (
                                <>Bạn có chắc muốn hủy đăng ký gói khám <strong>{selectedItem.registrationCode}</strong> của người bệnh <strong>{selectedItem.patientName}</strong>?</>
                            )}
                        </p>

                        {actionError && (
                            <div style={{ padding: '10px 14px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', fontSize: '0.85rem', marginBottom: '14px' }}>
                                {actionError}
                            </div>
                        )}

                        <div style={{ marginBottom: '20px' }}>
                            <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 600, color: 'var(--c-navy)', marginBottom: '6px' }}>
                                {actionType === 'confirm' ? 'Ghi chú lễ tân (Không bắt buộc)' : 'Lý do hủy đăng ký (*)'}
                            </label>
                            <textarea
                                value={actionNote}
                                onChange={(e) => setActionNote(e.target.value)}
                                placeholder={actionType === 'confirm' ? 'Nhập ghi chú tiếp đón nếu có...' : 'Nhập lý do hủy...'}
                                rows={3}
                                className="form-textarea"
                                style={{ width: '100%', padding: '10px' }}
                            />
                        </div>

                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                            <button
                                type="button"
                                className="btn btn-secondary btn-md"
                                onClick={() => {
                                    setSelectedItem(null);
                                    setActionType(null);
                                }}
                                disabled={actionLoading}
                            >
                                Đóng
                            </button>
                            <button
                                type="button"
                                className={`btn ${actionType === 'confirm' ? 'btn-success' : 'btn-danger'} btn-md`}
                                onClick={handleConfirmAction}
                                disabled={actionLoading}
                            >
                                {actionLoading ? 'Đang xử lý...' : (actionType === 'confirm' ? 'Xác nhận đăng ký' : 'Xác nhận hủy')}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};
