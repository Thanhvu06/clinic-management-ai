import React, { useState, useEffect } from 'react';
import {
    Receipt,
    Clock,
    CheckCircle,
    XCircle,
    Eye,
    RefreshCw,
    Info,
} from 'lucide-react';
import { billingApi } from '../../api/billingApi';
import type { InvoiceDto, InvoiceDetailDto } from '../../types';
import { InvoiceStatus } from '../../types';
import { InvoiceReceiptModal } from '../../components/billing/InvoiceReceiptModal';
import { useDialog } from '../../contexts/DialogContext';
import styles from './PatientInvoices.module.css';

export const PatientInvoices: React.FC = () => {
    const { showAlert } = useDialog();

    const [invoices, setInvoices] = useState<InvoiceDto[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [totalItems, setTotalItems] = useState<number>(0);
    const [page, setPage] = useState<number>(1);
    const [statusFilter, setStatusFilter] = useState<InvoiceStatus | undefined>(undefined);
    const pageSize = 10;

    // Detail modal
    const [selectedInvoice, setSelectedInvoice] = useState<InvoiceDetailDto | null>(null);
    const [detailLoading, setDetailLoading] = useState<boolean>(false);
    const [detailModalOpen, setDetailModalOpen] = useState<boolean>(false);

    const formatCurrency = (val: number) => {
        return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
    };

    const formatDateTime = (val?: string | null) => {
        if (!val) return '---';
        try {
            return new Intl.DateTimeFormat('vi-VN', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit',
            }).format(new Date(val));
        } catch {
            return val;
        }
    };

    const fetchInvoices = async () => {
        setLoading(true);
        try {
            const res = await billingApi.patient.getMyInvoices(page, pageSize, statusFilter);
            if (res.success && res.data) {
                setInvoices(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (err: any) {
            console.error('Lỗi khi tải hóa đơn:', err);
            showAlert(err?.message || 'Không thể tải danh sách hóa đơn của bạn.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchInvoices();
    }, [page, statusFilter]);

    const handleViewDetail = async (id: number) => {
        setDetailModalOpen(true);
        setDetailLoading(true);
        try {
            const res = await billingApi.patient.getMyInvoiceDetail(id);
            if (res.success && res.data) {
                setSelectedInvoice(res.data);
            }
        } catch (err: any) {
            showAlert(err?.message || 'Không thể tải chi tiết hóa đơn.', 'Lỗi', 'error');
            setDetailModalOpen(false);
        } finally {
            setDetailLoading(false);
        }
    };

    const renderStatusBadge = (status: InvoiceStatus) => {
        switch (status) {
            case InvoiceStatus.Paid:
                return (
                    <span className="badge badge-success">
                        <CheckCircle size={13} /> Đã thanh toán
                    </span>
                );
            case InvoiceStatus.Unpaid:
                return (
                    <span className="badge badge-warning">
                        <Clock size={13} /> Chờ thanh toán
                    </span>
                );
            case InvoiceStatus.Cancelled:
                return (
                    <span className="badge badge-danger">
                        <XCircle size={13} /> Đã hủy
                    </span>
                );
            default:
                return null;
        }
    };

    const totalPages = Math.ceil(totalItems / pageSize) || 1;

    return (
        <div className={styles.container}>
            {/* Header */}
            <div className={styles.headerRow}>
                <div className={styles.titleArea}>
                    <Receipt size={28} color="var(--c-primary)" />
                    <div>
                        <h2>Hóa đơn dịch vụ của tôi</h2>
                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                            Theo dõi chi phí khám chữa bệnh và lịch sử thanh toán tại ClinicCare AI (Dữ liệu demo)
                        </div>
                    </div>
                </div>

                <button type="button" className="btn-secondary" onClick={() => fetchInvoices()} title="Làm mới">
                    <RefreshCw size={14} /> Làm mới
                </button>
            </div>

            {/* Information notice */}
            <div className={styles.noticeBanner}>
                <Info size={20} style={{ flexShrink: 0 }} />
                <div>
                    <strong>Hướng dẫn thanh toán:</strong> Quý khách vui lòng xuất trình mã hóa đơn hoặc số điện thoại tại <strong>Quầy thu ngân / Lễ tân</strong> của phòng khám để hoàn tất thanh toán bằng Tiền mặt hoặc Chuyển khoản ngân hàng.
                </div>
            </div>

            {/* Filter Tabs */}
            <div className={styles.filterCard}>
                <div className={styles.statusTabs}>
                    <button
                        type="button"
                        className={`${styles.tabBtn} ${statusFilter === undefined ? styles.tabBtnActive : ''}`}
                        onClick={() => { setStatusFilter(undefined); setPage(1); }}
                    >
                        Tất cả hóa đơn
                    </button>
                    <button
                        type="button"
                        className={`${styles.tabBtn} ${statusFilter === InvoiceStatus.Unpaid ? styles.tabBtnActive : ''}`}
                        onClick={() => { setStatusFilter(InvoiceStatus.Unpaid); setPage(1); }}
                    >
                        Chờ thanh toán
                    </button>
                    <button
                        type="button"
                        className={`${styles.tabBtn} ${statusFilter === InvoiceStatus.Paid ? styles.tabBtnActive : ''}`}
                        onClick={() => { setStatusFilter(InvoiceStatus.Paid); setPage(1); }}
                    >
                        Đã thanh toán
                    </button>
                    <button
                        type="button"
                        className={`${styles.tabBtn} ${statusFilter === InvoiceStatus.Cancelled ? styles.tabBtnActive : ''}`}
                        onClick={() => { setStatusFilter(InvoiceStatus.Cancelled); setPage(1); }}
                    >
                        Đã hủy
                    </button>
                </div>

                <div style={{ fontSize: '0.85rem', color: 'var(--c-text-light)' }}>
                    Tổng số: <strong>{totalItems}</strong> hóa đơn
                </div>
            </div>

            {/* Invoices List */}
            <div className="card" style={{ padding: 0 }}>
                {loading ? (
                    <div style={{ padding: '48px', textAlign: 'center', color: 'var(--c-muted)' }}>
                        Đang tải hóa đơn của bạn...
                    </div>
                ) : invoices.length === 0 ? (
                    <div className="empty-state">
                        <Receipt size={48} className="empty-state-icon" />
                        <h3>Chưa có hóa đơn nào</h3>
                        <p>Hóa đơn sẽ tự động xuất hiện khi bác sĩ hoàn thành lượt khám hoặc bạn đăng ký gói khám sức khỏe.</p>
                    </div>
                ) : (
                    <div className="table-responsive">
                        <table className="table">
                            <thead>
                                <tr>
                                    <th>Mã hóa đơn</th>
                                    <th>Nguồn dịch vụ</th>
                                    <th>Số tiền</th>
                                    <th>Trạng thái</th>
                                    <th>Thời gian lập</th>
                                    <th style={{ textAlign: 'right' }}>Chi tiết</th>
                                </tr>
                            </thead>
                            <tbody>
                                {invoices.map((inv) => (
                                    <tr key={inv.id}>
                                        <td>
                                            <div style={{ fontWeight: 700, color: 'var(--c-navy-dark)' }}>
                                                {inv.invoiceCode}
                                            </div>
                                        </td>
                                        <td>
                                            <div style={{ fontWeight: 600 }}>{inv.sourceTypeName}</div>
                                            <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>
                                                Mã tham chiếu: {inv.appointmentCode || inv.registrationCode || 'N/A'}
                                            </div>
                                        </td>
                                        <td>
                                            <div style={{ fontWeight: 700, color: 'var(--c-primary)', fontSize: '1rem' }}>
                                                {formatCurrency(inv.totalAmount)}
                                            </div>
                                        </td>
                                        <td>
                                            {renderStatusBadge(inv.status)}
                                        </td>
                                        <td>
                                            <div style={{ fontSize: '0.85rem', color: 'var(--c-text)' }}>
                                                {formatDateTime(inv.createdAtUtc)}
                                            </div>
                                        </td>
                                        <td style={{ textAlign: 'right' }}>
                                            <button
                                                type="button"
                                                className="btn-secondary"
                                                style={{ padding: '6px 12px', fontSize: '0.85rem' }}
                                                onClick={() => handleViewDetail(inv.id)}
                                            >
                                                <Eye size={14} /> Xem phiếu
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}

                {/* Pagination */}
                {totalItems > pageSize && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', borderTop: '1px solid var(--c-border)' }}>
                        <span style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>
                            Trang {page} / {totalPages}
                        </span>
                        <div style={{ display: 'flex', gap: '8px' }}>
                            <button
                                type="button"
                                className="btn-secondary"
                                style={{ padding: '4px 12px', fontSize: '0.85rem' }}
                                disabled={page <= 1}
                                onClick={() => setPage(p => p - 1)}
                            >
                                Trang trước
                            </button>
                            <button
                                type="button"
                                className="btn-secondary"
                                style={{ padding: '4px 12px', fontSize: '0.85rem' }}
                                disabled={page >= totalPages}
                                onClick={() => setPage(p => p + 1)}
                            >
                                Trang sau
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {/* Receipt Modal */}
            <InvoiceReceiptModal
                isOpen={detailModalOpen}
                onClose={() => setDetailModalOpen(false)}
                invoice={selectedInvoice}
                loading={detailLoading}
            />
        </div>
    );
};
