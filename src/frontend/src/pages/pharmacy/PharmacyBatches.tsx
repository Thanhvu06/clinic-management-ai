import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Form, Input, InputNumber, Modal, Select } from 'antd';
import { Plus, RefreshCw } from 'lucide-react';
import axiosClient from '../../api/axiosClient';
import { pharmacyBatchApi } from '../../api/pharmacyBatchApi';
import type { ActiveMedicineDto, ApiResponse } from '../../types';
import type { CreateMedicineBatch, MedicineBatch, MedicineBatchFilter } from '../../types/pharmacyBatch';
import { DataTable, EmptyState, InlineError, LoadingState, PageHeader, Pagination, StatusBadge } from '../../components/common';
import type { DataTableColumn } from '../../components/common';
import { getApiErrorMessage } from '../../utils/apiError';
import { formatDateOnly } from '../../utils/formatters';
import styles from './PharmacyBatches.module.css';

const statusLabels = { Valid: 'Còn hạn', Expiring: 'Sắp hết hạn', Expired: 'Đã hết hạn', Disposed: 'Đã hủy' };
const badgeStatus = { Valid: 'completed', Expiring: 'pending', Expired: 'cancelled', Disposed: 'cancelled' };
const vietnamToday = () => {
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
    const part = (type: string) => parts.find(value => value.type === type)?.value;
    return `${part('year')}-${part('month')}-${part('day')}`;
};

