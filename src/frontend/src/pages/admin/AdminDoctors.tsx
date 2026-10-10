import React, { useState, useEffect } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { Plus, XCircle, Edit, Briefcase, Award } from 'lucide-react';
import { Alert, Avatar, Button, Checkbox, Form, Input, InputNumber, Modal, Select, Tag, Typography } from 'antd';
import { PageHeader, FilterBar, DataTable, Pagination, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import styles from './AdminDoctors.module.css';
import { useDialog } from '../../contexts/DialogContext';
import { formatDoctorName } from '../../utils/doctorNameHelper';

interface DoctorSpecialty {
    specialtyId: number;
    specialtyName: string;
    isPrimary: boolean;
}

interface Doctor {
    id: number;
    userId: string;
    fullName: string;
    email: string;
    academicTitle?: string;
    experienceYears: number;
    description: string;
    isActive: boolean;
    specialties: DoctorSpecialty[];
}

interface DoctorUser { id: string; fullName: string; email: string; }
interface Specialty { id: number; name: string; }

export const AdminDoctors: React.FC = () => {
    const { showAlert, showConfirm } = useDialog();
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState('');
    const [openingModal, setOpeningModal] = useState<'create' | number | null>(null);
    const [selectedSpecialty, setSelectedSpecialty] = useState<number | undefined>();
    const [totalItems, setTotalItems] = useState(0);
    const [page, setPage] = useState(1);
    
    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');

    // Modal
    const [modal, setModal] = useState<{ isOpen: boolean, isEdit: boolean, data: Partial<Doctor> }>({ isOpen: false, isEdit: false, data: {} });
    const [formLoading, setFormLoading] = useState(false);
    const [formError, setFormError] = useState('');
    
    // Data for Create Form
    const [unassignedUsers, setUnassignedUsers] = useState<DoctorUser[]>([]);
    const [allSpecialties, setAllSpecialties] = useState<Specialty[]>([]);

    const fetchDoctors = async () => {
        setLoading(true);
        setLoadError('');
        try {
            const params = new URLSearchParams({
                page: page.toString(),
                pageSize: '10'
            });
            if (search) params.append('search', search);
            if (statusFilter !== '') params.append('isActive', statusFilter);

            const res = await axiosClient.get<any, ApiResponse<any>>(`/admin/doctors?${params.toString()}`);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách bác sĩ.');
            if (res.success && res.data) {
                setDoctors(res.data.items);
                setTotalItems(res.data.totalItems);
            }
        } catch (error) {
            setLoadError((error as Error)?.message || 'Không thể tải danh sách bác sĩ.');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchDoctors();
    }, [page, statusFilter]);

    const loadFormDependencies = async () => {
        const [usersRes, specsRes, doctorsRes] = await Promise.all([
            axiosClient.get<any, ApiResponse<{ items: DoctorUser[] }>>('/admin/users?role=Doctor&isActive=true&pageSize=100'),
            axiosClient.get<any, ApiResponse<{ items: Specialty[] }>>('/admin/specialties?isActive=true&pageSize=100'),
            axiosClient.get<any, ApiResponse<{ items: Doctor[] }>>('/admin/doctors?pageSize=100')
        ]);
        for (const response of [usersRes, specsRes, doctorsRes]) {
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải dữ liệu hồ sơ bác sĩ.');
        }
        const assignedUsers = new Set(doctorsRes.data!.items.map(doctor => doctor.userId));
        setUnassignedUsers(usersRes.data!.items.filter(user => !assignedUsers.has(user.id)));
        setAllSpecialties(specsRes.data!.items);
    };

    const handleSearchSubmit = () => {
        if (page !== 1) setPage(1);
        else fetchDoctors();
    };

    const handleFormSubmit = async () => {
        setFormError('');
        setFormLoading(true);
        
        try {
            if (modal.isEdit) {
                // Update specialties
                if (modal.data.specialties && modal.data.specialties.length > 0) {
                    const primaryCount = modal.data.specialties.filter(s => s.isPrimary).length;
                    if (primaryCount !== 1) {
                        setFormError('Phải có chính xác 1 chuyên khoa chính.');
                        setFormLoading(false);
                        return;
                    }
                    await axiosClient.put<any, ApiResponse<any>>(`/admin/doctors/${modal.data.id}/specialties`, modal.data.specialties.map(s => ({
                        specialtyId: s.specialtyId,
                        isPrimary: s.isPrimary
                    })));
                }

                // Update basic info
                const res = await axiosClient.put<any, ApiResponse<any>>(`/admin/doctors/${modal.data.id}`, {
                    academicTitle: modal.data.academicTitle,
                    experienceYears: Number(modal.data.experienceYears || 0),
                    description: modal.data.description,
                    isActive: modal.data.isActive
                });
                
                if (res.success) {
                    showAlert('Cập nhật bác sĩ thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchDoctors();
                }
            } else {
                if (!modal.data.userId) {
                    setFormError('Vui lòng chọn tài khoản liên kết.');
                    setFormLoading(false);
                    return;
                }
                if (!modal.data.specialties || modal.data.specialties.length === 0) {
                    setFormError('Vui lòng chọn ít nhất 1 chuyên khoa.');
                    setFormLoading(false);
                    return;
                }
                const primaryCount = modal.data.specialties.filter(s => s.isPrimary).length;
                if (primaryCount !== 1) {
                    setFormError('Phải có chính xác 1 chuyên khoa chính.');
                    setFormLoading(false);
                    return;
                }

                const res = await axiosClient.post<any, ApiResponse<any>>('/admin/doctors', {
                    userId: modal.data.userId,
                    academicTitle: modal.data.academicTitle,
                    experienceYears: Number(modal.data.experienceYears || 0),
                    description: modal.data.description,
                    isActive: modal.data.isActive ?? true,
                    specialties: modal.data.specialties.map((s: any) => ({
                        specialtyId: s.specialtyId,
                        isPrimary: s.isPrimary
                    }))
                });
                if (res.success) {
                    showAlert('Thêm hồ sơ bác sĩ thành công.', 'Thành công', 'success');
                    setModal({ isOpen: false, isEdit: false, data: {} });
                    fetchDoctors();
                }
            }
        } catch (error: any) {
            setFormError(error?.message || 'Có lỗi xảy ra.');
        } finally {
            setFormLoading(false);
        }
    };

    const openCreateModal = async () => {
        setOpeningModal('create');
        try {
            await loadFormDependencies();
            setFormError(''); setSelectedSpecialty(undefined);
            setModal({ isOpen: true, isEdit: false, data: { isActive: true, experienceYears: 0, specialties: [] } });
        } catch (error) {
            showAlert((error as Error)?.message || 'Không thể tải dữ liệu hồ sơ bác sĩ.', 'Lỗi', 'error');
        } finally { setOpeningModal(null); }
    };

    const openEditModal = async (doc: Doctor) => {
        setOpeningModal(doc.id);
        try {
            await loadFormDependencies();
            setFormError(''); setSelectedSpecialty(undefined);
            setModal({ isOpen: true, isEdit: true, data: { ...doc, specialties: doc.specialties.map(s => ({ ...s })) } });
        } catch (error) {
            showAlert((error as Error)?.message || 'Không thể tải dữ liệu hồ sơ bác sĩ.', 'Lỗi', 'error');
        } finally { setOpeningModal(null); }
    };

    const handleAddSpecialty = (specId: number) => {
        if (!specId) return;
        const current = modal.data.specialties || [];
        if (current.find(s => s.specialtyId === specId)) return;
        
        const specName = allSpecialties.find(s => s.id === specId)?.name || '';
        const isPrimary = current.length === 0; // First one is primary by default

        setModal({
            ...modal,
            data: {
                ...modal.data,
                specialties: [...current, { specialtyId: specId, specialtyName: specName, isPrimary }]
            }
        });
    };

    const handleRemoveSpecialty = (specId: number) => {
        const current = modal.data.specialties || [];
        if (current.length === 1) {
            showAlert('Bác sĩ phải có ít nhất 1 chuyên khoa.', 'Lỗi', 'error');
            return;
        }
        
        const isRemovingPrimary = current.find(s => s.specialtyId === specId)?.isPrimary;
        const newSpecs = current.filter(s => s.specialtyId !== specId)
            .map((s, index) => ({ ...s, isPrimary: isRemovingPrimary ? index === 0 : s.isPrimary }));

        setModal({
            ...modal,
            data: {
                ...modal.data,
                specialties: newSpecs
            }
        });
    };

    const handleSetPrimarySpecialty = (specId: number) => {
        const current = modal.data.specialties || [];
        showConfirm('Đổi chuyên khoa chính có thể ảnh hưởng đến lịch hẹn hiện có. Bạn có chắc chắn muốn tiếp tục?', () => {
            const newSpecs = current.map(s => ({
                ...s,
                isPrimary: s.specialtyId === specId
            }));
    
            setModal({
                ...modal,
                data: {
                    ...modal.data,
                    specialties: newSpecs
                }
            });
        });
    };

    const closeModal = () => { if (!formLoading) setModal({ isOpen: false, isEdit: false, data: {} }); };

    return (
        <div className={styles.doctorsPage}>
            <PageHeader title="Quản lý bác sĩ" actions={<Button type="primary" icon={<Plus size={18} />} loading={openingModal === 'create'} disabled={openingModal !== null} onClick={openCreateModal}>Thêm hồ sơ bác sĩ</Button>} />
            <FilterBar>
                <Input.Search className={styles.doctorSearch} placeholder="Tìm theo tên..." enterButton="Lọc" value={search} onChange={e => setSearch(e.target.value)} onSearch={handleSearchSubmit} />
                <Select className={styles.doctorStatusFilter} aria-label="Trạng thái" value={statusFilter} onChange={value => { setStatusFilter(value); setPage(1); }} options={[
                    { value: '', label: 'Tất cả trạng thái' }, { value: 'true', label: 'Đang hoạt động' }, { value: 'false', label: 'Ngừng hoạt động' }
                ]} />
            </FilterBar>
            {loading ? <LoadingState /> : loadError ? <InlineError message={loadError} onRetry={fetchDoctors} />
                : doctors.length === 0 ? <EmptyState title="Không tìm thấy dữ liệu." /> : <>
                    <DataTable data={doctors} keyExtractor={doctor => doctor.id} columns={[
                        { header: 'Bác sĩ', accessor: doctor => <div className={styles.doctorIdentity}>
                            <Avatar>{doctor.fullName.charAt(0)}</Avatar>
                            <div><Typography.Text strong>{formatDoctorName(doctor.academicTitle, doctor.fullName)}</Typography.Text><div className={styles.doctorEmail}>{doctor.email}</div></div>
                        </div> },
                        { header: 'Chuyên môn', accessor: doctor => <>
                            <div className={styles.doctorExperience}><Briefcase size={14} />{doctor.experienceYears} năm kinh nghiệm</div>
                            <div className={styles.specialtyTags}>{doctor.specialties.map(s => <Tag key={s.specialtyId} color={s.isPrimary ? 'processing' : undefined} title={s.isPrimary ? 'Chuyên khoa chính' : undefined} icon={s.isPrimary ? <Award size={12} /> : undefined}>{s.specialtyName}</Tag>)}</div>
                        </> },
                        { header: 'Trạng thái', accessor: doctor => <StatusBadge status={doctor.isActive ? 'Approved' : 'Rejected'} label={doctor.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'} /> },
                        { header: 'Thao tác', align: 'right', accessor: doctor => <Button size="small" icon={<Edit size={14} />} loading={openingModal === doctor.id} disabled={openingModal !== null} onClick={() => openEditModal(doctor)}>Sửa</Button> }
                    ]} />
                    <Pagination page={page} totalPages={Math.ceil(totalItems / 10)} totalRecords={totalItems} onPageChange={setPage} />
                </>}
            {!loading && !loadError && <div className={styles.doctorTotal}>Tổng cộng: {totalItems} bác sĩ</div>}
            <Modal title={modal.isEdit ? 'Cập nhật hồ sơ bác sĩ' : 'Thêm hồ sơ bác sĩ'} open={modal.isOpen} onCancel={closeModal} footer={null} destroyOnHidden width={600}
                maskClosable={!formLoading} closable={!formLoading} keyboard={!formLoading}>
                {formError && <Alert className={styles.doctorFormAlert} type="error" title={formError} showIcon />}
                <Form layout="vertical" onFinish={handleFormSubmit} disabled={formLoading}>
                    {!modal.isEdit && <>
                        {unassignedUsers.length === 0 && <Alert className={styles.doctorFormAlert} type="info" title="Không còn tài khoản Bác sĩ nào chưa có hồ sơ." showIcon />}
                        <Form.Item label="Chọn tài khoản User liên kết (*)" htmlFor="doctorUser">
                            <Select id="doctorUser" aria-label="Chọn tài khoản User liên kết (*)" placeholder="-- Chọn User (Role Doctor) --" value={modal.data.userId || undefined} onChange={value => setModal({ ...modal, data: { ...modal.data, userId: value } })}
                                options={unassignedUsers.map(user => ({ value: user.id, label: `${user.fullName} (${user.email})` }))} />
                        </Form.Item>
                    </>}
                    <div className={styles.doctorFormGrid}>
                        <Form.Item label="Học hàm/Học vị" htmlFor="doctorAcademicTitle"><Input id="doctorAcademicTitle" value={modal.data.academicTitle || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, academicTitle: e.target.value } })} placeholder="VD: PGS. TS. BS" /></Form.Item>
                        <Form.Item label="Số năm K.Nghiệm" htmlFor="doctorExperience"><InputNumber id="doctorExperience" className={styles.doctorNumber} min={0} max={100} value={modal.data.experienceYears ?? 0} onChange={value => setModal({ ...modal, data: { ...modal.data, experienceYears: value ?? 0 } })} /></Form.Item>
                    </div>
                    <Form.Item label="Chuyên khoa (*)" htmlFor="doctorSpecialty">
                        <div className={styles.specialtyPicker}>
                            <Select id="doctorSpecialty" aria-label="Chuyên khoa (*)" className={styles.specialtySelect} placeholder="-- Chọn chuyên khoa để thêm --" value={selectedSpecialty} onChange={setSelectedSpecialty}
                                options={allSpecialties.filter(s => !(modal.data.specialties || []).some(selected => selected.specialtyId === s.id)).map(s => ({ value: s.id, label: s.name }))} />
                            <Button onClick={() => { if (selectedSpecialty) handleAddSpecialty(selectedSpecialty); setSelectedSpecialty(undefined); }}>Thêm</Button>
                        </div>
                        <div className={styles.selectedSpecialties}>
                            {(modal.data.specialties || []).length === 0 ? <Typography.Text type="secondary">Chưa chọn chuyên khoa nào.</Typography.Text>
                                : (modal.data.specialties || []).map(s => <div className={styles.selectedSpecialtyRow} key={s.specialtyId}>
                                    <div className={styles.selectedSpecialtyName}>{s.isPrimary && <Award size={16} />}<Typography.Text strong={s.isPrimary}>{s.specialtyName}</Typography.Text>{s.isPrimary && <Tag color="processing">Chính</Tag>}</div>
                                    <div className={styles.specialtyActions}>
                                        {!s.isPrimary && <Button size="small" onClick={() => handleSetPrimarySpecialty(s.specialtyId)}>Đặt làm chính</Button>}
                                        <Button type="text" danger aria-label={`Gỡ chuyên khoa ${s.specialtyName}`} icon={<XCircle size={16} />} onClick={() => handleRemoveSpecialty(s.specialtyId)} />
                                    </div>
                                </div>)}
                        </div>
                    </Form.Item>
                    <Form.Item label="Mô tả" htmlFor="doctorDescription"><Input.TextArea id="doctorDescription" rows={3} value={modal.data.description || ''} onChange={e => setModal({ ...modal, data: { ...modal.data, description: e.target.value } })} /></Form.Item>
                    <Form.Item><Checkbox checked={modal.data.isActive ?? true} onChange={e => setModal({ ...modal, data: { ...modal.data, isActive: e.target.checked } })}>Đang hoạt động</Checkbox></Form.Item>
                    <div className={styles.doctorFormActions}><Button disabled={formLoading} onClick={closeModal}>Hủy</Button><Button type="primary" htmlType="submit" disabled={formLoading}>{formLoading ? 'Đang lưu...' : 'Xác nhận lưu'}</Button></div>
                </Form>
            </Modal>
        </div>
    );
};
