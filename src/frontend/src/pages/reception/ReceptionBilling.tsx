import { Button, Input, Select, Radio, Modal } from 'antd';
import React, { useState, useEffect, useCallback, useRef } from 'react';
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
} from 'lucide-react';
import { billingApi } from '../../api/billingApi';
import axiosClient from '../../api/axiosClient';
import type {
    InvoiceDto,
    InvoiceDetailDto,
    BillingKpiDto,
    InvoiceSourceType,
    UnbilledVisitDto,
    PagedBillingResult,
    ApiResponse,
} from '../../types';
import {
    InvoiceStatus,
    PaymentMethod,
} from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { InvoiceReceiptModal } from '../../components/billing/InvoiceReceiptModal';
import { DataTable, LoadingState, StatusBadge, PageHeader, StatCard, InlineError } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import { getVisitStatusLabel } from '../../utils/visitStatusLabels';
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
    const [invoiceSourceOptions, setInvoiceSourceOptions] = useState<{ value: string; label: string }[]>([]);
    const [invoiceSourcesLoading, setInvoiceSourcesLoading] = useState(false);
    const [invoiceSourcesError, setInvoiceSourcesError] = useState('');
    const invoiceSourceRequest = useRef(0);

    const loadInvoiceSources = useCallback(async (source: 'visit' | 'appointment' | 'package') => {
        const request = ++invoiceSourceRequest.current;
        setInvoiceSourcesLoading(true);
        setInvoiceSourcesError('');
        setInvoiceSourceOptions([]);
        setReferenceId('');
        const options: { value: string; label: string }[] = [];
        const label = (code: string, name: string, date: string) => {
            const parsed = new Date(date);
            return `${code} — ${name} — ${Number.isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString('vi-VN')}`;
        };
        try {
            let currentPage = 1;
            let pages = 1;
            do {
                if (source === 'visit') {
                    const response = await billingApi.reception.getUnbilledVisits(undefined, currentPage, 10);
                    if (!response.success || !response.data) throw new Error('Không thể tải danh sách nguồn hóa đơn.');
                    const data = response.data;
                    const visits: UnbilledVisitDto[] = Array.isArray(data) ? data : data.items;
                    options.push(...visits.map(visit => ({ value: String(visit.visitId), label: label(visit.visitCode, visit.patientName, visit.visitDate) })));
                    pages = Array.isArray(data) ? 1 : data.totalPages ?? Math.ceil(data.totalItems / 10);
                } else {
                    const endpoint = source === 'appointment' ? '/reception/appointments' : '/reception/health-package-registrations';
                    const status = source === 'appointment' ? 'Completed' : 'Confirmed';
                    const params = new URLSearchParams({ status, page: String(currentPage), pageSize: '10' });
                    const response = await axiosClient.get<unknown, ApiResponse<{
                        items: { id: number; appointmentCode?: string; registrationCode?: string; patientName: string; appointmentDate?: string; preferredDate?: string; status: string }[];
                        totalItems: number;
                        totalPages?: number;
                    }>>(`${endpoint}?${params}`);
                    if (!response.success || !response.data) throw new Error('Không thể tải danh sách nguồn hóa đơn.');
                    options.push(...response.data.items.filter(item => item.status === status).map(item => ({
                        value: String(item.id),
                        label: label(source === 'appointment' ? item.appointmentCode || '---' : item.registrationCode || '---', item.patientName, source === 'appointment' ? item.appointmentDate || '' : item.preferredDate || ''),
                    })));
                    pages = response.data.totalPages ?? Math.ceil(response.data.totalItems / 10);
                }
                if (request !== invoiceSourceRequest.current) return;
                currentPage += 1;
            } while (currentPage <= pages);
            setInvoiceSourceOptions(options);
        } catch {
            if (request === invoiceSourceRequest.current) setInvoiceSourcesError('Không thể tải danh sách nguồn hóa đơn.');
        } finally {
            if (request === invoiceSourceRequest.current) setInvoiceSourcesLoading(false);
        }
    }, []);

    useEffect(() => () => { invoiceSourceRequest.current += 1; }, []);

    const changeInvoiceSource = (source: 'visit' | 'appointment' | 'package') => {
        if (source === createSourceType) return;
        setCreateSourceType(source);
        void loadInvoiceSources(source);
    };

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
        void loadInvoiceSources('visit');
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
            case InvoiceStatus.Paid: return <StatusBadge status="Completed" label="Đã thanh toán" />;
            case InvoiceStatus.Unpaid: return <StatusBadge status="Pending" label="Chờ thanh toán" />;
            case InvoiceStatus.Cancelled: return <StatusBadge status="Cancelled" label="Đã hủy" />;
            default: return null;
        }
    };

    const totalPages = Math.ceil(totalItems / pageSize) || 1;

    const invoiceColumns: DataTableColumn<InvoiceDto>[] = [
        {
            header: 'Mã HĐ',
            accessor: (inv) => <span className={styles.codeBadge}>{inv.invoiceCode}</span>,
        },
        {
            header: 'Bệnh nhân',
            accessor: (inv) => (
                <>
                    <div className={styles.primaryText}>{inv.patientName}</div>
                    <div className={styles.secondaryText}>{inv.patientPhone || 'Không có SĐT'}</div>
                </>
            ),
        },
        { header: 'Nguồn thu', accessor: 'sourceTypeName' },
        {
            header: 'Mã liên kết',
            accessor: (inv) => (
                <span className={styles.mutedText}>
                    {inv.visitCode || inv.appointmentCode || inv.registrationCode || '---'}
                </span>
            ),
        },
        {
            header: 'Tổng tiền',
            align: 'right',
            accessor: (inv) => <span className={styles.amount}>{formatCurrency(inv.totalAmount)}</span>,
        },
        { header: 'Trạng thái', accessor: (inv) => renderStatusBadge(inv.status) },
        { header: 'Ngày lập', accessor: (inv) => formatDateTime(inv.createdAtUtc) },
        {
            header: 'Thao tác',
            align: 'right',
            accessor: (inv) => (
                <div className={styles.actionBtnGroup}>
                    <Button
                        htmlType="button"
                        className={styles.rowActionBtn}
                        onClick={() => handleViewDetail(inv.id)}
                        title="Xem chi tiết & In phiếu thu"
                    >
                        <Eye size={13} /> Xem
                    </Button>

                    {inv.status === InvoiceStatus.Unpaid && (
                        <>
                            <Button
                                htmlType="button"
                                type="primary" className={styles.rowActionBtn}
                                onClick={() => handleOpenPaymentModal(inv)}
                                title="Thu tiền hóa đơn"
                            >
                                <CreditCard size={13} /> Thu tiền
                            </Button>

                            <Button
                                htmlType="button"
                                danger className={styles.rowActionBtn}
                                onClick={() => handleOpenCancelModal(inv)}
                                title="Hủy hóa đơn"
                            >
                                <Ban size={13} /> Hủy
                            </Button>
                        </>
                    )}
                </div>
            ),
        },
    ];

    const unbilledColumns: DataTableColumn<UnbilledVisitDto>[] = [
        {
            header: 'Mã lượt khám',
            accessor: (v) => <span className={styles.codeBadge}>{v.visitCode}</span>,
        },
        {
            header: 'Bệnh nhân',
            accessor: (v) => (
                <>
                    <div className={styles.primaryText}>{v.patientName}</div>
                    <div className={styles.secondaryText}>
                        {v.medicalRecordNumber ? `MRN: ${v.medicalRecordNumber}` : ''}
                        {v.phoneNumber ? ` • ${v.phoneNumber}` : ''}
                    </div>
                </>
            ),
        },
        {
            header: 'Khoa / Bác sĩ',
            accessor: (v) => (
                <>
                    <div>{v.departmentName}</div>
                    <div className={styles.secondaryText}>BS: {v.doctorName || 'Chưa gán'}</div>
                </>
            ),
        },
        { header: 'Ngày khám', accessor: 'visitDate' },
        {
            header: 'Trạng thái',
            accessor: (v) => (
                <StatusBadge
                    status={v.status}
                    label={getVisitStatusLabel(v.status)}
                />
            ),
        },
        {
            header: 'Mục chờ thu',
            align: 'center',
            accessor: (v) => <span className={styles.countPill}>{v.unbilledItemCount} mục</span>,
        },
        {
            header: 'Tạm tính',
            align: 'right',
            accessor: (v) => <span className={styles.amount}>{formatCurrency(v.estimatedTotal)}</span>,
        },
        {
            header: 'Thao tác',
            align: 'right',
            accessor: (v) => (
                <Button
                    htmlType="button"
                    type="primary" className={styles.rowActionBtn}
                    onClick={() => handleCreateInvoiceFromUnbilled(v.visitId)}
                    disabled={creatingInvoice}
                >
                    <Plus size={14} /> Lập hóa đơn
                </Button>
            ),
        },
    ];

    return (
        <div className={styles.container}>
            <PageHeader title="Quản lý Hóa đơn & Thu ngân" subtitle="Thu tiền tại quầy và theo dõi hóa đơn của phòng khám" badge={<Receipt size={28} className={styles.primaryIcon} />} actions={<><Button onClick={handleRefresh} title="Làm mới dữ liệu" icon={<RefreshCw size={15} />}>Làm mới</Button><Button type="primary" onClick={handleOpenCreateModal} icon={<Plus size={16} />}>Lập hóa đơn mới</Button></>} />
            <div className={styles.kpiGrid}>
                <StatCard title="Chờ thanh toán" value={kpi ? kpi.todayUnpaidInvoices : 0} icon={<Clock size={24} />} color="warning" />
                <StatCard title="Đã thu hôm nay" value={kpi ? kpi.todayPaidInvoices : 0} icon={<CheckCircle size={24} />} color="success" />
                <StatCard title="Đã hủy hôm nay" value={kpi ? kpi.todayCancelledInvoices : 0} icon={<XCircle size={24} />} color="danger" />
                <StatCard title="Thực thu hôm nay" value={kpi ? formatCurrency(kpi.todayRevenue) : '0 ₫'} icon={<DollarSign size={24} />} color="info" />
            </div>
            <div className={styles.tabs}><Button type={billingTab === 'invoices' ? 'primary' : 'default'} onClick={() => setBillingTab('invoices')} icon={<Receipt size={16} />}>Danh sách Hóa đơn ({totalItems})</Button><Button type={billingTab === 'unbilled' ? 'primary' : 'default'} onClick={() => setBillingTab('unbilled')} icon={<Clock size={16} />}>Hàng đợi chờ lập hóa đơn ({unbilledTotalCount})</Button></div>
            {billingTab === 'invoices' ? <>
                <form onSubmit={handleSearch} className={styles.filterForm}>
                    <Input className={styles.searchInput} prefix={<Search size={18} />} placeholder="Mã HĐ, mã khám, tên hoặc SĐT bệnh nhân..." value={search} onChange={e => setSearch(e.target.value)} />
                    <Select className={styles.filterSelect} value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả trạng thái'},{value:'1',label:'Chờ thanh toán'},{value:'2',label:'Đã thanh toán'},{value:'3',label:'Đã hủy'}]} />
                    <Select className={styles.filterSelect} value={sourceFilter} onChange={value => { setSourceFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả nguồn thu'},{value:'1',label:'Lịch khám chuyên khoa'},{value:'2',label:'Gói khám sức khỏe'}]} />
                    <div className={styles.dateRange}><Input type="date" className={styles.dateInput} value={fromDate} onChange={e => { setFromDate(e.target.value); setPage(1); }} title="Từ ngày" /><span>-</span><Input type="date" className={styles.dateInput} value={toDate} onChange={e => { setToDate(e.target.value); setPage(1); }} title="Đến ngày" /></div>
                    <Button htmlType="submit" icon={<Search size={14} />}>Tìm</Button>
                </form>
                <div className={styles.tableCard}>{loading ? <LoadingState message="Đang tải danh sách hóa đơn..." /> : <div className={styles.tableScroll}><DataTable columns={invoiceColumns} data={invoices} keyExtractor={inv => inv.id} emptyText="Không tìm thấy hóa đơn nào phù hợp với bộ lọc." /></div>}
                    {totalItems > pageSize && <div className={styles.pagination}><span className={styles.pageInfo}>Hiển thị trang {page} / {totalPages} (Tổng {totalItems} hóa đơn)</span><div className={styles.paginationActions}><Button disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Trang trước</Button><Button disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>Trang sau</Button></div></div>}
                </div>
            </> : <div className={styles.tableCard}><div className={styles.queueHeader}><div><h3 className={styles.queueTitle}>Hàng đợi ca khám có chi phí chờ lập hóa đơn ({unbilledTotalCount})</h3><div className={styles.secondaryText}>Bao gồm công khám, cận lâm sàng chỉ định và đơn thuốc đã xác nhận mua</div></div><Button onClick={() => fetchUnbilledVisits(unbilledPage)} disabled={unbilledLoading} icon={<RefreshCw size={14} className={unbilledLoading ? styles.spin : ''} />}>Làm mới hàng đợi</Button></div>
                {unbilledLoading ? <LoadingState message="Đang tải hàng đợi ca khám..." /> : <><div className={styles.tableScroll}><DataTable columns={unbilledColumns} data={unbilledVisits} keyExtractor={v => v.visitId} emptyText="Hiện không có lượt khám nào chờ lập hóa đơn." /></div>{unbilledTotalPages > 1 && <div className={styles.pagination}><Button disabled={unbilledPage <= 1 || unbilledLoading} onClick={() => { const prev = unbilledPage - 1; setUnbilledPage(prev); fetchUnbilledVisits(prev); }}>Trang trước</Button><span className={styles.pageInfo}>Trang {unbilledPage} / {unbilledTotalPages} (Tổng {unbilledTotalCount} ca khám)</span><Button disabled={unbilledPage >= unbilledTotalPages || unbilledLoading} onClick={() => { const next = unbilledPage + 1; setUnbilledPage(next); fetchUnbilledVisits(next); }}>Trang sau</Button></div>}</>}
            </div>}
            {createModalOpen && <Modal open width={520} className={styles.formModal} onCancel={() => setCreateModalOpen(false)} title="Lập hóa đơn mới" footer={<><Button onClick={() => setCreateModalOpen(false)}>Hủy bỏ</Button><Button type="primary" htmlType="submit" form="reception-create-invoice" disabled={creatingInvoice || invoiceSourcesLoading || !!invoiceSourcesError || !invoiceSourceOptions.some(option => option.value === referenceId)}>{creatingInvoice ? 'Đang lập hóa đơn...' : 'Tạo hóa đơn'}</Button></>}>
                <form id="reception-create-invoice" onSubmit={handleCreateInvoice} className={styles.modalForm}>
                    <div className={styles.formField}><label className={styles.formLabel}>Chọn nguồn tạo hóa đơn</label><div className={styles.radioGroup}><Radio name="sourceType" checked={createSourceType === 'visit'} onChange={() => changeInvoiceSource('visit')}>Lượt khám ngoại trú (Visit)</Radio><Radio name="sourceType" checked={createSourceType === 'appointment'} onChange={() => changeInvoiceSource('appointment')}>Lịch khám bệnh (Appointment)</Radio><Radio name="sourceType" checked={createSourceType === 'package'} onChange={() => changeInvoiceSource('package')}>Gói khám sức khỏe (Đã xác nhận)</Radio></div></div>
                    <div className={styles.formField}>
                        <label htmlFor="invoice-source-reference" className={styles.formLabel}>{createSourceType === 'visit' ? 'Chọn lượt khám *' : createSourceType === 'appointment' ? 'Chọn lịch hẹn đã khám xong *' : 'Chọn đăng ký gói khám đã xác nhận *'}</label>
                        <Select id="invoice-source-reference" className={styles.invoiceSourceSelect} showSearch={{ optionFilterProp: 'label' }} value={referenceId || undefined} onChange={value => setReferenceId(value)} options={invoiceSourceOptions} loading={invoiceSourcesLoading} disabled={creatingInvoice} notFoundContent={invoiceSourcesLoading ? <LoadingState message="Đang tải danh sách nguồn hóa đơn..." height="auto" /> : 'Không có mục nào chờ lập hóa đơn'} />
                        {invoiceSourcesError && <InlineError title="" message={invoiceSourcesError} onRetry={() => { void loadInvoiceSources(createSourceType); }} />}
                        <span className={styles.fieldHint}>{createSourceType === 'visit' ? 'Lượt khám ngoại trú đã hoàn tất khám, chỉ định cận lâm sàng và cấp thuốc (In-Billing hoặc sẵn sàng thanh toán).' : createSourceType === 'appointment' ? 'Lịch hẹn phải ở trạng thái "Completed" và chưa có hóa đơn còn hiệu lực.' : 'Đăng ký gói khám phải ở trạng thái "Confirmed" và chưa có hóa đơn còn hiệu lực.'}</span></div>
                </form>
            </Modal>}
            {paymentModalOpen && paymentInvoice && <Modal open width={520} className={styles.formModal} onCancel={() => !processingPayment && setPaymentModalOpen(false)} closable={{disabled:processingPayment}} title={`Thu tiền hóa đơn ${paymentInvoice.invoiceCode}`} footer={<><Button disabled={processingPayment} onClick={() => setPaymentModalOpen(false)}>Đóng</Button><Button type="primary" htmlType="submit" form="reception-process-payment" disabled={processingPayment}>{processingPayment ? 'Đang xử lý...' : 'Xác nhận đã thu tiền'}</Button></>}>
                <form id="reception-process-payment" onSubmit={handleProcessPayment} className={styles.modalForm}>
                    <div className={styles.paymentSummary}><div><strong>Bệnh nhân:</strong> {paymentInvoice.patientName} ({paymentInvoice.patientPhone})</div><div className={styles.paymentTotal}><strong>Tổng số tiền cần thu:</strong>{' '}<span className={styles.amount}>{formatCurrency(paymentInvoice.totalAmount)}</span></div></div>
                    <div className={styles.formField}><label className={styles.formLabel}>Phương thức thanh toán *</label><Select className={styles.paymentSelect} value={paymentMethod} onChange={value => setPaymentMethod(value)} options={[{value:PaymentMethod.Cash,label:'Tiền mặt tại quầy (Cash)'},{value:PaymentMethod.ManualBankTransfer,label:'Chuyển khoản trực tiếp (ManualBankTransfer)'}]} /></div>
                    <div className={styles.formField}><label className={styles.formLabel}>Số tiền khách thanh toán (VNĐ) *</label><Input type="number" value={paymentAmount} onChange={e => setPaymentAmount(e.target.value)} required min="0" /><span className={styles.fieldHint}>Hệ thống yêu cầu thu đúng toàn bộ giá trị hóa đơn ({formatCurrency(paymentInvoice.totalAmount)}).</span></div>
                    <div className={styles.formField}><label className={styles.formLabel}>Mã giao dịch / Mã tham chiếu ngân hàng</label><Input type="text" placeholder={paymentMethod === PaymentMethod.ManualBankTransfer ? 'Ví dụ: FT26090612345' : 'Tùy chọn'} value={paymentRefCode} onChange={e => setPaymentRefCode(e.target.value)} maxLength={100} /></div>
                    <div className={styles.formField}><label className={styles.formLabel}>Ghi chú thu tiền</label><Input.TextArea rows={2} placeholder="Ghi chú thêm nếu có..." value={paymentNote} onChange={e => setPaymentNote(e.target.value)} maxLength={500} /></div>
                </form>
            </Modal>}
            {cancelModalOpen && cancelInvoiceTarget && <Modal open width={520} className={styles.formModal} onCancel={() => !cancellingInvoice && setCancelModalOpen(false)} closable={{disabled:cancellingInvoice}} title={<span className={styles.cancelTitle}>Hủy hóa đơn {cancelInvoiceTarget.invoiceCode}</span>} footer={<><Button disabled={cancellingInvoice} onClick={() => setCancelModalOpen(false)}>Quay lại</Button><Button type="primary" danger htmlType="submit" form="reception-cancel-invoice" disabled={cancellingInvoice || !cancelReason.trim()}>{cancellingInvoice ? 'Đang hủy...' : 'Xác nhận hủy hóa đơn'}</Button></>}>
                <form id="reception-cancel-invoice" onSubmit={handleCancelInvoice} className={styles.modalForm}><div className={styles.cancelWarning}><strong>Cảnh báo:</strong> Việc hủy hóa đơn là vĩnh viễn và không thể hoàn tác. Hóa đơn đã hủy sẽ không thể thu tiền.</div><div className={styles.formField}><label className={styles.formLabel}>Lý do hủy hóa đơn *</label><Input.TextArea rows={3} placeholder="Nhập chi tiết lý do hủy (ví dụ: bệnh nhân đổi ý, sai thông tin...)" value={cancelReason} onChange={e => setCancelReason(e.target.value)} required maxLength={500} /></div></form>
            </Modal>}
            <InvoiceReceiptModal isOpen={detailModalOpen} onClose={() => setDetailModalOpen(false)} invoice={selectedInvoice} loading={detailLoading} />
        </div>
    );
};
