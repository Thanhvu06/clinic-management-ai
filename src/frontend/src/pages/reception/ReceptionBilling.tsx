import React, { useState, useEffect } from 'react';
import {
    Receipt,
    Clock,
    CheckCircle,
    XCircle,
    DollarSign,
    Search,
    RefreshCw,
    Plus,
    Eye,
    CreditCard,
    Ban,
    X,
} from 'lucide-react';
import { billingApi } from '../../api/billingApi';
import type {
    InvoiceDto,
    InvoiceDetailDto,
    BillingKpiDto,
    InvoiceSourceType,
    UnbilledVisitDto,
    PagedBillingResult,
} from '../../types';
import {
    InvoiceStatus,
    PaymentMethod,
} from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { InvoiceReceiptModal } from '../../components/billing/InvoiceReceiptModal';
import styles from './ReceptionBilling.module.css';

export const ReceptionBilling: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();

    // Data states
    const [invoices, setInvoices] = useState<InvoiceDto[]>([]);
    const [kpi, setKpi] = useState<BillingKpiDto | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [totalItems, setTotalItems] = useState<number>(0);
    const [page, setPage] = useState<number>(1);
    const pageSize = 10;

    // Filters
    const [search, setSearch] = useState<string>('');
    const [statusFilter, setStatusFilter] = useState<string>('');
    const [sourceFilter, setSourceFilter] = useState<string>('');
    const [fromDate, setFromDate] = useState<string>('');
    const [toDate, setToDate] = useState<string>('');

    // Tab state: 'invoices' or 'unbilled'
    const [billingTab, setBillingTab] = useState<'invoices' | 'unbilled'>('invoices');
    const [unbilledVisits, setUnbilledVisits] = useState<UnbilledVisitDto[]>([]);
    const [unbilledLoading, setUnbilledLoading] = useState<boolean>(false);
    const [unbilledPage, setUnbilledPage] = useState<number>(1);
    const [unbilledTotalPages, setUnbilledTotalPages] = useState<number>(1);
    const [unbilledTotalCount, setUnbilledTotalCount] = useState<number>(0);
    const unbilledPageSize = 10;

    // Detail / Receipt Modal
    const [detailModalOpen, setDetailModalOpen] = useState<boolean>(false);
    const [selectedInvoice, setSelectedInvoice] = useState<InvoiceDetailDto | null>(null);
    const [detailLoading, setDetailLoading] = useState<boolean>(false);

    // Create Invoice Modal
    const [createModalOpen, setCreateModalOpen] = useState<boolean>(false);
    const [createSourceType, setCreateSourceType] = useState<'visit' | 'appointment' | 'package'>('visit');
    const [referenceId, setReferenceId] = useState<string>('');
    const [creatingInvoice, setCreatingInvoice] = useState<boolean>(false);

    // Payment Modal
    const [paymentModalOpen, setPaymentModalOpen] = useState<boolean>(false);
    const [paymentInvoice, setPaymentInvoice] = useState<InvoiceDto | null>(null);
    const [paymentAmount, setPaymentAmount] = useState<string>('');
    const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>(PaymentMethod.Cash);
    const [paymentRefCode, setPaymentRefCode] = useState<string>('');
    const [paymentNote, setPaymentNote] = useState<string>('');
    const [processingPayment, setProcessingPayment] = useState<boolean>(false);

    // Cancel Modal
    const [cancelModalOpen, setCancelModalOpen] = useState<boolean>(false);
    const [cancelInvoiceTarget, setCancelInvoiceTarget] = useState<InvoiceDto | null>(null);
    const [cancelReason, setCancelReason] = useState<string>('');
    const [cancellingInvoice, setCancellingInvoice] = useState<boolean>(false);

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

    const fetchKpi = async () => {
        try {
            const res = await billingApi.reception.getTodayKpi();
            if (res.success && res.data) {
                setKpi(res.data);
            }
        } catch (err) {
            console.error('Lỗi khi tải KPI:', err);
        }
    };

    const fetchInvoices = async () => {
        setLoading(true);
        try {
            const statusNum = statusFilter ? (parseInt(statusFilter, 10) as InvoiceStatus) : undefined;
            const sourceNum = sourceFilter ? (parseInt(sourceFilter, 10) as InvoiceSourceType) : undefined;

            const res = await billingApi.reception.getInvoices({
                search: search.trim() || undefined,
                status: statusNum,
                sourceType: sourceNum,
                fromDate: fromDate || undefined,
                toDate: toDate || undefined,
                page,
                pageSize,
            });

            if (res.success && res.data) {
                setInvoices(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (err: any) {
            console.error('Lỗi khi tải danh sách hóa đơn:', err);
            showAlert(err?.message || 'Không thể tải danh sách hóa đơn.', 'Lỗi', 'error');
        } finally {
            setLoading(false);
        }
    };

    const fetchUnbilledVisits = async (targetPage = unbilledPage) => {
        setUnbilledLoading(true);
        try {
            const res = await billingApi.reception.getUnbilledVisits(undefined, targetPage, unbilledPageSize);
            if (res.success && res.data) {
                if (Array.isArray(res.data)) {
                    setUnbilledVisits(res.data);
                    setUnbilledTotalCount(res.data.length);
                    setUnbilledTotalPages(1);
                    setUnbilledPage(1);
                } else {
                    const paged = res.data as PagedBillingResult<UnbilledVisitDto>;
                    setUnbilledVisits(paged.items || []);
                    setUnbilledTotalCount(paged.totalItems ?? paged.items?.length ?? 0);
                    setUnbilledTotalPages(paged.totalPages ?? 1);
                    setUnbilledPage(paged.page ?? targetPage);
                }
            }
        } catch (err) {
            console.error('Lỗi khi tải danh sách lượt khám chưa thu:', err);
        } finally {
            setUnbilledLoading(false);
        }
    };

    useEffect(() => {
        fetchKpi();
        fetchUnbilledVisits();
    }, []);

    useEffect(() => {
        fetchInvoices();
    }, [page, statusFilter, sourceFilter]);

    const handleSearch = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchInvoices();
    };

    const handleRefresh = () => {
        fetchKpi();
        fetchInvoices();
        fetchUnbilledVisits();
    };

    const handleCreateInvoiceFromUnbilled = async (visitId: number) => {
        setCreatingInvoice(true);
        try {
            const res = await billingApi.reception.createInvoiceFromVisit({ patientVisitId: visitId });
            if (res.success && res.data) {
                showAlert('Lập hóa đơn viện phí cho lượt khám thành công!', 'Thành công', 'success');
                fetchKpi();
                fetchInvoices();
                fetchUnbilledVisits();
                setBillingTab('invoices');
                handleViewDetail(res.data.id);
            }
        } catch (err: any) {
            showAlert(err?.message || 'Không thể lập hóa đơn cho lượt khám này.', 'Lỗi lập hóa đơn', 'error');
        } finally {
            setCreatingInvoice(false);
        }
    };

    // Open detail
    const handleViewDetail = async (id: number) => {
        setDetailModalOpen(true);
        setDetailLoading(true);
        try {
            const res = await billingApi.reception.getInvoiceById(id);
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

    // Handle Create Invoice
    const handleOpenCreateModal = () => {
        setCreateSourceType('visit');
        setReferenceId('');
        setCreateModalOpen(true);
    };

    const handleCreateInvoice = async (e: React.FormEvent) => {
        e.preventDefault();
        const refIdNum = parseInt(referenceId.trim(), 10);
        if (isNaN(refIdNum) || refIdNum <= 0) {
            showAlert('Vui lòng nhập ID hợp lệ (số nguyên dương).', 'Dữ liệu không hợp lệ', 'warning');
            return;
        }

        setCreatingInvoice(true);
        try {
            let res;
            if (createSourceType === 'visit') {
                res = await billingApi.reception.createInvoiceFromVisit({ patientVisitId: refIdNum });
            } else if (createSourceType === 'appointment') {
                res = await billingApi.reception.createInvoiceFromAppointment({ appointmentId: refIdNum });
            } else {
                res = await billingApi.reception.createInvoiceFromHealthPackage({ healthPackageRegistrationId: refIdNum });
            }

            if (res.success && res.data) {
                showAlert(res.message || 'Lập hóa đơn thành công.', 'Thành công', 'success');
                setCreateModalOpen(false);
                fetchKpi();
                fetchInvoices();
                // Open newly created invoice receipt
                setSelectedInvoice(res.data);
                setDetailModalOpen(true);
            }
        } catch (err: any) {
            showAlert(err?.message || 'Không thể lập hóa đơn.', 'Lỗi lập hóa đơn', 'error');
        } finally {
            setCreatingInvoice(false);
        }
    };

    // Handle Pay Invoice
    const handleOpenPaymentModal = (invoice: InvoiceDto) => {
        setPaymentInvoice(invoice);
        setPaymentAmount(invoice.totalAmount.toString());
        setPaymentMethod(PaymentMethod.Cash);
        setPaymentRefCode('');
        setPaymentNote('');
        setPaymentModalOpen(true);
    };

    const handleProcessPayment = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!paymentInvoice) return;

        const amountNum = parseFloat(paymentAmount);
        if (isNaN(amountNum) || amountNum !== paymentInvoice.totalAmount) {
            showAlert(`Số tiền thanh toán phải khớp chính xác với tổng hóa đơn: ${formatCurrency(paymentInvoice.totalAmount)}`, 'Lỗi thanh toán', 'warning');
            return;
        }

        setProcessingPayment(true);
        try {
            const res = await billingApi.reception.processPayment(paymentInvoice.id, {
                amount: amountNum,
                method: paymentMethod,
                referenceCode: paymentRefCode.trim() || undefined,
                note: paymentNote.trim() || undefined,
            });

            if (res.success) {
                showAlert('Thu tiền và thanh toán hóa đơn thành công.', 'Thành công', 'success');
                setPaymentModalOpen(false);
                fetchKpi();
                fetchInvoices();
                // Load updated detail to show receipt
                handleViewDetail(paymentInvoice.id);
            }
        } catch (err: any) {
            showAlert(err?.message || 'Không thể xử lý thanh toán.', 'Lỗi thanh toán', 'error');
        } finally {
            setProcessingPayment(false);
        }
    };

    // Handle Cancel Invoice
    const handleOpenCancelModal = (invoice: InvoiceDto) => {
        setCancelInvoiceTarget(invoice);
        setCancelReason('');
        setCancelModalOpen(true);
    };

    const handleCancelInvoice = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!cancelInvoiceTarget) return;

        if (!cancelReason.trim()) {
            showAlert('Vui lòng nhập lý do hủy hóa đơn.', 'Yêu cầu lý do', 'warning');
            return;
        }

        showConfirm(`Bạn có chắc chắn muốn hủy hóa đơn ${cancelInvoiceTarget.invoiceCode}? Thao tác này không thể hoàn tác.`, async () => {
            setCancellingInvoice(true);
            try {
                const res = await billingApi.reception.cancelInvoice(cancelInvoiceTarget.id, {
                    reason: cancelReason.trim(),
                });
                if (res.success) {
                    showAlert('Đã hủy hóa đơn thành công.', 'Thành công', 'success');
                    setCancelModalOpen(false);
                    fetchKpi();
                    fetchInvoices();
                }
            } catch (err: any) {
                showAlert(err?.message || 'Không thể hủy hóa đơn.', 'Lỗi hủy hóa đơn', 'error');
            } finally {
                setCancellingInvoice(false);
            }
        });
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
                        <h2>Quản lý Hóa đơn & Thu ngân</h2>
                        <div style={{ fontSize: '0.85rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                            Tiếp nhận thanh toán tại quầy và theo dõi dòng tiền phòng khám (Dữ liệu demo)
                        </div>
                    </div>
                </div>

                <div className={styles.headerActions}>
                    <button type="button" className="btn-secondary" onClick={handleRefresh} title="Làm mới dữ liệu">
                        <RefreshCw size={15} /> Làm mới
                    </button>
                    <button type="button" className="btn-primary" onClick={handleOpenCreateModal}>
                        <Plus size={16} /> Lập hóa đơn mới
                    </button>
                </div>
            </div>

            {/* KPI Cards */}
            <div className={styles.kpiGrid}>
                <div className={styles.kpiCard}>
                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-warning-bg)', color: 'var(--c-warning)' }}>
                        <Clock size={24} />
                    </div>
                    <div className={styles.kpiContent}>
                        <span className={styles.kpiLabel}>Chờ thanh toán</span>
                        <span className={styles.kpiValue}>{kpi ? kpi.todayUnpaidInvoices : 0}</span>
                    </div>
                </div>

                <div className={styles.kpiCard}>
                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-success-bg)', color: 'var(--c-success)' }}>
                        <CheckCircle size={24} />
                    </div>
                    <div className={styles.kpiContent}>
                        <span className={styles.kpiLabel}>Đã thu hôm nay</span>
                        <span className={styles.kpiValue}>{kpi ? kpi.todayPaidInvoices : 0}</span>
                    </div>
                </div>

                <div className={styles.kpiCard}>
                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-danger-bg)', color: 'var(--c-danger)' }}>
                        <XCircle size={24} />
                    </div>
                    <div className={styles.kpiContent}>
                        <span className={styles.kpiLabel}>Đã hủy hôm nay</span>
                        <span className={styles.kpiValue}>{kpi ? kpi.todayCancelledInvoices : 0}</span>
                    </div>
                </div>

                <div className={styles.kpiCard}>
                    <div className={styles.kpiIconWrapper} style={{ backgroundColor: 'var(--c-info-bg)', color: 'var(--c-info)' }}>
                        <DollarSign size={24} />
                    </div>
                    <div className={styles.kpiContent}>
                        <span className={styles.kpiLabel}>Thực thu hôm nay</span>
                        <span className={styles.kpiValue}>{kpi ? formatCurrency(kpi.todayRevenue) : '0 ₫'}</span>
                    </div>
                </div>
            </div>

            {/* Main Tabs */}
            <div style={{ display: 'flex', gap: '8px', marginBottom: '16px' }}>
                <button
                    type="button"
                    className={billingTab === 'invoices' ? 'btn-primary' : 'btn-secondary'}
                    onClick={() => setBillingTab('invoices')}
                    style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                    <Receipt size={16} /> Danh sách Hóa đơn ({totalItems})
                </button>
                <button
                    type="button"
                    className={billingTab === 'unbilled' ? 'btn-primary' : 'btn-secondary'}
                    onClick={() => setBillingTab('unbilled')}
                    style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                    <Clock size={16} /> Hàng đợi chờ lập hóa đơn ({unbilledTotalCount})
                </button>
            </div>

            {billingTab === 'invoices' ? (
                <>
                    {/* Filter Bar */}
                    <div className={styles.filterCard}>
                        <form onSubmit={handleSearch} className={styles.filterForm}>
                            <div className={styles.searchBox}>
                                <Search size={18} className={styles.searchIcon} />
                                <input
                                    type="text"
                                    className={`form-input ${styles.searchInput}`}
                                    placeholder="Mã HĐ, mã khám, tên hoặc SĐT bệnh nhân..."
                                    value={search}
                                    onChange={(e) => setSearch(e.target.value)}
                                />
                            </div>

                            <div style={{ width: '160px' }}>
                                <select
                                    className="form-select"
                                    value={statusFilter}
                                    onChange={(e) => {
                                        setStatusFilter(e.target.value);
                                        setPage(1);
                                    }}
                                >
                                    <option value="">Tất cả trạng thái</option>
                                    <option value="1">Chờ thanh toán</option>
                                    <option value="2">Đã thanh toán</option>
                                    <option value="3">Đã hủy</option>
                                </select>
                            </div>

                            <div style={{ width: '160px' }}>
                                <select
                                    className="form-select"
                                    value={sourceFilter}
                                    onChange={(e) => {
                                        setSourceFilter(e.target.value);
                                        setPage(1);
                                    }}
                                >
                                    <option value="">Tất cả nguồn thu</option>
                                    <option value="1">Lịch khám chuyên khoa</option>
                                    <option value="2">Gói khám sức khỏe</option>
                                </select>
                            </div>

                            <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                                <input
                                    type="date"
                                    className="form-input"
                                    style={{ width: '135px' }}
                                    value={fromDate}
                                    onChange={(e) => {
                                        setFromDate(e.target.value);
                                        setPage(1);
                                    }}
                                    title="Từ ngày"
                                />
                                <span>-</span>
                                <input
                                    type="date"
                                    className="form-input"
                                    style={{ width: '135px' }}
                                    value={toDate}
                                    onChange={(e) => {
                                        setToDate(e.target.value);
                                        setPage(1);
                                    }}
                                    title="Đến ngày"
                                />
                            </div>

                            <button type="submit" className="btn-secondary">
                                <Search size={14} /> Tìm
                            </button>
                        </form>
                    </div>

                    {/* Table Card */}
                    <div className={styles.tableCard}>
                        {loading ? (
                            <div className={styles.loadingWrapper}>
                                <RefreshCw className={styles.spin} size={28} />
                                <span style={{ marginTop: '10px', color: 'var(--c-muted)' }}>Đang tải danh sách hóa đơn...</span>
                            </div>
                        ) : (
                            <div className={styles.tableResponsive}>
                                <table className={styles.table}>
                                    <thead>
                                        <tr>
                                            <th>Mã HĐ</th>
                                            <th>Bệnh nhân</th>
                                            <th>Nguồn thu</th>
                                            <th>Mã liên kết</th>
                                            <th style={{ textAlign: 'right' }}>Tổng tiền</th>
                                            <th>Trạng thái</th>
                                            <th>Ngày lập</th>
                                            <th style={{ textAlign: 'right' }}>Thao tác</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {invoices.length === 0 ? (
                                            <tr>
                                                <td colSpan={8} className={styles.emptyState}>
                                                    Không tìm thấy hóa đơn nào phù hợp với bộ lọc.
                                                </td>
                                            </tr>
                                        ) : (
                                            invoices.map((inv) => (
                                                <tr key={inv.id}>
                                                    <td>
                                                        <span className={styles.codeBadge}>{inv.invoiceCode}</span>
                                                    </td>
                                                    <td>
                                                        <div style={{ fontWeight: 600, color: 'var(--c-text)' }}>
                                                            {inv.patientName}
                                                        </div>
                                                        <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>
                                                            {inv.patientPhone || 'Không có SĐT'}
                                                        </div>
                                                    </td>
                                                    <td>
                                                        <span style={{ fontSize: '0.85rem' }}>{inv.sourceTypeName}</span>
                                                    </td>
                                                    <td>
                                                        <span style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>
                                                            {inv.visitCode || inv.appointmentCode || inv.registrationCode || '---'}
                                                        </span>
                                                    </td>
                                                    <td style={{ textAlign: 'right', fontWeight: 700, color: 'var(--c-primary)' }}>
                                                        {formatCurrency(inv.totalAmount)}
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
                                                        <div className={styles.actionBtnGroup}>
                                                            <button
                                                                type="button"
                                                                className="btn-secondary"
                                                                style={{ padding: '4px 10px', fontSize: '0.8rem' }}
                                                                onClick={() => handleViewDetail(inv.id)}
                                                                title="Xem chi tiết & In phiếu thu"
                                                            >
                                                                <Eye size={13} /> Xem
                                                            </button>

                                                            {inv.status === InvoiceStatus.Unpaid && (
                                                                <>
                                                                    <button
                                                                        type="button"
                                                                        className="btn-primary"
                                                                        style={{ padding: '4px 10px', fontSize: '0.8rem' }}
                                                                        onClick={() => handleOpenPaymentModal(inv)}
                                                                        title="Thu tiền hóa đơn"
                                                                    >
                                                                        <CreditCard size={13} /> Thu tiền
                                                                    </button>

                                                                    <button
                                                                        type="button"
                                                                        className="btn-danger"
                                                                        style={{ padding: '4px 10px', fontSize: '0.8rem' }}
                                                                        onClick={() => handleOpenCancelModal(inv)}
                                                                        title="Hủy hóa đơn"
                                                                    >
                                                                        <Ban size={13} /> Hủy
                                                                    </button>
                                                                </>
                                                            )}
                                                        </div>
                                                    </td>
                                                </tr>
                                            ))
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        )}

                        {/* Pagination */}
                        {totalItems > pageSize && (
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px 20px', borderTop: '1px solid var(--c-border)' }}>
                                <span style={{ fontSize: '0.85rem', color: 'var(--c-muted)' }}>
                                    Hiển thị trang {page} / {totalPages} (Tổng {totalItems} hóa đơn)
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
                </>
            ) : (
                <div className={styles.tableCard}>
                    <div style={{ padding: '16px 20px', borderBottom: '1px solid var(--c-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div>
                            <h3 style={{ fontSize: '1rem', fontWeight: 600, color: 'var(--c-text)', margin: 0 }}>
                                Hàng đợi ca khám có chi phí chờ lập hóa đơn ({unbilledTotalCount})
                            </h3>
                            <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)', marginTop: '2px' }}>
                                Bao gồm công khám, cận lâm sàng chỉ định và đơn thuốc đã xác nhận mua
                            </div>
                        </div>
                        <button type="button" className="btn-secondary" onClick={() => fetchUnbilledVisits(unbilledPage)} disabled={unbilledLoading} style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                            <RefreshCw size={14} className={unbilledLoading ? styles.spin : ''} /> Làm mới hàng đợi
                        </button>
                    </div>
                    {unbilledLoading ? (
                        <div className={styles.loadingWrapper}>
                            <RefreshCw className={styles.spin} size={28} />
                            <span style={{ marginTop: '10px', color: 'var(--c-muted)' }}>Đang tải hàng đợi ca khám...</span>
                        </div>
                    ) : (
                        <>
                            <div className={styles.tableResponsive}>
                            <table className={styles.table}>
                                <thead>
                                    <tr>
                                        <th>Mã lượt khám</th>
                                        <th>Bệnh nhân</th>
                                        <th>Khoa / Bác sĩ</th>
                                        <th>Ngày khám</th>
                                        <th>Trạng thái</th>
                                        <th style={{ textAlign: 'center' }}>Mục chờ thu</th>
                                        <th style={{ textAlign: 'right' }}>Tạm tính</th>
                                        <th style={{ textAlign: 'right' }}>Thao tác</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {unbilledVisits.length === 0 ? (
                                        <tr>
                                            <td colSpan={8} className={styles.emptyState}>
                                                Hiện không có lượt khám nào chờ lập hóa đơn.
                                            </td>
                                        </tr>
                                    ) : (
                                        unbilledVisits.map((v) => (
                                            <tr key={v.visitId}>
                                                <td>
                                                    <span className={styles.codeBadge}>{v.visitCode}</span>
                                                </td>
                                                <td>
                                                    <div style={{ fontWeight: 600, color: 'var(--c-text)' }}>{v.patientName}</div>
                                                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>
                                                        {v.medicalRecordNumber ? `MRN: ${v.medicalRecordNumber}` : ''}
                                                        {v.phoneNumber ? ` • ${v.phoneNumber}` : ''}
                                                    </div>
                                                </td>
                                                <td>
                                                    <div style={{ fontSize: '0.85rem', color: 'var(--c-text)' }}>{v.departmentName}</div>
                                                    <div style={{ fontSize: '0.8rem', color: 'var(--c-muted)' }}>BS: {v.doctorName || 'Chưa gán'}</div>
                                                </td>
                                                <td>
                                                    <div style={{ fontSize: '0.85rem', color: 'var(--c-text)' }}>{v.visitDate}</div>
                                                </td>
                                                <td>
                                                    <span className="badge badge-info">{v.status}</span>
                                                </td>
                                                <td style={{ textAlign: 'center' }}>
                                                    <span style={{ background: '#f1f5f9', color: '#475569', padding: '2px 8px', borderRadius: '12px', fontSize: '0.8rem', fontWeight: 600 }}>
                                                        {v.unbilledItemCount} mục
                                                    </span>
                                                </td>
                                                <td style={{ textAlign: 'right', fontWeight: 700, color: 'var(--c-primary)' }}>
                                                    {formatCurrency(v.estimatedTotal)}
                                                </td>
                                                <td style={{ textAlign: 'right' }}>
                                                    <button
                                                        type="button"
                                                        className="btn-primary"
                                                        style={{ padding: '6px 14px', fontSize: '0.85rem' }}
                                                        onClick={() => handleCreateInvoiceFromUnbilled(v.visitId)}
                                                        disabled={creatingInvoice}
                                                    >
                                                        <Plus size={14} /> Lập hóa đơn
                                                    </button>
                                                </td>
                                            </tr>
                                        ))
                                    )}
                                </tbody>
                            </table>
                        </div>
                        {unbilledTotalPages > 1 && (
                            <div className={styles.pagination}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    disabled={unbilledPage <= 1 || unbilledLoading}
                                    onClick={() => {
                                        const prev = unbilledPage - 1;
                                        setUnbilledPage(prev);
                                        fetchUnbilledVisits(prev);
                                    }}
                                >
                                    Trang trước
                                </button>
                                <span className={styles.pageInfo}>
                                    Trang {unbilledPage} / {unbilledTotalPages} (Tổng {unbilledTotalCount} ca khám)
                                </span>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    disabled={unbilledPage >= unbilledTotalPages || unbilledLoading}
                                    onClick={() => {
                                        const next = unbilledPage + 1;
                                        setUnbilledPage(next);
                                        fetchUnbilledVisits(next);
                                    }}
                                >
                                    Trang sau
                                </button>
                            </div>
                        )}
                        </>
                    )}
                </div>
            )}

            {/* Create Invoice Modal */}
            {createModalOpen && (
                <div className={styles.modalOverlay} onClick={() => setCreateModalOpen(false)}>
                    <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
                        <div className={styles.modalHeader}>
                            <h3>Lập hóa đơn mới</h3>
                            <button type="button" className="btn-secondary" style={{ padding: '4px 8px' }} onClick={() => setCreateModalOpen(false)}>
                                <X size={16} />
                            </button>
                        </div>
                        <form onSubmit={handleCreateInvoice}>
                            <div className={styles.modalBody}>
                                <div className="form-group">
                                    <label className="form-label">Chọn nguồn tạo hóa đơn</label>
                                    <div className={styles.radioGroup}>
                                         <label className={styles.radioLabel}>
                                             <input
                                                 type="radio"
                                                 name="sourceType"
                                                 checked={createSourceType === 'visit'}
                                                 onChange={() => setCreateSourceType('visit')}
                                             />
                                             Lượt khám ngoại trú (Visit)
                                         </label>
                                         <label className={styles.radioLabel}>
                                             <input
                                                 type="radio"
                                                 name="sourceType"
                                                 checked={createSourceType === 'appointment'}
                                                 onChange={() => setCreateSourceType('appointment')}
                                             />
                                             Lịch khám bệnh (Appointment)
                                         </label>
                                         <label className={styles.radioLabel}>
                                             <input
                                                 type="radio"
                                                 name="sourceType"
                                                 checked={createSourceType === 'package'}
                                                 onChange={() => setCreateSourceType('package')}
                                             />
                                             Gói khám sức khỏe (Đã xác nhận)
                                         </label>
                                     </div>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">
                                        {createSourceType === 'visit'
                                            ? 'Patient Visit ID (Mã ID lượt khám) *'
                                            : createSourceType === 'appointment'
                                            ? 'Appointment ID (Mã lịch hẹn) *'
                                            : 'Health Package Registration ID *'}
                                    </label>
                                    <input
                                        type="number"
                                        className="form-input"
                                        placeholder={
                                            createSourceType === 'visit'
                                                ? 'Ví dụ: 1'
                                                : createSourceType === 'appointment'
                                                ? 'Ví dụ: 10'
                                                : 'Ví dụ: 5'
                                        }
                                        value={referenceId}
                                        onChange={(e) => setReferenceId(e.target.value)}
                                        required
                                        min="1"
                                    />
                                    <span style={{ fontSize: '0.8rem', color: 'var(--c-muted)', display: 'block', marginTop: '4px' }}>
                                        {createSourceType === 'visit'
                                            ? 'Lượt khám ngoại trú đã hoàn tất khám, chỉ định cận lâm sàng và cấp thuốc (In-Billing hoặc sẵn sàng thanh toán).'
                                            : createSourceType === 'appointment'
                                            ? 'Lịch hẹn phải ở trạng thái "Completed" và chưa có hóa đơn còn hiệu lực.'
                                            : 'Đăng ký gói khám phải ở trạng thái "Confirmed" và chưa có hóa đơn còn hiệu lực.'}
                                    </span>
                                </div>
                            </div>
                            <div className={styles.modalFooter}>
                                <button type="button" className="btn-secondary" onClick={() => setCreateModalOpen(false)}>
                                    Hủy bỏ
                                </button>
                                <button type="submit" className="btn-primary" disabled={creatingInvoice}>
                                    {creatingInvoice ? 'Đang lập hóa đơn...' : 'Tạo hóa đơn'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Process Payment Modal */}
            {paymentModalOpen && paymentInvoice && (
                <div className={styles.modalOverlay} onClick={() => !processingPayment && setPaymentModalOpen(false)}>
                    <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
                        <div className={styles.modalHeader}>
                            <h3>Thu tiền hóa đơn {paymentInvoice.invoiceCode}</h3>
                            <button
                                type="button"
                                className="btn-secondary"
                                style={{ padding: '4px 8px' }}
                                disabled={processingPayment}
                                onClick={() => setPaymentModalOpen(false)}
                            >
                                <X size={16} />
                            </button>
                        </div>
                        <form onSubmit={handleProcessPayment}>
                            <div className={styles.modalBody}>
                                <div style={{ background: 'var(--c-bg)', padding: '12px 16px', borderRadius: '8px', marginBottom: '16px', fontSize: '0.875rem' }}>
                                    <div><strong>Bệnh nhân:</strong> {paymentInvoice.patientName} ({paymentInvoice.patientPhone})</div>
                                    <div style={{ marginTop: '4px' }}>
                                        <strong>Tổng số tiền cần thu:</strong>{' '}
                                        <span style={{ color: 'var(--c-primary)', fontWeight: 800, fontSize: '1.05rem' }}>
                                            {formatCurrency(paymentInvoice.totalAmount)}
                                        </span>
                                    </div>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Phương thức thanh toán *</label>
                                    <select
                                        className="form-select"
                                        value={paymentMethod}
                                        onChange={(e) => setPaymentMethod(parseInt(e.target.value, 10) as PaymentMethod)}
                                    >
                                        <option value={PaymentMethod.Cash}>Tiền mặt tại quầy (Cash)</option>
                                        <option value={PaymentMethod.ManualBankTransfer}>Chuyển khoản trực tiếp (ManualBankTransfer)</option>
                                    </select>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Số tiền khách thanh toán (VNĐ) *</label>
                                    <input
                                        type="number"
                                        className="form-input"
                                        value={paymentAmount}
                                        onChange={(e) => setPaymentAmount(e.target.value)}
                                        required
                                        min="0"
                                    />
                                    <span style={{ fontSize: '0.75rem', color: 'var(--c-muted)', display: 'block', marginTop: '2px' }}>
                                        Hệ thống yêu cầu thu đúng toàn bộ giá trị hóa đơn ({formatCurrency(paymentInvoice.totalAmount)}).
                                    </span>
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Mã giao dịch / Mã tham chiếu ngân hàng</label>
                                    <input
                                        type="text"
                                        className="form-input"
                                        placeholder={paymentMethod === PaymentMethod.ManualBankTransfer ? 'Ví dụ: FT26090612345' : 'Tùy chọn'}
                                        value={paymentRefCode}
                                        onChange={(e) => setPaymentRefCode(e.target.value)}
                                        maxLength={100}
                                    />
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Ghi chú thu tiền</label>
                                    <textarea
                                        className="form-input"
                                        rows={2}
                                        placeholder="Ghi chú thêm nếu có..."
                                        value={paymentNote}
                                        onChange={(e) => setPaymentNote(e.target.value)}
                                        maxLength={500}
                                    />
                                </div>
                            </div>

                            <div className={styles.modalFooter}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    disabled={processingPayment}
                                    onClick={() => setPaymentModalOpen(false)}
                                >
                                    Đóng
                                </button>
                                <button
                                    type="submit"
                                    className="btn-primary"
                                    disabled={processingPayment}
                                >
                                    {processingPayment ? 'Đang xử lý...' : 'Xác nhận đã thu tiền'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Cancel Invoice Modal */}
            {cancelModalOpen && cancelInvoiceTarget && (
                <div className={styles.modalOverlay} onClick={() => !cancellingInvoice && setCancelModalOpen(false)}>
                    <div className={styles.modalContent} onClick={(e) => e.stopPropagation()}>
                        <div className={styles.modalHeader}>
                            <h3 style={{ color: 'var(--c-danger)' }}>Hủy hóa đơn {cancelInvoiceTarget.invoiceCode}</h3>
                            <button
                                type="button"
                                className="btn-secondary"
                                style={{ padding: '4px 8px' }}
                                disabled={cancellingInvoice}
                                onClick={() => setCancelModalOpen(false)}
                            >
                                <X size={16} />
                            </button>
                        </div>
                        <form onSubmit={handleCancelInvoice}>
                            <div className={styles.modalBody}>
                                <div style={{ background: 'var(--c-danger-bg)', border: '1px solid #fecaca', padding: '12px 16px', borderRadius: '8px', marginBottom: '16px', color: 'var(--c-danger)', fontSize: '0.875rem' }}>
                                    <strong>Cảnh báo:</strong> Việc hủy hóa đơn là vĩnh viễn và không thể hoàn tác. Hóa đơn đã hủy sẽ không thể thu tiền.
                                </div>

                                <div className="form-group">
                                    <label className="form-label">Lý do hủy hóa đơn *</label>
                                    <textarea
                                        className="form-input"
                                        rows={3}
                                        placeholder="Nhập chi tiết lý do hủy (ví dụ: bệnh nhân đổi ý, sai thông tin...)"
                                        value={cancelReason}
                                        onChange={(e) => setCancelReason(e.target.value)}
                                        required
                                        maxLength={500}
                                    />
                                </div>
                            </div>
                            <div className={styles.modalFooter}>
                                <button
                                    type="button"
                                    className="btn-secondary"
                                    disabled={cancellingInvoice}
                                    onClick={() => setCancelModalOpen(false)}
                                >
                                    Quay lại
                                </button>
                                <button
                                    type="submit"
                                    className="btn-danger"
                                    disabled={cancellingInvoice || !cancelReason.trim()}
                                >
                                    {cancellingInvoice ? 'Đang hủy...' : 'Xác nhận hủy hóa đơn'}
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Invoice Receipt Modal */}
            <InvoiceReceiptModal
                isOpen={detailModalOpen}
                onClose={() => setDetailModalOpen(false)}
                invoice={selectedInvoice}
                loading={detailLoading}
            />
        </div>
    );
};