export const PharmacyBatches = () => {
    const [batches, setBatches] = useState<MedicineBatch[]>([]);
    const [status, setStatus] = useState<MedicineBatchFilter>('all');
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [total, setTotal] = useState(0);
    const [reload, setReload] = useState(0);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [notice, setNotice] = useState('');
    const [createOpen, setCreateOpen] = useState(false);
    const [medicines, setMedicines] = useState<ActiveMedicineDto[]>([]);
    const [medicinesLoading, setMedicinesLoading] = useState(false);
    const [medicinesError, setMedicinesError] = useState('');
    const [creating, setCreating] = useState(false);
    const [createError, setCreateError] = useState('');
    const [disposedBatch, setDisposedBatch] = useState<MedicineBatch | null>(null);
    const [disposing, setDisposing] = useState(false);
    const [disposeError, setDisposeError] = useState('');
    const [createForm] = Form.useForm<CreateMedicineBatch>();
    const [disposeForm] = Form.useForm<{ reason: string }>();

    useEffect(() => {
        let active = true;
        const load = async () => {
            setLoading(true);
            setError('');
            try {
                const response = await pharmacyBatchApi.list({ status, search: search.trim(), page, pageSize: 20 });
                if (!active) return;
                if (response.success && response.data) {
                    setBatches(response.data.items);
                    setTotal(response.data.totalItems);
                } else setError(response.message || 'Không tải được danh sách lô thuốc.');
            } catch (err) {
                if (active) setError(getApiErrorMessage(err, 'Không tải được danh sách lô thuốc.'));
            } finally {
                if (active) setLoading(false);
            }
        };
        void load();
        return () => { active = false; };
    }, [status, search, page, reload]);

    const loadMedicines = useCallback(async () => {
        setMedicinesLoading(true);
        setMedicinesError('');
        try {
            const response = await axiosClient.get<unknown, ApiResponse<ActiveMedicineDto[]>>('/medicines/active');
            if (response.success && response.data) setMedicines(response.data);
            else setMedicinesError(response.message || 'Không tải được danh sách thuốc.');
        } catch (err) {
            setMedicinesError(getApiErrorMessage(err, 'Không tải được danh sách thuốc.'));
        } finally { setMedicinesLoading(false); }
    }, []);

    const create = async (values: CreateMedicineBatch) => {
        setCreating(true);
        setCreateError('');
        try {
            const response = await pharmacyBatchApi.create({ ...values, batchNumber: values.batchNumber.trim(), unitCost: values.unitCost ?? null });
            if (!response.success) { setCreateError(response.message || 'Không thể nhập lô thuốc.'); return; }
            setCreateOpen(false);
            setNotice('Nhập lô thuốc thành công.');
            setPage(1);
            setReload(value => value + 1);
        } catch (err) { setCreateError(getApiErrorMessage(err, 'Không thể nhập lô thuốc.')); }
        finally { setCreating(false); }
    };

    const dispose = async ({ reason }: { reason: string }) => {
        if (!disposedBatch) return;
        setDisposing(true);
        setDisposeError('');
        try {
            const response = await pharmacyBatchApi.dispose(disposedBatch.id, reason.trim());
            if (!response.success) { setDisposeError(response.message || 'Không thể hủy lô thuốc.'); return; }
            setDisposedBatch(null);
            setNotice('Hủy lô thuốc thành công.');
            setReload(value => value + 1);
        } catch (err) { setDisposeError(getApiErrorMessage(err, 'Không thể hủy lô thuốc.')); }
        finally { setDisposing(false); }
    };

    const columns: DataTableColumn<MedicineBatch>[] = [
        { header: 'Thuốc', accessor: batch => <><strong>{batch.medicineName}</strong><div className={styles.muted}>{batch.medicineCode}</div></> },
        { header: 'Số lô', accessor: batch => batch.batchNumber },
        { header: 'Hạn dùng', accessor: batch => formatDateOnly(batch.expiryDate) },
        { header: 'Còn lại', accessor: batch => `${batch.quantityRemaining} ${batch.unit}` },
        { header: 'Thời hạn', accessor: batch => batch.daysToExpiry < 0 ? 'Đã hết hạn' : `Còn ${batch.daysToExpiry} ngày` },
        { header: 'Trạng thái', accessor: batch => <StatusBadge status={badgeStatus[batch.status]} label={statusLabels[batch.status]} /> },
        { header: 'Thao tác', accessor: batch => batch.status === 'Expired' ? <Button danger onClick={() => {
            disposeForm.resetFields(); setDisposeError(''); setDisposedBatch(batch);
        }}>Hủy lô</Button> : null }
    ];

    return <div className={styles.page}>
        <PageHeader title="Lô thuốc & hạn dùng" actions={<>
            <Button icon={<RefreshCw size={16} />} disabled={loading} onClick={() => setReload(value => value + 1)}>{loading ? 'Đang tải...' : 'Làm mới'}</Button>
            <Button type="primary" icon={<Plus size={16} />} onClick={() => {
                createForm.resetFields(); setCreateError(''); setCreateOpen(true); void loadMedicines();
            }}>Nhập lô mới</Button>
        </>} />
        {notice && <Alert type="success" title={notice} closable onClose={() => setNotice('')} />}
        <Card size="small"><div className={styles.filters}>
            <Select aria-label="Trạng thái lô" value={status} className={styles.statusFilter} onChange={value => { setStatus(value); setPage(1); }} options={[
                { value: 'all', label: 'Tất cả' }, { value: 'valid', label: 'Còn hạn' },
                { value: 'expiring', label: 'Sắp hết hạn (≤ 90 ngày)' }, { value: 'expired', label: 'Đã hết hạn' }
            ]} />
            <Input aria-label="Tìm thuốc" placeholder="Tìm theo tên hoặc mã thuốc" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} className={styles.search} />
        </div></Card>
        {loading ? <LoadingState message="Đang tải lô thuốc..." />
            : error ? <InlineError message={error} onRetry={() => setReload(value => value + 1)} />
                : batches.length === 0 ? <EmptyState title="Không có lô thuốc phù hợp." />
                    : <><DataTable data={batches} columns={columns} keyExtractor={batch => batch.id} />
                        <Pagination page={page} totalPages={Math.ceil(total / 20)} totalRecords={total} onPageChange={setPage} /></>}

        <Modal title="Nhập lô thuốc mới" open={createOpen} footer={null} destroyOnHidden onCancel={() => { if (!creating) setCreateOpen(false); }}>
            {medicinesLoading ? <LoadingState message="Đang tải danh sách thuốc..." />
                : medicinesError ? <InlineError message={medicinesError} onRetry={() => void loadMedicines()} />
                    : medicines.length === 0 ? <EmptyState title="Chưa có thuốc để nhập lô." />
                        : <Form form={createForm} layout="vertical" onFinish={create} onValuesChange={() => setCreateError('')}>
                            {createError && <InlineError message={createError} onRetry={() => createForm.submit()} />}
                            <Form.Item name="medicineId" label="Thuốc" rules={[{ required: true, message: 'Vui lòng chọn thuốc.' }]}>
                                <Select showSearch optionFilterProp="label" placeholder="Chọn thuốc" options={medicines.map(medicine => ({ value: medicine.id, label: `${medicine.code} – ${medicine.name}` }))} />
                            </Form.Item>
                            <Form.Item name="batchNumber" label="Số lô" rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập số lô.' }, { max: 50, message: 'Số lô tối đa 50 ký tự.' }]}>
                                <Input maxLength={50} />
                            </Form.Item>
                            <Form.Item name="expiryDate" label="Hạn dùng" rules={[{ required: true, message: 'Vui lòng chọn hạn dùng.' }, {
                                validator: (_, value: string) => !value || value > vietnamToday() ? Promise.resolve() : Promise.reject(new Error('Hạn dùng phải sau ngày hôm nay.'))
                            }]}><Input type="date" /></Form.Item>
                            <Form.Item name="quantity" label="Số lượng" rules={[{ required: true, message: 'Vui lòng nhập số lượng.' }, { type: 'integer', min: 1, max: 2147483647, message: 'Số lượng phải là số nguyên lớn hơn 0.' }]}>
                                <InputNumber precision={0} className={styles.control} />
                            </Form.Item>
                            <Form.Item name="unitCost" label="Giá nhập (tùy chọn)" rules={[{ type: 'number', min: 0, message: 'Giá nhập không được âm.' }]}>
                                <InputNumber min={0} precision={2} className={styles.control} />
                            </Form.Item>
                            <div className={styles.actions}><Button disabled={creating} onClick={() => setCreateOpen(false)}>Đóng</Button>
                                <Button type="primary" htmlType="submit" disabled={creating}>{creating ? 'Đang nhập lô...' : 'Nhập lô'}</Button></div>
                        </Form>}
        </Modal>

        <Modal title={`Hủy lô ${disposedBatch?.batchNumber ?? ''}`} open={Boolean(disposedBatch)} footer={null} destroyOnHidden onCancel={() => { if (!disposing) setDisposedBatch(null); }}>
            <Form form={disposeForm} layout="vertical" onFinish={dispose} onValuesChange={() => setDisposeError('')}>
                <Alert type="warning" title="Số lượng còn lại của lô sẽ được trừ khỏi tổng tồn kho." className={styles.warning} />
                {disposeError && <InlineError message={disposeError} onRetry={() => disposeForm.submit()} />}
                <Form.Item name="reason" label="Lý do hủy lô" rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập lý do hủy lô.' }, {
                    validator: (_, value: string) => !value || value.trim().length >= 5 ? Promise.resolve() : Promise.reject(new Error('Lý do hủy lô phải có ít nhất 5 ký tự.'))
                }, { max: 500, message: 'Lý do tối đa 500 ký tự.' }]}><Input.TextArea rows={3} maxLength={500} /></Form.Item>
                <div className={styles.actions}><Button disabled={disposing} onClick={() => setDisposedBatch(null)}>Đóng</Button>
                    <Button type="primary" danger htmlType="submit" disabled={disposing}>{disposing ? 'Đang hủy lô...' : 'Xác nhận hủy lô'}</Button></div>
            </Form>
        </Modal>
    </div>;
};
