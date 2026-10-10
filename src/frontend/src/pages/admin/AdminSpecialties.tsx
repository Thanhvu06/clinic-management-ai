import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Plus, Edit, Sparkles } from 'lucide-react';
import { Alert, Button, Checkbox, Form, Input, Modal, Select, Tag, Typography } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminSpecialties.module.css';
import { useDialog } from '../../contexts/DialogContext';

interface Specialty {
    id: number;
    specialtyCode: string;
    name: string;
    description: string;
    isActive: boolean;
    aiEnabled: boolean;
}

export const AdminSpecialties: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, isEdit: boolean, data: Partial<Specialty> }>({ isOpen: false, isEdit: false, data: {} });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');

    const fetchSpecialties = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/specialties?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải chuyên khoa.');
            if (res.success && res.data) {
                setSpecialties(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải chuyên khoa.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchSpecialties();
    }, [page, statusFilter]);

    const handleSearchSubmit = () => {
        if (page !== 1) setPage(1);
        else fetchSpecialties();
    };

    const handleFormSubmit = async () => {
        setFormError('');
        
        const submitData = async () => {
            setFormLoading(true);
            try {
                if (modal.isEdit) {
                    const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/specialties/${modal.data.id}`, {
                        name: modal.data.name,
                        description: modal.data.description,
                        isActive: modal.data.isActive
                    });
                    if (res.success) {
                        showAlert('Cập nhật chuyên khoa thành công.', 'Thành công', 'success');
                        setModal({ isOpen: false, isEdit: false, data: {} });
                        fetchSpecialties();
                    }
                } else {
                    const res = await axiosClient.post<any, ApiResponse<any>>('/admin/specialties', {
                        specialtyCode: modal.data.specialtyCode,
                        name: modal.data.name,
                        description: modal.data.description,
                        isActive: modal.data.isActive ?? true
                    });
                    if (res.success) {
                        showAlert('Thêm chuyên khoa thành công.', 'Thành công', 'success');
                        setModal({ isOpen: false, isEdit: false, data: {} });
                        fetchSpecialties();
                    }
                }
            } catch (error: any) {
                setFormError(error?.message || 'Có lỗi xảy ra.');
            } finally {
                setFormLoading(false);
            }
        };

        if (modal.isEdit && !modal.data.isActive) {
            showConfirm('Cảnh báo: Chuyên khoa ngừng hoạt động sẽ không hiển thị khi bệnh nhân đặt lịch mới, nhưng lịch sử vẫn được giữ. Bạn có chắc chắn?', () => {
                submitData();
            });
        } else {
            submitData();
        }
    };

    const openCreateModal = () => {
        setFormError('');
        setModal({ isOpen: true, isEdit: false, data: { isActive: true } });
    };

    const openEditModal = (spec: Specialty) => {
        setFormError('');
        setModal({ isOpen: true, isEdit: true, data: { ...spec } });
    };

    const closeModal = () => { if (!formLoading) setModal({ isOpen: false, isEdit: false, data: {} }); };

    return (
        <div className={styles.specialtiesPage}>
            <PageHeader title="Quản lý chuyên khoa" actions={<Button type="primary" icon={<Plus size={18} />} onClick={openCreateModal}>Thêm chuyên khoa</Button>} />
            <FilterBar>
                <Input.Search className={styles.specialtySearch} placeholder="Tìm theo mã, tên..." value={search}
                    onChange={e => setSearch(e.target.value)} onSearch={handleSearchSubmit} enterButton="Lọc" />
                <Select className={styles.specialtyFilter} aria-label="Trạng thái" value={statusFilter}
                    onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                        { value: '', label: 'Tất cả trạng thái' }, { value: 'true', label: 'Đang hoạt động' }, { value: 'false', label: 'Ngừng hoạt động' }
                    ]} />
            </FilterBar>
            {loading ? <LoadingState /> : loadError ? <InlineError message={loadError} onRetry={fetchSpecialties} />
                : specialties.length === 0 ? <EmptyState title="Không tìm thấy dữ liệu." /> : <>
                    <div className={styles.specialtyTable}>
                        <DataTable data={specialties} keyExtractor={specialty => specialty.id} columns={[
                            { header: 'Mã Khoa', accessor: specialty => <Typography.Text strong>{specialty.specialtyCode}</Typography.Text> },
                            { header: 'Tên chuyên khoa', accessor: specialty => <><Typography.Text strong>{specialty.name}</Typography.Text><Typography.Paragraph className={styles.specialtyDescription} type="secondary" ellipsis={{ rows: 1, tooltip: specialty.description }}>{specialty.description || 'Không có mô tả'}</Typography.Paragraph></> },
                            { header: 'Trạng thái', accessor: specialty => <Tag color={specialty.isActive ? 'success' : 'error'}>{specialty.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</Tag> },
                            { header: 'Tính năng AI', accessor: specialty => specialty.aiEnabled ? <Tag color="processing" icon={<Sparkles size={12} />}>Cho phép gợi ý</Tag> : <Tag>Tắt</Tag> },
                            { header: 'Thao tác', align: 'right', accessor: specialty => <Button size="small" icon={<Edit size={14} />} onClick={() => openEditModal(specialty)}>Sửa</Button> }
                        ]} />
                    </div>
                    <Pagination page={page} totalPages={Math.ceil(totalItems / 10)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.specialtyTotal}>Tổng cộng: {totalItems} chuyên khoa</div>}
            <Modal title={modal.isEdit ? 'Cập nhật chuyên khoa' : 'Thêm chuyên khoa'} open={modal.isOpen} onCancel={closeModal}
                footer={null} destroyOnHidden maskClosable={!formLoading} closable={!formLoading} keyboard={!formLoading}>
                {formError && <Alert className={styles.specialtyFormError} type="error" title={formError} showIcon />}
                <Form layout="vertical" onFinish={handleFormSubmit}>
                    <Form.Item label="Mã chuyên khoa (*)" htmlFor="specialtyCode"><Input id="specialtyCode" required disabled={modal.isEdit} value={modal.data.specialtyCode || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, specialtyCode: e.target.value } })} placeholder="VD: CARDIO" /></Form.Item>
                    <Form.Item label="Tên chuyên khoa (*)" htmlFor="specialtyName"><Input id="specialtyName" required value={modal.data.name || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, name: e.target.value } })} placeholder="VD: Tim mạch" /></Form.Item>
                    <Form.Item label="Mô tả" htmlFor="specialtyDescription"><Input.TextArea id="specialtyDescription" rows={3} value={modal.data.description || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, description: e.target.value } })} /></Form.Item>
                    <Form.Item><Checkbox checked={modal.data.isActive ?? true} onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}>Đang hoạt động</Checkbox></Form.Item>
                    {!modal.data.isActive && modal.isEdit && <Alert className={styles.specialtyWarning} type="warning" title="Ngừng hoạt động sẽ làm chuyên khoa bị ẩn khỏi form đặt lịch mới." showIcon />}
                    <div className={styles.specialtyFormActions}>
                        <Button disabled={formLoading} onClick={closeModal}>Hủy</Button>
                        <Button type="primary" htmlType="submit" loading={formLoading}>{formLoading ? 'Đang lưu...' : 'Xác nhận lưu'}</Button>
                    </div>
                </Form>
            </Modal>
        </div>
    );
};
