import React, { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Segmented } from 'antd';
import { Eye, RefreshCw } from 'lucide-react';
import { billingApi } from '../../api/billingApi';
import type { InvoiceDto, InvoiceDetailDto } from '../../types';
import { InvoiceStatus } from '../../types';
import { InvoiceReceiptModal } from '../../components/billing/InvoiceReceiptModal';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader } from '../../components/common/PageHeader';
import { DataTable } from '../../components/common/DataTable';
import { StatusBadge } from '../../components/common/StatusBadge';
import { Pagination } from '../../components/common/Pagination';
import { EmptyState } from '../../components/common/EmptyState';
import { LoadingState } from '../../components/common/LoadingState';
import { InlineError } from '../../components/common/InlineError';
import { formatVndCurrency } from '../../utils/formatters';
import styles from './PatientInvoices.module.css';

export const PatientInvoices: React.FC = () => {
    const { showAlert } = useDialog();
    const [invoices, setInvoices] = useState<InvoiceDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [statusFilter, setStatusFilter] = useState<InvoiceStatus | undefined>();
    const [selectedInvoice, setSelectedInvoice] = useState<InvoiceDetailDto | null>(null);
    const [detailLoading, setDetailLoading] = useState(false);
    const [detailModalOpen, setDetailModalOpen] = useState(false);
    const fetchInvoices = useCallback(async () => {
        setLoading(true); setError('');
        try {
            const res = await billingApi.patient.getMyInvoices(page, 10, statusFilter);
            if (!res.success) throw new Error(res.message || 'Không thể tải danh sách hóa đơn của bạn.');
            setInvoices(res.data?.items || []); setTotalItems(res.data?.totalItems || 0);
        } catch (err: any) { setError(err?.message || 'Không thể tải danh sách hóa đơn của bạn.'); }
        finally { setLoading(false); }
    }, [page, statusFilter]);
    useEffect(() => { void fetchInvoices(); }, [fetchInvoices]);
    const handleViewDetail = async (id: number) => {
        setDetailModalOpen(true); setDetailLoading(true);
        try {
            const res = await billingApi.patient.getMyInvoiceDetail(id);
            if (res.success && res.data) setSelectedInvoice(res.data);
        } catch (err: any) {
            showAlert(err?.message || 'Không thể tải chi tiết hóa đơn.', 'Lỗi', 'error'); setDetailModalOpen(false);
        } finally { setDetailLoading(false); }
    };
    const statusBadge = (status: InvoiceStatus) => <StatusBadge status={status === InvoiceStatus.Paid ? 'Paid' : status === InvoiceStatus.Unpaid ? 'Unpaid' : 'Cancelled'} label={status === InvoiceStatus.Paid ? 'Đã thanh toán' : status === InvoiceStatus.Unpaid ? 'Chờ thanh toán' : 'Đã hủy'} />;
    return <div className={styles.page}>
        <PageHeader title="Hóa đơn dịch vụ của tôi" subtitle="Theo dõi chi phí khám chữa bệnh và lịch sử thanh toán tại ClinicCare AI" actions={<Button onClick={fetchInvoices} loading={loading} icon={<RefreshCw size={18} />}>Làm mới</Button>} />
        <Alert type="info" showIcon title={<div><strong>Hướng dẫn thanh toán:</strong> Quý khách vui lòng xuất trình mã hóa đơn hoặc số điện thoại tại <strong>Quầy thu ngân / Lễ tân</strong> của phòng khám để hoàn tất thanh toán bằng Tiền mặt hoặc Chuyển khoản ngân hàng.</div>} />
        <div className={styles.filters}><Segmented value={statusFilter ?? 'all'} options={[{ label: 'Tất cả hóa đơn', value: 'all' }, { label: 'Chờ thanh toán', value: InvoiceStatus.Unpaid }, { label: 'Đã thanh toán', value: InvoiceStatus.Paid }, { label: 'Đã hủy', value: InvoiceStatus.Cancelled }]} onChange={value => { setStatusFilter(value === 'all' ? undefined : value as InvoiceStatus); setPage(1); }} /><span>Tổng số: <strong>{totalItems}</strong> hóa đơn</span></div>
        {loading ? <LoadingState message="Đang tải hóa đơn của bạn..." /> : error ? <InlineError message={error} onRetry={fetchInvoices} /> : invoices.length === 0 ? <EmptyState title="Chưa có hóa đơn nào" description="Hóa đơn sẽ tự động xuất hiện khi bác sĩ hoàn thành lượt khám hoặc bạn đăng ký gói khám sức khỏe." /> : <>
            <DataTable data={invoices} keyExtractor={invoice => invoice.id} columns={[
                { header: 'Mã hóa đơn', accessor: invoice => <strong>{invoice.invoiceCode}</strong> },
                { header: 'Nguồn dịch vụ', accessor: invoice => <div>{invoice.sourceTypeName}<div className={styles.reference}>Mã tham chiếu: {invoice.appointmentCode || invoice.registrationCode || 'N/A'}</div></div> },
                { header: 'Số tiền', accessor: invoice => formatVndCurrency(invoice.totalAmount) },
                { header: 'Trạng thái', accessor: invoice => statusBadge(invoice.status) },
                { header: 'Thời gian lập', accessor: invoice => invoice.createdAtUtc ? new Date(invoice.createdAtUtc).toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '---' },
                { header: 'Chi tiết', accessor: invoice => <Button icon={<Eye size={16} />} onClick={() => handleViewDetail(invoice.id)}>Xem phiếu</Button> }
            ]} />
            <Pagination page={page} totalPages={Math.ceil(totalItems / 10) || 1} totalRecords={totalItems} onPageChange={setPage} />
        </>}
        <InvoiceReceiptModal isOpen={detailModalOpen} onClose={() => setDetailModalOpen(false)} invoice={selectedInvoice} loading={detailLoading} />
    </div>;
};
