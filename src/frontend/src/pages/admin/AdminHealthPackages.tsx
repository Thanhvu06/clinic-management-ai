import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Plus, Edit, Trash2 } from 'lucide-react';
import { Alert, Button, Checkbox, Form, Input, InputNumber, Modal, Select, Tag, Typography } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminHealthPackages.module.css';
import { useDialog } from '../../contexts/DialogContext';

interface HealthPackage {
    id: number;
    code: string;
    name: string;
    description: string;
    targetGroup: string;
    price: number;
    includedServices: string;
    imageUrl?: string;
    isActive: boolean;
    sortOrder: number;
}

export const AdminHealthPackages: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [packages, setPackages] = useState<HealthPackage[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean; isEdit: boolean; data: Partial<HealthPackage> }>({
        isOpen: false,
        isEdit: false,
        data: {}
    });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');

    const fetchPackages = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/health-packages?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải gói khám.');
            if (res.success && res.data) {
                setPackages(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải gói khám.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchPackages();
    }, [page, statusFilter]);

    const handleSearchSubmit = () => {
        if (page !== 1) setPage(1);
        else fetchPackages();
    };

    const handleFormSubmit = async () => {
        setFormError('');
        setFormLoading(true);

        try {
            if (modal.isEdit) {
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/health-packages/${modal.data.id}`, {
                    name: modal.data.name,
                    description: modal.data.description,
                    targetGroup: modal.data.targetGroup,
                    price: Number(modal.data.price),
                    includedServices: modal.data.includedServices,
                    imageUrl: modal.data.imageUrl || '',
                    isActive: modal.data.isActive ?? true,
                    sortOrder: Number(modal.data.sortOrder) || 0
                });
                if (res.success) {
                    showAlert('Cập nhật gói khám thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchPackages();
                }
            } else {
                const res = await axiosClient.post<any, ApiResponse<any>>('/admin/health-packages', {
                    code: modal.data.code,
                    name: modal.data.name,
                    description: modal.data.description,
                    targetGroup: modal.data.targetGroup,
                    price: Number(modal.data.price),
                    includedServices: modal.data.includedServices,
                    imageUrl: modal.data.imageUrl || '',
                    isActive: modal.data.isActive ?? true,
                    sortOrder: Number(modal.data.sortOrder) || 0
                });
                if (res.success) {
                    showAlert('Thêm gói khám mới thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchPackages();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra khi lưu gói khám.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleDelete = (pkg: HealthPackage) => {
        showConfirm(`Bạn có chắc chắn muốn xóa gói khám "${pkg.name}"?`, async () => {
            try {
                const res = await axiosClient.delete<any, ApiResponse<any>>(`/admin/health-packages/${pkg.id}`);
                if (res.success) {
                    showAlert('Đã xóa gói khám thành công.', 'Thành công', 'success');
                    fetchPackages();
                }
            } catch (error: any) {
                showAlert(error?.message || 'Không thể xóa gói khám này.', 'Lỗi', 'error');
            }
        });
    };

    const formatCurrency = (val: number) => {
        return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(val);
    };

    const closeModal = () => { if (!formLoading) setModal({ isOpen: false, isEdit: false, data: {} }); };
    const openModal = (data: Partial<HealthPackage>, isEdit: boolean) => { setFormError(''); setModal({ isOpen: true, isEdit, data }); };

    return (
        <div className={styles.packagesPage}>
            <PageHeader title="Quản lý gói khám sức khỏe" actions={<Button type="primary" icon={<Plus size={18} />} onClick={() => openModal({ isActive: true, sortOrder: 0, price: 1000000 }, false)}>Thêm gói khám mới</Button>} />
            <FilterBar>
                <Input.Search className={styles.packageSearch} placeholder="Tìm theo tên hoặc mã gói..." enterButton="Tìm kiếm" value={search} onChange={e => setSearch(e.target.value)} onSearch={handleSearchSubmit} />
                <Select className={styles.packageStatusFilter} aria-label="Trạng thái" value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                    { value: '', label: 'Tất cả trạng thái' }, { value: 'true', label: 'Đang hoạt động' }, { value: 'false', label: 'Tạm ẩn' }
                ]} />
            </FilterBar>
            {loading ? <LoadingState /> : loadError ? <InlineError message={loadError} onRetry={fetchPackages} />
                : packages.length === 0 ? <EmptyState title="Không tìm thấy gói khám nào phù hợp." /> : <>
                    <DataTable data={packages} keyExtractor={pkg => pkg.id} columns={[
                        { header: 'Mã gói', accessor: pkg => <Typography.Text strong>{pkg.code}</Typography.Text> },
                        { header: 'Tên gói khám', accessor: pkg => <><Typography.Text strong>{pkg.name}</Typography.Text><div className={styles.packageDescription} title={pkg.description}>{pkg.description}</div></> },
                        { header: 'Đối tượng phù hợp', accessor: pkg => <Tag>{pkg.targetGroup || 'Mọi đối tượng'}</Tag> },
                        { header: 'Giá niêm yết', accessor: pkg => <Typography.Text strong className={styles.packagePrice}>{formatCurrency(pkg.price)}</Typography.Text> },
                        { header: 'Thứ tự', accessor: 'sortOrder' },
                        { header: 'Trạng thái', accessor: pkg => <StatusBadge status={pkg.isActive ? 'Approved' : 'Rejected'} label={pkg.isActive ? 'Đang mở' : 'Đã tắt'} /> },
                        { header: 'Thao tác', align: 'right', accessor: pkg => <div className={styles.packageRowActions}>
                            <Button size="small" title="Chỉnh sửa" icon={<Edit size={14} />} onClick={() => openModal({ ...pkg }, true)}>Sửa</Button>
                            <Button size="small" danger title="Xóa gói khám" aria-label="Xóa gói khám" icon={<Trash2 size={14} />} onClick={() => handleDelete(pkg)} />
                        </div> }
                    ]} />
                    <Pagination page={page} totalPages={Math.ceil(totalItems / 10)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.packageTotal}>Tổng số: {totalItems} gói khám</div>}
            <Modal title={modal.isEdit ? 'Chỉnh sửa gói khám' : 'Thêm gói khám mới'} open={modal.isOpen} onCancel={closeModal} footer={null} destroyOnHidden width={600}
                maskClosable={!formLoading} closable={!formLoading} keyboard={!formLoading}>
                {formError && <Alert className={styles.packageFormAlert} type="error" title={formError} showIcon />}
                <Form layout="vertical" onFinish={handleFormSubmit} disabled={formLoading}>
                    <div className={styles.packageFormGrid}>
                        <Form.Item label="Mã gói (*)" htmlFor="packageCode"><Input id="packageCode" required disabled={modal.isEdit || formLoading} value={modal.data.code || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, code: e.target.value } })} placeholder="VD: PKG-STANDARD" /></Form.Item>
                        <Form.Item label="Tên gói khám (*)" htmlFor="packageName"><Input id="packageName" required value={modal.data.name || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, name: e.target.value } })} placeholder="VD: Gói khám sức khỏe tổng quát tiêu chuẩn" /></Form.Item>
                        <Form.Item label="Giá niêm yết (VNĐ) (*)" htmlFor="packagePrice"><InputNumber className={styles.packageNumber} id="packagePrice" required min={0} step={50000} value={modal.data.price ?? 0} onChange={value => setModal({ ...modal, data: { ...modal.data, price: value ?? 0 } })} /></Form.Item>
                        <Form.Item label="Đối tượng phù hợp" htmlFor="packageTargetGroup"><Input id="packageTargetGroup" value={modal.data.targetGroup || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, targetGroup: e.target.value } })} placeholder="VD: Nam & Nữ mọi lứa tuổi" /></Form.Item>
                    </div>
                    <Form.Item label="Mô tả ngắn" htmlFor="packageDescription"><Input.TextArea id="packageDescription" rows={2} value={modal.data.description || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, description: e.target.value } })} placeholder="Mô tả lợi ích của gói khám..." /></Form.Item>
                    <Form.Item label="Danh mục dịch vụ bao gồm (JSON hoặc danh sách)" htmlFor="packageServices"><Input.TextArea id="packageServices" rows={3} value={modal.data.includedServices || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, includedServices: e.target.value } })} placeholder='["Khám tổng quát", "Xét nghiệm máu 18 chỉ số", "Siêu âm bụng", "Chụp X-quang phổi"]' /></Form.Item>
                    <Form.Item label="Thứ tự ưu tiên hiển thị" htmlFor="packageSortOrder"><InputNumber className={styles.packageNumber} id="packageSortOrder" value={modal.data.sortOrder ?? 0} onChange={value => setModal({ ...modal, data: { ...modal.data, sortOrder: value ?? 0 } })} /></Form.Item>
                    <Form.Item><Checkbox checked={modal.data.isActive ?? true} onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}>Kích hoạt mở bán gói này</Checkbox></Form.Item>
                    <div className={styles.packageFormActions}><Button disabled={formLoading} onClick={closeModal}>Hủy</Button><Button type="primary" htmlType="submit" disabled={formLoading}>{formLoading ? 'Đang lưu...' : (modal.isEdit ? 'Lưu thay đổi' : 'Tạo gói khám')}</Button></div>
                </Form>
            </Modal>
        </div>
    );
};
