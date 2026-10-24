import { formatPrescriptionCode } from '../../utils/prescriptionCode';
import { getPrescriptionStatusLabel } from '../../utils/prescriptionStatusLabels';
import { getApiErrorMessage } from '../../utils/apiError';
import { formatDateOnly, formatDateTime } from '../../utils/formatters';
import type { BatchAllocation } from '../../types/pharmacyBatch';
import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Search, Eye, AlertCircle, Printer } from 'lucide-react';
import { Alert, Button, Input, Modal, Select, Typography } from 'antd';
import { PageHeader, DataTable, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import { usePrescriptionPayment } from './usePrescriptionPayment';
import { useOpenPrescriptionFromQuery } from './useOpenPrescriptionFromQuery';
import styles from './PharmacyPrescriptions.module.css';
import { useDialog } from '../../contexts/DialogContext';
import { PrescriptionSafetyPanel } from '../../components/prescriptions/PrescriptionSafetyPanel';
import type { PrescriptionSafetyWarningDto } from '../../types/prescriptionSafety';
import { useCopilotResource } from '../../components/copilot/copilotResourceContext';
interface PrescriptionListItem {
    id: number;
    code?: string;
    prescriptionCode?: string;
    appointmentId: number;
    appointmentCode: string;
    appointmentDate: string;
    patientName: string;
    patientPhone: string;
    doctorName: string;
    status: string;
    itemCount: number;
    createdAt: string;
    dispensedAt?: string;
    notes?: string;
}

interface PrescriptionDetail {
    safetyWarnings?: PrescriptionSafetyWarningDto[];
    safetyOverrideReason?: string | null;
    id: number;
    code?: string;
    prescriptionCode?: string;
    appointmentId: number;
    appointmentCode: string;
    patientId: number;
    patientName: string;
    patientPhone: string;
    doctorId: number;
    doctorName: string;
    status: string;
    notes?: string;
    createdAt: string;
    dispensedAt?: string;
    items: PrescriptionItemDetail[];
}

interface PrescriptionItemDetail {
    batchAllocations?: BatchAllocation[];
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    quantity: number;
    availableStock: number;
    isActive?: boolean;
    dosage: string;
    frequency: string;
    durationDays?: number;
    instructions?: string;
}

export const PharmacyPrescriptions: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const { setSelection } = useCopilotResource();
    const [prescriptions, setPrescriptions] = useState<PrescriptionListItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('Issued'); // default to pending

    // Modal
    const [selectedPrescription, setSelectedPrescription] = useState<PrescriptionDetail | null>(null);
    const [detailModalOpen, setDetailModalOpen] = useState(false);
    const [detailLoading, setDetailLoading] = useState(false);
    const [dispenseLoading, setDispenseLoading] = useState(false);
    const payment = usePrescriptionPayment(
        detailModalOpen && selectedPrescription?.status === 'Issued' ? selectedPrescription.id : null
    );

    useEffect(() => {
        if (!selectedPrescription) {
            setSelection(null);
            return;
        }
        setSelection({
            context: { prescriptionId: selectedPrescription.id },
            source: 'pharmacy-prescription-detail',
            label: 'Đơn thuốc đang mở'
        });
    }, [selectedPrescription, setSelection]);

    const fetchIdRef = React.useRef(0);

    const fetchPrescriptions = React.useCallback(async (silent = false) => {
        const currentFetchId = ++fetchIdRef.current;
        if (!silent) setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter) params.append('status', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/pharmacy/prescriptions?${params.toString()}`);
            if (currentFetchId !== fetchIdRef.current) return;
            if (res.success && res.data) {
                setPrescriptions(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            if (currentFetchId === fetchIdRef.current && !silent) {
                setLoading(false);
            }
        }
    }, [page, search, statusFilter]);

    useEffect(() => {
        fetchPrescriptions();
    }, [fetchPrescriptions]);

    useEffect(() => {
        setSelection(null);
    }, [page, search, statusFilter, setSelection]);

    useEffect(() => {
        const refresh = () => { void fetchPrescriptions(true); };
        window.addEventListener('cliniccare:copilot-action-completed', refresh);
        return () => window.removeEventListener('cliniccare:copilot-action-completed', refresh);
    }, [fetchPrescriptions]);

    // Background polling (every 25s)
    useEffect(() => {
        const timer = setInterval(() => {
            fetchPrescriptions(true);
        }, 25000);
        return () => clearInterval(timer);
    }, [fetchPrescriptions]);

    const handleSearchSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        setPage(1);
        fetchPrescriptions();
    };

    const handleOpenDetail = async (id: number) => {
        setSelectedPrescription(null);
        setDetailModalOpen(true);
        setDetailLoading(true);
        try {
            const res = await axiosClient.get<any, ApiResponse<PrescriptionDetail>>(`/pharmacy/prescriptions/${id}`);
            if (res.success && res.data) {
                setSelectedPrescription(res.data);
            }
        } catch (error: any) {
            showAlert(error?.message || 'Không thể tải thông tin đơn thuốc.', 'Lỗi', 'error');
            setDetailModalOpen(false);
        } finally {
            setDetailLoading(false);
        }
    };

    const handleDispense = (id: number) => {
        showConfirm('Xác nhận cấp phát thuốc cho đơn này? Số lượng tồn kho tương ứng sẽ tự động bị khấu trừ.', async () => {
            setDispenseLoading(true);
            try {
                const res = await axiosClient.post<any, ApiResponse<any>>(`/pharmacy/prescriptions/${id}/dispense`);
                if (res.success) {
                    showAlert('Cấp phát thuốc thành công! Tồn kho đã được cập nhật.', 'Thành công', 'success');
                    setDetailModalOpen(false);
                    setSelectedPrescription(null);
                    fetchPrescriptions();
                }
            } catch (error: any) {
                const status = error?.response?.status;
                const errorCode = error?.response?.data?.errorCode;
                const errorMessage = getApiErrorMessage(error, 'Không thể cấp phát thuốc.');

                if (status === 409 || errorCode === 'PRESCRIPTION_ALREADY_DISPENSED' || errorCode === 'DISPENSE_CONFLICT') {
                    showAlert('Đơn thuốc này vừa được người khác xử lý hoặc dữ liệu đã thay đổi. Hệ thống sẽ tự động cập nhật lại danh sách.', 'Xung đột dữ liệu (409)', 'warning');
                    setDetailModalOpen(false);
                    setSelectedPrescription(null);
                    fetchPrescriptions();
                } else if (status === 422 || errorCode === 'INSUFFICIENT_MEDICINE_STOCK' || errorCode === 'MEDICINE_INACTIVE' || errorCode === 'PRESCRIPTION_NOT_DISPENSABLE') {
                    showAlert(errorMessage, 'Không thể cấp phát (422)', 'error');
                    handleOpenDetail(id);
                } else {
                    showAlert(errorMessage, 'Lỗi cấp phát', 'error');
                }
            } finally {
                setDispenseLoading(false);
            }
        });
    };

    useOpenPrescriptionFromQuery(!loading, handleOpenDetail);


    // Check if any item lacks sufficient stock or is inactive
    const hasInsufficientStock = selectedPrescription?.items.some(
        item => item.availableStock < item.quantity
    );
    const hasInactiveMedicine = selectedPrescription?.items.some(
        item => item.isActive === false
    );
    const canDispense = selectedPrescription?.status === 'Issued' && !hasInsufficientStock && !hasInactiveMedicine
        && payment.isFullyPaid && !payment.loading && !payment.error;

    return (
        <>
            <PageHeader title="Đơn thuốc chờ cấp" />
            <div className={styles.filters}>
                <form onSubmit={handleSearchSubmit} className={styles.filterForm}>
                    <Input type="text" className={styles.searchInput} placeholder="Tìm theo tên BN, SĐT hoặc mã lịch..." prefix={<Search size={16} aria-hidden="true" />} value={search} onChange={e => setSearch(e.target.value)} />
                    <Select className={styles.statusFilter} aria-label="Trạng thái" value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[{value:'',label:'Tất cả trạng thái'},{value:'Issued',label:'Đang chờ cấp thuốc'},{value:'Dispensed',label:'Đã hoàn thành cấp phát'}]} />
                    <Button htmlType="submit">Tìm</Button>
                </form>
            </div>
            {loading ? <LoadingState message="Đang tải danh sách đơn thuốc..." /> : prescriptions.length === 0 ? <EmptyState title="Không có đơn thuốc nào phù hợp." /> : <div className={styles.tableScroll}><DataTable data={prescriptions} keyExtractor={p => p.id} columns={[
                {header:'Mã đơn',accessor:p => <><strong>{formatPrescriptionCode(p)}</strong><div className={styles.secondary}>{p.appointmentCode}</div></>},
                {header:'Bệnh nhân',accessor:p => <><strong>{p.patientName}</strong><div className={styles.secondary}>{p.patientPhone}</div></>},
                {header:'Bác sĩ chỉ định',accessor:'doctorName'},
                {header:'Thời gian kê đơn',accessor:p => formatDateTime(p.createdAt)},
                {header:'Số lượng thuốc',accessor:p => <span className={styles.itemCount}>{p.itemCount} loại thuốc</span>},
                {header:'Trạng thái',accessor:p => <StatusBadge status={p.status === 'Dispensed' ? 'completed' : 'pending'} label={getPrescriptionStatusLabel(p.status, 'pharmacy')} />},
                {header:'Thao tác',align:'right',accessor:p => <Button type="primary" onClick={() => handleOpenDetail(p.id)}><Eye size={14} aria-hidden="true" /> Xem & cấp phát</Button>}
            ]} /></div>}
            {!loading && prescriptions.length > 0 && totalItems > 10 && <div className={styles.pagination}><span className={styles.secondary}>Tổng số: {totalItems} đơn thuốc</span><div className={styles.pageControls}><Button disabled={page === 1} onClick={() => setPage(p => Math.max(1, p - 1))}>Trang trước</Button><span>Trang {page}</span><Button disabled={page * 10 >= totalItems} onClick={() => setPage(p => p + 1)}>Trang sau</Button></div></div>}
            <Modal open={detailModalOpen} onCancel={() => { if (!dispenseLoading) { setDetailModalOpen(false); setSelectedPrescription(null); } }} title={`Chi tiết đơn thuốc ${selectedPrescription ? formatPrescriptionCode(selectedPrescription) : ''}`} width={740} className={styles.detailModal} mask={{closable:false}} keyboard={false} closable={{disabled:dispenseLoading}} footer={
                <div className={styles.modalFooter}><Button htmlType="button" onClick={() => window.print()}><Printer size={16} aria-hidden="true" /> In đơn thuốc</Button><div className={styles.footerActions}><Button htmlType="button" disabled={dispenseLoading} onClick={() => { setDetailModalOpen(false); setSelectedPrescription(null); }}>Đóng</Button>{selectedPrescription?.status === 'Issued' && <Button type="primary" htmlType="button" disabled={dispenseLoading || !canDispense} onClick={() => handleDispense(selectedPrescription.id)}>{dispenseLoading ? 'Đang xử lý...' : 'Xác nhận cấp thuốc & trừ kho'}</Button>}</div></div>
            }>
                <div className={styles.appointmentCode}>Mã lịch hẹn: {selectedPrescription?.appointmentCode}</div>
                {detailLoading || payment.loading ? <LoadingState message={payment.loading ? 'Đang kiểm tra thanh toán...' : 'Đang tải dữ liệu đơn thuốc...'} /> : selectedPrescription && <>
                    <div className={styles.patientBanner}>
                        <div><span className={styles.secondary}>Bệnh nhân:</span><div className={styles.patientName}>{selectedPrescription.patientName}</div><div className={styles.secondary}>SĐT: {selectedPrescription.patientPhone}</div></div>
                        <div><span className={styles.secondary}>Bác sĩ chỉ định:</span><div className={styles.patientName}>{selectedPrescription.doctorName}</div><div className={styles.secondary}>Thời gian kê: {formatDateTime(selectedPrescription.createdAt)}</div></div>
                        <div><span className={styles.secondary}>Trạng thái cấp phát:</span><div><StatusBadge status={selectedPrescription.status === 'Dispensed' ? 'completed' : 'pending'} label={getPrescriptionStatusLabel(selectedPrescription.status, 'pharmacy')} /></div></div>
                    </div>
                    {selectedPrescription.status === 'Issued' && <div>
                        {payment.error ? <InlineError message={payment.error} onRetry={payment.retry} />
                                : payment.status && (payment.isFullyPaid ? <Typography.Text>Thanh toán: Đã thanh toán</Typography.Text> : <Alert type="warning" showIcon title="Đơn chưa thanh toán. Người bệnh cần thanh toán tại quầy thu ngân trước khi cấp thuốc." />)}
                    </div>}
                    {selectedPrescription.notes && <div className={styles.doctorNotes}><strong>Ghi chú từ bác sĩ:</strong> {selectedPrescription.notes}</div>}
                    {!!selectedPrescription.safetyWarnings?.length && <PrescriptionSafetyPanel result={{ allergies: [], warnings: selectedPrescription.safetyWarnings }} overrideReason={selectedPrescription.safetyOverrideReason} />}
                    {hasInsufficientStock && selectedPrescription.status === 'Issued' && <div className={styles.stockWarning}><AlertCircle size={20} /><span><strong>Cảnh báo tồn kho:</strong> Có thuốc trong đơn không đủ số lượng tồn kho để cấp phát. Vui lòng nhập thêm hàng hoặc trao đổi lại với bác sĩ!</span></div>}
                    {hasInactiveMedicine && selectedPrescription.status === 'Issued' && <div className={styles.inactiveWarning}><AlertCircle size={20} /><span><strong>Cảnh báo danh mục:</strong> Có thuốc trong đơn đã ngừng cung cấp hoặc ngừng hoạt động. Không thể cấp phát đơn này!</span></div>}
                    <h4 className={styles.itemsHeading}>Danh mục thuốc chỉ định</h4>
                    <div className={styles.itemsScroll}><DataTable data={selectedPrescription.items.map((item,index) => ({...item,lineIndex:index}))} keyExtractor={item => item.lineIndex} columns={[
                        {header:'Tên thuốc',accessor:item => <><strong>{item.medicineName}</strong><div className={styles.medicineMeta}><span>{item.medicineCode}</span>{item.isActive === false && <StatusBadge status="cancelled" label="Ngừng hoạt động" size="sm" />}</div>{item.batchAllocations?.map((allocation, index) => <div key={`${allocation.batchNumber}-${allocation.expiryDate}-${index}`} className={styles.instructions}>Lô {allocation.batchNumber} · HSD {formatDateOnly(allocation.expiryDate).slice(3)} · {allocation.quantity} {item.unit}</div>)}</>},
                        {header:'Số lượng',accessor:item => <strong>{item.quantity} {item.unit}</strong>},
                        {header:'Còn dùng được',accessor:item => <span className={item.availableStock < item.quantity ? styles.insufficientStock : styles.availableStock}>{item.availableStock} {item.unit}</span>},
                        {header:'Liều dùng',accessor:'dosage'},
                        {header:'Tần suất & chỉ dẫn',accessor:item => <><div>{item.frequency}</div>{item.instructions && <div className={styles.instructions}>{item.instructions}</div>}</>}
                    ]} /></div>
                </>}
            </Modal>
        </>
    );
};
