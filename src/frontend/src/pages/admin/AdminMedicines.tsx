import React, { useState, useEffect, useRef } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse, MedicineDto, MedicineCategoryDto } from '../../types';
import { medicineApi, medicineImageUrl } from '../../api/medicineApi';
import { MedicineCategoryManager } from './MedicineCategoryManager';
import { Plus, Edit, AlertTriangle } from 'lucide-react';
import { Alert, Button, Checkbox, Form, Input, InputNumber, Modal, Select, Tag, Typography } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminMedicines.module.css';
import { useDialog } from '../../contexts/DialogContext';

type Medicine = MedicineDto;

export const AdminMedicines: React.FC = () => {
    const { showAlert } = useDialog();
    const [medicines, setMedicines] = useState<Medicine[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');
    const [togglingId, setTogglingId] = useState<number | null>(null);
    const toggleInFlight = useRef<number | null>(null);
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean; isEdit: boolean; data: Partial<Medicine> }>({
        isOpen: false,
        isEdit: false,
        data: {}
    });
    const [formLoading, setFormLoading] = useState(false);
    const [imageDeleting, setImageDeleting] = useState(false);
    const [formError, setFormError] = useState('');
    const [categories, setCategories] = useState<MedicineCategoryDto[]>([]);
    const [selectedImage, setSelectedImage] = useState<File | null>(null);
    const [imagePreview, setImagePreview] = useState<string | null>(null);
    const [savedMedicineId, setSavedMedicineId] = useState<number | null>(null);
    const fetchCategories = async () => {
        try { const res = await medicineApi.getCategories(); if (res.success) setCategories(res.data ?? []); }
        catch (error) { showAlert((error as { message?: string }).message || 'Không thể tải nhóm thuốc.', 'Lỗi', 'error'); }
    };
    useEffect(() => { fetchCategories(); }, []);
    useEffect(() => { setSelectedImage(null); setSavedMedicineId(null); }, [modal.isOpen, modal.data.id]);
    useEffect(() => {
        if (!selectedImage) { setImagePreview(null); return; }
        const preview = URL.createObjectURL(selectedImage); setImagePreview(preview);
        return () => URL.revokeObjectURL(preview);
    }, [selectedImage]);
    const catalogPayload = () => ({
        activeIngredient: modal.data.activeIngredient || null, strength: modal.data.strength || null,
        dosageForm: modal.data.dosageForm || null, manufacturer: modal.data.manufacturer || null,
        categoryId: modal.data.categoryId ?? null, isPrescriptionRequired: modal.data.isPrescriptionRequired ?? true,
        description: modal.data.description || null, storageInstructions: modal.data.storageInstructions || null
    });
    const finishSave = async (id: number, message: string) => {
        // Retain the saved id when image upload fails so retry cannot create a duplicate.
        setSavedMedicineId(id);
        if (selectedImage) await medicineApi.uploadImage(id, selectedImage);
        showAlert(message, 'Thành công', 'success');
        setModal({ isOpen: false, isEdit: false, data: {} }); fetchMedicines();
    };
    const deleteImage = async () => {
        if (!modal.data.id) return;
        setImageDeleting(true); setFormError('');
        try {
            await medicineApi.deleteImage(modal.data.id);
            setSelectedImage(null);
            setModal(current => ({ ...current, data: { ...current.data, imagePath: null, imageUrl: null } }));
            fetchMedicines();
        } catch (error) { setFormError((error as { message?: string }).message || 'Không thể xóa ảnh.'); }
        finally { setImageDeleting(false); }
    };

    const fetchMedicines = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/medicines?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh mục thuốc.');
            if (res.success && res.data) {
                setMedicines(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải danh mục thuốc.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchMedicines();
    }, [page, statusFilter]);

    const handleSearchSubmit = () => {
        if (page !== 1) setPage(1);
        else fetchMedicines();
    };

    const handleFormSubmit = async () => {
        setFormError('');
        setFormLoading(true);

        try {
            if (!modal.isEdit && savedMedicineId !== null) {
                await finishSave(savedMedicineId, 'Thêm thuốc mới vào danh mục thành công.');
                return;
            }
            if (modal.isEdit) {
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/medicines/${modal.data.id}`, {
                    ...catalogPayload(),
                    name: modal.data.name,
                    unit: modal.data.unit,
                    reorderLevel: Number(modal.data.reorderLevel) || 10,
                    unitPrice: modal.data.unitPrice !== undefined && modal.data.unitPrice !== null ? Number(modal.data.unitPrice) : null,
                    isActive: modal.data.isActive ?? true
                });
                if (res.success) {
                    await finishSave(modal.data.id!, 'Cập nhật thông tin thuốc thành công.');
                }
            } else {
                const res = await axiosClient.post<any, ApiResponse<any>>('/admin/medicines', {
                    ...catalogPayload(),
                    code: modal.data.code,
                    name: modal.data.name,
                    unit: modal.data.unit,
                    stockQuantity: Number(modal.data.stockQuantity) || 0,
                    reorderLevel: Number(modal.data.reorderLevel) || 10,
                    unitPrice: modal.data.unitPrice !== undefined && modal.data.unitPrice !== null ? Number(modal.data.unitPrice) : null,
                    isActive: modal.data.isActive ?? true
                });
                if (res.success) {
                    await finishSave(res.data.id, 'Thêm thuốc mới vào danh mục thành công.');
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra khi lưu thông tin thuốc.');
        } finally {
            setFormLoading(false);
        }
    };

    const handleToggleStatus = async (id: number) => {
        if (toggleInFlight.current !== null) return;
        toggleInFlight.current = id;
        setTogglingId(id);
        try {
            const res = await axiosClient.patch<any, ApiResponse<any>>(`/admin/medicines/${id}/toggle-status`);
            if (res.success) {
                await fetchMedicines();
            }
        } catch (error: any) {
            showAlert(error?.message || 'Không thể đổi trạng thái thuốc.', 'Lỗi', 'error');
        } finally {
            toggleInFlight.current = null;
            setTogglingId(null);
        }
    };


    const modalBusy = formLoading || imageDeleting;
    const closeModal = () => { if (!modalBusy) setModal({ isOpen: false, isEdit: false, data: {} }); };
    const openModal = (data: Partial<Medicine>, isEdit: boolean) => { setFormError(''); setModal({ isOpen: true, isEdit, data }); };

    return (
        <div className={styles.medicinesPage}>
            <PageHeader title="Danh mục thuốc & Vật tư y tế" actions={<Button type="primary" icon={<Plus size={18} />} onClick={() => openModal({ isActive: true, stockQuantity: 100, reorderLevel: 20, unit: 'Viên' }, false)}>Thêm thuốc mới</Button>} />
            <FilterBar>
                <Input.Search className={styles.medicineSearch} placeholder="Tìm theo tên thuốc hoặc mã..." enterButton="Tìm kiếm" value={search} onChange={e => setSearch(e.target.value)} onSearch={handleSearchSubmit} />
                <Select className={styles.medicineStatusFilter} aria-label="Trạng thái" value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                    { value: '', label: 'Tất cả trạng thái' }, { value: 'true', label: 'Đang sử dụng' }, { value: 'false', label: 'Tạm ngưng' }
                ]} />
            </FilterBar>
            {loading ? <LoadingState message="Đang tải danh mục thuốc..." /> : loadError ? <InlineError message={loadError} onRetry={fetchMedicines} />
                : medicines.length === 0 ? <EmptyState title="Không tìm thấy thuốc nào trong danh mục." /> : <>
                    <DataTable ariaLabel="Danh mục thuốc" data={medicines} keyExtractor={medicine => medicine.id} columns={[
                        { header: 'Mã thuốc', accessor: medicine => <Typography.Text strong>{medicine.code}</Typography.Text> },
                        { header: 'Tên thuốc', accessor: medicine => <Typography.Text strong>{medicine.name}</Typography.Text> },
                        { header: 'Nhóm', accessor: medicine => medicine.categoryName || '—' },
                        { header: 'Kê đơn', accessor: medicine => <Tag>{(medicine.isPrescriptionRequired ?? true) ? 'Kê đơn' : 'Không kê đơn'}</Tag> },
                        { header: 'Đơn vị tính', accessor: medicine => <Tag>{medicine.unit}</Tag> },
                        { header: 'Đơn giá (VNĐ)', accessor: medicine => medicine.unitPrice ? <Typography.Text strong className={styles.medicinePrice}>{medicine.unitPrice.toLocaleString('vi-VN')} đ</Typography.Text> : <Typography.Text type="secondary">Chưa định giá</Typography.Text> },
                        { header: 'Tồn kho hiện tại', accessor: medicine => <Typography.Text strong type={medicine.stockQuantity <= medicine.reorderLevel ? 'danger' : undefined} className={styles.medicineStock}>
                            {medicine.stockQuantity}{medicine.stockQuantity <= medicine.reorderLevel && <span className={styles.stockWarning}><AlertTriangle size={15}><title>Tồn kho dưới ngưỡng cảnh báo!</title></AlertTriangle></span>}
                        </Typography.Text> },
                        { header: 'Mức cảnh báo', accessor: medicine => <Typography.Text type="secondary">{medicine.reorderLevel}</Typography.Text> },
                        { header: 'Trạng thái', accessor: medicine => <StatusBadge status={medicine.isActive ? 'Approved' : 'Rejected'} label={medicine.isActive ? 'Đang dùng' : 'Tạm ngưng'} /> },
                        { header: 'Thao tác', align: 'right', accessor: medicine => <div className={styles.medicineRowActions}>
                            <Button size="small" icon={<Edit size={14} />} title="Chỉnh sửa" onClick={() => openModal({ ...medicine }, true)}>Sửa</Button>
                            <Button size="small" title={medicine.isActive ? 'Ngưng sử dụng' : 'Mở lại'} loading={togglingId === medicine.id} disabled={togglingId !== null} onClick={() => handleToggleStatus(medicine.id)}>{medicine.isActive ? 'Khóa' : 'Mở'}</Button>
                        </div> }
                    ]} />
                    <Pagination page={page} totalPages={Math.ceil(totalItems / 10)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.medicineTotal}>Tổng số: {totalItems} thuốc</div>}
            <MedicineCategoryManager categories={categories} onChanged={() => { fetchCategories(); fetchMedicines(); }} />
            <Modal className={styles.medicineModal} centered title={modal.isEdit ? 'Chỉnh sửa thông tin thuốc' : 'Thêm thuốc mới'} open={modal.isOpen} onCancel={closeModal} footer={null} destroyOnHidden width={600}
                maskClosable={!modalBusy} closable={!modalBusy} keyboard={!modalBusy}>
                {formError && <Alert className={styles.medicineFormAlert} type="error" title={formError} showIcon />}
                <Form layout="vertical" onFinish={handleFormSubmit} disabled={modalBusy}>
                    <div className={styles.medicineFormGrid}>
                        <Form.Item label="Mã thuốc (*)" htmlFor="medicineCode"><Input id="medicineCode" required disabled={modal.isEdit || modalBusy} value={modal.data.code || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, code: e.target.value } })} placeholder="VD: PARA500" /></Form.Item>
                        <Form.Item label="Tên thuốc (*)" htmlFor="medicineName"><Input id="medicineName" required value={modal.data.name || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, name: e.target.value } })} placeholder="VD: Paracetamol 500mg" /></Form.Item>
                        <Form.Item label="Đơn vị tính (*)" htmlFor="medicineUnit"><Input id="medicineUnit" required value={modal.data.unit || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, unit: e.target.value } })} placeholder="VD: Viên, Vỉ, Chai, Hộp" /></Form.Item>
                        <Form.Item label="Mức cảnh báo hết hàng" htmlFor="medicineReorderLevel"><InputNumber id="medicineReorderLevel" className={styles.medicineNumber} required min={1} value={modal.data.reorderLevel || 10} onChange={value => setModal({ ...modal, data: { ...modal.data, reorderLevel: value ?? 0 } })} /></Form.Item>
                        <Form.Item label="Đơn giá bán (VNĐ)" htmlFor="medicineUnitPrice"><InputNumber id="medicineUnitPrice" className={styles.medicineNumber} min={0} step={500} value={modal.data.unitPrice ?? null} onChange={value => setModal({ ...modal, data: { ...modal.data, unitPrice: value } })} placeholder="VD: 5000" /></Form.Item>
                        {!modal.isEdit && <Form.Item label="Số lượng tồn ban đầu" htmlFor="medicineInitialStock"><InputNumber id="medicineInitialStock" className={styles.medicineNumber} min={0} value={modal.data.stockQuantity || 0} onChange={value => setModal({ ...modal, data: { ...modal.data, stockQuantity: value ?? 0 } })} /></Form.Item>}
                    </div>
                    {([
                        ['activeIngredient', 'Hoạt chất', 200], ['strength', 'Hàm lượng', 100],
                        ['dosageForm', 'Dạng bào chế', 100], ['manufacturer', 'Nhà sản xuất', 200],
                        ['description', 'Mô tả', 1000], ['storageInstructions', 'Hướng dẫn bảo quản', 500]
                    ] as const).map(([field, label, maxLength]) => <Form.Item key={field} label={label} htmlFor={`medicine-${field}`}>
                        <Input id={`medicine-${field}`} maxLength={maxLength} value={modal.data[field] ?? ''} onChange={e => setModal({ ...modal, data: { ...modal.data, [field]: e.target.value } })} />
                    </Form.Item>)}
                    <Form.Item label="Nhóm thuốc" htmlFor="medicineCategory">
                        <select id="medicineCategory" className={styles.medicineCategorySelect} disabled={modalBusy} value={modal.data.categoryId ?? ''} onChange={e => setModal({ ...modal, data: { ...modal.data, categoryId: e.target.value ? Number(e.target.value) : null } })}>
                            <option value="">Không có nhóm</option>{categories.filter(c => c.isActive).map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                        </select>
                    </Form.Item>
                    <Form.Item><Checkbox checked={modal.data.isPrescriptionRequired ?? true} onChange={e => setModal({ ...modal, data: { ...modal.data, isPrescriptionRequired: e.target.checked } })}>Thuốc kê đơn</Checkbox></Form.Item>
                    <Form.Item label="Ảnh thuốc" htmlFor="medicineImage"><input id="medicineImage" className={styles.medicineImageInput} type="file" accept="image/jpeg,image/png,image/webp" disabled={modalBusy} onChange={e => setSelectedImage(e.target.files?.[0] ?? null)} /></Form.Item>
                    {(imagePreview || modal.data.imageUrl) && <img className={styles.medicineImagePreview} src={imagePreview ?? medicineImageUrl(modal.data.imageUrl!)} alt="Xem trước ảnh thuốc" />}
                    {modal.data.imageUrl && <Button className={styles.medicineImageDelete} disabled={modalBusy} loading={imageDeleting} onClick={deleteImage}>Xóa ảnh</Button>}
                    <Form.Item><Checkbox checked={modal.data.isActive ?? true} onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}>Kích hoạt cho phép kê đơn & cấp phát thuốc này</Checkbox></Form.Item>
                    <div className={styles.medicineFormActions}><Button disabled={modalBusy} onClick={closeModal}>Hủy</Button><Button type="primary" htmlType="submit" disabled={formLoading || imageDeleting}>{formLoading ? 'Đang lưu...' : (modal.isEdit ? 'Lưu thay đổi' : 'Thêm thuốc')}</Button></div>
                </Form>
            </Modal>
        </div>
    );
};
