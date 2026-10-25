import { getStockTransactionLabel } from '../../utils/stockTransactionLabels';
import { formatPrescriptionReference } from '../../utils/prescriptionCode';
import { formatDateTime } from '../../utils/formatters';
import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ActiveMedicineDto, ApiResponse } from '../../types';
import { Button, Input, InputNumber, Modal, Segmented, Select } from 'antd';
import { MedicineCard } from '../../components/medicines/MedicineCard';
import { DataTable, EmptyState, InlineError, LoadingState, PageHeader, StatusBadge } from '../../components/common';
import styles from './PharmacyInventory.module.css';
import { Plus, Clock, User, RefreshCw, Search } from 'lucide-react';
import { useDialog } from '../../contexts/DialogContext';
interface StockTransaction {
    id: number;
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    type: string;
    quantityChange: number;
    balanceAfter: number;
    prescriptionId?: number;
    prescriptionCreatedAt?: string;
    reason?: string;
    actorName: string;
    createdAt: string;
}
export const PharmacyInventory: React.FC = () => {
    const { showAlert } = useDialog();
    const [transactions, setTransactions] = useState<StockTransaction[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    const [view, setView] = useState<'Lưới' | 'Bảng'>('Bảng');
    const [search, setSearch] = useState('');
    const [medicinesLoading, setMedicinesLoading] = useState(true);
    const [medicinesError, setMedicinesError] = useState('');

    // Modal
    const [activeMedicines, setActiveMedicines] = useState<ActiveMedicineDto[]>([]);
    const [modalOpen, setModalOpen] = useState(false);
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');
    const [selectedMedId, setSelectedMedId] = useState<number>(0);
    const [transType, setTransType] = useState<number>(2); // 2: StockIn, 4: Adjustment
    const [qty, setQty] = useState<number>(100);
    const [reason, setReason] = useState<string>('');

    const fetchTransactions = async () => {
        setLoading(true);
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '15'
            });
            const res = await axiosClient.get<any, ApiResponse<any>>(`/pharmacy/inventory-transactions?${params.toString()}`);
            if (res.success && res.data) {
                setTransactions(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch {
            // Handled
        } finally {
            setLoading(false);
        }
    };

    const fetchMedicines = async () => {
        setMedicinesLoading(true);
        setMedicinesError('');
        try {
            const res = await axiosClient.get<any, ApiResponse<ActiveMedicineDto[]>>('/medicines/active');
            if (res.success && res.data) {
                setActiveMedicines(res.data);
                if (res.data.length > 0 && selectedMedId === 0) {
                    setSelectedMedId(res.data[0].id);
                }
            } else {
                setMedicinesError('Chưa thể tải danh sách thuốc. Bạn vui lòng thử lại.');
            }
        } catch {
            setMedicinesError('Chưa thể tải danh sách thuốc. Bạn vui lòng thử lại.');
        } finally {
            setMedicinesLoading(false);
        }
    };

    useEffect(() => {
        fetchTransactions();
        fetchMedicines();
    }, [page]);

    const handleFormSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFormError('');
        setFormLoading(true);

        try {
            const res = await axiosClient.post<any, ApiResponse<any>>('/pharmacy/inventory/adjust', {
                medicineId: selectedMedId,
                type: transType,
                quantity: Number(qty),
                reason: reason.trim()
            });

            if (res.success) {
                showAlert('Cập nhật tồn kho dược phẩm thành công.', 'Thành công', 'success');
                setModalOpen(false);
                setReason('');
                fetchTransactions();
                fetchMedicines();
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra khi cập nhật tồn kho.');
        } finally {
            setFormLoading(false);
        }
    };


    const getTypeBadge = (type: string) => {
        switch (type) {
            case 'Initial': return <StatusBadge status="completed" label="Tồn đầu kỳ" />;
            case 'StockIn': return <StatusBadge status="completed" label="Nhập kho" />;
            case 'Dispense': return <StatusBadge status="cancelled" label="Xuất đơn" />;
            case 'Adjustment': return <StatusBadge status="pending" label="Kiểm kê" />;
            default: return <StatusBadge status="default" label={getStockTransactionLabel(type)} />;
        }
    };

    const query = search.trim().toLocaleLowerCase('vi-VN');
    const matchesMedicine = (name: string, code: string) => `${name} ${code}`.toLocaleLowerCase('vi-VN').includes(query);
    const filteredMedicines = activeMedicines.filter(m => matchesMedicine(m.name, m.code));
    const filteredTransactions = transactions.filter(t => matchesMedicine(t.medicineName, t.medicineCode));

    return (
        <>
            <PageHeader title="Lịch sử kho" actions={<><Button onClick={fetchTransactions}><RefreshCw size={16} aria-hidden="true" /> Làm mới</Button><Button type="primary" onClick={() => { setModalOpen(true); setFormError(''); }}><Plus size={18} aria-hidden="true" /> Nhập kho / điều chỉnh tồn</Button></>} />
            <div className={styles.toolbar}>
                <Input className={styles.search} prefix={<Search size={16} aria-hidden="true" />} aria-label="Tìm thuốc trong kho" placeholder="Tìm theo tên hoặc mã thuốc" value={search} onChange={event => setSearch(event.target.value)} allowClear />
                <Segmented options={['Bảng', 'Lưới']} value={view} onChange={setView} aria-label="Chế độ xem kho thuốc" />
            </div>
            <section hidden={view !== 'Lưới'} aria-label="Lưới thuốc trong kho">
                {medicinesLoading ? <LoadingState message="Đang tải danh sách thuốc..." /> : medicinesError ? <InlineError message={medicinesError} onRetry={fetchMedicines} /> : filteredMedicines.length === 0 ? <EmptyState title="Chưa có thuốc phù hợp" description="Bạn thử tìm với tên hoặc mã thuốc khác nhé." /> : <div className={styles.medicineGrid}>{filteredMedicines.map(medicine => <MedicineCard key={medicine.id} medicine={medicine} to={`/medicines/${medicine.id}`} showImagePlaceholder={false} />)}</div>}
            </section>
            <div hidden={view !== 'Bảng'}>
                {loading ? <LoadingState message="Đang tải lịch sử giao dịch kho..." /> : filteredTransactions.length === 0 ? <EmptyState title="Chưa có giao dịch biến động tồn kho nào." /> : <div className={styles.tableScroll}><DataTable data={filteredTransactions} keyExtractor={t => t.id} columns={[
                    {header:'Thời gian',className:styles.transactionTime,accessor:t => <div className={styles.inlineContent}><Clock size={14} /><span>{formatDateTime(t.createdAt)}</span></div>},
                    {header:'Mặt hàng thuốc',accessor:t => <><strong>{t.medicineName}</strong><div className={styles.secondary}>{t.medicineCode}</div></>},
                    {header:'Loại giao dịch',accessor:t => getTypeBadge(t.type)},
                    {header:'Số lượng biến động',accessor:t => <strong className={t.quantityChange > 0 ? styles.stockIncrease : styles.stockDecrease}>{t.quantityChange > 0 ? `+${t.quantityChange}` : t.quantityChange} {t.unit}</strong>},
                    {header:'Tổng tồn sau giao dịch (gồm lô hết hạn)',accessor:t => <strong>{t.balanceAfter} {t.unit}</strong>},
                    {header:'Lý do / căn cứ',accessor:t => t.prescriptionId ? formatPrescriptionReference({ id: t.prescriptionId, createdAt: t.prescriptionCreatedAt }, t.reason) : t.reason || '-'},
                    {header:'Người thực hiện',accessor:t => <div className={styles.inlineContent}><User size={14} className={styles.actorIcon} /><span>{t.actorName}</span></div>}
                ]} /></div>}
                {!loading && filteredTransactions.length > 0 && totalItems > 15 && <div className={styles.pagination}><span className={styles.secondary}>Tổng số: {totalItems} giao dịch</span><div className={styles.pageControls}><Button disabled={page === 1} onClick={() => setPage(p => Math.max(1, p - 1))}>Trang trước</Button><span>Trang {page}</span><Button disabled={page * 15 >= totalItems} onClick={() => setPage(p => p + 1)}>Trang sau</Button></div></div>}
            </div>
            <Modal open={modalOpen} onCancel={() => { if (!formLoading) setModalOpen(false); }} title="Nhập kho / điều chỉnh tồn kho" width={520} className={styles.stockModal} mask={{closable:false}} keyboard={false} closable={{disabled:formLoading}} footer={<><Button htmlType="button" disabled={formLoading} onClick={() => setModalOpen(false)}>Hủy</Button><Button type="primary" htmlType="submit" form="pharmacy-stock-adjustment" disabled={formLoading}>{formLoading ? 'Đang lưu...' : 'Xác nhận nhập kho'}</Button></>}>
                <form id="pharmacy-stock-adjustment" onSubmit={handleFormSubmit} className={styles.stockForm}>
                    {formError && <InlineError title={formError} message="" />}
                    <div><label className={styles.fieldLabel}>Chọn mặt hàng thuốc (*)</label><Select className={styles.fullWidth} value={selectedMedId} onChange={value => setSelectedMedId(Number(value))} options={activeMedicines.map(m => ({value:m.id,label:`${m.name} (${m.code}) - Còn dùng được: ${m.usableStock}; Tổng tồn (gồm lô hết hạn): ${m.stockQuantity} ${m.unit}`}))} />
                        <input className={styles.validationInput} aria-hidden="true" tabIndex={-1} value={selectedMedId || ''} required onChange={e => setSelectedMedId(Number(e.target.value))} />
                    </div>
                    <div className={styles.formPair}>
                        <div><label className={styles.fieldLabel}>Loại tác vụ (*)</label><Select className={styles.fullWidth} value={transType} onChange={value => setTransType(Number(value))} options={[{value:2,label:'Nhập thêm hàng'},{value:4,label:'Điều chỉnh sau kiểm kê'}]} /></div>
                        <div><label className={styles.fieldLabel}>Số lượng nhập (*)</label><InputNumber type="number" min={1} className={styles.fullWidth} required value={qty} onChange={value => setQty(Number(value))} /></div>
                    </div>
                    <div><label className={styles.fieldLabel}>Lý do / nhà cung cấp / mã phiếu nhập</label><Input.TextArea rows={3} value={reason} onChange={e => setReason(e.target.value)} placeholder="VD: Nhập 50 hộp từ Dược Hậu Giang theo hóa đơn HD-9821..." /></div>
                </form>
            </Modal>
        </>
    );
};
